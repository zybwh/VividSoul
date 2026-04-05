# VRMA Authoring Reference

This file records concrete problems encountered while building and exporting VRMA clips with Blender MCP and the VRM Add-on.

## Problems seen in practice

### 1. Scene had no usable VRM humanoid

Symptom:

- Blender scene only contained default objects
- VRMA export prerequisite failed or had nothing valid to export

Fix:

- Create a standard VRM humanoid armature
- Set it to VRM 1.0
- Auto-assign required human bones
- Confirm the required-bone check passes before animating

### 2. Relaxed arm pose used the wrong local axis

Symptom:

- Lowering the arms made them point upward or outward

Cause:

- The arm bones were not rotating around the assumed axis

Fix:

- Inspect local axes for `upper_arm`, `lower_arm`, and `hand`
- Test a small rotation first
- Only then block in the neutral pose

## 3. Blender 5.1 action API mismatch

Symptom:

- Code using `action.fcurves` failed

Cause:

- In this Blender version, animation data lived under action `layers`, `strips`, and `channelbags`

Fix:

- Iterate fcurves through `layers -> strips -> channelbags -> fcurves`
- Apply interpolation and cyclic modifiers there

## 4. Timeline screenshot capture failed

Symptoms:

- `TIMELINE` was not a valid area type
- screenshot overrides failed after switching editors

Fix:

- Use `DOPESHEET_EDITOR` with `ui_type = TIMELINE`
- After changing the area type, reacquire the current `WINDOW` region

## 5. VRoid Hub rejected the VRMA with `Invalid path "translation"`

Observed structure:

- The exported file contained multiple `translation` channels
- Besides `Hips`, extra channels came from:
  - expression nodes such as `blink`, `oh`, and custom `breath`
  - lookAt target nodes

Fix:

- Parse the VRMA JSON chunk and map each `translation` channel back to its node
- Export a compatibility variant without expression and lookAt animation
- Remove empty `expressions` blocks if needed

Practical rule:

- For VRoid Hub compatibility, aim for mostly `rotation` channels and only the minimal safe extension keys

## 6. Hips stayed still but the head and feet still drifted

Symptom:

- The pelvis looked static, yet the head and feet still moved in world space

Cause:

- Torso rotations propagated through the chain and visually created head or foot drift even without explicit hips translation

Fix:

- Measure world-space drift for `head`, `hips`, `foot.L`, and `foot.R`
- Use numbers before making another subjective adjustment

## 7. Reducing amplitude was not enough to stop foot sliding

Symptom:

- Lower-body motion looked quieter, but the feet still slid on the floor

Cause:

- The stance problem was structural, not just amplitude-related

Fix:

- Add planted-foot IK:
  - foot target empty for each foot
  - knee pole target empty for each leg
  - IK on lower leg with chain length `2`
- Then animate small pelvis motion against the planted targets

This produced:

- real pelvis motion
- near-zero horizontal foot drift
- a more physically believable standing idle

## 8. Compatibility and realism need separate exports

Observed tradeoff:

- Full VRMA can contain richer expression and lookAt behavior
- VRoid Hub compatible VRMA often needs a stripped-down channel set

Default recommendation:

- Export both:
  - full version for capable VRMA players
  - compatibility version for stricter consumers

## 9. VividSoul-specific findings

Based on the current `VividSoul` Unity runtime:

- The safest and most valuable VRMA content is humanoid pose animation with restrained `Hips` translation.
- `VividSoul` clearly supports loop, play-once, hold-last-frame, and return-to-idle at the app layer.
- Preset and custom expressions have a runtime path, but should be treated as optional because their visible effect depends on the target avatar setup.
- `lookAt` exists in third-party runtime interfaces, but should not be treated as a dependable authoring target for `VividSoul` right now.
- The current package installer auto-discovers files by keywords in filenames:
  - `idle`
  - `click`
  - `pose`

Practical recommendation:

- For `VividSoul`, produce pose-focused VRMAs first.
- Add expression channels only after validating that the target avatar and runtime path actually benefit from them.
- Do not depend on VRMA-driven gaze control for shipping content yet.

## Recommended debug sequence

When a new VRMA looks wrong:

1. Inspect channel paths in the exported file
2. Measure world-space drift of key bones
3. Compare against a known-good sample VRMA if available
4. Separate full and compatibility goals
5. Re-export only after identifying which channels or constraints are causing the issue
