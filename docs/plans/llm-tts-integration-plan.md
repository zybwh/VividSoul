# VividSoul LLM / TTS 集成计划

## 文档信息

- 项目：`VividSoul`
- 范围：`LLM provider` 抽象、会话与记忆、主动消息、TTS 框架、设置窗口、聊天输入 UI
- 关联文档：`../architecture.md`、`../STATUS.md`、`./dialogue-bubble-implementation-plan.md`
- 日期：`2026-04-05`
- 状态：`planned`

## 1. 结论先行

基于当前 `VividSoul` 的代码结构，最快可落地的方案不是先引入完整后端，也不是先重构到 `VContainer`，而是：

1. 继续沿用当前 `DesktopPetRuntimeController` 的手写组装根模式。
2. 在 `Assets/App/Runtime/AI/` 下新增一层薄的 `AI Runtime`。
3. 用 `provider adapter + 会话编排器 + 记忆存储 + UI presenter` 的组合先把文本对话跑通。
4. `TTS` 先接接口和设置，不默认启用播放。
5. 设置页先做成同一透明窗口内的独立浮层“窗口”，不要一上来做真正的系统第二窗口。

一句话方案：

> 第一版先把 `当前单窗口桌宠 + 对话气泡` 升级为 `可切 provider、有 session 和记忆、支持主动发言、可扩展到 TTS` 的轻量 AI Runtime，而不是直接上重量级后端架构。

## 2. 当前代码现状与机会点

从现有代码可以确认：

- `DesktopPetRuntimeController` 在 `Awake()` 里手动创建服务，并通过 `EnsureServices()` 懒加载补齐，当前没有正式 DI 容器。
- `DesktopPetRuntimeHud` 已经是运行时 UI 总入口，负责右键菜单、状态条和对白气泡。
- 当前右键主菜单里已经预留了 `设置`，但仍是禁用项。
- 对话展示链路已经存在，可以直接把 `LLM` 输出接到 `DesktopPetSpeechBubblePresenter`。
- 设置已经通过 `desktop-pet-settings.json` 持久化，适合继续扩展 `AI` 相关设置。
- 内容系统已经识别 `Voice` 类型内容和音量字段，但还没有真正的 `TTS` 调用与播放链路。

这意味着：

- 现在最适合做“薄抽象 + 快速接入”。
- 不必等待 `Soul Backend` 就能先把文本 LLM 跑起来。
- 未来即使迁移到后端网关，客户端核心逻辑也可以保持不变，只替换 provider adapter。

## 3. 本轮设计目标

### 3.1 必须满足

1. 支持多种 `provider`，切换 provider 时聊天核心逻辑不变。
2. 支持连续对话，不是单问单答。
3. 同时支持：
   - 用户主动发消息
   - `mate` 后台定时主动发消息
4. `TTS` 先做框架和设置，默认关闭。
5. 提供正式的 `LLM` 设置页，而不是 `ContextMenu` 式调试按钮。
6. 提供更优雅的聊天输入和历史界面。

### 3.2 本轮不强求

- 先不上真正的多窗口原生窗体系统。
- 先不上复杂长期记忆检索或向量数据库。
- 先不上语音输入 `STT`。
- 先不上动作、表情、口型的完整 AI 编排，只预留结构化字段。

## 4. 推荐目录与模块划分

建议新增：

```text
VividSoul/Assets/App/Runtime/
  AI/
    Providers/
    Sessions/
    Memory/
    Prompting/
    Tts/
    Transport/
  UI/
    Chat/
    Settings/
```

建议核心类型：

- `ILlmProvider`
- `ILlmProviderRegistry`
- `LlmProviderProfile`
- `LlmRequestContext`
- `LlmResponseEnvelope`
- `MateConversationOrchestrator`
- `ChatSessionService`
- `ChatSessionStore`
- `ConversationSummaryService`
- `MateProactiveScheduler`
- `ITtsProvider`
- `ITtsPlaybackService`
- `AiSettingsData`
- `AiSecretsStore`
- `DesktopPetChatOverlayPresenter`
- `DesktopPetSettingsWindowPresenter`

## 5. Provider 抽象设计

### 5.1 设计原则

- UI 和业务逻辑永远不直接依赖具体 SDK。
- `provider` 切换只影响 adapter 和配置，不影响会话、记忆、HUD、输入面板。
- 第一版优先支持 `OpenAI-compatible` 协议，这样可以一口气覆盖：
  - `OpenAI`
  - `OpenRouter`
  - `SiliconFlow`
  - `Ollama` 的兼容端口
  - 其它兼容 `chat/completions` 的服务
- 后续再补专用 adapter：
  - `Anthropic`
  - `Gemini`

### 5.2 接口建议

```csharp
public interface ILlmProvider
{
    string ProviderId { get; }
    bool SupportsStreaming { get; }
    bool SupportsSystemPrompt { get; }
    Task<LlmResponseEnvelope> GenerateAsync(LlmRequestContext request, CancellationToken cancellationToken);
}
```

### 5.3 配置模型

建议把“用户可切换 provider”拆成：

- `AiGlobalSettings`
  - `ActiveProviderId`
  - `GlobalSystemPrompt`
  - `Temperature`
  - `MaxOutputTokens`
  - `EnableStreaming`
  - `EnableProactiveMessage`
  - `EnableTts`
  - `MemoryWindowTurns`
  - `SummaryThreshold`
- `LlmProviderProfile`
  - `Id`
  - `DisplayName`
  - `ProviderType`
  - `BaseUrl`
  - `Model`
  - `ApiKeyRef`
  - `Enabled`

### 5.4 为什么这样最适合当前项目

因为当前 `DesktopPetRuntimeController` 已经是手动创建服务的组合根，所以第一版直接在那里挂：

- `provider registry`
- `conversation orchestrator`
- `session service`
- `proactive scheduler`
- `tts service`

这样改动面最小，且未来切到 `VContainer` 时只需要搬注册方式，不需要推翻接口。

## 6. 会话与记忆设计

### 6.1 Session 模型

建议按“角色维度”维护当前会话：

- `SessionId = characterFingerprint + userProfile`
- 同一个角色连续聊天共享 session
- 切换角色后默认切到另一条 session

这里可以直接复用现有 `ModelFingerprintService`，把不同 VRM 角色自然区分开。

### 6.2 消息结构

```csharp
public enum ChatInvocationSource
{
    UserInput,
    ProactiveTick
}

public sealed record ChatMessage(
    string Id,
    string SessionId,
    ChatRole Role,
    string Text,
    DateTimeOffset CreatedAt,
    ChatInvocationSource Source);
```

### 6.3 记忆分层

第一版不要直接上复杂 RAG，建议三层：

1. `RecentTurns`
   - 最近 `N` 轮原始消息
   - 直接发给模型
2. `ConversationSummary`
   - 老消息超过阈值后压缩为摘要
   - 放在 prompt 前部
3. `PinnedMemory`
   - 用户名、偏好、禁忌、关系设定、角色长期设定
   - 可由系统或手动写入

### 6.4 存储策略

建议新增：

- `persistentDataPath/ai/sessions/<sessionId>.json`
- `persistentDataPath/ai/memory/<sessionId>.json`
- `persistentDataPath/ai/topics/<sessionId>.json`

不要把完整会话历史全部塞进 `desktop-pet-settings.json`，否则设置文件会很快膨胀。

### 6.5 会话编排

每次请求都走同一条编排链：

1. 读取 `AI settings`
2. 取得当前 `provider profile`
3. 解析当前角色对应的 `session`
4. 组装 `system prompt + pinned memory + summary + recent turns + 当前触发`
5. 调用 `provider`
6. 规范化结果为 `LlmResponseEnvelope`
7. 追加到会话
8. 必要时做摘要压缩
9. 把 `display text` 送给 HUD / 聊天面板
10. 若 `TTS` 已启用，再走音频链路

## 7. 两种调用方式的统一设计

### 7.1 用户主动发消息

入口：

- 聊天输入框
- 发送按钮
- 回车发送

流程：

1. 用户输入文本
2. `DesktopPetChatOverlayPresenter` 发出 `UserMessageSubmitted`
3. `MateConversationOrchestrator` 按标准 session 流程调用
4. 文本结果同时更新：
   - 聊天历史面板
   - 头顶对白气泡

### 7.2 Mate 主动发消息

入口：

- `MateProactiveScheduler`

机制建议：

- 使用后台定时器或 `Update + nextFireTime`
- 满足条件时发起一次 `ProactiveTick`

建议触发条件：

- 距离上一次 AI 发言超过冷却时间
- 当前没有正在进行的请求
- 当前没有设置窗口或输入框处于强交互状态
- 当前角色已加载
- 用户没有开启 `Do Not Disturb`

建议两段式生成：

1. 先由调度器判断“该不该说”
2. 真需要说时，再调用 LLM 生成：
   - 轻话题
   - 问候
   - 对当前时间/闲置状态的评论

这样可以避免角色过于聒噪。

### 7.3 统一请求源

无论是用户输入还是主动消息，都走同一个 `ChatInvocationSource` 和同一个 `MateConversationOrchestrator`。

这点非常重要，因为：

- 记忆逻辑只做一套
- provider 调用只做一套
- UI 展示只做一套
- 将来切后端也只改 transport 层

## 8. LLM 响应结构建议

虽然当前先只消费文本，但建议从第一版起就统一输出结构：

```json
{
  "displayText": "晚安呀，今天辛苦了。",
  "mood": "gentle",
  "suggestedPoseId": "",
  "topicTag": "night_greeting",
  "shouldSpeak": true,
  "ttsText": "晚安呀，今天辛苦了。"
}
```

建议客户端第一版只使用：

- `displayText`
- `ttsText`
- `shouldSpeak`

先预留但暂不执行：

- `mood`
- `suggestedPoseId`
- `expression`
- `lookAt`

这样未来要把 AI 文本扩展到动作和表情时，不需要改 provider 主协议。

## 9. TTS 设计

### 9.1 当前目标

- 先建接口
- 先建设置
- 先建播放通路
- 默认关闭

### 9.2 接口建议

```csharp
public interface ITtsProvider
{
    string ProviderId { get; }
    Task<TtsSynthesisResult> SynthesizeAsync(TtsRequest request, CancellationToken cancellationToken);
}
```

### 9.3 播放层拆分

建议拆成两层：

- `ITtsProvider`
  - 负责把文本变成音频数据
- `ITtsPlaybackService`
  - 负责 Unity 里播放 `AudioClip`

第一版先把 `ITtsPlaybackService` 接好，但 `EnableTts = false`。

### 9.4 为什么先不启动

因为当前最需要先验证的是：

- prompt 是否对
- session 和记忆是否对
- 主动发言节奏是否对
- UI 交互是否顺

如果文本还没稳定就接语音，调试成本会明显上升。

## 10. 设置页设计

### 10.1 入口

当前右键菜单已经有 `设置` 占位，建议直接启用它，打开一个正式的设置浮层。

### 10.2 不建议第一版做真正系统第二窗口

原因：

- 当前工程是单透明桌宠窗口架构。
- 真正第二窗口会带来平台差异、焦点切换、点击穿透和置顶管理的新复杂度。
- 输入框、滚动列表、密码框在 Unity 单窗口 Overlay 里更容易快速做好。

因此建议：

> 第一版做“视觉上是独立窗口”的 `SettingsWindowPanel`，但它仍属于同一个 `Canvas`。

### 10.3 Tab 设计

建议设置页分为：

- `General`
- `Avatar`
- `Animation`
- `Audio`
- `LLM`

其中 `LLM` 页包含：

- provider 列表
- 当前启用 provider
- API URL
- API Key
- Model
- 全局 system prompt
- temperature
- max output tokens
- 是否启用主动消息
- 主动消息最短/最长间隔
- session 记忆窗口
- 摘要阈值
- 是否启用 TTS
- LLM 整体调用统计

建议统计首批字段：

- 总请求数
- 成功数 / 失败数
- 成功率
- 平均耗时
- 累计输入字符
- 累计输出字符
- 最近一次请求的 provider / model / 时间 / 错误

### 10.4 API Key 存储建议

UI 上要有输入框，但存储不要直接和普通设置完全混在一起。

建议：

- `AiSettingsData` 放普通设置
- `ApiKey` 放 `AiSecretsStore`

第一版可以先做独立 secrets 文件，后续再迁移到系统 Keychain / Credential Manager。

## 11. 聊天输入与历史 UI 设计

### 11.1 设计目标

不要做 `Unity debug panel` 风格，不要一整块硬邦邦的测试面板。

建议改成两态：

1. `Compact Composer`
   - 收起时是一条细长圆角输入条
   - 固定在屏幕底部偏中或右下
   - 只显示 placeholder 和发送按钮
2. `Expanded Chat Panel`
   - 点开后展开为半高聊天卡片
   - 上方是历史消息
   - 下方是输入区

### 11.2 视觉建议

- 半透明毛玻璃或柔和浅色卡片
- 圆角
- 阴影弱化，不要像工具窗口
- 用户消息右对齐
- `mate` 消息左对齐
- `mate` 最新回复同时走头顶气泡

### 11.3 交互建议

- `Enter` 发送
- `Shift+Enter` 换行
- 请求中发送按钮变为 loading
- 可折叠
- 可记住上次展开状态
- 打开输入框时临时确保可点击，不受 click-through 干扰

### 11.4 为什么要同时保留气泡和聊天面板

- 气泡适合轻量、角色感强的即时反馈
- 聊天面板适合连续对话和查看上下文

建议关系：

- 聊天面板是主交互层
- 气泡是角色表达层
- 同一条 AI 回复同时进入二者

## 12. 与当前 UI 的最小接线方式

建议仍由 `DesktopPetRuntimeHud` 作为总入口，新增：

- `DesktopPetChatOverlayPresenter`
- `DesktopPetSettingsWindowPresenter`

HUD 继续负责：

- 创建 `Canvas`
- 处理焦点
- 管理菜单显示
- 管理对白气泡
- 管理聊天和设置浮层显示/隐藏

这样可以保持当前“运行时 UI 都挂在 HUD 上”的结构一致性。

## 13. 推荐实现顺序

### Phase 1：AI 数据与设置骨架

交付物：

- `AiSettingsData`
- `LlmProviderProfile`
- `AiSecretsStore`
- 设置页 `LLM` tab 骨架

完成标准：

- 能保存 provider、base URL、model、system prompt、全局开关

### Phase 2：单 provider 文本对话跑通

交付物：

- `ILlmProvider`
- `OpenAiCompatibleLlmProvider`
- `MateConversationOrchestrator`
- `ChatSessionStore`

完成标准：

- 用户发一条消息，角色能回一条文本
- 回复能进入聊天历史和头顶气泡

### Phase 3：连续对话与摘要记忆

交付物：

- `recent turns`
- `summary memory`
- `pinned memory`

完成标准：

- 连续多轮对话不会退化成单轮问答
- 历史超过阈值后自动摘要

### Phase 4：主动发言

交付物：

- `MateProactiveScheduler`
- 主动消息设置
- 主动话题 prompt

完成标准：

- 角色能按配置间隔主动发起轻量话题

### Phase 5：TTS 框架接线但默认关闭

交付物：

- `ITtsProvider`
- `ITtsPlaybackService`
- `EnableTts` 设置

完成标准：

- 文本回复已具备后续一键开启语音的路径
- 默认不开启真实播报

## 14. 当前最值得额外考虑的点

除了你已经列的 6 点，我建议再补这些：

1. `Provider Health Check`
   - 设置页里加一个 `测试连接` 按钮
2. `请求取消`
   - 用户再次发送时能取消上一次请求
3. `成本保护`
   - 支持每轮最大 token 和主动消息节流
4. `角色维度的 system prompt override`
   - 全局 prompt 之外，允许每个角色再叠一层 persona prompt
5. `开发调试面板`
   - 可查看最后一次 prompt、provider、耗时、token usage
6. `对话导出`
   - 后续问题排查会很有用
7. `Do Not Disturb`
   - 防止主动消息过于打扰
8. `流式接口预留`
   - 第一版可不做真 streaming，但接口最好别锁死

## 15. 最终建议

对当前 `VividSoul`，最稳的路线是：

1. 先在客户端内做 `AI Runtime` 薄层。
2. 第一批 provider 以 `OpenAI-compatible` 为主，快速覆盖多个服务。
3. 会话与记忆先走 `recent turns + summary + pinned memory`。
4. UI 上同时做：
   - 正式 `LLM` 设置页
   - 优雅聊天输入与历史面板
   - 继续复用头顶对白气泡
5. `TTS` 只接框架，不在文本链路稳定前打开。

这样做的好处是：

- 实现快
- 风险低
- 不破坏现有 HUD 和对话气泡成果
- 后续无论接 `Soul Backend` 还是继续本地直连 provider，都能平滑演进
