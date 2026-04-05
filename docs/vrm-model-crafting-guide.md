# VRM 模型制作指南

基于对 5 个制作精良的 VRM 参考模型的逆向分析，结合 Blender MCP 的实际工具能力，推导出的可执行制作规范。

> 核心原则：不是看到什么就用什么，而是基于真实数据推导最适合自己能力的方案。

> **可执行 Skill**：`.cursor/skills/vrm-model-rigging/SKILL.md` 包含完整的流程清单、代码模板和验证步骤。本文档保留分析数据作为背景参考。

## 五个参考模型一览

| | 模型1 | 模型2 | 模型3 | 模型4 | 模型5 |
|--|-------|-------|-------|-------|-------|
| VRM 版本 | 0.x | 0.x | 0.x | 0.x | **1.0** |
| 总骨骼 | 878 | 531 | 297 | 194 | 206 |
| 核心骨骼 | ~50 | 55 | 65 | **52** | **52** |
| Twist | 8 | 8 | 20 | **0** | **0** |
| 辅助骨 | 0 | 0 | 27 | **0** | **0** |
| 网格数 | 2 | 2 | 2 | **3** | **3** |
| Shape Keys | 117 | 46 | 12 | 57 | 57 |
| 骨骼 Roll | 全 0° | 全 0° | 全 0° | 全 0° | **非 0°(VRM 1.0)** |
| 脊柱连接 | 全 True | 全 True | 全 True | 全 True | 全 False(有间隙) |
| 碰撞体 | 0 | 0 | 0 | **262** | 22 |
| 面部绑定 | 多骨 | 多骨 | 多骨 | **刚性 Head** | **刚性 Head** |

**关键发现：模型4和5证明——只用 52 根标准骨骼、0 Twist、0 辅助骨，也能做出质量好的 VRM。**

---

## 第一部分：我的实际执行方案

### 骨骼方案

采用 **52 根 VRM 标准骨骼**（模型4/5的极简路线），不加 Twist 和辅助骨。

理由：
- Twist 骨需要精确手动涂权重（我通过 MCP 无法做到交互式权重涂绘）
- 辅助骨（_B/_F/_M）需要极细的方向性权重分流（5 个模型中只有 1 个用了）
- 模型4/5 证明标准集完全够用

骨骼规则：
- **Roll**：VRM 0.x 全部 0°；VRM 1.0 按标准惯例（手臂 90°，腿 180°）
- **手臂完美水平**：UpperArm → LowerArm → Hand 的 Z 坐标完全相同
- **腿完美竖直**：UpperLeg → LowerLeg → Foot 的 X 坐标完全相同
- **Armature 变换为 identity**
- **肩/髋 use_connect = False**，其余链内全连接

### 网格方案

采用 **3 个独立网格**（Body + Face + Hair）。

- Face **刚性绑定** Head 骨骼（100% 顶点只受 1 根骨影响，最简最稳）
- Hair 由物理骨骼驱动
- Body 使用自动权重 + 清理

### Shape Keys 方案

采用模型4/5 的 **Fcl_ 系统化命名**，~15 个起步：

```
必须：Fcl_EYE_Close, Fcl_EYE_Close_L, Fcl_EYE_Close_R
      Fcl_MTH_A, Fcl_MTH_I, Fcl_MTH_U, Fcl_MTH_E, Fcl_MTH_O
可选：Fcl_ALL_Joy, Fcl_ALL_Angry, Fcl_ALL_Sorrow, Fcl_ALL_Fun
      Fcl_BRW_Angry, Fcl_BRW_Sorrow
```

### 物理方案

- 基础 Spring Bone：头发 1~2 组，可选裙摆
- 碰撞体按需添加（不追求模型4 的 262 个极致）
- `secondary` Empty 作为容器

### MToon 材质方案（**不使用 PBR！**）

**VRM 必须使用 MToon 材质。PBR 材质在 VRM 查看器/运行时中会极其发黑。** 这是之前精灵女孩模型发黑的根本原因。

核心参数（36 个专业材质的统一标准）：

| 参数 | 值 | 解释 |
|------|-----|------|
| Lit Color Factor | **(1, 1, 1, 1)** | 必须白色——颜色完全由贴图提供 |
| Shade Color Factor | **(0.31, 0.31, 0.31)** | 阴影 = 原色 × 31%，不是纯黑 |
| BaseColor Tex = Shade Tex | **同一张图** | 阴影只是同贴图变暗，不换色 |
| GI Equalization | **0.9** | 环境光响应大，整体更亮 |

分区调参：

| 部位 | Shading Shift | Shading Toony | 效果 |
|------|---------------|---------------|------|
| 皮肤 | **+0.52** | 0.92 | 大面积亮区，皮肤看起来通透 |
| 头发 | **-0.22** | 0.78 | 更多阴影层次，头发有立体感 |
| 服装 | **-0.05** | 0.95 | 标准卡通分界 |
| 眼睛 | **-0.05** | 0.95 | 无描边；高光 Shade=(1,1,1) 全亮 |

> 详细代码见 `.cursor/skills/vrm-model-rigging/SKILL.md` 第 6 节。

---

## 第二部分：验证流程（关键！）

> 之前做的模型骨骼和网格分离了都没发现——**验证能力是核心短板。**

### 我能用的验证工具

| 工具 | 用途 |
|------|------|
| `execute_blender_code` | 数值检查（坐标、权重、变换矩阵） |
| `get_viewport_screenshot` | **视觉检查**（唯一能"看"的方式） |
| `get_scene_info` / `get_object_info` | 结构检查 |

### 六步验证清单（每次绑定后必须全跑）

#### CHECK 1：父子关系
```python
# 每个 Mesh 的 parent 是 Armature？有 Armature modifier？
for o in bpy.data.objects:
    if o.type == 'MESH':
        assert o.parent == arm
        assert any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers)
```

#### CHECK 2：变换干净
```python
# Armature location = 0, scale = 1, matrix = identity
assert arm.matrix_world.is_identity
```

#### CHECK 3：骨骼在网格内部
```python
# 每根 VRM 骨骼的 head 坐标在 Body mesh 的 bounding box 内
# 若任何骨骼在 mesh 外 → 骨骼和模型分离了！
```

#### CHECK 4：权重完整
```python
# 每根 VRM 映射骨骼对应的 Vertex Group 都有 > 0 的加权顶点
# 若某骨骼 0 加权顶点 → 这个骨骼没有控制任何网格！
```

#### CHECK 5：变形测试（数值）
```python
# Pose 模式旋转骨骼 → depsgraph 评估 → 检查顶点是否移动
# 若 moved_count = 0 → 骨骼动了但网格没跟！
```

#### CHECK 6：变形测试（视觉）
```python
# 设置测试 Pose（手臂下垂、弯腿、转头）→ 截图
# 人眼检查：网格是否跟随、有无撕裂、有无扭曲
# 这一步是 CHECK 5 的补充——数值通过不代表视觉正确
```

### 失败模式速查

| 症状 | 可能原因 |
|------|---------|
| 骨骼在 mesh 外面 | 坐标系不匹配（Y-up vs Z-up 未 apply） |
| 骨骼动了网格不动 | 未 parent、无 Armature modifier、Vertex Group 名称不匹配 |
| 网格扭曲/拧麻花 | 骨骼 roll 错误、权重脏（多骨竞争） |
| 关节处塌陷 | 权重过渡不平滑（自动权重的常见问题，可接受） |
| 导出 VRM 失败 | 多个 Armature、bone mapping 缺失、属性设在错误对象上 |

---

## 第三部分：各参考模型的技术细节

### 骨骼 Roll 规律

| VRM 版本 | 脊柱 | 手臂 | 腿 | 手指 | 物理骨 |
|----------|------|------|-----|------|--------|
| 0.x (模型1~4) | 0° | 0° | 0° | 0° | 0° |
| 1.0 (模型5) | 0° | 90° | 180° | 90° | 各异 |

VRM 0.x 和 1.0 使用不同的骨骼朝向约定。Blender VRM 插件导入时会自动处理。

### 连接模式

五个模型一致的规则：
- **肩从躯干偏移** → use_connect = False
- **髋从骨盆偏移** → use_connect = False
- **手指从手腕偏移** → use_connect = False
- **同一肢体链内部** → use_connect = True（或 VRM 1.0 中有微小间隙但仍算连接）

### 网格架构

| 模式 | 模型 | 优点 | 缺点 |
|------|------|------|------|
| 2 网格 (Body+Face) | 1, 2, 3 | 管理简单 | 头发和身体共享权重 |
| 3 网格 (Body+Face+Hair) | **4, 5** | Hair 独立由物理驱动，Face 刚性绑定最简 | 多一个对象 |

**推荐 3 网格**——Face 刚性绑定到 Head 是最安全的做法（4141/4229 个面部顶点 = 只受 1 根骨影响）。

### Shape Keys 命名规范

| 前缀 | 含义 | 示例 |
|------|------|------|
| Fcl_ALL_ | 全脸预设 | Fcl_ALL_Joy (眼+嘴+眉一起动) |
| Fcl_EYE_ | 仅眼部 | Fcl_EYE_Close |
| Fcl_MTH_ | 仅嘴部 | Fcl_MTH_A |
| Fcl_BRW_ | 仅眉毛 | Fcl_BRW_Angry |
| Fcl_HA_ | 牙齿/獠牙 | Fcl_HA_Fung1 |

### 物理系统对比

| | 模型1 | 模型2 | 模型3 | 模型4 | 模型5 |
|--|-------|-------|-------|-------|-------|
| Spring 组 | 多 | 3 | 3 | 7 | 多 |
| 碰撞体组 | 0 | 0 | 0 | **47** | 22 |
| 碰撞体 Empty | 0 | 0 | 0 | **262** | 22 |

碰撞体防止头发/裙摆穿入身体，是高级优化而非基础必需。

### 身体比例

| | 模型1 | 模型2 | 模型3 | 模型4 | 模型5 |
|--|-------|-------|-------|-------|-------|
| 总高 | ~1.7m | ~1.4m | ~1.59m | ~1.32m | ~1.48m |
| 头身比 | ~7 | ~8.7 | ~10.1 | ~9.6 | ~10.8 |
| 腿占比 | ~55% | ~60% | ~62% | ~60% | ~60% |

### VRM 表情映射

| 方式 | 模型 | 说明 |
|------|------|------|
| 多 SK 组合 | 1, 2 | joy = 7 个 SK 混合（日式极致） |
| 单 SK 直映 | 3, **4, 5** | joy = Fcl_ALL_Joy@1.0 |

**推荐单 SK 直映**——每个 VRM 表情对应 1 个预制好的 Shape Key，简单可靠。

---

## 第四部分：Blender MCP 工具能力边界

### 能做到的

| 能力 | 方式 |
|------|------|
| 创建/编辑骨架 | `execute_blender_code` → bpy.ops + edit_bones |
| 精确设置骨骼位置/roll | edit_bones.head/tail/roll 赋值 |
| Parent mesh → armature | `bpy.ops.object.parent_set` |
| 自动权重 | `parent_set(type='ARMATURE_AUTO')` |
| 创建 Shape Keys | `shape_key_add()` + 顶点偏移 |
| 设置 VRM 属性 | vrm_addon_extension API |
| 导出 VRM | `bpy.ops.export_scene.vrm()` |
| 视觉检查 | `get_viewport_screenshot` |
| 数值验证 | Python 脚本计算各种指标 |
| Pose 测试 | pose.bones 设旋转 + depsgraph 评估 |

### 做不到的

| 限制 | 影响 | 应对 |
|------|------|------|
| 无法交互式权重涂绘 | 不能精确手动调权重 | 靠自动权重 + 脚本清理 |
| 无法在视口中拖拽骨骼 | 骨骼位置必须用坐标数值设定 | 从网格顶点分布计算骨骼位置 |
| 无法创建精确的 Shape Key 变形 | 面部表情顶点偏移靠代码估算 | 用简单几何偏移（闭眼=眼部顶点下移） |
| 截图分辨率有限 | 细节可能看不清 | 多角度截图 + 数值双重验证 |

### 应对策略

1. **骨骼位置**：不凭猜测，而是先分析网格顶点分布，从顶点坐标统计中提取关节位置
2. **权重**：用自动权重后，脚本检查每顶点骨骼数，清理超过 4 根的脏权重
3. **Shape Keys**：用简单但可靠的几何变换（闭眼=Y 轴缩放，张嘴=下半嘴下移）
4. **验证**：六步清单每次必跑，视觉截图 + 数值双验证

---

*文档基于 2026-04 对 5 个参考模型的逆向分析 + Blender MCP 工具能力评估。*
