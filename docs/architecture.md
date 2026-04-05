# VividSoul Architecture

## 文档信息

- 项目：`VividSoul`
- 参考对象：Steam 桌宠《你妈》
- 关联文档：`prd.md`、`plans/utsuwa-vividsoul-rebuild-plan.md`
- 版本：`v0.1`
- 日期：`2026-04-04`
- 状态：`working draft`

## 1. 结论先行

基于对参考产品本地安装包、Steam 工坊内容和程序集的逆向，`VividSoul` 如果想借鉴这条已经验证过的路径，最稳妥的技术方案不是 `Tauri + Three.js`，而是：

- 前端客户端采用 `Unity 6` 原生桌宠客户端
- 后端保留独立的 `Soul Backend`
- 模型内容采用 `VRM` 为核心格式
- 本地导入和 Steam 工坊下载统一走目录型内容包协议
- 桌宠窗口能力通过平台插件实现，而不是网页层伪装

一句话概括：

> 参考品的成功关键不是复杂 AI，也不是花哨架构，而是 `Unity + VRM + 原生窗口插件 + Steam 工坊 + 运行时角色装配器` 这条极其务实的产品工程路线。

这份文档的建议是：

- 如果目标是尽快做出一款 Steam 上可打的 `桌宠 + VRM + Win/macOS` 产品，主线应切到 `Unity Native Client`
- 如果目标是最大化复用 `utsuwa`，则保留现有 `Tauri/WebGL` 路线作为备选支线，但不要把它当作参考品同路线方案

## 2. 逆向得到的关键事实

### 2.1 运行时与构建方式

本机逆向确认：

- 参考产品是 `Unity 6`
- macOS 客户端是 `.app` 原生应用，不是 Electron
- 主程序和 `UnityPlayer.dylib` 都是 `x86_64 + arm64` universal binary
- 脚本后端是 `Mono`，不是 `IL2CPP`

关键证据：

- `Info.plist` 包含 `Unity Player version 6000.0.48f1`
- `Contents/Frameworks` 中存在 `libmonobdwgc-2.0.dylib`、`libmono-native.dylib`
- `Contents/Resources/Data/Managed` 存在完整托管程序集

### 2.2 VRM 是真实主链，不是噱头兼容

逆向确认：

- 用户当前模型来源是 `Workshop`
- 存档中保存的是 `.vrm` 文件绝对路径
- 工坊下载目录中确实存在实际 `.vrm` 文件
- `file` 检查表明该文件是 `glTF binary model`

说明：

- 它不是将 VRM 先离线转换后只加载内部 prefab
- 它在运行时直接处理 `.vrm`
- Steam 工坊只是内容分发层，不是另一套运行时格式

### 2.3 内容系统是双轨，但重心明显偏 VRM

程序集方法表显示它同时支持两条加载链：

- `VrmModelLoader.LoadVrmFromPathAsync`
- `VrmModelLoader.LoadVrmFromFolderAsync`
- `AssetBundleCharacterLoader.LoadBundleAsync`
- `AssetBundleCharacterLoader.LoadCharacterPrefabAsync`

从存档和工坊实物看，当前产品主线是 `VRM`。

这说明：

- `VRM` 是面向开放模型生态的主链
- `AssetBundle` 更像历史遗留能力或首方内容保底能力

### 2.4 工坊协议是目录型协议

工坊内容目录实物显示：

- 有的包只有 `.vrm`
- 有的包是 `.vrm + config.json + preview.jpg + thumbnail.jpg`
- 还有早期包是 `.vrm + Info.txt + Prev.jpeg`

程序集还暴露出：

- `WorkshopUI.GetVrmPath`
- `WorkshopUI.GetBundlePath`
- `WorkshopUI.LoadVrmModel`
- `WorkshopUI.LoadBundleModel`
- `WorkshopUI.ViewAnimation`
- `WorkshopUI.ViewVoice`

说明：

- 工坊项不是统一打成 AssetBundle
- 游戏是扫描目录并根据文件结构决定加载方式
- 模型、动画、语音是三个并列内容维度

### 2.5 参考品用了哪些关键三方库

逆向可识别的关键依赖：

- `VRM10.dll`
- `UniGLTF.dll`
- `UniHumanoid.dll`
- `VrmLib.dll`
- `FastSpringBone10.dll`
- `Facepunch.Steamworks.Posix.dll`
- `Kirurobo.UniWindowController.dll`
- `LibUniWinC.bundle`
- `StandaloneFileBrowser.dll`
- `Kybernetik.Animancer.dll`
- `UniTask.dll`
- `ES3` 相关类型
- `I2.Loc` 相关类型
- `RainbowArt.CleanFlatUI.*`

### 2.6 角色不是直接“换模型”，而是运行时装配

`CharacterManager` 暴露出大量运行时组装方法：

- `AddRequiredComponents`
- `FixPhysBoneReferences`
- `SetupIdleAnimation`
- `SetupClickAnimation`
- `SetupPoseAnimation`
- `CalculateCharacterHeight`
- `SaveModelSource`
- `GetSavedModelSource`

这说明参考品并不是把每个模型都预制成同构 prefab，而是：

1. 先加载 VRM
2. 再把桌宠逻辑组件动态挂上去
3. 再复制或重绑待机、点击、姿势等行为能力

### 2.7 参考品还做了运行时换装

`OutfitManager` 暴露出：

- `CopySkinnedMeshRenderer`
- `RemapBones`
- `BuildSourceHumanBoneMap`

说明：

- 它不是简单材质切换
- 它在运行时复制网格并重映射骨骼
- 这是高复杂度能力，不适合作为 `VividSoul` 的 MVP 主线

### 2.8 桌宠窗口行为是产品成功关键之一

从程序集和插件可见：

- `GameManager.SetTopMost`
- `SystemTrayManager.SetClickThrough`
- `SettingsUI.OnTopMostChanged`
- `SettingsUI.OnAutoStartChanged`
- `SettingsUI.OnMonitorChanged`

更重要的是：

- `LocalFileLoaderUI.DisableTopmost`
- `LocalFileLoaderUI.RestoreTopmost`

说明它已经踩过一个非常真实的产品坑：

- 桌宠窗口置顶后，系统文件选择器可能被遮挡或交互异常
- 正确做法是打开文件选择器前临时关闭 `TopMost`

## 3. 对 VividSoul 的架构决策

### 3.1 主推荐路径

`VividSoul` 采用：

- `Unity Native Client` 作为桌面前端
- `Soul Backend` 作为后端智能服务
- `VRM` 作为模型主格式
- `Steam Workshop` 作为社区分发层

### 3.2 为什么不建议把 PRD 的 Tauri 路线作为主线

`prd.md` 当前写的是：

- `Tauri` 首选
- `Svelte / Three.js / three-vrm`
- 前后端彻底分离

这套路线不是不可做，但和参考品相比，工程风险更高：

- 桌宠窗口行为在网页栈中更难做到真正稳定
- 点击穿透、透明窗口、系统托盘、显示器切换、系统层拖拽等体验更容易在平台层卡住
- WebGL VRM 运行时虽然成熟，但复杂模型和长驻稳定性未必比 Unity 路线更稳
- 如果要复刻参考品的 Steam 工坊、本地导入、运行时装配和后续动画扩展，Unity 生态路径明显更短

### 3.3 最终推荐

建议对 `VividSoul` 做出如下产品级选择：

- `桌宠客户端`：`Unity`
- `Soul Engine`：独立后端服务
- `Steam 工坊`：客户端消费，创作者工具后置
- `AI`：通过结构化协议控制客户端行为，不把 LLM 接入写死在客户端

### 3.4 当前工作区的实施落点

当前工作区里最适合作为第一阶段宿主工程的是 `VividSoul/`。

原因：

- 它已经是可打开的 Unity 工程
- 已经内置 `VRM`、`UniGLTF`、`UniWindowController`、`StandaloneFileBrowser`
- 已经集成 `Steamworks.NET`
- 已经存在可参考的 `VRMLoader` 和 `SteamWorkshop` 相关脚本

实施策略：

- 不在现有 `MATE ENGINE - Scripts` 中继续堆逻辑
- 新代码统一落在 `VividSoul/Assets/App/`
- 现有脚本只作为参考实现和依赖提供者
- 新架构通过独立命名空间和独立目录逐步替换旧逻辑

## 4. 推荐目标架构

### 4.1 高层结构

```text
+------------------------------------------------+
| Unity Desktop Client                           |
| - VRM runtime                                  |
| - desktop pet window runtime                   |
| - interaction and animation                    |
| - local import                                 |
| - workshop browser / downloader                |
| - TTS playback / lip-sync                      |
+--------------------------+---------------------+
                           |
                           | WebSocket / streaming HTTP
                           |
+--------------------------v---------------------+
| Soul Backend                                     |
| - prompt orchestration                           |
| - provider routing                               |
| - structured behavior JSON normalization         |
| - session / memory / policy                      |
| - optional NSFW behavior templates               |
+--------------------------+---------------------+
                           |
                           | provider adapters
                           |
+--------------------------v---------------------+
| LLM / TTS / STT Providers                        |
| OpenAI / Claude / Gemini / Grok / Ollama / etc. |
+------------------------------------------------+
```

### 4.2 客户端职责

客户端负责：

- 模型导入、加载、渲染
- 桌宠窗口行为
- 用户交互
- 动画和表情执行
- 物理表现
- 本地设置持久化
- 工坊内容浏览和安装
- TTS 播放与口型驱动

客户端不负责：

- prompt orchestration
- 多模型供应商适配
- provider-specific response parsing
- 会话长期记忆策略
- 复杂安全或风格策略

### 4.3 后端职责

后端负责：

- LLM 路由
- 提示词和角色策略
- 输出统一结构化控制协议
- 会话状态与长期记忆
- 可选 NSFW 行为模板
- TTS/STT 编排

## 5. 推荐客户端技术栈

### 5.1 必选

- 引擎：`Unity 6 LTS`
- VRM：`UniVRM 1.0`
- 异步：`UniTask`
- Steam：抽象层先行，初始适配器优先复用宿主工程已存在的 `Steamworks.NET`
- 文件选择：`StandaloneFileBrowser`
- 文本 UI：`TextMeshPro` 优先；运行时高兼容覆盖层可在必要时退回 `UGUI Text`
- 窗口：`Kirurobo.UniWindowController`

### 5.2 建议

- 动画：`Animancer`
- DI：`VContainer`
- 事件总线：`SignalBus`
- 本地化：`Unity Localization`
- 存档：自定义 `JSON`，并同步到 `Steam Auto Cloud`

### 5.3 不建议在 MVP 默认引入

- `VRC SDK` 运行时依赖
- `AssetBundle` 用户导入链
- 复杂运行时换装
- 大量商城 UI 套件
- `Easy Save 3`
- `I2 Localization`

这些能力参考品确实用了或保留了痕迹，但对 `VividSoul` MVP 不是必要项。

### 5.4 对现有宿主工程的复用原则

优先复用：

- `UniVRM / UniGLTF`
- `Kirurobo.UniWindowController`
- `StandaloneFileBrowser`
- `Steamworks.NET`
- Unity 自带 `Localization`、`UGUI`、`Timeline`

只借思路，不直接继承业务逻辑：

- 现有 `VRMLoader`
- `SteamWorkshopHandler`
- `SteamWorkshopAutoLoader`
- 现有 avatar library 和 mod handler 逻辑

仅作为可选实验能力保留，不进入主线：

- `LLMUnity`

理由：

- `LLMUnity` 更适合本地模型或嵌入式实验
- `VividSoul` 的主方案仍然是后端驱动的 `Soul Backend`
- 客户端不应绑定特定 LLM SDK

## 6. 推荐客户端模块划分

### 6.1 `Platform`

职责：

- 透明窗口
- 置顶
- 点击穿透
- 托盘
- 自启动
- 显示器切换
- 热键

接口建议：

```csharp
public interface IWindowService
{
    void SetTopMost(bool enabled);
    void SetClickThrough(bool enabled);
    void MoveToMonitor(int monitorIndex);
    void EnsureVisible();
}
```

### 6.2 `Content`

职责：

- 扫描本地内容
- 扫描工坊目录
- 统一解析元数据
- 输出标准化内容项

建议数据模型：

```csharp
public enum ContentSource
{
    BuiltIn,
    Local,
    Workshop
}

public enum ContentType
{
    Model,
    Animation,
    Voice
}

public sealed record ContentItem(
    string Id,
    ContentSource Source,
    ContentType Type,
    string RootPath,
    string EntryPath,
    string Title,
    string Description,
    string PreviewPath,
    string AgeRating);
```

### 6.3 `Avatar`

职责：

- 运行时加载 VRM
- 销毁旧模型
- 统一角色根节点
- 计算角色高度
- 安装交互组件
- 修正物理和碰撞

建议模块：

- `VrmLoaderService`
- `CharacterRuntimeAssembler`
- `CharacterRuntimeContext`

### 6.4 `Interaction`

职责：

- 拖拽
- 缩放
- 旋转
- 视线跟随
- 点击触发
- 边界校正

建议模块：

- `DragScaleRotateController`
- `HeadFollowController`
- `ClickInteractionController`
- `DesktopPetBoundsService`

### 6.5 `Animation`

职责：

- 待机动画
- 点击动画
- 姿势动画
- 后续支持 `VRMA`

建议模块：

- `IdleAnimationService`
- `ClickAnimationService`
- `PoseAnimationService`

### 6.6 `AI Runtime`

职责：

- 和后端通信
- 接收结构化行为协议
- 将行为协议路由到表达、动作、物理、语音模块

建议模块：

- `SoulClient`
- `BehaviorDispatcher`
- `BehaviorExecutionQueue`

### 6.7 `Persistence`

职责：

- 保存窗口状态
- 保存模型来源
- 保存设置
- 同步 Steam Cloud

建议保存字段：

- `modelSource`
- `modelData`
- `position`
- `rotation`
- `scale`
- `topMost`
- `clickThrough`
- `monitorIndex`
- `voiceVolume`
- `frameRate`
- `headFollow`
- `handFollow`
- `lockScale`
- `lockRotation`

### 6.8 `Workshop`

职责：

- 搜索和浏览工坊内容
- 订阅和下载
- 下载进度
- 解析已下载目录
- 加载目录中的模型、动画或语音内容

MVP 只做消费端：

- 浏览
- 搜索
- 安装
- 加载

创作者上传端后置。

### 6.9 `UI`

职责：

- 运行时 HUD
- 状态提示
- 对话气泡
- 后续聊天面板或输入入口

当前已落地的运行时 UI 形态包括：

- `DesktopPetRuntimeHud`
- `DesktopPetSpeechBubblePresenter`

设计原则：

- 角色对白优先走可复用的运行时覆盖层，而不是把临时文本直接塞进动作或动画模块。
- 对话气泡作为独立 UI 通道存在，便于后续从“内置动作测试文案”平滑升级到“后端结构化对话响应”。
- 文本展示层需要优先保证 shipped player 稳定性，必要时可以为了兼容性选择 `UGUI Text` 而不是强依赖运行时动态 TMP 资源。

## 7. 运行时导入链设计

### 7.1 本地导入

推荐流程：

1. 用户点击导入
2. `WindowService` 临时关闭 `TopMost`
3. 调用 `StandaloneFileBrowser`
4. 用户选择 `.vrm`
5. 恢复 `TopMost`
6. 调用 `VrmLoaderService`
7. 调用 `CharacterRuntimeAssembler`
8. 保存当前模型来源为 `Local`

### 7.2 工坊导入

推荐流程：

1. 工坊 UI 搜索内容
2. 订阅内容
3. Steam 下载到本地工坊目录
4. `ContentCatalogService` 重新扫描目录
5. 找到内容包入口文件
6. 根据 `type` 选择加载链
7. 保存当前模型来源为 `Workshop`

### 7.3 为什么本地导入和工坊导入必须统一

这是参考品最值得借鉴的一点。

原因：

- 本地导入和工坊下载本质上都是“给客户端一个目录”
- 统一协议后，内容系统会简单很多
- 也方便后续接入模型市场或社区同步目录

## 8. 内容包协议

### 8.1 模型包 `v1`

目录结构：

```text
item/
  item.json
  preview.jpg
  thumbnail.jpg
  model.vrm
```

推荐 `item.json`：

```json
{
  "schemaVersion": 1,
  "type": "Model",
  "title": "Fugue",
  "description": "Fugue from Honkai Star Rail",
  "ageRating": "Everyone",
  "entry": "model.vrm",
  "preview": "preview.jpg",
  "thumbnail": "thumbnail.jpg",
  "tags": ["anime", "vrm"]
}
```

### 8.2 动画包 `v2`

```text
item/
  item.json
  idle_01.vrma
  click_01.vrma
  pose_01.vrma
  preview.jpg
```

### 8.3 语音包 `v2`

```text
item/
  item.json
  click.wav
  idle_01.wav
  idle_02.wav
  preview.jpg
```

### 8.4 兼容策略

为兼容早期工坊内容，建议保留降级规则：

- 没有 `item.json` 时，直接扫描目录中的 `.vrm`
- 如果存在单个 `.vrm`，可推断为 `Model`
- 预览图优先查找 `preview.jpg`、`thumbnail.jpg`、`Prev.jpeg`

## 9. AI 控制协议

后端输出建议采用结构化 JSON：

```json
{
  "text": "Good morning. You look busy today.",
  "emotion": "playful",
  "expression": "smile_soft",
  "action": "wave",
  "pose": "lean_forward",
  "lookAt": "cursor",
  "intensity": 0.45,
  "physicsProfile": "gentle_idle",
  "voice": {
    "style": "warm",
    "speed": 1.0
  },
  "metadata": {
    "adultTone": true
  }
}
```

客户端只做执行，不做重推理：

- `emotion` -> 表情映射
- `action` -> 动作播放
- `pose` -> 姿势切换
- `lookAt` -> 头眼跟随模式
- `physicsProfile` -> 弹簧骨骼参数组
- `voice` -> TTS 选择与速度

## 10. 建议工程结构

```text
Assets/App/
  Runtime/
    Bootstrap/
    Core/
    Platform/
    Content/
    Avatar/
    Interaction/
    Animation/
    AI/
    Audio/
    Workshop/
    Settings/
    UI/
  Editor/
  Tests/
```

### 10.1 依赖注入

建议：

- 使用 `VContainer`
- 所有平台、内容、后端通信能力都通过服务注入

不要做：

- 全局静态单例泛滥
- UI 直接访问底层平台插件

### 10.2 事件系统

建议：

- `SignalBus`
- UI -> Runtime -> Services 通过事件或 command 分离

### 10.3 初始代码根目录

第一阶段所有新代码统一放在：

```text
VividSoul/Assets/App/
```

目录目标：

- `Runtime/Content`：内容协议与内容扫描
- `Runtime/Avatar`：VRM 加载与角色装配
- `Runtime/Platform`：窗口、托盘、热键、自启动
- `Runtime/Workshop`：Steam 工坊消费端
- `Runtime/Settings`：设置与状态持久化
- `Runtime/AI`：后端协议与行为调度

执行原则：

- 不修改旧脚本就能新建能力的，优先新建
- 需要借旧逻辑时，通过 adapter 包装，而不是直接扩写旧脚本
- 任何新能力都先通过接口建模，再接具体 SDK

## 11. 和现有文档的关系

### 11.1 与 `prd.md` 的关系

这份文档和 `prd.md` 的一致点：

- 继续坚持前后端分离
- 继续坚持开放模型生态
- 继续坚持结构化 AI 控制协议
- 继续坚持 Win/macOS 双端桌宠体验

这份文档和 `prd.md` 的主要差异：

- 客户端前端从 `Tauri/Web` 主推切换为 `Unity Native` 主推

原因很简单：

- 这是更接近参考产品实证路线的方案
- 桌宠平台能力和 VRM 运行时都更低风险

### 11.2 与 `plans/utsuwa-vividsoul-rebuild-plan.md` 的关系

现有 `utsuwa` 重建计划更适合作为：

- Web/Tauri 技术栈保守复用路线
- 或一个平行实验分支

这份 `architecture.md` 则是：

- 面向参考品借鉴后的主推荐架构
- 面向 Steam 桌宠落地的产品架构

## 12. MVP 范围

### 12.1 In Scope

- 本地导入 `.vrm`
- Steam 工坊浏览和加载模型
- 透明无边框桌宠窗口
- 置顶
- 点击穿透
- 拖拽
- 缩放
- 旋转
- 显示器切换
- 托盘和热键
- 基础待机、点击、姿势动画
- 后端驱动的结构化 AI 行为
- TTS 和基础口型同步
- Steam Cloud 同步设置和当前模型

### 12.2 Out of Scope

- 复杂运行时换装
- VRC 完整生态兼容
- 用户上传工坊内容的应用内编辑器
- 多角色同时显示
- 全量 `glTF` 兼容
- 高复杂度成人交互系统

## 13. 分阶段计划

### Phase 0: Benchmark-aligned spike

- 用 Unity 6 建立空白桌宠客户端
- 跑通透明窗口、置顶、穿透
- 本地导入任意 `.vrm`

### Phase 1: Runtime MVP

- 运行时角色装配
- 拖拽、缩放、旋转
- 视线跟随
- 点击动画
- 持久化

### Phase 2: Soul Backend integration

- WebSocket 通信
- 结构化行为协议
- 表情、动作、姿势执行
- TTS 集成

### Phase 3: Workshop consumption

- 工坊搜索
- 订阅与下载进度
- 目录型内容包加载
- 模型切换

### Phase 4: Content extensions

- 动画包
- 语音包
- Creator Tool

## 14. 最大风险

### 14.1 VRM 兼容性

- 不同 VRM 模型质量差异大
- 弹簧骨骼、材质、表情映射不稳定

建议：

- 只支持一小组明确验证过的 VRM 版本
- 加入导入后健康检查和降级策略

### 14.2 窗口平台差异

- Win/macOS 上透明、穿透、拖拽行为不一致

建议：

- `IWindowService` 抽象必须前置
- 平台能力不要直接写进 UI 脚本

### 14.3 工坊 UGC

- 用户内容质量不稳定
- 版权和 NSFW 内容审核会成为平台问题

建议：

- 客户端先只做消费协议
- 创作者工具和审核策略分开设计

### 14.4 过早引入复杂换装

- 参考品的换装系统复杂度很高
- 一旦做跨骨架 remap，就会进入大量模型兼容性问题

建议：

- MVP 完全跳过

## 15. 继续逆向最值得挖的方向

这轮已经确认的新增点：

- 内容系统是 `VRM + AssetBundle` 双轨
- 工坊和本地导入都走目录型内容
- 客户端已经为 `Model / Animation / Voice` 三类内容留了入口
- 打开文件选择器前会临时取消 `TopMost`
- 角色切换走运行时装配器，不是简单 prefab 替换

下一步最值得继续挖：

1. 反编译 `WorkshopUI` 和 `VrmModelLoader` 的 IL 逻辑，恢复更精确的内容包协议和错误处理流程
2. 找到 Windows 版安装包，确认托盘、自启动、透明点击穿透的具体平台实现
3. 继续验证 `VRC` 相关程序集是否属于真实运行时依赖，还是历史遗留
4. 反编译 `OutfitManager`，确认换装系统到底可否跨模型稳定工作
5. 反编译 `ViewAnimation` 和 `ViewVoice` 相关逻辑，补出动画包和语音包协议

## 16. 最终建议

如果 `VividSoul` 的目标是：

- 快速做出可用的 Steam 桌宠产品
- 支持开放 VRM 模型
- 在 Win/macOS 上稳定常驻
- 后端驱动 AI 行为

那么推荐路线非常明确：

> 采用 `Unity Native Client + Soul Backend`，把 `VRM` 当成内容主格式，把 Steam 工坊当成目录型内容分发层，把桌宠窗口能力当成一等公民来设计。

这条路线和参考品最接近，也最容易在产品体验上达到同一数量级。
