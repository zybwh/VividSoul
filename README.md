# vividsoul

`vividsoul` 是一个以 `Unity 6` 为核心的桌宠 + AI 伴侣项目。
`vividsoul` is a `Unity 6` desktop pet + AI companion project.

当前仓库是一个明确的 `vibe coding` 产物：先快速把体验、交互和系统边界跑通，再逐步收敛实现与工程质量。
This repository is intentionally a `vibe coding` artifact: validate experience, interaction loops, and system boundaries first, then harden the implementation over time.

## 当前目标 | Current Goal

第一目标是做出一个在 `macOS` 上真实可用的桌宠 + AI 伴侣原型：
The first milestone is a genuinely usable `macOS` desktop pet + AI companion prototype:

- 可导入和运行 `VRM` 角色
- 具备桌面陪伴感的基础交互、动作和对话
- 支持本地或开放模型驱动的 AI 聊天能力
- 逐步接入记忆、提醒和主动消息能力

- Import and run `VRM` characters
- Support core desktop-presence interactions, actions, and chat
- Use local or open-model powered AI conversation flows
- Gradually add memory, reminders, and proactive companion behavior

## 项目说明 | What This Repo Contains

仓库根目录是协作层，主 Unity 项目位于 `VividSoul/`。
The repository root is the coordination layer, while the main Unity project lives in `VividSoul/`.

| Path | 说明 / Purpose |
|------|----------------|
| `VividSoul/` | Unity 6 主项目：运行时代码、资源、`StreamingAssets` |
| `docs/` | 规范文档：`PRD`、架构、计划、状态、经验总结 |
| `AGENTS.md` | 仓库级代理工作规则 |
| `Builds/` | 构建产物，例如 `Builds/VividSoul/macOS/` |
| `Exports/` | 生成内容与工具日志 |
| `downloads/` | 第三方原始下载和参考材料 |
| `tmp/` | 临时截图和草稿文件 |
| `utsuwa/` | 只读参考项目，不作为当前实现主线 |
| `scripts/` | 仓库脚本，例如构建、导出、清理 |

## 推荐阅读顺序 | Recommended Reading Order

1. `docs/STATUS.md`
2. `docs/README.md`
3. `docs/prd.md`
4. `docs/architecture.md`

## 常用命令 | Common Commands

- `macOS` 批量构建 / macOS batch build: `scripts/build-vividsoul.sh`
- 导出待机烘焙资源 / idle bake export: `scripts/export-vrma-idle-bake.sh`
- 清理临时文件 / clear temporary files: `scripts/cleanup-temp.sh`

更多环境变量和排障说明见 `scripts/README.md`。
See `scripts/README.md` for environment variables and troubleshooting details.

## Default VRM Credit

默认内置 `VRM` 模型目标文件名为 `8329754995701333594.vrm`。
The intended built-in default `VRM` model filename is `8329754995701333594.vrm`.

来源页面：
Source page:
[https://hub.vroid.com/characters/360475117069004909/models/5726275402896222509](https://hub.vroid.com/characters/360475117069004909/models/5726275402896222509)

模型贡献者：`Dairlsuta`
Contributor: `Dairlsuta`

## License

当前仓库代码采用 `Apache-2.0` License，兼顾开放协作、商业友好和更明确的专利授权条款。
The repository code is licensed under the `Apache-2.0` License, balancing open collaboration, commercial friendliness, and clearer patent grant terms.

注意：仓库内第三方代码、模型、素材和依赖仍以各自目录中的原始许可证为准，根目录 `LICENSE` 不覆盖第三方条款。
Note: third-party code, assets, models, and dependencies keep their own original licenses. The root `LICENSE` does not override third-party terms.
