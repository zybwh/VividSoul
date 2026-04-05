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

## Known Issues

- Unity build output was previously written under `VividSoul/Builds/`; this is being moved to a root-level `Builds/VividSoul/` layout.
- Some old exported assets and screenshots were previously written directly to the repository root.
- `utsuwa` remains in-tree for reference, so project rules must keep it read-only without hiding it from search entirely.

## Next Steps

- Verify the new Unity build output path inside the editor workflow.
- Verify the new export path used by VRMA tooling and batch utilities.
- Continue reducing root-level clutter by keeping all future downloads, exports, and screenshots inside their dedicated directories.
