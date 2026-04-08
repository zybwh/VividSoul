# Bug Lessons

Record only meaningful, reusable lessons here.
This document is for design flaws, architectural misses, repeated integration mistakes, and debugging patterns worth remembering.

## Entry Template

- Date:
- Area:
- Symptom:
- Root cause:
- Fix:
- Reusable lesson:

## Entries

- Date: `2026-04-08`
- Area: `VividSoul` MiniMax structured judge prompts
- Symptom: Real reminder E2E for the local-provider path could stall or silently skip reminder creation even after the user message reached the provider, especially on `ReminderIntentJudge` / `MemoryJudge` style structured passes.
- Root cause: The MiniMax path was using `reasoning_split=true` with long, JSON-heavy judge prompts. In that mode the model could spend most or all of its token budget in `reasoning_content`, leaving final `message.content` empty; our structured passes only consumed final content, so reminder intent fell back to no-op. The first editor-driven E2E workaround also exposed that `Application.isBatchMode` cannot be queried from a background thread during batch execution.
- Fix: First shorten judge prompts and cap injected grounding context, then upgrade the provider abstraction to support OpenAI-style tool calls so `ReminderIntentJudge` / `MemoryJudge` can request structured arguments through `tool_calls` instead of depending on free-form JSON in final content. The batch transport switch also now avoids touching `Application.isBatchMode` unsafely off the main thread during editor-driven E2E runs.
- Reusable lesson: For MiniMax `reasoning_split` flows, prompt brevity and output-budget discipline matter more than adding more JSON rules. If a pass needs reliable structured output, prefer native tool/function calling over asking the model to print JSON in `message.content`, and keep plain-text JSON parsing only as a compatibility fallback rather than the primary protocol.

- Date: `2026-04-05`
- Area: `VividSoul` macOS desktop-pet file dialog integration
- Symptom: Opening "添加角色" on macOS could spawn a frozen dialog on the current desktop space while a second, interactive picker appeared on another Space.
- Root cause: The desktop-pet window uses `UniWindowController` / `LibUniWinC` for nonstandard window behavior, but the model import flow had switched to a separately managed native dialog path (`NSOpenPanel` / earlier AppleScript), causing Spaces and focus handling to diverge from the window stack that owns the pet overlay.
- Fix: Route the macOS model file picker back through `Kirurobo.FilePanel` on top of `LibUniWinC` so the chooser is created by the same window/plugin stack as the player window.
- Reusable lesson: On macOS desktop-overlay apps, native modal UI should stay inside the same window-management/plugin stack as the host window whenever possible; mixing separate focus/modal mechanisms can produce Space-specific duplicate or frozen dialogs even when each API works in isolation.

- Date: `2026-04-05`
- Area: `VividSoul` local import validation UX
- Symptom: Selecting the wrong file type during local import was technically blocked, but the app printed a full exception stack and gave the user no graceful in-app explanation.
- Root cause: User input validation was implemented by throwing a generic exception from the file-dialog service, and the runtime failure path treated it like an actual fault.
- Fix: Introduce a dedicated user-facing exception path and show the message through a lightweight HUD status banner while suppressing stack-trace style logging for expected validation failures.
- Reusable lesson: Expected user mistakes such as wrong file type, cancel, or unsupported content should use a separate validation/reporting path from real runtime faults so logs stay actionable and the UI can respond calmly.

- Date: `2026-04-05`
- Area: `VividSoul` runtime avatar swap and fallback motion
- Symptom: Replacing the current character could leave the new avatar stuck in T-pose, while subsequent pose clicks stopped responding and the player log spammed `NullReferenceException`.
- Root cause: `DesktopPetFallbackMotionController` cached bone `Transform` references from the previous model and tried to restore them after Unity had already destroyed the old avatar hierarchy during the swap.
- Fix: Prune invalid cached bone references before restoring, sampling, or blending fallback poses, then rebuild the cache from the newly loaded model root.
- Reusable lesson: Any Unity system that caches scene-object references across avatar or prefab swaps must treat destroyed objects as invalid on every reuse path, not only during initial rebinding.
