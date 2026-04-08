# vividsoul

[中文 README](README.md)

<p align="center">
  <strong>A native desktop character, not a chatbot wrapped in a window.</strong><br/>
  A <code>Unity 6</code>, <code>VRM</code>, and open-model desktop pet + AI companion experiment.
</p>

<p align="center">
  <img alt="stage" src="https://img.shields.io/badge/stage-prototype-black" />
  <img alt="platform" src="https://img.shields.io/badge/platform-macOS-555555" />
  <img alt="engine" src="https://img.shields.io/badge/engine-Unity%206-111111" />
  <img alt="avatar" src="https://img.shields.io/badge/avatar-VRM-4b5563" />
  <img alt="workflow" src="https://img.shields.io/badge/workflow-vibe%20coding-7c3aed" />
  <img alt="policy" src="https://img.shields.io/badge/contrib-AI%20only-0f766e" />
</p>

`vividsoul` is not trying to be another web chat shell. It is trying to become a character system that can actually live on the desktop: imported, clicked, dragged, spoken with, remembered, and felt as a persistent presence.

The active track is to converge those capabilities into a genuinely usable native `macOS` experience, with the main implementation under `VividSoul/`.

## Why It Hits Different

- It puts `AI` inside a desktop character instead of putting a character skin around a chat box
- It treats presence as a first-class product quality, alongside actions, state, reminders, and proactivity
- It uses `VRM` as the character substrate and aims for a real loop of import, runtime use, and long-term companionship

## Current Focus

Right now, the first phase is focused on four things:

- Character: import and run `VRM` avatars, then grow toward a real local character library
- Interaction: polish desktop-pet style clicking, dragging, actions, bubbles, and desktop presence
- Conversation: wire in local or open-model chat as actual runtime behavior, not just static UI
- Memory: turn memory, reminders, and proactive messages into durable character state

## Quick Links

- Current status: `docs/STATUS.md`
- Doc index: `docs/README.md`
- Product direction: `docs/prd.md`
- Architecture: `docs/architecture.md`
- Main Unity project: `VividSoul/`

## Vibe Coding Notice

This repository explicitly runs on a `vibe coding` workflow: get the experience loop, interaction feel, and system boundaries working first, then harden the abstractions, architecture, and engineering quality afterward.

That also means you should assume the following risks are real:

- Code quality risk: some implementations prioritize iteration speed over ideal structure or robustness
- Reinventing-the-wheel risk: similar capabilities may be implemented more than once before the design fully settles
- Refactor churn risk: paths that work today may later be merged, replaced, or removed

So the right mental model for this repo is not "finished architecture", but "a live prototype with clear product intent".

## Contribution Protocol

This project currently accepts code changes only when they are generated or primarily driven by an `AI` coding agent.

To keep provenance explicit, every `commit` or `PR` must include a `coding agent + model` attribution tag. The recommended suffix format is:

```text
[agent: Cursor][model: GPT-5.4]
```

Example:

```text
feat: add local reminder retry path [agent: Cursor][model: GPT-5.4]
```

For `PR`s, the same attribution can appear in the title or clearly in the body. Changes without this marker are considered out of policy for this repository.

## Repository Map

The repository root is the coordination layer, while the main Unity project lives in `VividSoul/`.

| Path | Purpose |
|------|---------|
| `VividSoul/` | Main Unity 6 project with runtime code, assets, and `StreamingAssets` |
| `docs/` | Canonical project docs: `PRD`, architecture, plans, status, and lessons |
| `AGENTS.md` | Repository-wide agent working rules |
| `Builds/` | Build outputs such as `Builds/VividSoul/macOS/` |
| `Exports/` | Generated assets and tool logs |
| `downloads/` | Raw third-party downloads and reference material |
| `tmp/` | Temporary screenshots and scratch files |
| `scripts/` | Repository scripts for build, export, and cleanup workflows |

## Common Commands

- macOS batch build: `scripts/build-vividsoul.sh`
- Idle bake export: `scripts/export-vrma-idle-bake.sh`
- Clear temporary files: `scripts/cleanup-temp.sh`

See `scripts/README.md` for environment variables and troubleshooting details.

## Default VRM Credit

The intended built-in default `VRM` model filename is `8329754995701333594.vrm`.

Source page:
[https://hub.vroid.com/characters/360475117069004909/models/5726275402896222509](https://hub.vroid.com/characters/360475117069004909/models/5726275402896222509)

Contributor: `Dairlsuta`

## License

Repository code is licensed under `Apache-2.0`.

Third-party code, models, assets, and dependencies keep their own original licenses. The root `LICENSE` does not override those terms.
