# desktop_pet

Workspace for **VividSoul** (Unity desktop pet client) and related assets. This repository root is the coordination layer; the Unity project lives under `VividSoul/`.

## Quick map

| Path | Role |
|------|------|
| `VividSoul/` | Unity 6 project: runtime code, assets, `StreamingAssets` |
| `docs/` | Canonical PRD, architecture, plans, status, bug lessons |
| `AGENTS.md` | Stable rules for agents working in this repo |
| `Builds/` | Player builds (e.g. `Builds/VividSoul/macOS/`) |
| `Exports/` | Generated outputs and logs (`Exports/VividSoul/generated/`, `Exports/VividSoul/logs/`) |
| `downloads/` | Raw third-party packs and unpacked reference material |
| `tmp/` | Temporary screenshots and scratch files |
| `utsuwa/` | Read-only reference app (do not change unless explicitly requested) |
| `scripts/` | Repo-local helpers (Unity batch build, idle bake export, temp cleanup) |

## Where to read first

1. `docs/STATUS.md`
2. `docs/README.md`
3. `docs/prd.md` and `docs/architecture.md`

## Common tasks

- **macOS player build (batch):** `scripts/build-vividsoul.sh` (set `UNITY_EDITOR` to your Unity binary path if auto-detect fails)
- **Idle bake VRMA/GLB export:** `scripts/export-vrma-idle-bake.sh`
- **Clear `tmp/`:** `scripts/cleanup-temp.sh`

See `scripts/README.md` for environment variables and troubleshooting.
