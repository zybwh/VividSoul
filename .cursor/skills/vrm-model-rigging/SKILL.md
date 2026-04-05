---
name: vrm-model-rigging
description: Rig, bind, add expressions, and export VRM character models in Blender via Blender MCP. Use when the user asks to create a VRM from a mesh, rig a character for VRM export, add shape keys or expressions, fix broken rigging, or prepare a 3D model for use in VRM-based apps like VividSoul. Covers armature creation, weight painting, shape keys, MToon materials, spring bones, and the full VRM export pipeline with built-in verification.
---

# VRM Model Rigging

Rig meshes and export VRM characters using Blender MCP (`execute_blender_code`, `get_viewport_screenshot`).

Derived from reverse-engineering 5 production VRM models. See [reference.md](reference.md) for raw data.

## Default workflow

Copy this checklist and work through it:

```text
VRM Rigging Checklist
- [ ] 1. Inspect source mesh (verts, bounds, transforms)
- [ ] 2. Prepare mesh (apply transforms, separate Face, optional Hair)
- [ ] 3. Create armature (52 VRM bones, positions from mesh analysis)
- [ ] 4. Parent & weight (auto weights, then clean)
- [ ] 5. Create shape keys on Face mesh
- [ ] 6. Setup MToon materials (CRITICAL for correct brightness/color)
- [ ] 7. Set VRM properties (bone mapping, expressions, meta)
- [ ] 8. Run 6-step verification
- [ ] 9. Export VRM
- [ ] 10. Final verification screenshot
```

## 1. Inspect source mesh

Before any rigging:

```python
obj = bpy.data.objects['YOUR_MESH']
m = obj.data
print(f'Verts: {len(m.vertices)}, Polys: {len(m.polygons)}')
# World-space bounding box
import mathutils
ws = [obj.matrix_world @ mathutils.Vector(v) for v in obj.bound_box]
min_z = min(v.z for v in ws); max_z = max(v.z for v in ws)
print(f'Height: {max_z - min_z:.4f}m, Z range: [{min_z:.4f}, {max_z:.4f}]')
# Check for residual transforms
print(f'Rotation: {obj.rotation_euler[:]}')
print(f'Scale: {obj.scale[:]}')
print(f'Matrix identity: {obj.matrix_world.is_identity}')
```

If mesh has non-identity transforms (common with GLB imports): **apply all transforms first**.

```python
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
```

## 2. Prepare mesh

### Target architecture: 3 meshes

| Mesh | Content | Shape Keys | Binding |
|------|---------|------------|---------|
| Body | body, clothes, accessories | **0** | Auto weights to armature |
| Face | face, eyes, mouth, brows | **all** | Rigid to Head bone (1 bone per vertex) |
| Hair | hair strands | **0** | Physics bones + Head |

### AI-generated mesh fix (Hunyuan3D, etc.)

AI-generated meshes (Hunyuan3D, Meshy, etc.) are typically **fragmented** — hundreds of disconnected islands instead of a single connected surface. This causes vertices to fly off during bone deformation. Professional VRM models are always single connected surfaces per mesh object.

**Diagnosis**: Count mesh islands. If > 1 island per mesh, apply the fix below.

```python
# Check island count
bpy.ops.object.mode_set(mode='EDIT')
bm = bmesh.from_edit_mesh(obj.data)
bm.verts.ensure_lookup_table()
bpy.ops.mesh.select_all(action='DESELECT')
bm.verts[0].select = True
bpy.ops.mesh.select_linked()
linked = sum(1 for v in bm.verts if v.select)
# If linked < total_verts → fragmented mesh, needs remesh
```

**Fix**: Voxel Remesh to merge fragments into a single surface, then decimate.

```python
# 1. Voxel Remesh — 8mm voxel bridges gaps between fragments
obj.data.remesh_voxel_size = 0.008
obj.data.use_remesh_fix_poles = True
obj.data.use_remesh_preserve_volume = True
bpy.ops.object.voxel_remesh()

# 2. Remove any remaining disconnected fragments
bpy.ops.object.mode_set(mode='EDIT')
bm = bmesh.from_edit_mesh(obj.data)
bm.verts.ensure_lookup_table()
bpy.ops.mesh.select_all(action='DESELECT')
bm.verts[0].select = True
bpy.ops.mesh.select_linked()
bpy.ops.mesh.select_all(action='INVERT')
bpy.ops.mesh.delete(type='VERT')
bpy.ops.object.mode_set(mode='OBJECT')

# 3. Decimate to ~15K polys (matching reference VRM models)
mod = obj.modifiers.new('Decimate', 'DECIMATE')
mod.ratio = 15000 / len(obj.data.polygons)
bpy.ops.object.modifier_apply(modifier='Decimate')
```

Target: **single connected surface, 10K–25K vertices** (reference models: M4=10K, M5=8K, M2=23K).

### Separation strategy

If starting from a **clean** single mesh (or after remesh):

1. For AI-generated models: keep as single Body mesh (separation creates holes)
2. For clean hand-modeled meshes: separate Face and Hair if distinct geometry exists
3. If separation is too complex, single Body mesh is valid (used by M1, M2, M3)

### Scale target

VRM models are typically 1.3m–1.7m tall. If the source mesh is a different scale, resize before rigging.

## 3. Create armature

### Bone position strategy

**Never guess bone positions.** Compute them from mesh geometry in ALL 3 axes:

**Y-axis (front-back) is the most common error.** The spine chain must sit at the front-back midpoint of the body cross-section, NOT at the centroid of all vertices (which is biased by hair/clothing). Use this pattern:

```python
def body_y_center(z, dz=0.04, x_range=(-0.08, 0.08)):
    """Find the Y midpoint between front and back body surface at height z."""
    pts = [v for v in verts_world if abs(v.z - z) < dz and x_range[0] <= v.x <= x_range[1]]
    if not pts:
        return 0
    ys = [v.y for v in pts]
    return (max(ys) + min(ys)) / 2  # Midpoint between front and back surface
```

For **limbs** (arms, legs), use the median of vertices filtered to the correct X range:

```python
def limb_center(z, dz=0.02, x_range=None):
    pts = [v for v in verts_world if abs(v.z - z) < dz]
    if x_range:
        pts = [v for v in pts if x_range[0] <= v.x <= x_range[1]]
    xs = sorted([v.x for v in pts])
    ys = sorted([v.y for v in pts])
    return Vector((xs[len(xs)//2], ys[len(ys)//2], z))
```

### Alignment verification (CRITICAL)

After creating the armature, verify alignment with KD-tree nearest-vertex check:

```python
from mathutils import kdtree
kd = kdtree.KDTree(len(mesh.vertices))
for i, v in enumerate(mesh.vertices):
    kd.insert(mesh_obj.matrix_world @ v.co, i)
kd.balance()

for bone in armature.data.bones:
    bone_world = arm_obj.matrix_world @ bone.head_local
    nearest_20 = kd.find_n(bone_world, 20)
    avg_y = sum(co.y for co, idx, dist in nearest_20) / 20
    dy = bone_world.y - avg_y
    # dy should be < 0.03 for all core bones
```

If any core bone has |dY| > 3cm, fix before proceeding.

### 52 VRM standard bones

Create exactly these bones. No Twist, no helpers — they require manual weight painting that MCP cannot reliably do.

Spine chain (all connected, roll=0):
- Root → Hips → Spine → Chest → UpperChest → Neck → Head

Arm chains (Shoulder NOT connected from UpperChest, rest connected):
- L/R Shoulder → UpperArm → LowerArm → Hand

Leg chains (UpperLeg NOT connected from Hips, rest connected):
- L/R UpperLeg → LowerLeg → Foot → Toes

Finger chains (first finger NOT connected from Hand, rest connected):
- L/R Thumb (3 bones), Index (3), Middle (3), Ring (3), Little (3)

Eyes (NOT connected):
- L/R Eye

### Bone placement rules

- **Arms perfectly horizontal**: UpperArm/LowerArm/Hand must share the EXACT same Z coordinate
- **Legs perfectly vertical**: UpperLeg/LowerLeg/Foot must share the EXACT same X coordinate
- **All rolls = 0.0°** (for VRM 0.x target)
- **Armature at origin**, identity transform

### Connection rules

| Segment | use_connect |
|---------|-------------|
| Spine chain (Root→Head) | True |
| UpperChest → Shoulder | **False** |
| Shoulder → UpperArm → LowerArm → Hand | True (except Shoulder→UpperArm can be True) |
| Hips → UpperLeg | **False** |
| UpperLeg → LowerLeg → Foot → Toes | True |
| Hand → Finger first joint | **False** |
| Finger internal joints | True |

## 4. Parent & weight

```python
# Select mesh first, then armature, make armature active
body.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_AUTO')
```

For Face mesh — **rigid binding** to Head:

```python
face.select_set(True)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.parent_set(type='ARMATURE_NAME')
# Then assign ALL face vertices to Head group with weight 1.0
vg = face.vertex_groups.get('Head') or face.vertex_groups.new(name='Head')
vg.add(list(range(len(face.data.vertices))), 1.0, 'REPLACE')
```

### Weight cleanup (after auto weights)

```python
# Limit each vertex to max 4 bone influences
for v in body.data.vertices:
    groups = sorted(v.groups, key=lambda g: g.weight, reverse=True)
    for g in groups[4:]:
        body.vertex_groups[g.group].remove([v.index])
    # Normalize remaining
    total = sum(g.weight for g in v.groups)
    if total > 0:
        for g in v.groups:
            g.weight /= total
```

## 5. Shape keys

Create on Face mesh only. Use `Fcl_` naming convention:

### Minimum set

```
Fcl_EYE_Close      — blink both eyes
Fcl_EYE_Close_L    — blink left
Fcl_EYE_Close_R    — blink right
Fcl_MTH_A          — mouth vowel A
Fcl_MTH_I          — mouth vowel I
Fcl_MTH_U          — mouth vowel U
Fcl_MTH_E          — mouth vowel E
Fcl_MTH_O          — mouth vowel O
```

### Extended set (recommended)

```
Fcl_ALL_Joy, Fcl_ALL_Angry, Fcl_ALL_Sorrow, Fcl_ALL_Fun
Fcl_BRW_Angry, Fcl_BRW_Sorrow
```

### Implementation approach

Since MCP cannot sculpt interactively, use geometric transforms:

- **Blink**: move upper eyelid vertices down toward lower eyelid
- **Mouth vowels**: identify lip vertices, apply displacement patterns (A=open wide, I=wide, U=pucker, E=wide-open, O=round)
- **Expressions**: combine eye narrowing + brow offset + mouth shape

Identify face landmark vertices by position analysis before creating shape keys.

## 6. MToon materials (CRITICAL)

**VRM models MUST use MToon materials, not PBR.** PBR materials will render extremely dark in VRM viewers because VRM runtimes expect MToon shading. This is the #1 cause of "VRM looks black/dark" issues.

### Converting from PBR to MToon

When importing from GLB/glTF, materials are PBR (Principled BSDF). Convert each material:

```python
import bpy

for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    ext = mat.vrm_addon_extension
    mtoon = ext.mtoon1

    # --- Core color parameters ---
    # Lit Color = WHITE (1,1,1,1) — let the texture provide all color
    mtoon.pbr_metallic_roughness.base_color_factor = (1.0, 1.0, 1.0, 1.0)

    # Shade Color = ~31% gray (production standard from 5 reference models)
    mtoon.extensions.vrmc_materials_mtoon.shade_color_factor = (0.31, 0.31, 0.31)

    # --- Shading parameters ---
    # Toony: 0.78~0.95 (higher = more cartoon-like, sharper light/shadow boundary)
    mtoon.extensions.vrmc_materials_mtoon.shading_toony_factor = 0.95

    # Shift: controls how much area is lit vs shaded
    #   Skin: 0.5 (positive = mostly lit, looks bright)
    #   Clothes: -0.05 (normal shadow distribution)
    #   Hair: -0.22 (more shadow for depth)
    mtoon.extensions.vrmc_materials_mtoon.shading_shift_factor = -0.05

    # GI Equalization: 0.9 (high value = ambient light contributes more = brighter overall)
    mtoon.extensions.vrmc_materials_mtoon.gi_equalization_factor = 0.9

    # Alpha mode
    mtoon.alpha_mode = 'OPAQUE'  # or 'BLEND' for transparent parts
```

### Key rules from production models

| Rule | Detail |
|------|--------|
| **Lit Color Factor = (1,1,1)** | Never darken this. All color comes from the texture. |
| **BaseColor Texture = Shade Texture** | Use the **same** image for both. Shade is just the base multiplied by Shade Color Factor. |
| **Shade Color ≈ (0.31, 0.31, 0.31)** | Shadows are 31% of lit brightness, NOT pure black. |
| **Skin gets positive Shading Shift** | +0.5 makes skin look brighter (more area in lit zone). |
| **Hair gets negative Shading Shift** | -0.22 gives hair more shadow depth/dimension. |
| **GI Equalization ≈ 0.9** | Makes the model respond well to ambient/environmental light. |

### Setting textures

```python
# For each material, assign the same diffuse texture to both BaseColor and ShadeMultiply
mat = bpy.data.materials['MyMaterial']
tree = mat.node_tree

# Find the existing texture image (from GLB import)
source_img = None
for node in tree.nodes:
    if node.type == 'TEX_IMAGE' and node.image:
        source_img = node.image
        break

if source_img:
    # Assign to BaseColor texture node
    bc_node = tree.nodes.get('Mtoon1BaseColorTexture.Image')
    if bc_node:
        bc_node.image = source_img

    # Assign SAME image to ShadeMultiply texture node
    shade_node = tree.nodes.get('Mtoon1ShadeMultiplyTexture.Image')
    if shade_node:
        shade_node.image = source_img
```

### Per-part tuning (recommended)

| Part | Shading Shift | Shading Toony | Outline |
|------|---------------|---------------|---------|
| Skin | **+0.52** | 0.92 | Width 0.0006, color (0.13, 0.08, 0.08) |
| Face | **+0.52** | 0.92 | Width 0.0006, color (0.13, 0.08, 0.08) |
| Clothes | **-0.05** | 0.95 | Width 0.0010, color (0,0,0) |
| Hair | **-0.22** | 0.78 | Width 0.0006, color (0.17, 0.18, 0.25) |
| Eyes | **-0.05** | 0.95 | **None** (width 0) |
| Eye highlight | **-0.05** | 0.95 | **None** — Shade Color = (1,1,1) for full bright |

### Outline setup

Outlines in VRM use Geometry Nodes modifiers (MToon Outline). They are automatically created by the VRM addon when exporting. Key parameters:

- **Outline Width Mode**: `worldCoordinates` (most common)
- **Outline Width**: 0.0006~0.0010 (meters in world space)
- **Outline Color**: dark tint matching the material (not pure black for skin)

### Common mistakes that cause dark VRM

| Mistake | Result | Fix |
|---------|--------|-----|
| Using PBR material instead of MToon | Entire model very dark | Convert all materials to MToon |
| Lit Color Factor ≠ (1,1,1) | Model dimmed/tinted | Set to white |
| Missing ShadeMultiply texture | Shadow areas are solid gray | Assign same texture as BaseColor |
| Shading Shift too negative | Too much shadow area | Use positive values for skin (+0.5) |
| GI Equalization = 0 | No ambient light response | Set to 0.9 |

## 7. VRM properties

### Bone mapping (VRM 0.x)

```python
ext = arm.data.vrm_addon_extension
ext.spec_version = '0.0'  # or '1.0'
vrm0 = ext.vrm0
bone_map = {
    'hips': 'Hips', 'spine': 'Spine', 'chest': 'Chest',
    'upperChest': 'UpperChest', 'neck': 'Neck', 'head': 'Head',
    'leftShoulder': 'LeftShoulder', 'leftUpperArm': 'LeftUpperArm',
    # ... all 52 bones
}
for vrm_name, blender_name in bone_map.items():
    for hb in vrm0.humanoid.human_bones:
        if hb.bone == vrm_name:
            hb.node.bone_name = blender_name
```

### Expression mapping (VRM 0.x)

Map shape keys to VRM blend shape groups: `a`, `i`, `u`, `e`, `o`, `blink`, `blink_l`, `blink_r`, `joy`, `angry`, `sorrow`, `fun`.

Use **single SK direct mapping** (not multi-SK composition):

```
a → Fcl_MTH_A@1.0
blink → Fcl_EYE_Close@1.0
joy → Fcl_ALL_Joy@1.0
```

### Metadata

Set title, author, version, and usage permissions.

## 8. Verification (CRITICAL)

Run ALL six checks after rigging. Do not skip any.

### CHECK 1: Parent chain

```python
for o in bpy.data.objects:
    if o.type == 'MESH':
        assert o.parent == arm, f'{o.name} not parented to armature!'
        assert any(m.type == 'ARMATURE' and m.object == arm for m in o.modifiers), \
            f'{o.name} missing Armature modifier!'
```

### CHECK 2: Transform clean

```python
assert all(abs(v) < 0.001 for v in arm.location), 'Armature not at origin!'
assert all(abs(v - 1) < 0.001 for v in arm.scale), 'Armature scale not 1!'
```

### CHECK 3: Bones inside mesh

```python
bbox = [body.matrix_world @ Vector(v) for v in body.bound_box]
min_xyz = Vector((min(v.x for v in bbox), min(v.y for v in bbox), min(v.z for v in bbox)))
max_xyz = Vector((max(v.x for v in bbox), max(v.y for v in bbox), max(v.z for v in bbox)))
for bone in vrm_mapped_bones:
    bh = arm.matrix_world @ bone.head_local
    inside = all(min_xyz[i] - 0.05 <= bh[i] <= max_xyz[i] + 0.05 for i in range(3))
    assert inside, f'BONE OUTSIDE MESH: {bone.name} at {bh[:]}'
```

**This catches the "bones separated from mesh" problem.**

### CHECK 4: Weight completeness

```python
for bone_name in vrm_bone_names:
    vg = body.vertex_groups.get(bone_name)
    if not vg: continue
    count = sum(1 for v in body.data.vertices
                for g in v.groups if g.group == vg.index and g.weight > 0.01)
    assert count > 0, f'Bone {bone_name} has ZERO weighted vertices!'
```

### CHECK 5: Deformation test (numerical)

```python
# Pose mode: rotate UpperArm, check vertices move
pose_bone.rotation_euler = (0, 0, math.radians(-45))
bpy.context.view_layer.update()
depsgraph = bpy.context.evaluated_depsgraph_get()
eval_mesh = body.evaluated_get(depsgraph).data
moved = sum(1 for i in range(len(eval_mesh.vertices))
            if (eval_mesh.vertices[i].co - original[i]).length > 0.001)
assert moved > 100, f'Only {moved} vertices moved — mesh not deforming!'
# Reset pose after test
```

### CHECK 6: Deformation test (VISUAL)

```python
# Apply test pose: arm down, leg forward, head turn
poses = {
    'LeftUpperArm': (0, 0, -60°),
    'RightLowerArm': (-90°, 0, 0),
    'LeftUpperLeg': (-45°, 0, 0),
    'Head': (0, 0, 15°),
}
# Then: get_viewport_screenshot
# Visually confirm: mesh follows bones, no tearing, no twisting
# RESET POSE before continuing
```

**Always take the screenshot. Numbers can pass while visuals fail.**

## 9. Export

```python
bpy.ops.export_scene.vrm(filepath='/path/to/output.vrm')
# Check return value — {'CANCELLED'} means export failed
```

Common export failures:
- Multiple armatures in scene → delete extras
- Missing bone mappings → check step 6
- VRM properties on wrong object → must be on the Armature's data

## 10. Post-export

1. Verify file exists and size is reasonable (> 1 MB for textured model)
2. Take final T-pose screenshot for the record
3. If targeting VividSoul: copy to `VividSoul/Assets/StreamingAssets/` or the model library path

## Failure recovery

| Symptom | Cause | Fix |
|---------|-------|-----|
| Bones floating outside mesh | Coordinate system mismatch (Y-up GLB vs Z-up Blender) | Apply transforms on mesh, rebuild armature |
| Mesh doesn't move with bones | Missing parent or Armature modifier | Re-parent with ARMATURE_AUTO |
| Twisted limbs on export | Wrong bone rolls | Set all rolls to 0° and re-export |
| VRM export cancelled | Multiple armatures or missing bone mapping | Clean scene, verify mapping |
| Shape keys have no effect | Shape keys on wrong mesh, or zero deltas | Create on Face mesh, verify vertex displacements |

## Additional resources

- For detailed per-model analysis data, see [reference.md](reference.md)
