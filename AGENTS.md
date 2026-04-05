# desktop_pet - Agent Working Context

This file defines the stable, repository-wide guidance for coding agents working in this project.
It should contain enduring rules and global context only.
Current progress, active milestones, and recent cleanup status belong in `docs/STATUS.md`, not here.

## 1. Repository Shape

- The repository root is the workspace-level coordination layer, not a dumping ground for generated files.
- `VividSoul/` is the primary Unity 6 desktop client and the current implementation core.
- `utsuwa/` is a read-only reference project. Use it for comparison and inspiration, but do not modify it unless the user explicitly asks.
- `docs/` is the canonical documentation root for PRD, architecture, plans, bug lessons, and status.
- `Builds/` stores build outputs for top-level projects.
- `Exports/` stores generated assets and tool logs.
- `downloads/` stores raw third-party downloads, unpacked packages, and other reference-only source materials.
- `tmp/` stores temporary screenshots and scratch images.
- `.cursor/skills/` stores project-local skills. Prefer the project-local version when a local skill overlaps with a global one.

## 2. Product Goal

`VividSoul` is an AI-driven, open-model, desktop-first 3D companion product:

- Unity native desktop client is the current mainline.
- `VRM` is the primary model format, with `VRMA` as the primary animation exchange format.
- The long-term system shape includes a separate backend "soul" layer, but the repository must keep the Unity client usable on its own.
- The product should support open model import, runtime character assembly, desktop pet interaction, and directory-based content packaging.

## 3. Stable Product Shape

- Treat `VividSoul` as the core Unity client, not as the whole repository.
- Keep project-wide docs, build artifacts, exports, temp files, and project-local automation at the repository root instead of burying them inside `VividSoul/`.
- Prefer directory-based content packages for models, animations, voice, and config rather than bespoke prefab-only pipelines.
- `utsuwa` is a baseline and reference source, not the implementation target and not the canonical source of truth.

## 4. Hard Constraints

- Do not modify `utsuwa/` unless the user explicitly requests it.
- Do not place new build outputs, exported assets, downloaded packages, logs, or temp screenshots directly in the repository root.
- Build outputs go under `Builds/<Project>/...`.
- Generated assets go under `Exports/<Project>/generated/...`.
- Tool logs and batch outputs go under `Exports/<Project>/logs/...`.
- Temporary screenshots and scratch images go under `tmp/`.
- Raw third-party downloads and unpacked reference packs go under `downloads/` and remain non-canonical.
- Do not treat `downloads/`, `Builds/`, `Exports/`, `tmp/`, or Unity cache folders as source code.
- When a document exists under `docs/`, that file is the canonical copy. Do not keep parallel working copies in the repository root or under `VividSoul/Docs/`.

## 5. Preferred Stack And Libraries

- Client: `Unity 6`
- Model/runtime: `UniVRM`, `VRM10`, `UniGLTF`
- Desktop windowing: `Kirurobo.UniWindowController`
- File dialogs: `StandaloneFileBrowser`
- Platform/workshop integrations: `Steamworks.NET` and adapters around platform SDKs
- `utsuwa` and its web stack remain reference material only unless the user explicitly asks to revive that path

## 6. Implementation Principles

- Prefer new product code under `VividSoul/Assets/App/`.
- Treat `VividSoul/Assets/ThirdParty/` as vendor code. Change it only when integration work truly requires it.
- Keep runtime and editor workflows explicit. Avoid hidden fallback paths and fix the source invariant instead of masking defects.
- Preserve filename conventions that the runtime already depends on, especially animation package keywords such as `idle`, `click`, and `pose`.
- If a tool or skill generates new `vrma`, `vrm`, or related deliverables, save them into the organized export directories instead of the root.
- Keep the repository legible by separating source, reference material, generated output, temporary files, and build artifacts.
- Only after completing a task that modified Unity-related code or assets under `VividSoul/` (for example `Assets/`, `Packages/`, `ProjectSettings/`, or packaged content the player loads): close the loop with a macOS player build from the repository root via `scripts/build-vividsoul.sh` (invokes `VividSoul.Editor.VividSoulBuildTools.BuildMacOS`; see `scripts/README.md` for `UNITY_EDITOR` / `UNITY_PROJECT`). Skip this when the task touched only docs, repo-root helpers, or other paths outside the Unity project with no impact on the built player.
- After a successful build from the step above: quit any **running VividSoul player** (the built `VividSoul.app` — not Unity Editor, not the batch `Unity` process doing the build), then launch `Builds/VividSoul/macOS/VividSoul.app` to verify.

## 7. Documentation Workflow

### Pre-flight reading

Read `docs/STATUS.md` at the start of each implementation session.
Read `docs/README.md` when the task spans multiple areas or when you need the canonical document layout.

Read additional context on demand:

- `docs/prd.md` for product direction and user-visible scope
- `docs/architecture.md` for runtime shape, content system, and platform integrations
- `docs/plans/` for active or historical plans
- `docs/decisions.md` for durable repo layout and process decisions
- `docs/bug-lessons.md` when fixing bugs or touching unstable areas

### Post-task updates

After completing work, update the relevant project documents:

- `docs/STATUS.md` for active progress, known issues, and next steps
- `docs/README.md` when the documentation structure changes
- `docs/decisions.md` when a durable convention or boundary changes
- `docs/architecture.md` for architecture or integration changes
- `docs/prd.md` only when the product direction changes
- relevant files under `docs/plans/` when planned work advances or changes
- `docs/bug-lessons.md` when a bug reveals a reusable lesson or systemic issue
