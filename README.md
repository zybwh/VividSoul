# vividsoul

[English README](README.en.md)

<p align="center">
  <strong>一个原生桌面角色，而不是聊天框套壳。</strong><br/>
  基于 <code>Unity 6</code>、<code>VRM</code> 和开放模型的桌宠 + AI companion 实验。
</p>

<p align="center">
  <img alt="stage" src="https://img.shields.io/badge/stage-prototype-black" />
  <img alt="platform" src="https://img.shields.io/badge/platform-macOS-555555" />
  <img alt="engine" src="https://img.shields.io/badge/engine-Unity%206-111111" />
  <img alt="avatar" src="https://img.shields.io/badge/avatar-VRM-4b5563" />
  <img alt="workflow" src="https://img.shields.io/badge/workflow-vibe%20coding-7c3aed" />
  <img alt="policy" src="https://img.shields.io/badge/contrib-AI%20only-0f766e" />
</p>

`vividsoul` 想做的不是另一个网页聊天壳，而是一个真的能住在桌面上的角色系统：能被导入、被点击、被拖动、会说话、会记事、会提醒你，也会在屏幕边缘持续“存在”。

现阶段主线是把这些能力收敛成一个能在 `macOS` 上真实使用的原生体验，主实现位于 `VividSoul/`。

## Why It Hits Different

- 它把 `AI` 放进桌面角色里，而不是把角色塞进聊天框里
- 它强调“存在感”而不是单轮问答，动作、状态、提醒和主动性都是一等能力
- 它以 `VRM` 为角色载体，目标是让导入、运行和长期陪伴形成闭环

## 当前聚焦

目前第一阶段聚焦这四件事：

- 角色：导入并运行 `VRM` 角色，逐步建立本地角色库
- 交互：打磨桌宠式点击、拖动、动作、气泡和桌面停留感
- 对话：接入本地或开放模型驱动的聊天能力，而不是只做静态 UI
- 记忆：把记忆、提醒和主动消息变成角色的长期状态，而不是一次性输出

## 快速入口

- 当前状态：`docs/STATUS.md`
- 文档索引：`docs/README.md`
- 产品方向：`docs/prd.md`
- 技术架构：`docs/architecture.md`
- Unity 主项目：`VividSoul/`

## Vibe Coding Notice

这个仓库明确采用 `vibe coding` 驱动：先快速把体验闭环、交互感觉和系统边界跑通，再逐步补抽象、收口架构和工程化质量。

这同时意味着你应该默认以下风险存在：

- 代码质量风险：部分实现优先服务验证速度，而不是最稳健或最优雅的结构
- 重复造轮子风险：相近能力可能在统一方案落地前被临时实现多次
- 架构回收风险：当前可用路径后续可能被整体合并、替换或删除

所以看这个仓库时，最好把它理解成“有明确产品方向的活原型”，而不是已经冻结形态的最终工程。

## Contribution Protocol

本项目当前只接受由 `AI` coding agent 生成或主导提交的代码变更。

为保证来源可追踪，所有 `commit` 或 `PR` 都必须带上 `coding agent + model` 标记。推荐统一使用下面的尾注格式：

```text
[agent: Cursor][model: GPT-5.4]
```

示例：

```text
feat: add local reminder retry path [agent: Cursor][model: GPT-5.4]
```

如果使用 `PR`，也可以把同样的标记放在标题或正文显眼位置。没有这类标记的变更，默认视为不符合仓库协作约定。

## Repository Map

仓库根目录是协作层，主 Unity 项目位于 `VividSoul/`。

| 路径 | 说明 |
|------|------|
| `VividSoul/` | Unity 6 主项目，包含运行时代码、资源和 `StreamingAssets` |
| `docs/` | 项目文档根目录，包含 `PRD`、架构、计划、状态和经验总结 |
| `AGENTS.md` | 仓库级代理工作规则 |
| `Builds/` | 构建产物输出目录，例如 `Builds/VividSoul/macOS/` |
| `Exports/` | 生成内容和工具日志 |
| `downloads/` | 第三方原始下载与参考材料 |
| `tmp/` | 临时截图和草稿文件 |
| `scripts/` | 构建、导出、清理等仓库脚本 |

## 常用命令

- `macOS` 批量构建：`scripts/build-vividsoul.sh`
- 导出待机烘焙资源：`scripts/export-vrma-idle-bake.sh`
- 清理临时文件：`scripts/cleanup-temp.sh`

更多环境变量和排障说明见 `scripts/README.md`。

## 默认 VRM 说明

默认内置 `VRM` 模型目标文件名为 `8329754995701333594.vrm`。

来源页面：
[https://hub.vroid.com/characters/360475117069004909/models/5726275402896222509](https://hub.vroid.com/characters/360475117069004909/models/5726275402896222509)

模型贡献者：`Dairlsuta`

## License

当前仓库代码采用 `Apache-2.0` License。

仓库内第三方代码、模型、素材和依赖仍以各自目录中的原始许可证为准，根目录 `LICENSE` 不覆盖第三方条款。
