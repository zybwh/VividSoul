# VividSoul 角色库与模型导入实施计划

## 文档信息

- 项目：`VividSoul`
- 范围：角色模型导入、角色库、角色切换、Workshop 收口
- 关联文档：`../prd.md`、`../architecture.md`、`./implementation-plan.md`
- 日期：`2026-04-05`
- 状态：`active`

## 1. 决策摘要

本计划固定以下产品与实现决策：

1. `本地导入` 必须支持，但只允许`导入到角色库`，不允许引用原始文件路径。
2. `Steam Workshop` 作为第二内容来源，不作为唯一入口。
3. 用户统一从`角色库`中查看、应用、删除和管理角色。
4. `BuiltIn / Local / Workshop` 只作为来源标签，不再形成三套割裂系统。
5. 所有角色最终都必须落成统一的目录型内容包，供运行时和 UI 复用。

一句话结论：

> `VividSoul` 的“添加角色”不是“记住一个文件路径”，而是“把一个模型安装进角色库”。

## 2. 目标与非目标

### 2.1 目标

- 建立用户可理解、可维护的`角色库`概念。
- 跑通本地 `.vrm` 导入到角色库并立即应用。
- 让现有“替换角色”从路径缓存升级为角色库选择。
- 让 Workshop 下载内容也收口到同一套角色库浏览与应用体验。
- 为后续封面图、标签、收藏、更新、删除和兼容检查提供稳定数据基础。

### 2.2 非目标

- 不做“引用外部文件并跟随原路径变化”的能力。
- 不做应用内 Workshop 上传器。
- 不做复杂换装或跨骨架服装 remap。
- 不做云端角色库同步。
- 不在本轮引入模型市场或远程下载站点。

## 3. 用户体验设计

### 3.1 用户入口

运行时角色相关入口调整为：

- `添加角色`
- `角色库`
- `Steam Workshop`

职责划分：

- `添加角色`：从本地选择 `.vrm` 或后续的角色包目录，并安装到角色库。
- `角色库`：浏览所有已安装角色，执行应用、删除、显示文件等操作。
- `Steam Workshop`：浏览和订阅内容；下载完成后统一进入角色库视图。

### 3.2 本地导入流程

1. 用户点击 `添加角色`。
2. 客户端临时关闭窗口 `TopMost`。
3. 调用系统文件选择器，仅允许 `.vrm`。
4. 恢复窗口 `TopMost`。
5. 校验文件可读性、扩展名、体积与基础 VRM 载入可行性。
6. 将模型复制到角色库目标目录。
7. 生成标准化 `item.json`。
8. 生成或补齐预览资源。
9. 将新角色加入角色库，并提示：
   - `立即切换`
   - `稍后在角色库中使用`

### 3.3 角色库流程

角色库中的每个条目至少展示：

- 预览图
- 标题
- 来源标签：`BuiltIn` / `Local` / `Workshop`
- 当前使用标记
- 操作：`应用`、`删除`、`显示文件`

本轮不要求复杂分页或多条件筛选，但数据模型必须支持后续扩展：

- 搜索
- 标签筛选
- 收藏
- 最近使用
- 更新可用

### 3.4 Workshop 流程

1. 用户在 `Steam Workshop` 中搜索并订阅角色。
2. Steam 下载到本地 Workshop 目录。
3. 客户端扫描目录并识别模型内容包。
4. 识别出的角色出现在统一角色库中，来源标记为 `Workshop`。
5. 用户从统一角色库中执行应用与管理。

关键原则：

- Workshop 不直接走一条独立“特殊加载链”。
- 下载完成后的用户体验必须与本地导入统一。

## 4. 内容与存储协议

### 4.1 角色库存储根目录

角色库统一存放在用户数据目录：

`Application.persistentDataPath/Content/Models/`

每个角色使用独立目录：

`Application.persistentDataPath/Content/Models/<model-id>/`

### 4.2 角色目录结构

推荐最小结构：

```text
<model-id>/
  item.json
  model.vrm
  preview.jpg
  thumbnail.jpg
```

MVP 要求：

- `item.json` 必须存在。
- `model.vrm` 必须存在。
- `preview.jpg` / `thumbnail.jpg` 可后补，缺失时允许退化到默认占位图。

### 4.3 `item.json` 建议字段

```json
{
  "schemaVersion": 2,
  "type": "Model",
  "id": "a7d26d3c1d7c45a7a5d11c0d56d2d88f",
  "title": "Cartethyia Alter",
  "entry": "model.vrm",
  "preview": "preview.jpg",
  "thumbnail": "thumbnail.jpg",
  "source": "Local",
  "sourceId": "",
  "author": "",
  "description": "",
  "ageRating": "Unknown",
  "tags": ["vrm"],
  "importedAt": "2026-04-05T20:00:00+08:00",
  "fingerprint": "sha256:..."
}
```

说明：

- `id`：角色库内稳定标识。
- `source`：`BuiltIn` / `Local` / `Workshop`。
- `sourceId`：对 Workshop 可填 `publishedFileId`。
- `fingerprint`：用于去重和后续重导入识别。
- `schemaVersion = 2`：表示角色库版模型包，兼容现有更简单的 `schemaVersion = 1` 包。

### 4.4 去重策略

本地导入时按 `fingerprint` 去重：

- 若角色库内已存在同一模型内容：
  - 不重复复制
  - 直接提示“该模型已在角色库中”
  - 支持立即切换到已有角色

MVP 阶段允许先使用文件哈希。

## 5. 当前实现与目标实现的差距

当前运行时已经具备：

- `FileSystemContentCatalog`：识别目录型内容。
- `StandaloneFileDialogService`：选择本地 `.vrm`。
- `SelectedContentStore`：保存当前选中的内容。
- `CachedModelStore`：保存最近模型路径列表。
- `DesktopPetRuntimeHud`：通过“替换角色”菜单显示缓存模型。

当前主要问题：

- 当前“本地导入”本质是直接加载外部路径。
- 当前“替换角色”是路径缓存，不是角色库。
- 当前设置层持久化的是绝对路径，不是“已安装角色”。
- Workshop 与本地内容虽然有统一扫描基础，但用户体验尚未收口为统一角色库。

目标状态：

- 本地模型先安装进角色库，再从角色库应用。
- 最近使用与当前选中基于角色库项，而不是任意外部路径。
- “替换角色”升级为“角色库”视图或其子菜单。

## 6. 架构落点

### 6.1 新增模块建议

建议在 `VividSoul/Assets/App/Runtime/` 下新增：

- `Content/ModelLibraryItem`
- `Content/ModelLibraryManifest`
- `Content/ModelLibraryService`
- `Content/ModelLibraryPaths`
- `Content/ModelImportService`
- `Content/ModelImportResult`
- `Content/ModelFingerprintService`
- `Content/ModelLibraryMigrationService`

### 6.2 优先复用或改造的现有模块

- `FileSystemContentCatalog`
  - 扩展 `item.json` 字段解析
  - 支持角色库目录扫描
- `StandaloneFileDialogService`
  - 继续负责文件选择，不做引用逻辑
- `DesktopPetRuntimeController`
  - 将“选择本地模型并直接加载”改为“导入到角色库并应用”
- `DesktopPetRuntimeHud`
  - 用角色库入口替换现有的路径缓存列表
- `CachedModelStore`
  - 逐步降级为“最近使用角色”或直接被角色库服务替代
- `SelectedContentStore`
  - 短期可继续保存已安装模型的库内路径
  - 中期建议改成保存角色库 `id`

### 6.3 迁移策略

必须考虑当前已经存在的旧设置数据。

迁移规则建议：

1. 若 `SelectedContentStore` 指向的是`角色库外部`的本地 `.vrm` 且文件仍存在：
   - 启动时自动导入到角色库
   - 更新当前选择到角色库中的新路径
2. 若旧路径文件已不存在：
   - 清除该项并回退到默认内置角色
3. `CachedModelStore` 中的旧外部本地路径：
   - 能导入则迁移进角色库
   - 不存在则移除
4. `BuiltIn` 与 `Workshop` 内容保持兼容，不做破坏性迁移

## 7. 分阶段实施计划

### Phase 1：角色库基础设施

目标：

- 先让“角色库”成为一个真实存在的安装目录和数据概念。

任务：

- 定义角色库根目录与目录约定。
- 定义 `schemaVersion = 2` 的模型包 manifest。
- 实现 `ModelLibraryPaths`。
- 实现角色库扫描服务。
- 扩展 `FileSystemContentCatalog` 以读取新增字段。
- 明确角色库条目数据结构。

完成标准：

- 能扫描角色库根目录并列出已安装模型。
- 每个条目都能解析来源、标题、封面和入口模型路径。

### Phase 2：本地导入链路

目标：

- 跑通“选择本地 `.vrm` -> 安装到角色库 -> 立即可用”。

任务：

- 实现 `ModelImportService`。
- 校验 `.vrm` 输入。
- 计算 `fingerprint` 并做去重判断。
- 将模型复制到 `<model-id>/model.vrm`。
- 生成 `item.json`。
- 生成默认预览图占位策略。
- 返回导入结果供 UI 决定是否立即切换。

完成标准：

- 本地导入后，原文件路径不再参与后续加载。
- 导入结果在角色库中可见。
- 用户可一键切换到新导入角色。

### Phase 3：设置与旧数据迁移

目标：

- 让旧版本“路径缓存”平滑过渡到角色库。

任务：

- 实现 `ModelLibraryMigrationService`。
- 启动时迁移旧 `SelectedContentStore`。
- 启动时迁移旧 `CachedModelStore`。
- 为缺失文件提供安全降级。
- 将“当前选中角色”指向角色库中的有效项。

完成标准：

- 从旧版本升级后，不再依赖角色库外部路径。
- 丢失的旧路径不会导致错误状态或空白角色。

### Phase 4：角色库 UI 与菜单替换

目标：

- 用“角色库”取代当前“替换角色 -> 缓存路径列表”。

任务：

- 在 `DesktopPetRuntimeHud` 中新增：
  - `添加角色`
  - `角色库`
  - `Steam Workshop`
- 将当前缓存模型列表替换为角色库列表。
- 支持当前角色标记。
- 支持删除本地导入角色。
- 支持显示文件位置。

完成标准：

- 用户不需要理解任何绝对路径。
- “替换角色”不再暴露底层缓存实现。

### Phase 5：Workshop 收口

目标：

- 让 Workshop 内容也出现在统一角色库中。

任务：

- 扫描 Workshop 下载目录。
- 解析模型内容包并附加来源信息。
- 将角色库视图升级为聚合视图：
  - `BuiltIn`
  - `Local`
  - `Workshop`
- 为 Workshop 条目标记来源与 `sourceId`。

完成标准：

- 用户从统一角色库中浏览和应用 Workshop 模型。
- 本地导入与 Workshop 模型的切换操作保持一致。

### Phase 6：体验补强

目标：

- 把角色库从“能用”提升到“好用”。

候选任务：

- 自动生成预览图
- 搜索与标签
- 收藏与最近使用
- 重导入 / 刷新元数据
- 删除确认与磁盘占用提示

本阶段不阻塞主链上线。

## 8. 任务拆分建议

### Task Group A：角色库数据与路径

- 角色库根目录工具
- 角色库条目 DTO
- manifest v2 定义
- 哈希与去重服务

### Task Group B：导入服务

- 本地 `.vrm` 选择
- 导入校验
- 复制与落盘
- 去重返回结果

### Task Group C：迁移与兼容

- 旧设置迁移
- 缺失路径清理
- 内置与 Workshop 兼容回退

### Task Group D：UI 与运行时接线

- `添加角色` 菜单项
- `角色库` 列表 UI
- 立即应用
- 删除与显示文件

### Task Group E：Workshop 聚合

- 下载目录扫描
- 来源标记
- 聚合排序与展示

## 9. 验收标准

满足以下条件即可认为主计划落地：

1. 本地 `.vrm` 导入后，会被复制到角色库，而不是直接引用原路径。
2. 删除或移动原始 `.vrm` 文件，不影响角色库中的已导入角色。
3. 当前角色选择与最近角色列表不再依赖角色库外部路径。
4. 用户可从角色库中查看并切换本地与 Workshop 角色。
5. 升级到新版本后，旧的本地外部路径设置能够被迁移或安全清理。
6. 运行时 UI 中不再向用户暴露绝对路径。

## 10. 风险与控制

### 风险 1：旧路径迁移不完整

控制：

- 迁移逻辑单独封装
- 所有外部本地路径必须经过“导入或清理”两分支

### 风险 2：角色库与现有内容扫描重复建模

控制：

- 角色库优先复用现有 `FileSystemContentCatalog`
- 只在必要处扩展 manifest 和来源信息

### 风险 3：预览图生成拖慢主线

控制：

- MVP 先允许占位图
- 预览生成后置为增强项

### 风险 4：Workshop 继续变成第二套系统

控制：

- UI 与运行时一律从聚合后的角色库视图消费
- 不新增只给 Workshop 使用的模型切换页面

## 11. 当前建议的实施顺序

推荐严格按下面顺序推进：

1. `Phase 1：角色库基础设施`
2. `Phase 2：本地导入链路`
3. `Phase 3：设置与旧数据迁移`
4. `Phase 4：角色库 UI 与菜单替换`
5. `Phase 5：Workshop 收口`
6. `Phase 6：体验补强`

原因：

- 先把“安装到角色库”的底层约束立住，再改 UI，风险最小。
- Workshop 必须建立在角色库和统一内容协议已经稳定的前提上。
- 预览、搜索、收藏这类体验增强不应阻塞本地主线落地。

## 12. 实施开始时的明确决定

当前可以直接执行的工程决定如下：

- 角色库根目录：`Application.persistentDataPath/Content/Models`
- 本地导入：只允许复制入库，不允许路径引用
- 运行时选择：优先继续兼容路径型 `SelectedContentState`，随后再演进到角色库 `id`
- UI 主入口：`添加角色`、`角色库`、`Steam Workshop`
- 首轮实现重点：`角色库基础设施 + 本地导入链路 + 旧数据迁移`

这意味着下一批实现不应先去做：

- Workshop 搜索页细节
- 复杂缩略图生成
- 上传器
- 换装

而应该先把“模型安装入库并可稳定切换”做完整。
