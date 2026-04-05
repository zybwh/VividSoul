# VividSoul Status

- Date: `2026-04-05`
- Phase: repository cleanup and Unity-first foundation
- Core project: `VividSoul`
- Reference project: `utsuwa` (read-only)

## Current Focus

- Clean up the repository layout so source, docs, downloads, generated assets, temp files, and builds are clearly separated.
- Treat `VividSoul` as the Unity implementation core without letting it become the storage location for every project-wide artifact.
- Move VRMA-related guidance into project-local Cursor skills and make its output paths explicit.

## Recent Progress

- Added a repository-level `AGENTS.md` with stable working rules.
- Standardized `docs/` as the canonical documentation root.
- Started consolidating duplicate docs from the root and `VividSoul/Docs/`.
- Started organizing downloads, generated VRMA assets, temp screenshots, and build/export locations at the repository root.
- Added root `README.md`, `docs/decisions.md`, and `scripts/` helpers (`build-vividsoul`, idle-bake VRMA export, `cleanup-temp`).
- Updated desktop pet movement so runtime move actions auto-apply the example movement behavior when no movement VRMA is configured, avoiding pure translation-only test moves.
- Fixed the built-in example desktop movement behavior manifest so its relative VRMA paths resolve from `Defaults/Behavior/example_desktop_move/` to `Defaults/Animations/`.
- Switched the built-in example desktop movement behavior to movement-only bindings and to the `_full` walk clips so applying movement support no longer overrides idle/click/pose and uses the higher-fidelity local walk exports.
- Authored and exported a new direct Blender-MCP walk loop (`VRMA_WalkInPlace_pose_loop_reauthored.vrma`) and pointed the example desktop movement behavior at it, with vertical movement falling back to the same loop until a dedicated vertical clip is reauthored.
- Fixed model replacement leaving the new avatar stuck in T-pose by clearing stale animation playback sessions when a new model finishes loading.
- Fixed fallback pose recovery during model replacement by pruning destroyed bone references before reusing cached humanoid transforms.
- Added a focused implementation plan for unifying local imports and Steam Workshop into a role-library based character management flow, with local models required to be copied into the library instead of referenced in place.
- Started implementing the model-library import flow: local VRM picks now import into `Application.persistentDataPath/Content/Models/` before loading, and legacy selected local model paths migrate into the library on startup.
- Fixed the macOS build pipeline to copy `StandaloneFileBrowser.bundle` into the built player so the new runtime "添加角色" file picker can open in the shipped app.
- Switched the macOS player model file picker from custom `NSOpenPanel` / AppleScript paths to `Kirurobo.FilePanel` via `LibUniWinC`, aiming to keep the dialog in the same windowing stack as the desktop-pet overlay and avoid the cross-Space frozen duplicate panel issue.
- Relaxed macOS file-dialog extension filtering for local imports so `.vrm` / `.vrma` / `.json` picks are validated after selection instead of relying on native filter disable-state behavior.
- Changed invalid local-import file picks to use a user-facing validation path instead of exception-style failure logging, and added a lightweight in-app HUD status message so wrong file types fail gracefully without polluting the model library.
- Added a dedicated implementation plan for runtime dialogue bubbles, covering bubble-style UI, typewriter text, adaptive sizing, auto-hide reuse, and the initial 7 built-in action test lines.
- Implemented the first approved runtime dialogue bubble feature in `DesktopPetRuntimeHud`: built-in poses now trigger reusable speech-bubble playback with typewriter text, adaptive sizing, auto-wrap, timed auto-hide, and a finalized shojo-manga-inspired dialogue bubble style built from `Modern UI Pack` visual resources plus runtime-composed UGUI layers.

## Known Issues

- Unity build output was previously written under `VividSoul/Builds/`; this is being moved to a root-level `Builds/VividSoul/` layout.
- Some old exported assets and screenshots were previously written directly to the repository root.
- `utsuwa` remains in-tree for reference, so project rules must keep it read-only without hiding it from search entirely.

## Next Steps

- Verify the new Unity build output path inside the editor workflow.
- Verify the new export path used by VRMA tooling and batch utilities.
- Re-test the macOS "添加角色" flow on a non-primary Space to confirm the file dialog no longer opens a frozen duplicate panel.
- Connect the runtime dialogue bubble channel to future backend-driven dialogue / structured text responses instead of only the current 7 built-in pose test lines.
- Continue reducing root-level clutter by keeping all future downloads, exports, and screenshots inside their dedicated directories.
