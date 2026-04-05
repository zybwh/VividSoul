# VividSoul Implementation Plan

## 文档信息

- 项目：`VividSoul`
- 宿主工程：`VividSoul`
- 关联文档：`../architecture.md`、`../prd.md`
- 专题实施计划：`./model-library-import-plan.md`
- 版本：`v0.1`
- 日期：`2026-04-04`
- 状态：`active`

## 1. 目标

这份计划的目标不是完整重建参考产品，而是尽快做出一条可以连续交付的实现主线：

1. 先把 `桌宠 + VRM + Steam + Steam Workshop` 跑通。
2. 再把 `LLM` 作为后端能力接进来。
3. 再把 workshop 从“模型分发”扩展到“动作 / 交互 / 衣服”等内容扩展。

## 2. 实施原则

- 新能力统一从 `VividSoul/Assets/App/` 起新目录。
- 旧脚本默认只参考，不继续扩写。
- 优先打通运行链路，再做抽象完善。
- Steam 和 Workshop 必须从一开始就走可替换 adapter。
- 本地导入和 workshop 下载必须统一到同一套内容协议。
- LLM 必须走客户端协议层，不直接把模型 SDK 接进客户端业务。

## 3. 当前可直接复用的现成能力

从宿主工程可直接复用：

- `UniVRM / UniGLTF`
- `Kirurobo.UniWindowController`
- `StandaloneFileBrowser`
- `Steamworks.NET`
- Unity `Localization`
- Unity `UGUI`

可以借鉴但不直接复用业务逻辑：

- `VRMLoader.cs`
- `SteamWorkshopHandler.cs`
- `SteamWorkshopAutoLoader.cs`

不作为主线依赖：

- `LLMUnity`

## 4. 首批里程碑

### Milestone 0: App Runtime Foundation

目标：

- 在宿主工程内建立独立的新代码骨架

交付物：

- `Assets/App/Runtime` 目录结构
- `App.Runtime.asmdef`
- 核心内容协议类型
- 基础设置和模型来源类型

完成标准：

- 新代码能在不依赖旧脚本的前提下独立编译
- 后续功能都能往 `Assets/App/Runtime` 里继续堆

### Milestone 1: Content Package Foundation

目标：

- 把“本地导入”和“Workshop 下载”统一到内容包协议

交付物：

- `ContentSource`
- `ContentType`
- `ContentItem`
- `ContentManifest`
- `FileSystemContentCatalog`

完成标准：

- 能扫描一个目录并识别：
  - `Model`
  - `Animation`
  - `Voice`
- 能兼容：
  - `item.json`
  - 无 manifest 的单 `.vrm` 目录

### Milestone 2: Local VRM Runtime

目标：

- 跑通本地 `.vrm` 导入和显示

交付物：

- `IModelLoader`
- `VrmModelLoaderService`
- `CharacterRuntimeAssembler`
- `SelectedContentStore`

完成标准：

- 从文件对话框选择 `.vrm`
- 成功显示模型
- 能保存并恢复最近一次加载模型

### Milestone 3: Desktop Pet Window Runtime

目标：

- 跑通桌宠核心窗口能力

交付物：

- `IWindowService`
- `UniWindowWindowService`
- `DesktopPetBoundsService`
- 拖拽 / 缩放 / 旋转控制器

完成标准：

- 窗口置顶
- 点击穿透
- 拖拽
- 缩放
- 旋转
- 打开文件选择器时自动降级 `TopMost`

### Milestone 4: Steam and Workshop Consumption

目标：

- 跑通 Steam 工坊消费链路

交付物：

- `ISteamPlatformService`
- `IWorkshopService`
- `SteamworksNetWorkshopService`
- workshop 下载目录扫描
- 模型内容加载

完成标准：

- 能列出订阅内容
- 能识别下载目录里的内容包
- 能从 workshop 加载模型

### Milestone 5: Animation and Interaction Extensions

目标：

- 让 workshop 除模型外还能分发行为扩展

交付物：

- `AnimationPackageInstaller`
- `BehaviorPackageInstaller`
- `IBehaviorPreset`
- `VRMA` 接入

完成标准：

- 模型可附加动作包
- 行为包可绑定表情、姿势、动作映射

### Milestone 6: Soul Backend Integration

目标：

- 让客户端开始具备 AI 行为驱动能力

交付物：

- `SoulClient`
- `BehaviorDispatcher`
- `BehaviorExecutionQueue`
- 行为协议 DTO

完成标准：

- 客户端可接收后端结构化 JSON
- 能驱动：
  - 表情
  - 动作
  - 姿势
  - 视线跟随
  - 语音参数

## 5. 当前推荐实施顺序

按实际开发顺序，建议这样推进：

1. `Milestone 0`
2. `Milestone 1`
3. `Milestone 2`
4. `Milestone 3`
5. `Milestone 4`
6. `Milestone 6`
7. `Milestone 5`

理由：

- `LLM` 接入不应该阻塞桌宠和内容系统主线
- 动画 / 交互扩展应建立在 workshop 内容协议稳定之后

## 6. 内容协议落地版本

### 6.1 Model Package

```text
item/
  item.json
  preview.jpg
  model.vrm
```

### 6.2 Animation Package

```text
item/
  item.json
  idle_01.vrma
  click_01.vrma
  pose_01.vrma
  preview.jpg
```

### 6.3 Voice Package

```text
item/
  item.json
  click.wav
  idle_01.wav
  idle_02.wav
  preview.jpg
```

### 6.4 Behavior Package

```text
item/
  item.json
  behavior.json
  prompts.json
```

### 6.5 Outfit Package

`Outfit` 先保留协议，不进首轮实现。

## 7. Steam 实现策略

第一阶段策略：

- Steam SDK 适配层优先使用宿主工程已有的 `Steamworks.NET`
- 所有业务代码只依赖：
  - `ISteamPlatformService`
  - `IWorkshopService`

这样后面如果要换成 `Facepunch.Steamworks`，只改 adapter，不动上层业务。

## 8. LLM 实现策略

第一阶段不把 `LLM` 直接集成到 Unity 逻辑里。

统一采用：

- 客户端：行为执行器
- 后端：大模型编排和结构化响应生成

客户端协议建议：

```json
{
  "text": "你回来啦。",
  "emotion": "playful",
  "expression": "smile_soft",
  "action": "wave",
  "pose": "lean_forward",
  "lookAt": "cursor",
  "intensity": 0.5,
  "voice": {
    "style": "warm",
    "speed": 1.0
  },
  "extensions": {
    "interactionPreset": "tease_v1"
  }
}
```

## 9. 第一批代码落地范围

这轮开始实现时，只做下面这些：

- `Assets/App/Runtime/App.Runtime.asmdef`
- `Assets/App/Runtime/Content`
- `Assets/App/Runtime/Settings`

也就是先把：

- 内容协议
- 内容目录扫描
- 模型来源状态
- 选择内容的持久化模型

先立起来。

这一步的意义：

- 后面的本地导入和 Workshop 加载都能直接接上
- 不需要现在就碰复杂的窗口或 Steam 回调细节

## 10. 第一批任务清单

### Task Group A: Runtime Scaffold

- 创建 `Assets/App/Runtime`
- 创建 `App.Runtime.asmdef`
- 创建基础命名空间 `VividSoul`

### Task Group B: Content Contracts

- `ContentSource`
- `ContentType`
- `ContentItem`
- `ContentManifest`
- `ContentManifestFile`

### Task Group C: Content Discovery

- `FileSystemContentCatalog`
- manifest 解析
- 无 manifest 回退策略
- 扩展名识别

### Task Group D: Selection State

- `SelectedContentSource`
- `SelectedContentState`
- `AppSettingsData`

## 11. 完成这一轮后你应该能得到什么

完成第一轮实现后，应当具备：

- 一套独立于旧脚本的新 runtime 骨架
- 一套统一的内容包模型
- 一套本地 / workshop 通用的内容扫描逻辑
- 后续接 `VRM loader`、`Steam workshop`、`LLM behavior` 的稳定接口点

## 12. 风险控制

### 风险 1：过早改旧工程

控制：

- 新代码全部放 `Assets/App`
- 旧脚本不直接改，除非被证明必须接管

### 风险 2：Steam 绑定过深

控制：

- 上层只依赖 `IWorkshopService`

### 风险 3：内容协议过早复杂化

控制：

- 先只支持 `Model / Animation / Voice`
- `Outfit` 仅保留协议

### 风险 4：LLM 抢占主线

控制：

- 先把桌宠和内容系统跑通
- 再接后端行为协议

## 13. 当前开始实现的决定

当前决定如下：

- 宿主工程：`VividSoul`
- 新代码根目录：`VividSoul/Assets/App`
- 第一实现切片：`Content + Settings foundation`

这意味着接下来第一批提交的代码应聚焦于：

- 内容协议
- 目录扫描
- 选择状态

而不是直接去写复杂 UI、Steam 搜索页或 LLM 会话逻辑。
