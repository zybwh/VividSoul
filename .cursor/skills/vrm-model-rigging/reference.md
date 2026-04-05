# VRM Model Rigging — Reference Data

Raw analysis data from 5 production VRM models, used to derive the rigging skill.

## Model comparison matrix

| | M1 | M2 | M3 | M4 | M5 |
|--|-------|-------|-------|-------|-------|
| VRM ver | 0.x | 0.x | 0.x | 0.x | **1.0** |
| Total bones | 878 | 531 | 297 | 194 | 206 |
| Core (VRM) | ~50 | 55 | 65 | **52** | **52** |
| Twist | 8 | 8 | 20 | **0** | **0** |
| Deform helpers | 0 | 0 | 27 | **0** | **0** |
| Meshes | 2 | 2 | 2 | **3** | **3** |
| Body verts | 63K | 23K | 54K | 10K | 8K |
| Face verts | 6K | 3K | 3K | 4K | 4K |
| Hair verts | (in body) | (in body) | (in body) | 16K | **47K** |
| Shape keys | 117 | 46 | 12 | 57 | 57 |
| Bone rolls | all 0° | all 0° | all 0° | all 0° | 90°/180° |
| Spine connect | all True | all True | all True | all True | all False |
| Collider empties | 0 | 0 | 0 | **262** | 22 |
| Face binding | multi-bone | multi-bone | multi-bone | **rigid Head** | **rigid Head** |
| Outline mods | per-material | per-material | per-material | per-material | Hair only |

## Key conclusions per model

### M1 (878 bones, MMD-origin)
- Massive physics bone count (~500 cloth/hair)
- 117 atomic JP-named shape keys, composed into VRM expressions (joy = 7 SKs)
- 4 arm twist bones per side, 4 hand twist bones per side
- VRM 0.x, no upperChest

### M2 (531 bones, mechanical wings)
- Rich wing/accessory bones (68)
- 3 spring bone groups (accessories, cloth, breast)
- IK helper bones (12)
- All bone rolls = 0°

### M3 (297 bones, fantasy dress)
- **27 deform helper bones** (_B/_F/_M at every major joint)
- 20 twist bones (arm + forearm + thigh + calf)
- Helpers and main bone share exact head position, differ by weight direction
- Only model with upperChest mapped (among VRM 0.x)
- Knee: front verts → Knee_F, back verts → Knee_B

### M4 (194 bones, VRoid casual)
- **Zero twist, zero helpers** — standard 52 bones only
- 3 separate meshes (Body + Face + Hair)
- Face: 100% rigid binding to Head (4229 verts, all 1 bone per vert)
- 57 shape keys with Fcl_ naming (Fcl_EYE_, Fcl_MTH_, Fcl_BRW_, Fcl_ALL_, Fcl_HA_)
- 262 spring bone collider empties (richest physics collider setup)
- Perfectly horizontal arms (Z = 1.1612 across entire chain)
- Perfectly straight legs (X = 0.0686 across entire chain)

### M5 (206 bones, VRoid dark style, VRM 1.0)
- **VRM 1.0 spec** — different bone roll convention (arms 90°, legs 180°)
- Spine chain has small gaps (not use_connect)
- Same J_Bip/J_Sec/J_Adj naming as M4
- Same Fcl_ shape key system
- Body and Face have NO outline modifiers (only Hair)
- 143 secondary/physics bones

## Bone roll conventions

### VRM 0.x (models 1–4)
All bones: roll = 0.0°

### VRM 1.0 (model 5)
| Chain | Roll |
|-------|------|
| Spine | 0° |
| Arms | 90° |
| Legs | 180° |
| Feet/Toes | 0° |
| Fingers | 90° (varies for thumb) |
| Physics | Varied (±170° typical) |

## Bone positions (normalized to ~1.4m model)

Representative positions from M4 (scaled to percentage of total height):

| Bone | X% | Y% | Z% |
|------|-----|-----|-----|
| Hips | 0% | -0.3% | 63% |
| Spine | 0% | -1.1% | 67% |
| Chest | 0% | -1.3% | 74% |
| UpperChest | 0% | -0.3% | 81% |
| Neck | 0% | 2.3% | 90% |
| Head | 0% | 1.6% | 95% |
| Shoulder | ±1.5% | 1.7% | 88% |
| UpperArm | ±6.5% | 1.7% | 88% |
| LowerArm | ±22% | 1.7% | 88% |
| Hand | ±36% | 1.7% | 88% |
| UpperLeg | ±5.2% | 0% | 61% |
| LowerLeg | ±5.2% | 0.5% | 37% |
| Foot | ±5.2% | 2.1% | 10% |
| Toes | ±5.2% | -6.1% | 5% |

## Shape key naming convention (Fcl_ system)

| Prefix | Region | Examples |
|--------|--------|----------|
| Fcl_ALL_ | Full face preset | Neutral, Angry, Fun, Joy, Sorrow, Surprised |
| Fcl_EYE_ | Eyes only | Close, Close_L, Close_R, Angry, Fun, Joy, Joy_L, Joy_R, Sorrow, Surprised, Spread, Iris_Hide, Highlight_Hide |
| Fcl_MTH_ | Mouth only | A, I, U, E, O, Close, Up, Down, Angry, Small, Large, Neutral, Fun, Joy, Sorrow, Surprised, SkinFung |
| Fcl_BRW_ | Eyebrows | Angry, Fun, Joy, Sorrow, Surprised |
| Fcl_HA_ | Teeth/fangs | Hide, Fung1, Fung2, Fung3, Short (+ _Up/_Low variants) |

Minimum for VRM: EYE_Close, EYE_Close_L, EYE_Close_R, MTH_A/I/U/E/O

## VRM expression mapping patterns

### Pattern A: Single SK direct (recommended)

```
a → Fcl_MTH_A@1.0
blink → Fcl_EYE_Close@1.0
joy → Fcl_ALL_Joy@1.0
```

Used by M4 and M5. Simplest, most reliable.

### Pattern B: Multi SK composition

```
joy = ウィンク@1 + ウィンク右@1 + ワ1@1 + 口横広げ@partial + にこり@1
angry = ｷﾘｯ@partial + じと目@partial + 瞳小@partial + 怒り@1
```

Used by M1, M2. Maximum expressiveness but complex.

## Spring bone parameters

| Use case | Stiffness | Gravity | Drag | Radius |
|----------|-----------|---------|------|--------|
| Hair | 0.8–1.0 | 0.00 | 0.1–0.4 | 0.001–0.020 |
| Cloth/skirt | 1.0–3.0 | 0.10–0.15 | 0.4–1.0 | 0.020 |
| Breast | 0.8–2.5 | 0.00 | 0.8 | 0.020 |
| Accessories | 1.0–3.0 | 0.00 | 0.4–1.0 | 0.020 |
| Hood/cape | 1.0 | 0.00 | 0.4 | 0.020 |

## MToon material parameters (from M2, 36 materials analyzed)

### Standard values across all 36 production materials

| Parameter | Value | Notes |
|-----------|-------|-------|
| Lit Color Factor | **(1.0, 1.0, 1.0, 1.0)** | Always white — texture provides color |
| Shade Color Factor | **(0.31, 0.31, 0.31)** | Consistent across 34/36 materials |
| BaseColor Texture = Shade Texture | **YES (100%)** | All 36 materials use the same image for both |
| GI Equalization | **0.9** | All materials |
| Alpha Mode | OPAQUE (body/face) or BLEND (hair/transparent parts) | |

### Per-part shading parameters

| Part type | Shading Toony | Shading Shift | Outline Width | Outline Color |
|-----------|---------------|---------------|---------------|---------------|
| Skin | 0.92 | **+0.52** | 0.0006 | (0.13, 0.08, 0.08) |
| Face | 0.92 | **+0.52** | 0.0006 | (0.13, 0.08, 0.08) |
| Hair | 0.78 | **-0.22** | 0.0006 | (0.17, 0.18, 0.25) |
| Clothing | 0.95 | **-0.05** | 0.0010 | (0, 0, 0) |
| Eyes | 0.95 | -0.05 | **0** (none) | — |
| Eye highlight | 0.95 | -0.05 | 0 | Shade Color = (1,1,1) |
| Eye white | 0.95 | -0.05 | 0 | Shade Color = (0.88, 0.88, 0.88) |
| Teeth | 0.95 | -0.05 | 0 | — |

### Exceptions

- Eye highlight (`9.Eye_Hi`): Shade Color = (1.0, 1.0, 1.0) — fully bright in shadow too
- Eye whites and irises: Shade Color = (0.88, 0.88, 0.88) — slightly less shadow darkening
- Eyes have **Emissive Factor = (0.058, 0.058, 0.058)** — subtle self-illumination

### Principled BSDF compatibility values

The Principled BSDF node (for Blender viewport preview) uses:
- Metallic = 1.0, Roughness = 1.0 (effectively disables specular)
- Base Color linked to texture

## Verification baseline values

From testing on M5 (known-good):

- CHECK 1: All meshes parent=Armature, have Armature modifier ✓
- CHECK 2: Armature matrix identity ✓
- CHECK 3: All VRM bone heads inside Body bounding box ✓
- CHECK 4: All VRM-mapped bones have >0 weighted vertices ✓
- CHECK 5: Rotating UpperArm -45° moves 1362/8278 body vertices, max displacement 0.46m ✓
- CHECK 6: Visual pose test shows correct deformation without separation ✓

## Blender MCP capabilities and limits

### Can do
- Create/edit armatures via `execute_blender_code`
- Set bone positions, rolls, parents, connections
- Auto weights (`parent_set(type='ARMATURE_AUTO')`)
- Rigid binding (vertex group assignment)
- Shape key creation via vertex displacement
- VRM property configuration
- VRM export
- Viewport screenshots
- Numerical verification via Python

### Cannot do
- Interactive weight painting (brush strokes)
- Interactive bone dragging
- Sculpt-quality shape key authoring
- Fine per-vertex weight adjustment at scale

### Mitigation
- Bone positions: compute from mesh vertex distribution, never guess
- Weights: auto weights + script cleanup (limit 4 bones/vertex)
- Shape keys: geometric transforms (close eye = move verts down)
- Verification: 6-step checklist with screenshot always last
