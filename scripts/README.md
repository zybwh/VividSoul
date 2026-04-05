# Scripts

Bash helpers for repeatable Unity batch work from the repository root.  
All scripts assume macOS paths; Unity Hub installs are auto-detected when `UNITY_EDITOR` is unset.

## Environment

| Variable | Meaning |
|----------|---------|
| `UNITY_EDITOR` | Full path to the Unity executable, e.g. `/Applications/Unity/Hub/Editor/6000.3.3f1/Unity.app/Contents/MacOS/Unity` |
| `UNITY_PROJECT` | Optional override for the Unity project path (default: `<repo>/VividSoul`) |

## Commands

### `build-vividsoul.sh`

Runs a headless macOS player build via `-executeMethod VividSoul.Editor.VividSoulBuildTools.BuildMacOS`.  
Output: `Builds/VividSoul/macOS/VividSoul.app` (see `VividSoulBuildTools.cs`).

### `export-vrma-idle-bake.sh`

Runs the idle bake exporter via `-executeMethod VividSoul.Editor.VividSoulIdleBakeTools.ExportIdleBaseAssetsBatch`.  
Output: `Exports/VividSoul/generated/glb/` and `Exports/VividSoul/generated/vrma/`.

Requires default StreamingAssets inputs (model + pose VRMA) to exist.

### `cleanup-temp.sh`

Removes everything under `tmp/` (recreates an empty `tmp/`). Does not touch `downloads/`, `Exports/`, or `Builds/`.

## Logs

Batch Unity logs are written under `Exports/VividSoul/logs/` with a timestamped filename when using these scripts.
