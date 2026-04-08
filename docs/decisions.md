# Design decisions

Short-lived task notes go in `docs/STATUS.md` or `docs/plans/`.  
This file records **durable** choices that should not be re-litigated on every cleanup.

## D-001 — Repository layout vs Unity project root

**Context:** Build outputs, downloads, generated VRMA/VRM, logs, and temp screenshots were mixing with source at the repo root and inside `VividSoul/`.

**Decision:** Keep `VividSoul/` focused on the Unity project. Use root-level buckets:

- `Builds/<Project>/...` for player builds
- `Exports/<Project>/generated/{vrma,vrm,glb}/` for tooling output
- `Exports/<Project>/logs/` for Unity batch logs and similar
- `downloads/` for third-party archives and unpacked reference packs
- `tmp/` for disposable screenshots and scratch files

**Consequences:** Editor scripts must resolve paths relative to the repo root (two levels above `Assets`), not only inside `VividSoul/`.

## D-002 — `utsuwa` is reference-only

**Context:** `utsuwa/` is a full product codebase copied in-tree for comparison.

**Decision:** Treat `utsuwa/` as read-only reference unless the product owner explicitly asks for edits there.

**Consequences:** Git and Cursor ignore rules may still exclude `utsuwa/` bulk or build artifacts to keep search usable; that does not imply permission to refactor it casually.

## D-003 — Canonical documentation under `docs/`

**Context:** PRD and architecture existed at repo root and duplicated under `VividSoul/Docs/`.

**Decision:** `docs/` is the single canonical documentation tree. Do not maintain parallel copies at root or under `VividSoul/Docs/`.

**Consequences:** Cross-links in markdown use paths relative to `docs/` (e.g. `plans/...`, `../prd.md` from `plans/`).

## D-004 — Ignore policy for generated vs curated output

**Context:** Generated VRMA/VRM, Blender intermediates, and batch logs should not inflate git history or invite accidental commits of multi-hundred-megabyte folders.

**Decision:** Ignore the entire `Exports/` tree, plus `VividSoul/*.log` (e.g. `Build_batchmode.log`). Continue ignoring `Builds/`, `downloads/`, `tmp/`, Unity `Library/`, etc.

**Consequences:** Tooling still writes to `Exports/<Project>/...` locally. Anything that must ship with the player belongs under `VividSoul/Assets/` (e.g. `StreamingAssets`) or another tracked path—not under `Exports/`.

## D-005 — Project-local Cursor skills

**Context:** VRMA authoring guidance mixed global workflow with VividSoul-specific runtime notes.

**Decision:** Keep a project-local skill under `.cursor/skills/vrma-motion-authoring/` with output paths aligned to this repo layout.

**Consequences:** Prefer this local skill in this workspace when it overlaps with a global copy.

## D-006 — AI-only code contribution provenance

**Context:** The repository is intentionally operated as an AI-first `vibe coding` project, which makes provenance and review context part of the product process rather than optional metadata.

**Decision:** This project accepts code changes only when they are generated or primarily driven by an AI coding agent. Every commit or pull request must include explicit `coding agent + model` attribution metadata.

**Recommended format:**

- `[agent: Cursor][model: GPT-5.4]`

**Consequences:**

- Commits without agent/model attribution are out of policy for this repository
- PRs should include the same attribution in the title or clearly in the body
- Reviewers should interpret the attribution as provenance metadata, not as a quality guarantee

## D-007 — Issue-first, agent-executed implementation flow

**Context:** This repository uses `vibe coding` as a product workflow, where the human side is expected to provide ideas, goals, and acceptance signals rather than personally carrying technical implementation.

**Decision:** New work should be described through issues first, with goals, expected outcomes, constraints, and acceptance criteria. Implementation is then primarily executed by AI coding agents instead of relying on humans to directly author the technical changes.

**Consequences:**

- Discussion should bias toward intent and acceptance, not early implementation detail
- Agents are the default execution path for turning issues into code
- The repository should document project-local skills and execution affordances that help agents complete common tasks

