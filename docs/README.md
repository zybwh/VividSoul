# Documentation Guide

`docs/` is the canonical documentation root for this repository.
Do not keep parallel copies of the same document in the repository root or under `VividSoul/Docs/`.

## Structure

- `prd.md`: product direction, target users, and user-visible scope
- `architecture.md`: runtime architecture, content system, platform integration, and technical direction
- `plans/`: implementation plans, rebuild plans, and milestone execution documents
- `STATUS.md`: current focus, known issues, recent progress, and next steps
- `bug-lessons.md`: reusable lessons from meaningful bugs and architectural misses
- `decisions.md`: durable design decisions (repo layout, reference projects, ignore policy)

## Reading Order

Start here when you need project context:

1. `STATUS.md`
2. `prd.md`
3. `architecture.md`
4. the relevant file under `plans/`
5. `bug-lessons.md` when the task involves debugging or unstable areas
6. `decisions.md` when changing directory conventions, ignore rules, or reference-project policy

## Documentation Rules

- Keep only one canonical copy of each project document.
- Prefer updating an existing canonical document over creating a new sibling document with a similar name.
- Use `plans/` for execution-oriented documents and step breakdowns.
- Use `STATUS.md` for current state; `AGENTS.md` is for stable agent rules only, not live progress.
- Use `bug-lessons.md` for reusable engineering lessons, not one-off scratch notes.
