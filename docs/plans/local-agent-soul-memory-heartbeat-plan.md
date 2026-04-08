# VividSoul 本地 Agent `Soul / Memory / Heartbeat` 计划

## 文档信息

- 项目：`VividSoul`
- 范围：纯本地 `LLM provider` 路径下的 `soul`、长期 `memory`、持久化 `heartbeat/reminder`、隐藏式 `session/thread` 管理
- 明确不包含：`OpenClaw` / 后端实时 gateway 语义
- 关联文档：`../STATUS.md`、`../architecture.md`、`./llm-tts-integration-plan.md`、`./desktop-pet-runtime-ui-restructure-plan.md`
- 日期：`2026-04-07`
- 状态：`draft`

## 1. 决策摘要

本计划先固定以下方向，作为后续实现与评审基线：

1. 当前讨论范围只针对 `OpenAI-compatible`、`MiniMax` 这类纯本地 `LLM provider` 路径，不把 `OpenClaw` 或其它后端 runtime 语义混入本轮设计。
2. `Soul` 只承载三类内容：
   - 当前模型角色本身的设定
   - 用户长期互动习惯
   - 当前角色与当前用户之间的关系状态
3. `Soul` 不承载通用 agent 运行规则，不把类似 `AGENTS.md` 的全局操作守则塞进 `soul`。
4. 本计划中的 `heartbeat` 不是“闲聊型主动搭话调度器”，而是可靠的本地计时器 / 提醒系统：
   - 用户说“1 小时后提醒我……”
   - 本地 runtime 必须持久化并在到点后真正提醒
   - 即使中途重启，重新进入应用后也必须补触发
5. `Memory` 是本轮核心能力，必须成为独立系统，而不是仅靠最近若干轮对话硬塞进 prompt。
6. 正常用户路径不暴露“切 session / 开新话题 / 清空短期上下文”这类开发者式操作，相关维护能力只放在设置或诊断页。
7. 为降低模型漂移风险，不要求单轮聊天同时输出大而全的结构化 `JSON`。用户可见回复继续保持纯文本，结构化理解拆成多个专用 `LLM judge / summarizer pass`，每个 pass 只返回极小、扁平的结构。
8. 默认由 `LLM` 负责理解自然语言并判断“这句话到底是什么意思”；本地程序只负责确定性约束，例如 schema 校验、覆盖优先级、reminder 状态机、绝对时间合法性与持久化。
9. 持久化采用“`Markdown` 为主、`JSON/JSONL` 为辅”的策略：
   - 可读、可审阅、可人工修订的长期资料优先落 `Markdown`
   - 对可靠执行要求高的索引、状态、提醒、原始 transcript 落 `JSON/JSONL`

一句话结论：

> `VividSoul` 的本地智能层应当从“最近几轮聊天 + provider 调用”升级为“角色化 `Soul` + 分层 `Memory` + 可恢复 `Reminder Scheduler`”的轻量本地 agent runtime。

## 2. 背景与问题定义

当前本地链路大致是：

- `LocalLlmConversationBackend`
- `MateConversationOrchestrator`
- `ChatSessionStore`
- `OpenAI-compatible / MiniMax provider`

当前已具备：

- 每个角色一条基础本地聊天 session
- 最近若干轮消息持久化
- 全局 `system prompt`
- 基于当前角色名的最小 persona 注入

当前明显缺失：

- 没有“这个模型角色是谁”的稳定人格锚点
- 没有“这个用户平时怎么和我互动”的长期习惯沉淀
- 没有“这个角色和这个用户已经相处到什么状态”的关系层
- 没有 thread 级 compaction 与长期 memory 检索
- 没有可靠的提醒 / 定时器系统
- 没有跨重启恢复的 reminder queue

当前最大风险也很明确：

- 如果把所有需求都堆进一次 LLM 输出，要求它返回大段嵌套 `JSON`，很容易导致模型漂移、字段缺失、错误落盘。
- 如果让用户频繁显式管理 session，又会明显破坏桌宠沉浸感，让产品越来越像调试台。

## 3. 目标与非目标

### 3.1 目标

- 让不同导入模型真正拥有独立、可感知的角色设定，而不是只换皮不换魂。
- 让系统持续学习用户互动习惯，并跨会话保持稳定体验。
- 让角色与用户之间的关系感能够逐步积累，而不是每次重聊都像初见。
- 让对话能够自动 compact，而不要求用户手动“重新开话题”。
- 让“1 小时后提醒我做什么”这类请求变成可靠的本地提醒能力。
- 让 reminder 在应用重启、休眠恢复之后仍然能恢复与补触发。
- 保持主聊天回路稳健，避免因结构化输出失败而影响用户可见回复。

### 3.2 非目标

- 不把本轮设计扩展为后端驱动的 `Soul Backend` 全量替代方案。
- 不引入 `OpenClaw` 风格的实时 agent workspace / external runtime 语义。
- 第一版不做复杂向量数据库或外部 memory service 依赖。
- 第一版不做真正的系统级后台守护进程或原生 OS 提醒中心集成。
- 第一版不在主聊天 UI 暴露“切 session / 新建话题 / 清空上下文”等入口。
- 第一版不让 LLM 自由定义任意结构化动作协议，只处理聊天、memory、reminder 三类核心问题。

## 4. 设计原则

### 4.1 `LLM-first` 语义判断，`Markdown/JSON` 极简落盘

- 自然语言理解优先交给专用 `LLM judge`
- 不用大量规则、正则或句式模板去硬判复杂语义
- 长期可读知识优先用 `Markdown`
- 可靠执行状态优先用 `JSON/JSONL`
- 不设计大而全、层级很深的统一结构
- 所有结构化 `judge / summarizer` 都必须是“单任务、扁平、少字段、可重试”的

### 4.2 用户只看见连续关系，不看见底层 thread

- 正常体验中，用户只会感知到“这个角色一直记得我”
- 底层 thread 切换、compact、索引更新全部自动发生
- 只有在设置 / 诊断页里才允许手动重置或修复

### 4.3 `Soul` 与 `Memory` 分离，但强关联

- `Soul` 解决“我是谁、我们怎么相处”
- `Memory` 解决“我记得什么”
- `Soul` 不是 memory 的替代品，也不是通用规则仓库

### 4.4 提醒可靠性高于人格化措辞

- reminder 的核心是“到点触发”
- 人格化提醒文案只是展示层加分项
- 即使 LLM 不可用，也要能用本地模板完成提醒

### 4.5 热路径只做关键写入，重处理放后台

吸收当前主流 agent memory 实践后，本计划固定：

- 是否该记、记什么、属于显式修正还是推断候选，默认由专用 `LLM memory judge` 决定
- 本地程序不负责替代 `LLM` 做语义判断，只负责写入约束和状态迁移
- 热路径写入：
  - reminder 创建 / 取消
  - `LLM memory judge` 输出的 `explicit_override` / `explicit_preference`
  - 当前线程消息流水
  - 明确的未完成承诺
- 后台写入：
  - compact summary
  - `inferred_habit_candidate` 的候选汇总与晋升
  - habits / bond 汇总
  - 索引整理

### 4.6 命名空间必须显式隔离

吸收分层 memory 的常见实践后，本地 memory 至少区分：

- `user` 级长期信息
- `character` 级角色与关系信息
- `thread` 级短期上下文
- `reminder` 级可执行承诺

防止“角色人格”“用户偏好”“临时话题”混写到同一份上下文里。

## 5. 推荐目标架构

建议在当前本地 provider 链路上增加一层专门的本地 agent runtime：

```text
DesktopPetRuntimeHud
  -> LocalLlmConversationBackend
     -> LocalAgentRuntimeService
        -> SoulPromptAssembler
        -> MemoryOrchestrator
        -> ReminderScheduler
        -> ReminderIntentJudge
        -> MemoryJudge
        -> CompactSummarizer
        -> MateConversationOrchestrator
           -> ILlmProvider
```

职责分层建议：

- `LocalLlmConversationBackend`
  - 保持为 UI 与本地 agent runtime 的桥接层
  - 接收用户消息
  - 向 UI 分发回复、状态和 reminder 触发事件
- `LocalAgentRuntimeService`
  - 负责整体 turn pipeline
  - 负责 `soul` / `memory` / reminder 的协调
  - 负责隐藏式 thread 管理
- `ReminderIntentJudge`
  - 专用 `LLM` pass
  - 负责理解 reminder 相关自然语言，不负责实际调度
- `MemoryJudge`
  - 专用 `LLM` pass
  - 负责判断一轮对话里哪些信息值得沉淀，以及属于何种 memory 语义
- `MateConversationOrchestrator`
  - 收敛为“prompt 构造 + provider 调用 + 原始 transcript 追加”的下层编排器
  - 不再独自承担完整 memory 逻辑
- `ReminderScheduler`
  - 负责本地持久化 reminder 的装载、轮询、触发和恢复

## 6. `Soul` 设计

### 6.1 `Soul` 的组成

`Soul` 固定拆成四层：

1. `Role`
   - 当前模型角色本身的设定
   - 例如名字、语气、说话节奏、边界、气质、常见动作倾向
2. `Habits`
   - 用户长期互动习惯
   - 例如用户喜欢简洁回复、不喜欢被频繁打断、经常在晚上聊天
3. `Bond`
   - 当前角色与当前用户之间的关系感
   - 例如熟悉度、近期共同话题、最近一次承诺、当前相处氛围
4. `SoulState`
   - 运行时状态
   - 例如当前 mood、上次说话时间、上次活跃时间、最近一次提醒是否已送达

### 6.2 持久化建议

```text
persistentDataPath/ai/local-agent/
  user/
    habits.md
    user-facts.md
    user-state.json
  characters/<characterFingerprint>/
    role.md
    bond.md
    soul-state.json
    memory/
      facts.md
      thread-index.json
      threads/
        <threadId>.jsonl
      compact/
        <compactId>.md
      compact-index.json
  reminders/
    index.json
    items/
      <reminderId>.json
```

说明：

- `role.md`
  - 当前模型角色的“人格锚点”
  - 初次导入时可生成模板，后续允许用户或系统细修
- `habits.md`
  - 用户全局互动习惯
  - 不随模型切换而丢失
- `bond.md`
  - 该模型与该用户的专属关系摘要
- `facts.md` / `user-facts.md`
  - 相对稳定的长期事实记忆

### 6.3 文件格式建议

`Markdown` 文件建议允许一个极小的 YAML frontmatter，但正文保持普通 `Markdown`：

```md
---
schema: vivid-soul-role-v1
displayName: "Nia"
defaultMood: "gentle"
proactivity: "low"
updatedAt: "2026-04-07T12:00:00+08:00"
---

## Identity

- 一个安静、细腻、反应柔和的桌面陪伴角色

## Speech Style

- 中文为主
- 偏口语，不喜欢长篇说明

## Boundaries

- 不强行打断用户
- 不连续重复问候

## Embodiment

- 更适合轻动作、微表情、轻提醒
```

固定要求：

- frontmatter 字段必须很少，只承载几个稳定键
- 复杂说明一律写在正文
- 不把大量嵌套结构塞进 frontmatter

### 6.4 `Soul` 更新策略

- `role.md`
  - 默认人工主导
  - 可由后续“角色设定优化器”辅助生成草稿
  - 不在每轮聊天热路径里频繁重写
- `habits.md`
  - 可在后台周期性更新
  - 只沉淀稳定习惯，不写一时情绪
- `bond.md`
  - 允许根据近期互动逐步更新
  - 作为角色化长期关系记忆的主要落点
- `soul-state.json`
  - 运行时频繁更新
  - 只放机器态，不放大段自然语言

## 7. `Memory` 设计

### 7.1 分层模型

本地 memory 固定分成五层：

1. `Recent Turns`
   - 当前活跃 thread 的最近若干轮消息
   - 直接进入 prompt
2. `Thread Transcript`
   - 原始消息流水
   - 以 `JSONL` 记录，作为事实源
3. `Compact Summary`
   - 对老消息块做摘要压缩
   - 以 `Markdown` 形式存档
4. `Long-term Facts`
   - 用户事实、偏好、禁忌、长期项目、角色关系状态
   - 以 `Markdown` 维护
5. `Open Commitments`
   - 已承诺但尚未完成的事项
   - 例如 reminder、答应下次继续的话题

### 7.2 thread / session 策略

正常用户不直接管理 session。系统内部维护隐藏式 thread：

- 一个角色对应一个当前活跃 thread
- thread 太长时自动 compact
- 长时间无对话超过时间窗口时，在下一次用户 turn 自动开新 thread
- 手动重置入口只放到设置或诊断页

建议触发 thread 轮换的条件：

- `compact` 触发：
  - 消息轮数超过阈值
  - 命中 `SummaryThreshold`
- `thread rollover` 触发：
  - 距离上一次用户消息超过时间窗口
- 用户在设置页手动重置

第一版不把“粗估 token”作为硬触发条件，避免在实现早期把 thread 切换与 token 估算误差绑死。

### 7.3 为什么不把 session 暴露给用户

- “开始新话题”“清空短期上下文”会破坏连续陪伴感
- 桌宠不是聊天工作台，正常用户不该理解底层 thread 概念
- 真正需要维护时，放在设置里更合理

### 7.4 检索策略

第一版不直接上复杂向量库，先固定一套分层检索：

- 始终注入：
  - `role.md`
  - `habits.md`
  - `bond.md`
  - `open commitments`
- 高频注入：
  - `Recent Turns`
- 条件检索：
  - 最近 1-2 份相关 `Compact Summary`
  - 当前角色级 / 用户级 `facts.md` 中与当前话题标签相关的条目

第一版相关性来源建议：

- 最近时间
- 关键词 / 标签重叠
- 当前 thread 引用链路

后续如果确实有必要，再追加 embedding 检索，而不是一开始就引入复杂依赖。

### 7.5 写入策略

#### 热路径写入

语义判断方式固定为：

- 默认由 `MemoryJudge` 判断当前 turn 是否包含值得写入的 memory
- `MemoryJudge` 负责区分：
  - `explicit_override`
  - `explicit_preference`
  - `inferred_habit_candidate`
  - `stable_fact`
  - `open_commitment`
  - `no_write`
- 本地程序只根据 `judge` 输出执行写入策略，不自行用传统规则替代 NLP 理解

以下内容允许在用户 turn 后立即写入：

- 用户显式要求创建 / 取消 reminder
- `MemoryJudge` 输出的 `explicit_override`
- `MemoryJudge` 输出的 `explicit_preference`
- `MemoryJudge` 输出的高置信度 `stable_fact`
- 当前 turn 的 transcript
- `MemoryJudge` 输出的 `open_commitment`

#### 后台写入

以下内容建议异步执行：

- 老对话 compact
- `MemoryJudge` 输出的 `inferred_habit_candidate` 汇总
- `habits.md` 汇总
- `bond.md` 汇总
- `facts.md` 归并与去重
- 索引刷新

冲突优先级固定为：

1. `explicit_override`
2. `explicit_preference`
3. `stable_fact`
4. `inferred_habit_candidate`

也就是说，用户显式修正永远可以覆盖过去的推断性习惯，而推断性习惯不能反向压过用户明确表达。

这样可以吸收“`LLM` 负责语义判断”和“程序负责确定性约束”的组合优势，同时避免主聊天延迟过高。

### 7.6 compact 设计

当 thread 达到阈值时：

1. 选取较老的一段 transcript
2. 生成固定模板的 compact `Markdown`
3. 写入 `compact/<compactId>.md`
4. 更新 `compact-index.json`
5. 主 thread 只保留最近窗口

compact 文档建议模板：

```md
# Compact Summary

## Time Range

- 2026-04-01 ~ 2026-04-07

## Ongoing Topics

- 用户正在准备周三汇报
- 最近频繁讨论桌宠 UI 和 memory 设计

## Stable Facts

- 用户偏好简短直接的回复

## Bond Notes

- 当前角色与用户已形成“偏技术共创”的互动气氛

## Open Commitments

- 下次继续讨论 reminder 触发后的 UI 表现
```

compact 的目标不是可执行结构，而是给后续 prompt 提供可读、可引用的高密度上下文。

### 7.7 事实记忆的推荐形态

长期事实优先采用“小文档集合 + 轻索引”，而不是一个不断膨胀的大 profile `JSON`。

原因：

- 一个大 profile 越长越容易更新出错
- 小文档更容易追加、修订和审阅
- 更符合“`Markdown` 易读、易记录”的目标

因此本计划推荐：

- `user-facts.md`
- `characters/<fp>/memory/facts.md`
- `bond.md`
- `compact/*.md`

再由轻量 `index.json` 提供检索辅助元数据，而不是把所有长期知识全塞进一个大对象。

## 8. `Heartbeat / Reminder` 设计

### 8.1 定义

本计划明确：

- `heartbeat` = 本地持久化提醒 / 计时器系统
- 不等于“定时闲聊”
- 闲聊式主动消息以后可另做 `ProactiveScheduler`
- 当前不要把二者混为一谈

### 8.2 reminder 创建流程

建议采用“专用 reminder judge + 普通文本回复”的双通路：

1. 用户发送消息
2. `ReminderIntentJudge` 判断是否存在 reminder 意图
3. 若存在且时间明确：
   - 生成一个极小 reminder 结构
   - 立即持久化
4. 再生成普通文本回复
5. 若时间不明确：
   - 不落盘
   - 聊天回复里追问澄清

这样可以避免把 reminder 创建绑定到主回复文本的解析上，同时避免让主聊天 pass 承担 reminder 解析责任。

### 8.3 为什么不用“主回复顺便带大 JSON”

- 用户可见回复应该稳定输出自然语言
- reminder 是可靠执行能力，不适合依赖长回复里的附带结构
- reminder 只需要少量字段，值得用单独 `LLM judge` 提高可靠性

### 8.4 reminder 数据模型

提醒对象建议保持扁平：

```json
{
  "id": "rem_20260407_001",
  "title": "交电费",
  "note": "提醒用户交电费",
  "dueAtUtc": "2026-04-07T13:00:00Z",
  "timezone": "Asia/Shanghai",
  "status": "pending",
  "createdAtUtc": "2026-04-07T12:00:00Z",
  "updatedAtUtc": "2026-04-07T12:00:00Z",
  "characterFingerprint": "sha256-...",
  "threadId": "thread_...",
  "deliveredAtUtc": "",
  "acknowledgedAtUtc": ""
}
```

字段要求：

- 必须用绝对 `UTC` 作为执行事实源
- 可保留原时区用于展示
- 保留 `characterFingerprint` 只作为提醒文案的角色化提示，不把 reminder 逻辑强绑定到某个模型必须在线

### 8.5 调度器实现策略

`ReminderScheduler` 建议作为本地常驻服务：

- Unity 运行时每 `1s` 或 `2s` 轮询一次
- 使用 `DateTimeOffset.UtcNow` 与 `dueAtUtc` 比较
- 不是依赖一条单纯的内存倒计时链
- 启动时和应用恢复焦点时都要全量补扫一次 `pending` reminder

关键结论：

- 引擎计时器负责“调度循环频率”
- 绝对时间戳负责“提醒事实源”

这两者要分开，否则跨重启恢复会不稳。

### 8.6 触发与补触发

到点时：

1. 把 reminder 标记为 `firing`
2. 分发 reminder 事件到聊天 / HUD
3. 使用本地模板生成保底提醒文案
4. 若当前 provider 可用，再可选生成更角色化的一句提醒
5. 成功后标记为 `delivered`

应用重启或休眠恢复后：

- 对所有 `pending` 且 `dueAtUtc <= now` 的 reminder 立即补触发
- 避免“因为应用中途退出而永远漏提醒”

### 8.7 提醒展示策略

第一版建议：

- 聊天历史里追加一条 reminder 消息
- HUD 状态消息弹出一次
- 可选显示对白气泡

优先级建议：

- 保底：状态消息 + 聊天历史
- 增强：角色化对白气泡

### 8.8 第一版延期项

- 周期性重复提醒
- 原生系统通知中心
- 应用完全退出时的 OS 级准点唤醒
- 复杂的 reminder 自然语言编辑

## 9. 模型输出与抽取策略

### 9.1 总原则

- 用户可见回复：独立主回复 pass，继续输出纯文本
- reminder：独立 `ReminderIntentJudge`
- memory 写入：独立 `MemoryJudge`
- compact：独立 `CompactSummarizer`

不再追求“一次调用包打天下”。

本节明确：

- 复杂自然语言语义分类默认交给 `LLM`
- 本地程序不尝试用大量传统逻辑去替代 `LLM` 的理解能力
- 本地程序只负责：
  - schema 校验
  - 写入优先级
  - reminder 状态机
  - 时间合法性
  - 幂等与去重
  - 持久化

### 9.2 `ReminderIntentJudge`

`ReminderIntentJudge` 是一个专用 `LLM` pass，负责判断：

- create reminder
- cancel reminder
- complete reminder
- needs clarification
- no reminder intent

schema 要求：

- 扁平
- 低字段数
- 单一职责
- 最多返回少量操作

建议最小字段：

- `operation`
- `title`
- `dueAtUtc`
- `sourceText`
- `confidence`

其中：

- 如果时间不明确，返回 `needs_clarification`
- 若 `confidence` 过低，本地程序不落盘，只让主回复继续追问

### 9.3 `MemoryJudge`

`MemoryJudge` 是一个专用 `LLM` pass，负责从一次对话里判断高价值 memory 候选，并识别其语义类型：

- 用户偏好
- 用户事实
- 当前长期项目
- 角色关系变化
- 开放承诺
- 显式修正
- 推断性习惯候选

建议输出结构只包含最少字段：

- `memoryType`
- `scope`
- `text`
- `priority`
- `replaces`
- `sourceThreadId`
- `confidence`

并限制：

- 单次最多若干条
- 低置信度则不直接晋升为稳定 memory
- `explicit_override` 必须允许覆盖旧的推断性习惯
- `inferred_habit_candidate` 先进入候选池，不直接改写稳定偏好

推荐 `memoryType` 最小集合：

- `explicit_override`
- `explicit_preference`
- `stable_fact`
- `inferred_habit_candidate`
- `open_commitment`
- `bond_update`
- `no_write`

### 9.4 judge / parse 失败处理

结构化抽取失败时：

- 不影响用户可见回复
- reminder 不落盘
- memory 写入可跳过
- 记录轻量诊断日志，便于后续修正 prompt

这条原则非常关键：memory / reminder 不能把主聊天链路拖死。

### 9.5 为什么不使用“传统逻辑优先”

本计划明确放弃“正则 / 句式模板优先判断复杂语义”的主路线，原因是：

- 用户纠正偏好、修正事实、表达长期习惯时，本质上是复杂自然语言理解问题
- 如果让程序自己硬判，会很快退化成大量脆弱规则
- `Memory` 的质量上限主要取决于语义判断质量，而不是规则覆盖率

因此本地逻辑只做以下确定性边界：

- reminder 时间必须能落成合法绝对时间
- reminder 状态迁移必须走有限状态机
- `explicit_override` 的优先级高于一切推断型候选
- 持久化格式必须合法
- 同一 reminder 或 memory 写入需要幂等与去重

## 10. 与现有代码的衔接建议

### 10.1 `LocalLlmConversationBackend`

从当前“直接调用 `MateConversationOrchestrator.GenerateReplyAsync(...)`”升级为：

- 把用户消息交给 `LocalAgentRuntimeService`
- 接收文本回复
- 接收 reminder 触发事件
- 接收 memory 写入完成状态

### 10.2 `MateConversationOrchestrator`

建议逐步收缩职责，只保留：

- provider profile 解析
- provider 调用
- transcript 追加
- 最近窗口消息拼接

新增职责不要继续堆在它身上：

- reminder 管理
- compact 管理
- `soul` 文档读写

### 10.3 新增模块建议

建议在 `VividSoul/Assets/App/Runtime/AI/` 下新增：

```text
AI/
  LocalAgent/
    LocalAgentRuntimeService.cs
    Soul/
      SoulPromptAssembler.cs
      SoulProfileStore.cs
      SoulStateStore.cs
    Memory/
      MemoryOrchestrator.cs
      ThreadStore.cs
      CompactStore.cs
      MemoryJudge.cs
      MemoryRetriever.cs
      CompactSummarizer.cs
    Reminders/
      ReminderScheduler.cs
      ReminderStore.cs
      ReminderIntentJudge.cs
      ReminderRecord.cs
```

允许目录细节按现有习惯微调，但必须保持三大边界：

- `Soul`
- `Memory`
- `Reminder`

### 10.4 设置项建议

第一版建议在设置里增加或收敛：

- `MemoryWindowTurns`
- `SummaryThreshold`
- `EnableReminderDiagnostics`
- `ReminderScanIntervalSeconds`
- `Reset Current Character Memory`
- `Rebuild Current Character Compact`
- `View Pending Reminders`

已有的 `EnableProactiveMessages` 不应直接复用为 reminder 开关，因为它们不是同一能力。

## 11. UI 与用户体验建议

### 11.1 正常主路径

主聊天体验里：

- 不出现 session 管理按钮
- 不出现“开始新话题”快捷按钮
- 不出现“清空上下文”快捷按钮

### 11.2 设置 / 诊断页

如果确实需要修复或清理，放在设置的高级区：

- 查看当前角色 memory 摘要
- 重建 compact
- 清除当前角色 memory
- 查看 pending reminders
- 取消某个 reminder

### 11.3 reminder 交互

第一版建议 reminder 被触发后：

- 允许用户“知道了”
- 允许用户“稍后提醒”
- 取消 / 完成操作先走简单按钮或后续聊天命令

## 12. 分阶段实施计划

### Phase 1：隐藏 thread 与 compact 骨架

交付物：

- `thread-index.json`
- `threads/*.jsonl`
- `compact/*.md`
- 自动 compact 触发条件

完成标准：

- 长对话不再无限堆积到单一 recent turns
- 用户无感知地保持连续聊天体验

### Phase 2：`Soul` 文档与 prompt 组装

交付物：

- `role.md`
- `habits.md`
- `bond.md`
- `SoulPromptAssembler`

完成标准：

- 当前角色的人设、用户习惯、关系摘要能稳定进入 prompt
- 模型切换后角色感明显变化，而用户习惯仍可继承

### Phase 3：memory 写入与检索

交付物：

- `MemoryJudge`
- `facts.md` / `user-facts.md`
- `MemoryRetriever`

完成标准：

- 系统能记住用户偏好、稳定事实、近期承诺
- prompt 不再只依赖 recent turns

### Phase 4：reminder 创建与调度

交付物：

- `ReminderIntentJudge`
- `ReminderStore`
- `ReminderScheduler`
- 启动补触发逻辑

完成标准：

- “一小时后提醒我……”可创建成功
- 到点时能触发
- 重启后能恢复并补提醒

### Phase 5：设置与诊断页

交付物：

- memory 维护入口
- reminder 列表与取消入口
- compact 重建入口

完成标准：

- 正常用户主体验不被打断
- 出问题时用户仍有自助修复能力

## 13. E2E 场景用例

以下场景应作为后续本地 provider 路径的主要验收样例。它们既可以用于人工回归，也可以作为未来 Play Mode / 集成测试脚本的目标行为。

### E2E-01：角色自我介绍必须带模型角色感

前置：

- 当前角色已加载
- `role.md` 明确写有角色名字、说话风格和自我定位
- 当前 provider 为纯本地路径，例如 `MiniMax`

步骤：

1. 用户发送“你是谁？”
2. 等待聊天回复与对白气泡渲染完成

期望：

- 回复以当前模型角色的口吻回答，而不是通用的“我是 AI 助手 / 语言模型”
- 回复内容能体现 `role.md` 中的人设特征
- 聊天面板文本与气泡文本风格一致
- 不出现明显脱离角色的 provider 自报家门内容，除非 `role.md` 明确允许

### E2E-02：切换模型后角色感必须隔离

前置：

- 已准备两个不同风格的模型角色
- 两个角色拥有不同的 `role.md`

步骤：

1. 加载角色 A，发送“你是谁？”
2. 切换到角色 B，再发送“你是谁？”

期望：

- 两次回复在语气、措辞、自我描述上有明显差异
- 角色 B 的回复不继承角色 A 的人格措辞
- 角色切换后仍保留用户层面的习惯信息，但不串用另一角色的 bond 描述

### E2E-03：用户互动习惯必须被学到并跨重启保留

前置：

- 当前角色已加载
- `habits.md` 初始为空或不含目标偏好

步骤：

1. 用户发送“以后叫我阿布，回答尽量短一点。”
2. 完成一次正常回复
3. 关闭应用并重新启动
4. 用户发送“晚安前跟我说一句。”

期望：

- 系统能把“称呼我阿布”“回复短一点”沉淀为用户习惯
- 重启后回复仍沿用“阿布”这一称呼
- 重启后回复长度明显更简洁
- `habits.md` 或相应用户事实文档中可见稳定沉淀后的内容

### E2E-04：关系状态要能延续，而不是每次像初见

前置：

- 用户已与当前角色发生过多轮互动
- `bond.md` 已形成初步关系摘要

步骤：

1. 用户在前一次会话中提到“我最近一直在做桌宠的 memory 系统”
2. 经过一段时间或重启后，用户发送“我们最近主要在折腾什么？”

期望：

- 回复能提到近期共同话题，而不是完全失忆
- 回复带有“我们一直在聊”的连续关系感
- 回忆内容主要来自 compact / bond / facts，而不是只靠 recent turns

### E2E-05：长对话后仍能自动 compact 且维持记忆

前置：

- 将 `MemoryWindowTurns` 和 `SummaryThreshold` 配成较低值，便于触发

步骤：

1. 连续进行足够多轮对话，超过 recent turns 阈值
2. 期间注入一个稳定偏好，例如“我喜欢简短回答”
3. 继续若干轮后再次问“我喜欢什么风格的回答？”

期望：

- 系统自动产生 compact 文档，而不是无限追加到单一 thread
- 用户不需要手动点击“开始新话题”或“清空上下文”
- 回复仍能正确回忆“喜欢简短回答”
- `threads/*.jsonl`、`compact/*.md` 和索引文件按预期更新

### E2E-06：明确的 reminder 请求必须真正落盘

前置：

- 本地 provider 路径工作正常
- `ReminderScheduler` 已启动

步骤：

1. 用户发送“1 小时后提醒我开会”
2. 等待本轮对话完成

期望：

- 回复明确确认 reminder 已创建
- reminder 被写入 `reminders/items/<id>.json`
- `reminders/index.json` 出现对应 pending 项
- reminder 不是只存在于聊天文本里，而是成为独立可执行记录

### E2E-07：reminder 到点必须触发

前置：

- 已存在一个 `pending` reminder，且 `dueAtUtc` 即将到期

步骤：

1. 保持应用运行直到 reminder 到点

期望：

- 到点后会出现聊天记录、状态提示，必要时出现对白气泡
- reminder 状态从 `pending` 进入 `delivered` 或等价终态
- 同一 reminder 不重复连续触发

### E2E-08：重启后已到期 reminder 必须补触发

前置：

- 已创建一个将在短时间内到期的 reminder

步骤：

1. 创建 reminder 后关闭应用
2. 等到 reminder 的 `dueAtUtc` 已经过期
3. 重新启动应用

期望：

- 应用启动后会扫描到已到期但未送达的 reminder
- reminder 会在恢复时补触发
- 不会因为中途重启而永久漏掉
- reminder 最终状态可追踪，且不会无限重复补发

### E2E-09：取消 reminder 后不得继续触发

前置：

- 已存在一个 pending reminder

步骤：

1. 用户发送“取消刚才那个提醒”
2. 等待 reminder 原定触发时间过去

期望：

- 系统能找到对应 reminder 并更新状态
- 被取消的 reminder 不会再触发
- 聊天历史中应能看到取消成功的确认反馈

### E2E-10：用户纠正偏好后，旧 memory 必须让位

前置：

- 系统曾记录旧偏好，例如“叫我老板”

步骤：

1. 用户发送“以后别叫我老板，叫我 Yubo”
2. 随后在新的若干轮对话里继续交互

期望：

- 后续回复应使用 `Yubo`
- 旧称呼不应继续高频出现
- 相关 facts / habits 文档体现出对旧信息的修正，而不是无脑并存

### E2E-11：用户可询问待办提醒，系统能从 memory / reminder 中回答

前置：

- 已存在至少一个 pending reminder

步骤：

1. 用户发送“我让你提醒我的是什么来着？”

期望：

- 系统能回答当前待办提醒的主题与大致时间
- 回答来源不依赖聊天窗口最近几轮必须还在
- 若 reminder 已经送达或取消，回答内容应与真实状态一致

### E2E-12：结构化抽取失败时，主聊天回复不能崩

前置：

- 人为制造 `ReminderIntentJudge` 或 `MemoryJudge` 返回无效结构

步骤：

1. 用户发送普通聊天内容
2. 或发送一条本应创建 reminder 的消息

期望：

- 用户仍能收到自然语言回复
- UI 不出现原始 `JSON`、异常堆栈或明显调试文本
- 结构化写入失败时可以跳过 reminder / memory 更新，但主回复链路保持可用

## 14. 风险与缓解

### 风险 1：结构化输出漂移

风险：

- provider 对复杂结构输出不稳定

缓解：

- 不做大统一 schema
- 拆成多个单一职责 extractor
- schema 保持扁平、字段少
- parse 失败不阻断主回复

### 风险 2：memory 误记或过记

风险：

- `LLM judge` 误把临时内容当长期事实，或错误地把推断候选提升为稳定偏好

缓解：

- `MemoryJudge` 输出必须区分 `explicit_override`、`explicit_preference` 与 `inferred_habit_candidate`
- 只对高优先级、高置信度内容做热路径写入
- 推断型候选先交给后台汇总再决定是否晋升
- 关键事实带来源 thread 和时间戳

### 风险 3：Markdown 文档越写越乱

风险：

- 自动更新后文档逐渐失控

缓解：

- 采用固定模板和少量 frontmatter
- 后台汇总时重写成规范化模板
- 长文本说明收敛到固定章节

### 风险 4：reminder 重复触发或漏触发

风险：

- 重启、休眠、时钟变化导致提醒错乱

缓解：

- 以 `dueAtUtc` 为唯一执行事实源
- 用 `status` / `deliveredAtUtc` 做幂等保护
- 启动、恢复时补扫 `pending`

## 15. 验收标准

本计划实现后，至少应满足以下结果：

1. 当前本地 provider 路径可以对不同模型使用不同 `role.md`，形成稳定角色感。
2. 用户的互动习惯能够跨聊天和跨模型保留，而不会每次重学。
3. 每个角色与用户之间都能保留独立的 `bond` 关系摘要。
4. 长对话会自动 compact，正常用户不需要手动管理 session。
5. 系统能把一部分长期事实从 recent turns 中提升到长期 memory。
6. 用户说“1 小时后提醒我……”后，reminder 会被持久化并在到点触发。
7. 应用中途重启后，已到期但未送达的 reminder 会在恢复时补触发。
8. 主聊天回复仍以自然文本为主，不会因为大结构输出失败而显著退化。
9. `MemoryJudge` 能将用户显式修正识别为覆盖型写入，并优先于推断性习惯候选。
10. thread 会基于 `消息轮数 + compact 阈值 + 时间窗口` 自动管理，而不是要求用户手动重置上下文。
11. 至少 `E2E-01`、`E2E-03`、`E2E-05`、`E2E-06`、`E2E-08`、`E2E-10`、`E2E-12` 通过，证明角色感、习惯记忆、显式修正覆盖、compact、reminder 落盘恢复和失败兜底均已打通。

## 16. 最终建议

对当前 `VividSoul` 的纯本地 provider 路径，最稳妥的演进方式不是继续把逻辑堆进单一 `system prompt` 或单次大 `JSON` 输出，而是：

1. 用 `Markdown` 把角色设定、用户习惯、关系摘要固定下来。
2. 用隐藏 thread + compact + facts 把 `memory` 做成真正的长期能力。
3. 用专用 reminder extractor 和本地调度器把 `heartbeat` 做成可靠提醒系统。
4. 让正常用户只感知“这个角色越来越懂我”，而不是被迫理解 session、compact 和诊断细节。

这样既能把 `memory` 做成核心资产，又能避免结构复杂度过早压垮当前本地 provider 方案。
