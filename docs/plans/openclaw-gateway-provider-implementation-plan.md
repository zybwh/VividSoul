# VividSoul OpenClaw Gateway Provider 实施计划

## 文档信息

- 项目：`VividSoul`
- 范围：`OpenClaw` 独立 provider、Gateway WebSocket 协议接入、会话绑定、主动消息、聊天与气泡 UI 集成
- 关联文档：`../STATUS.md`、`../architecture.md`、`./llm-tts-integration-plan.md`
- 日期：`2026-04-06`
- 状态：`active`

## 1. 决策摘要

本计划固定以下产品与实现决策：

1. `OpenClaw` 必须作为独立 provider 接入，而不是复用现有 `OpenAI-compatible` provider 伪装实现。
2. `OpenClaw` 接入采用 `WS-only` 的 `Gateway protocol` 方案，不保留 `HTTP chat/completions` 降级路径。
3. 激活 `OpenClaw` 时，远端 `Gateway session / memory / proactive runtime` 是事实源，客户端只做连接、镜像、渲染和用户交互。
4. 当前本地 `OpenAI-compatible / MiniMax` 能力继续保留，但要上提为统一的会话后端抽象，避免 HUD 和聊天面板直接依赖单次请求式 `orchestrator`。
5. 当激活 `OpenClaw` provider 时，本地 `MateProactiveScheduler` 不再负责主动发言，主动消息由 `OpenClaw` 推送驱动。
6. `OpenClaw` 的设置、密钥、连接状态和诊断信息必须有独立 UI，不继续套用现有 `API URL + Model` 表单语义。

一句话结论：

> 这一轮不是“再加一个模型 API”，而是给 `VividSoul` 接入一条可常驻、可订阅、可接收主动消息的实时会话网关。

## 2. 背景与问题定义

当前 `VividSoul` 的聊天链路已经跑通：

- `DesktopPetRuntimeHud` 提供聊天入口与消息展示。
- `MateConversationOrchestrator` 负责读取本地 `AI settings`、拼接近期消息、调用 `ILlmProvider`、保存会话和统计。
- `OpenAiCompatibleLlmProvider` 与 `MiniMaxLlmProvider` 都是典型的同步请求-响应形态。

当前链路适合：

- 用户发一句，模型回一句。
- 本地维护最近若干轮聊天历史。
- 把单次回答同步送到聊天面板和头顶气泡。

当前链路不适合：

- 维持常驻网关会话。
- 接收远端主动触发的消息。
- 订阅远端会话事件流。
- 把“连接状态 / session 状态 / 未读主动消息”作为一等 UI 状态。

而 `OpenClaw` 的产品意义并不只是“另一个模型供应商”，更像是：

- 一个持久会话网关。
- 一个有 `session`、`event stream` 和 `proactive runtime` 的外部系统。
- 一个可在自身内部完成路由、推理、工具调用和主动发言的 agent runtime。

因此，`OpenClaw` 接入不能继续沿用“纯 HTTP provider”心智，而必须提升为独立的实时会话后端。

## 3. 目标与非目标

### 3.1 目标

- 在运行时 `LLM` 设置中提供独立的 `OpenClaw` provider。
- 基于 `OpenClaw Gateway WebSocket protocol` 建立常驻连接、鉴权、订阅和重连。
- 将 `VividSoul` 当前角色与 `OpenClaw` session 建立稳定绑定。
- 让用户输入消息通过 `Gateway session` 发送，而不是本地直接调用 HTTP completions。
- 让远端主动产生的消息可以实时进入：
  - 聊天历史
  - 头顶对白气泡
  - 未来 TTS 播放链路
- 在 UI 中展示连接状态、最近错误、当前 agent、当前 session、未读消息与同步状态。
- 在不推翻当前本地 provider 链路的前提下，引入统一的 `conversation backend` 抽象。

### 3.2 非目标

- 不做 `OpenClaw` 的 HTTP 兼容路径。
- 不做完整的 `OpenClaw` 工具调用可视化面板。
- 不做多 agent 工作流编辑器。
- 不做公网暴露场景下的权限系统重构。
- 不做 `OpenClaw` 插件开发或向上游贡献 provider。
- 不做把本地 `summary memory` 与远端 session memory 双向合并。

## 4. 关键产品决策

### 4.1 Provider 定位

`OpenClaw` 在产品上必须被用户识别为一个独立能力，而不是“你自己填写了一个兼容 OpenAI 的地址”。

因此设置页中必须体现：

- `OpenClaw` 是单独的 provider 类型。
- 它使用 `Gateway WS URL` 和 `Gateway Token`。
- 它强调 `Agent` / `Session` / `Connection` 概念，而不是 `Model API URL`。

### 4.2 主动发言归属

激活 `OpenClaw` 后：

- 主动发言节奏由 `OpenClaw` 负责。
- 客户端只负责接收和展示主动消息。
- 客户端本地的 `proactive scheduler` 进入停用状态，避免双重主动发言源。

### 4.3 事实源归属

激活 `OpenClaw` 后：

- 远端 `session transcript` 是事实源。
- 客户端本地 transcript 只作为缓存和 UI 镜像。
- 本地不再尝试对 `OpenClaw` 会话做独立摘要压缩，避免双重记忆层互相污染。

### 4.4 失败与降级策略

`OpenClaw` 不做 HTTP 降级。

失败处理仅允许：

- 显示连接失败或认证失败状态。
- 重试连接。
- 用户切回其它 provider。

不允许：

- 在后台偷偷退回 `OpenAI-compatible`。
- 以“当前地址像 OpenAI”名义悄悄改走另一套链路。

## 5. 当前实现与差距

当前已具备：

- `DesktopPetRuntimeController` 作为手写组合根，在 `Awake()` 内构造运行时服务。
- `DesktopPetRuntimeHud` 作为主 HUD、聊天面板、设置窗口和对白气泡的统一入口。
- `DesktopPetChatOverlayPresenter` 已支持消息历史、输入发送、Provider 摘要和请求中状态。
- `DesktopPetSettingsWindowPresenter` 已有 `LLM` tab、provider 切换、持久化和密钥保存能力。
- `AiSettingsStore` / `AiSecretsStore` / `ChatSessionStore` / `LlmUsageStatsStore` 已提供基础持久化能力。

当前主要差距：

- 没有“会话后端”抽象，HUD 仍默认对接单次请求式本地回答链路。
- 没有常驻连接管理器。
- 没有 `Gateway protocol` 帧模型与请求-事件路由层。
- 没有远端 session 与本地角色的绑定模型。
- 没有针对主动消息的 UI 生命周期设计。
- 现有 provider 设置模型仍偏向 `HTTP model provider`，无法表达 `Gateway agent / session` 语义。

## 6. 总体架构方案

### 6.1 顶层形态

建议把当前单一聊天链路改造成“双后端 + 单入口”结构：

- `MateConversationService`
- `IMateConversationBackend`
- `LocalLlmConversationBackend`
- `OpenClawConversationBackend`

职责分层：

- `DesktopPetRuntimeHud`
  - 继续做聊天面板、设置窗口、对白气泡总入口。
  - 不再直接关心“这次请求由谁完成”，只监听统一消息事件。
- `MateConversationService`
  - 作为唯一的会话编排入口。
  - 根据激活 provider 选择 backend。
  - 统一向 UI 暴露消息、状态、错误和未读变更。
- `LocalLlmConversationBackend`
  - 包装当前 `MateConversationOrchestrator`。
  - 继续服务 `OpenAI-compatible / MiniMax`。
- `OpenClawConversationBackend`
  - 接管连接、发消息、收事件、镜像 transcript 和主动消息分发。

### 6.2 组合根策略

本轮继续沿用 `DesktopPetRuntimeController` 的手写组合根，不引入正式 DI 容器。

原因：

- 当前工程已经采用 `Awake()` 手工创建服务的模式。
- 本轮重点是打通外部网关接入，不是同时做容器迁移。
- 保持组合根不变能显著降低接入复杂度和风险。

因此建议仍在 `DesktopPetRuntimeController` 中创建：

- `MateConversationService`
- `LocalLlmConversationBackend`
- `OpenClawConversationBackend`
- `OpenClawGatewayClient`
- 必要的 settings / secrets / stores

## 7. 模块划分与建议文件

建议在 `VividSoul/Assets/App/Runtime/AI/` 下新增如下目录与类型：

```text
AI/
  Backends/
    IMateConversationBackend.cs
    MateConversationService.cs
    LocalLlmConversationBackend.cs
  OpenClaw/
    OpenClawConversationBackend.cs
    OpenClawGatewayClient.cs
    OpenClawConnectionManager.cs
    OpenClawEventRouter.cs
    OpenClawMessageMapper.cs
    OpenClawConnectionState.cs
  OpenClaw/Protocol/
    OpenClawGatewayFrame.cs
    OpenClawGatewayRequest.cs
    OpenClawGatewayResponse.cs
    OpenClawGatewayEvent.cs
    OpenClawGatewayMethodNames.cs
  OpenClaw/Session/
    OpenClawSessionBindingService.cs
    OpenClawSessionBinding.cs
    OpenClawSessionKeyBuilder.cs
  OpenClaw/Storage/
    OpenClawTranscriptMirrorStore.cs
    OpenClawDiagnosticsStore.cs
  Settings/
    OpenClawProviderSettings.cs
    OpenClawProviderSettingsFile.cs
```

允许根据现有目录习惯略作调整，但必须保持职责边界清晰。

## 8. 统一会话后端抽象

### 8.1 新接口

建议新增：

- `IMateConversationBackend`
- `MateConversationService`

`IMateConversationBackend` 应至少覆盖以下语义：

- 初始化
- 激活角色上下文
- 用户发消息
- 连接或断开
- 读取当前连接状态
- 发出消息事件
- 发出错误事件

建议统一事件模型：

- `ConversationMessageReceived`
- `ConversationStateChanged`
- `ConversationErrorOccurred`
- `ConversationUnreadChanged`

### 8.2 为什么不复用 `ILlmProvider`

`ILlmProvider` 当前表达的是一次性生成接口，适合：

- 同步问答
- 单轮文本结果
- 本地 recent-turns prompt 拼接

它不适合表达：

- 长连接
- 会话订阅
- 主动推送
- 重连
- 未读状态
- 会话同步

因此本轮不应强行把 `OpenClaw` 塞进 `ILlmProvider`。

## 9. OpenClaw Provider 配置设计

### 9.1 配置模型

建议新增专用配置结构，而不是继续滥用现有 `LlmProviderProfile.BaseUrl / Model`：

- `GatewayWsUrl`
- `AgentId`
- `SessionMode`
- `CustomSessionKeyTemplate`
- `AutoConnectOnStartup`
- `AutoReconnect`
- `ReceiveProactiveMessages`
- `MirrorTranscriptLocally`
- `EnableBubbleForIncoming`
- `EnableTtsForIncoming`
- `ShowUnreadBadge`
- `QuietHoursEnabled`
- `QuietHoursStart`
- `QuietHoursEnd`
- `Enabled`

建议把凭据继续放在 `AiSecretsStore`，但为 `OpenClaw` 单独保留 key 名称和说明文案。

### 9.2 配置持久化迁移

当前 `AiSettingsStore` 使用 `LlmProviderProfile[]` 保存 provider 列表。

建议本轮升级为更通用的 `AI provider profile` 结构：

- 通用字段：
  - `Id`
  - `DisplayName`
  - `ProviderType`
  - `Enabled`
- `Http provider` 专属字段：
  - `BaseUrl`
  - `Model`
- `OpenClaw` 专属字段：
  - `GatewayWsUrl`
  - `AgentId`
  - `SessionMode`
  - 其它 runtime flags

迁移原则：

- 对已有 `OpenAI-compatible / MiniMax` 配置零损迁移。
- 新增 `OpenClaw` 配置时，不破坏旧 provider 读取逻辑。
- 若用户未配置 `OpenClaw`，当前行为完全不变。

## 10. Gateway WS 协议接入方案

### 10.1 连接流程

`OpenClawConversationBackend` 初始化流程建议固定为：

1. 读取当前 `OpenClaw` 配置与 token。
2. 建立 WebSocket 连接。
3. 完成 `connect.challenge` -> `connect` -> `hello-ok` 握手。
4. 记录当前连接状态和协商出的协议版本。
5. 解析当前角色对应的 session key。
6. 订阅：
   - `sessions.subscribe`
   - `sessions.messages.subscribe`
7. 拉取当前 session 的首屏历史或预览。
8. 进入常驻监听状态。

### 10.2 发送路径

用户输入消息时：

1. `DesktopPetChatOverlayPresenter` 发出提交事件。
2. `DesktopPetRuntimeHud` 调用 `MateConversationService.SendUserMessageAsync(text)`。
3. 当前 backend 若为 `OpenClawConversationBackend`，则发送 `sessions.send`。
4. UI 立即进入“发送中 / 等待远端回流”状态。
5. 最终消息以 `session.message` 或相关事件回流为准，不以本地同步返回为准。

### 10.3 接收路径

客户端需要优先处理以下事件族：

- `session.message`
- `chat`
- `sessions.changed`
- `presence`
- `heartbeat`
- `cron`

其中真正驱动 UI 聊天内容的首要事件是：

- `session.message`
- 必要时辅以 `chat` 事件补充 transcript-only 更新

### 10.4 事件标准化

必须把远端事件先转换成客户端统一消息模型，再进入 HUD。

建议统一字段：

- `MessageId`
- `SessionKey`
- `Role`
- `Text`
- `OccurredAt`
- `Source`
- `IsProactive`
- `ShouldBubble`
- `ShouldSpeak`

其中：

- 用户本人发出的消息回流也要标准化处理，用于最终一致性。
- 主动消息由 `Source != UserInput` 或由 event 上下文推断得出。

## 11. 角色与 Session 绑定设计

### 11.1 默认绑定策略

建议默认使用 `PerCharacter` 模式：

- 一个角色一个 `OpenClaw session`
- `sessionKey` 基于 `ModelFingerprintService` 的角色指纹生成

建议格式：

- `vividsoul:<characterFingerprint>`

如果当前角色为空，则不建立活跃 `OpenClaw` session。

### 11.2 其它模式

允许保留：

- `Global`
  - 全角色共用一条 session
- `Custom`
  - 用户手动指定 session key 模板

但第一轮产品默认值仍应为 `PerCharacter`。

### 11.3 会话切换行为

当用户切换角色时：

1. 计算新角色 session key。
2. 取消旧 session 订阅。
3. 切换到新 session 的订阅。
4. 拉取新 session 预览历史。
5. 清理 UI 的“发送中”状态。
6. 在聊天面板顶部刷新 `agent / session / status` 摘要。

## 12. 本地镜像与去重策略

### 12.1 镜像原则

`OpenClaw` 的 transcript 是事实源，本地只做镜像缓存。

镜像的目的：

- 首屏更快渲染最近消息。
- 断线重连前保留已见内容。
- 给聊天面板提供离线可见历史。
- 给调试和问题排查保留基础轨迹。

### 12.2 建议存储路径

- `persistentDataPath/ai/openclaw/transcripts/<sessionKey>.json`
- `persistentDataPath/ai/openclaw/bindings.json`
- `persistentDataPath/ai/openclaw/diagnostics/<date>.json`

### 12.3 去重策略

必须避免以下重复源：

- 本地乐观插入的用户消息与远端回流用户消息重复显示。
- 重连后补拉历史时与已有镜像重复。
- 同一事件通过多个 event family 重复投递。

建议优先使用以下键去重：

1. 远端 message id
2. session key + seq
3. session key + role + timestamp + text hash

## 13. 聊天与对白气泡集成

### 13.1 聊天面板

`DesktopPetChatOverlayPresenter` 需要从当前“请求驱动 UI”升级为“事件驱动 UI”：

- 支持显示连接状态。
- 支持显示 `OpenClaw` 的 `agent` 与 `session`。
- 支持未读消息角标。
- 支持主动消息进入历史。
- 支持在发送后等待远端回流，而不是直接把本地生成结果当最终回复。

建议面板头部新增：

- `Provider: OpenClaw`
- `Agent: <agentId>`
- `Session: <sessionKey>`
- `Status: Connected / Reconnecting / AuthFailed / Disconnected`

### 13.2 对白气泡

对白气泡仍沿用现有 `DesktopPetSpeechBubblePresenter`。

接线规则：

- 远端 assistant 消息默认进入聊天历史。
- 若当前不处于 `DND`、设置页强交互或文本输入强聚焦状态，则同步弹气泡。
- 主动消息与普通回复使用同一气泡播放通道，不引入第二套表现系统。

### 13.3 未读与提示

当聊天面板收起时：

- 若收到新 assistant 消息，则 launcher 显示未读角标。
- 若消息为主动消息，可额外显示短时 HUD 状态提示。

## 14. 主动消息与勿扰策略

### 14.1 主动消息来源

`OpenClaw` 主动消息可能来自：

- heartbeat 驱动
- cron / scheduled task
- wake / webhook 触发后的 agent 输出
- 远端 session 内持续运行后的后续消息

客户端不负责生成这些消息，只负责：

- 判断是否展示
- 判断是否播语音
- 判断是否计入未读

### 14.2 展示策略

建议分为三层：

- `History`
  - 所有进入当前 session 的 assistant 消息都写入聊天历史
- `Bubble`
  - 仅在未处于勿扰和强交互状态时显示
- `TTS`
  - 仅在用户显式开启且当前消息允许播报时触发

### 14.3 DND 策略

新增 `Do Not Disturb` 后，主动消息行为应变为：

- 继续写入聊天历史
- 不弹气泡
- 不播语音
- 保留未读角标

## 15. 连接状态机与错误处理

建议最少具备以下连接状态：

- `Disconnected`
- `Connecting`
- `Authenticating`
- `Subscribing`
- `Connected`
- `Reconnecting`
- `AuthFailed`
- `Faulted`

建议最少处理以下错误类别：

- URL 非法
- token 缺失
- token 认证失败
- WS 握手失败
- 订阅失败
- 会话切换失败
- 事件反序列化失败
- 连接中断

UI 规则：

- `AuthFailed` 必须给出明确用户文案，不自动盲重试。
- `Reconnecting` 允许自动重试，并在聊天面板显示状态。
- `Faulted` 时保留最近错误与最近成功连接时间。

## 16. 设置页方案

### 16.1 Provider 类型

`LlmProviderType` 新增：

- `OpenClaw`

设置页在该类型下必须切换到专用表单，不继续展示纯 `API URL / Model` 语义。

### 16.2 OpenClaw 专属表单

建议字段：

- `Gateway WS URL`
- `Gateway Token`
- `Agent ID`
- `Session Mode`
- `Custom Session Key Template`
- `Auto Connect`
- `Auto Reconnect`
- `Receive Proactive Messages`
- `Mirror Transcript Locally`
- `Bubble Incoming Messages`
- `TTS Incoming Messages`
- `Connect Test`
- `Reconnect`
- `Resubscribe`
- `Resync History`

### 16.3 状态与诊断

建议在设置页或聊天头部显示：

- 当前连接状态
- 最近一次成功连接时间
- 最近一次错误摘要
- 当前订阅 session
- 当前未读数
- 最近事件时间

## 17. 运行时接线改造

### 17.1 `DesktopPetRuntimeController`

需要从“直接持有 `MateConversationOrchestrator`”改为：

- 创建 `MateConversationService`
- 创建 `LocalLlmConversationBackend`
- 创建 `OpenClawConversationBackend`
- 监听角色切换并通知当前 backend 更新角色上下文

### 17.2 `DesktopPetRuntimeHud`

需要从“提交请求后等待单次异步结果”改为：

- 向 `MateConversationService` 发送用户消息
- 订阅统一消息事件
- 订阅连接状态事件
- 订阅错误事件
- 在 UI 生命周期中管理订阅与取消订阅

### 17.3 `DesktopPetChatOverlayPresenter`

需要增强：

- 连接状态显示
- agent / session 摘要
- 未读角标
- 新消息去重追加
- 发送中状态与回流完成状态分离

### 17.4 `DesktopPetSettingsWindowPresenter`

需要增强：

- `OpenClaw` provider 类型标签
- `OpenClaw` 专属字段布局
- secrets 保存和加载
- 连接测试按钮与状态回显

## 18. 实施阶段

### Phase 1：后端抽象重构

交付物：

- `IMateConversationBackend`
- `MateConversationService`
- `LocalLlmConversationBackend`
- HUD 改为依赖统一服务

完成标准：

- 当前 `OpenAI-compatible / MiniMax` 行为不回退
- 聊天面板和气泡仍能正常显示本地 provider 回复

### Phase 2：OpenClaw 配置与设置页

交付物：

- `OpenClaw` provider 类型
- settings/secrets 迁移
- 设置页专属表单
- 基础连接状态展示

完成标准：

- 用户可保存 `Gateway WS URL`、`Agent ID`、token 和运行标志
- 不影响旧 provider 的保存与加载

### Phase 3：Gateway WS 客户端

交付物：

- `OpenClawGatewayClient`
- 握手与鉴权
- 请求/响应/事件基础模型
- 自动重连框架

完成标准：

- 能连接受控 `OpenClaw gateway`
- 能完成 `connect` 并维持稳定长连

### Phase 4：Session 绑定与消息收发

交付物：

- session key 生成
- session subscribe / unsubscribe
- 用户消息发送
- 历史预览同步
- transcript 镜像与去重

完成标准：

- 用户在聊天面板发消息后，消息可通过 session 回流看到
- 切换角色后能切换到对应 session

### Phase 5：主动消息与 UI 完整集成

交付物：

- 主动消息进入聊天历史
- launcher 未读角标
- 对白气泡联动
- `DND` / 强交互状态下的展示策略

完成标准：

- 远端主动消息能稳定进入 UI
- 不会和本地主动调度器冲突

### Phase 6：稳定性与诊断

交付物：

- 最近错误记录
- 连接测试
- 重连策略
- 手动重同步入口
- 基础诊断日志

完成标准：

- 常见错误可定位
- 断线、认证失败、订阅失败时用户能理解当前状态

## 19. 验收标准

本方案完成后，至少应满足以下结果：

1. 设置页可明确配置并启用 `OpenClaw`。
2. 激活 `OpenClaw` 后，聊天面板通过 WS 会话发送消息并接收回复。
3. 切换 VRM 角色后，客户端会切换到对应的 `OpenClaw session`。
4. `OpenClaw` 远端主动消息可以进入聊天历史。
5. 主动消息在允许时会显示头顶气泡，在 `DND` 下只保留未读。
6. 当前本地 `OpenAI-compatible / MiniMax` 功能仍可用。
7. 用户可在 UI 中看到连接状态、最近错误和当前 session 摘要。

## 20. 风险与缓解

### 20.1 协议复杂度高于普通 HTTP provider

风险：

- WS 协议、事件模型和 session 语义明显比当前本地 provider 复杂。

缓解：

- 先引入统一 backend 抽象，再单独实现 `OpenClawConversationBackend`，避免污染旧链路。

### 20.2 会话事实源双写

风险：

- 本地 mirror 与远端 transcript 容易出现重复、错序、双重 summary。

缓解：

- 明确远端为事实源，本地只做镜像，不做二次记忆编排。

### 20.3 主动消息打扰感

风险：

- `OpenClaw` 可能比本地 scheduler 更积极地发消息。

缓解：

- 客户端必须有 `DND`、气泡开关、TTS 开关和未读策略。

### 20.4 凭据权限过高

风险：

- `Gateway token` 接近 operator credential，不适合裸暴露公网。

缓解：

- 默认仅支持受控私网入口。
- 在设置页明确安全提示。
- secrets 独立存储，不进入普通 settings 文件。

## 21. 明确延期项

以下能力明确延期到本计划之后：

- `OpenClaw` 工具调用时间线可视化
- 多 agent 管理与切换工作台
- 结构化动作 / 表情 / 口型控制协议
- 与本地 `summary memory` 的双向合并
- 远端配置探测与自动建档
- 细粒度权限或公网多租户方案

## 22. 最终建议

对当前 `VividSoul`，`OpenClaw` 最稳妥的落地方式不是“再接一个 HTTP provider”，而是：

1. 先把现有聊天链路升级为统一 `conversation backend` 架构。
2. 将 `OpenClaw` 作为独立的 `WS-only realtime provider` 接入。
3. 让远端 session、memory 和 proactive 成为事实源。
4. 客户端专注于：
   - 连接
   - 会话绑定
   - UI 渲染
   - 气泡 / 未读 / TTS 策略
   - 状态与诊断

这样做的好处是：

- 产品定位清晰。
- 能真正吃到 `OpenClaw` 的主动消息能力。
- 不会把现有 `OpenAI-compatible` 抽象硬扭成不适合的实时网关模型。
- 后续若继续扩展其它实时后端，也能复用同一套 backend 架构。
