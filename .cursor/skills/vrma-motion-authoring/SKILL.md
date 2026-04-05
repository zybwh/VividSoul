---
name: vrma-motion-authoring
description: Create, tweak, export, validate, and troubleshoot VRMA clips for VividSoul with Blender MCP and the VRM Add-on. Use when the user asks to make VRMA motions for VRM avatars, tune idle or gesture animations, debug compatibility, or organize generated VRMA and VRM outputs for this repository.
---

# VividSoul VRMA Motion Authoring

Use this project-local skill instead of the global version when working inside this repository.

## Repository-specific output rules

Always keep generated files out of the repository root.

- Full or compatibility `vrma` outputs: `Exports/VividSoul/generated/vrma/`
- Generated `vrm` outputs: `Exports/VividSoul/generated/vrm/`
- Generated `glb` outputs: `Exports/VividSoul/generated/glb/`
- Screenshots and scratch images: `tmp/screenshots/`
- Raw downloaded packs and unpacked references: `downloads/`

If you need a new subdirectory, create it under one of the locations above instead of inventing a new root-level folder.

## Default workflow

Copy this checklist and work through it:

```text
VRMA Authoring Checklist
- [ ] Confirm target motion, duration, frame rate, and style
- [ ] Inspect Blender MCP tools and current scene
- [ ] Ensure a VRM 1.0 humanoid armature exists
- [ ] Block out the pose and motion in Blender
- [ ] Validate planted feet / body drift quantitatively
- [ ] Export the full VRMA when needed
- [ ] Export a compatibility VRMA when needed
- [ ] Save outputs into Exports/VividSoul/generated/*
- [ ] Save screenshots into tmp/screenshots/
- [ ] Report final paths and any compatibility caveats
```

## VividSoul-specific guidance

- Optimize for what the current Unity runtime reliably consumes.
- Prefer pose-focused VRMAs first.
- Small `Hips` translation is acceptable when it materially improves the motion.
- Do not depend on VRMA-driven `lookAt` for shipping content.
- Treat expressions as optional until the target avatar proves they are useful.
- When packaging simple local motions for discovery, keep filename keywords consistent with the runtime conventions:
  - `idle`
  - `click`
  - `pose`

## Compatibility policy

When the user asks for VRoid Hub compatibility or stricter interchange safety:

- keep humanoid pose animation
- remove risky lookAt animation
- remove expression animation unless explicitly requested
- inspect the exported structure instead of guessing what caused rejection

## Deliverables

When closing the task, report:

- final export path or paths
- whether the result is full, compatibility-focused, or both
- any measured drift findings when realism matters
- any limitations that still depend on avatar setup or player support

## Additional resources

For concrete pitfalls and fixes discovered during real VRMA authoring in this repo, see [reference.md](reference.md).
