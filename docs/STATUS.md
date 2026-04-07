# VividSoul Status

- Date: `2026-04-06`
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
- Added a dedicated `LLM / TTS` integration plan covering provider abstraction, session and memory management, proactive mate messages, `LLM` settings UI, and a staged `TTS` framework rollout without enabling speech playback yet.
- Implemented the first `LLM settings` runtime skeleton: the right-click `设置` entry now opens a dedicated in-app settings window with a new `LLM` tab, multi-provider profile persistence, separate API key storage, and an overall `LLM` usage statistics panel ready for future provider calls to populate.
- Refined the new runtime settings window from a fullscreen-feeling overlay into a centered popup-style panel and fixed the `LLM` tab layout so its content area can render the provider fields, prompt settings, and usage stats instead of showing a blank pane.
- Reworked the `LLM` settings page layout into a more conventional form-based panel: provider editing, global dialogue policy, status hints, and statistics actions now use consistent row heights, label widths, spacing, and footer actions instead of the previous cramped debug-like layout.
- Added the first runtime chat interaction layer: `DesktopPetRuntimeHud` now keeps a persistent chat launcher and expandable chat panel alive in the main overlay, with message history, input send flow, a new `聊天` context-menu entry, and a temporary local placeholder reply path that also reuses the existing speech bubble channel.
- Replaced the chat panel's placeholder reply path with the first real `OpenAI-compatible` runtime chain: user sends now resolve the active provider from `LLM` settings, load the provider API key, persist per-character local chat history under `persistentDataPath/ai/sessions/`, record usage stats, and route successful model replies back into both the chat history and the existing speech bubble channel.
- Split `MiniMax` out into a dedicated runtime provider type instead of treating it as a generic `OpenAI-compatible` profile, and added compatibility normalization so existing MiniMax-looking profiles migrate automatically during settings load while keeping the special reasoning-split request path isolated from other providers.
- Retuned the runtime dialogue bubble for longer AI replies: the bubble body now allows a much wider text block, uses a larger minimum width for explicit multi-line replies, switches text alignment from centered to upper-left, and slightly loosens spacing so paragraph-style answers do not feel cramped or overly narrow.
- Added a first dialogue-output formatting layer for AI replies: provider output now gets normalized toward single-paragraph spoken dialogue, strips `<think>` / markdown structure / noisy line breaks, supports a tiny markdown-to-rich-text subset for assistant display only, and augments the runtime system prompt so the model avoids markdown-heavy or list-heavy responses in the first place.
- Fixed another runtime dialogue bubble sizing issue for wrapped or rich-text replies by making the height estimate account for actual generated line count instead of relying only on the old preferred-height path, reducing bottom clipping when responses become multi-line.
- Added a conversation-activity suppression window for fallback ambient pose rotation so the background random pose scheduler pauses during live chat and shortly after replies/errors, reducing the chance that idle pose cycling interrupts the visible dialogue.
- Retuned the runtime dialogue bubble again for wrapped Chinese dialogue: the content inset now reserves a larger rounded-corner safety area, actual rendered multi-line text triggers wider padding automatically, the bubble stroke was softened, very long replies now clamp to a max visible height with internal vertical scrolling that follows the newest revealed text instead of overflowing the screen, and non-scrolling replies now use a vertically centered body layout with preserved top/bottom padding instead of reading like top-pinned debug text.
- Replaced the runtime dialogue bubble's old `Outline`-style hard border with a layered soft-stroke silhouette built from dedicated background pieces, avoiding the visibly upscaled pixel-edge artifact that appeared on larger bubbles.
- Added a dedicated implementation plan for a `WS-only` `OpenClaw` gateway provider, covering backend abstraction beyond one-shot HTTP providers, gateway session binding per character, proactive message reception, transcript mirroring, and dedicated settings / diagnostics UI.
- Reworked runtime chat from a single one-shot `MateConversationOrchestrator` path into a unified conversation-backend service, keeping the existing local providers alive while allowing HUD and chat UI to consume provider-agnostic message / state events.
- Added the first active `OpenClaw` runtime integration pass: a dedicated `OpenClaw` provider type, provider-profile persistence for gateway settings, a `Gateway WebSocket` client with session create / subscribe / send flows, per-character session-key binding, local transcript mirroring, unread tracking, and chat / speech-bubble updates driven from the unified event stream.
- Retuned the `OpenClaw` runtime path against a real gateway: the WS handshake now uses the gateway-compatible `cli` client identity, outbound chat turns now go through `chat.send`, UI completion waits for the matching `chat` final event instead of the initial RPC ack, and assistant message deduplication avoids duplicate display when both `chat` and `session.message` arrive for the same turn.
- Hardened `OpenClaw` connection reuse: token changes no longer reuse an existing WS session, and auth / scope failures now invalidate the current connection so the next attempt is forced to perform a fresh `connect` handshake with updated scopes.
- Switched the runtime `OpenClaw` send path to a hybrid transport that matches the currently available gateway permissions: chat submission now goes through `HTTP /v1/chat/completions` with `x-openclaw-session-key`, while the existing `WebSocket` connection is kept only for passive `agent` / `chat` event intake and falls back to the HTTP response body if the matching final event is not observed in time.
- Updated the Unity macOS player configuration so non-secure HTTP connections are explicitly allowed for the current plain-HTTP OpenClaw gateway endpoint, fixing the player-side `Insecure connection not allowed` failure that was caused by `PlayerSettings.insecureHttpOption` still being disabled.
- Corrected the hybrid `OpenClaw` turn-completion logic after validating the live gateway: the HTTP response body can return a placeholder like `No response from OpenClaw.` even when the real assistant text arrives over `WebSocket chat:final`, so the runtime now treats HTTP as fire-and-forget submit transport and waits for the next session-scoped assistant final event instead of trusting the HTTP body as the final reply.
- Added targeted runtime diagnostics for the `OpenClaw` hybrid path so `Player.log` now records HTTP submit attempts/results, WebSocket connect and frame summaries, and assistant-turn lifecycle transitions under `[OpenClawHttp]`, `[OpenClawWs]`, and `[OpenClawBackend]` prefixes for future bug triage.
- Adapted the `OpenClaw` client to treat `NO_REPLY`-style outputs as an intentional "no reply" protocol signal instead of user-visible dialogue: exact fallback markers such as `NO`, `NO_REPLY`, and `No response from OpenClaw.` are now suppressed from the chat UI, while the temporary debugging logs added during protocol triage were reduced back down to only key failure warnings plus a single no-reply suppression record.
- Replaced the runtime dialogue bubble's remaining body/tail bitmap edge dependency with Unity 6.3 Vector Graphics runtime generation: the main bubble and tail pieces are now built from SVG-derived vector sprites at their actual display size, leaving only the soft shadow as a bitmap layer so enlarged dialogue bubbles no longer inherit the old `Modern UI Pack` pixel fringe.
- Adjusted the Vector Graphics experiment to rasterize the generated vector bubble pieces into high-resolution runtime textures before assigning them to `uGUI Image`, avoiding the visibility issue from feeding raw vector-built sprites directly into the existing speech-bubble UI while still removing dependence on the old low-resolution border sprites.
- Replaced the unstable player-side vector sprite path with direct runtime generation of high-resolution anti-aliased rounded-rect / circle mask textures for the speech bubble body and tail, keeping the new non-bitmap-scaling approach while restoring reliable visibility inside the existing `uGUI Image + Mask` pipeline.
- Fixed wide runtime dialogue bubbles clipping on the right edge by making the procedural bubble raster scale adapt to the requested body size instead of always assuming a fixed `4x` texture scale, and cached the fixed tail-circle sprites to avoid recreating the same small textures every message.
- Refined long runtime dialogue bubble overflow handling so scrollable bubbles keep their full top/bottom content padding and no longer auto-pin to the latest typed line, reducing the "both ends look cut off" failure mode on tall Chinese replies.
- Added a max-height constrained scrollable viewport for long runtime context submenus so role-library and pose lists no longer overflow offscreen when the entry count grows.
- Fixed the procedural default-idle pose mix so only the sampled upper body from `VRMA_01` is preserved while hips and legs fall back to each model's own base stance, reducing foot overlap on `VRM0.x` imports and keeping the editor idle-bake path aligned with the runtime rule.
- Added a first `设置 -> 常规` role-library management section that lists imported local characters and supports in-place apply plus two-step confirmed delete for managed library items.
- Switched newly imported local model-library directories from full-hash names to `title-shortHash` form, added startup migration for legacy hash-only local library folders, and shortened the settings UI to show compact relative storage paths instead of full absolute library locations.
- Added a GLB metadata name probe for `.vrm` imports so new local-library items and legacy cleanup both prefer real VRM meta names over numeric file names, allowing the remaining hash-prefix fallback directories to be normalized into readable role names.
- Added a dedicated runtime UI restructure plan to address the growing interaction-shell mismatch between the right-click menu, chat launcher, settings popup, role-library management, and status feedback layers before continuing piecemeal UI fixes.
- Locked the first runtime-shell direction in the new UI plan: no permanent floating main-entry button, keep a linear right-click menu as the single navigation entry, remove cascading submenus, and route `聊天 / 角色库 / 动作` into one shared main panel instead of continuing to scatter them across buttons, menus, and settings.
- Refined the new runtime-shell plan after product review: `动作` is no longer treated as a peer user-facing page, but as a Unity-owned predefined action catalog selected by `LLM` or runtime logic via structured intents, with the old action entry expected to be replaced by a low-frequency `动作管理 / 注册` surface.
- Added typography and UI-asset direction to the runtime-shell plan: stop treating `Modern UI Pack` as a long-term visual dependency, prefer self-controlled runtime visuals, and standardize on calling system-installed fonts for display instead of bundling system font files into the app.
- Unified runtime UI font resolution across `DesktopPetRuntimeHud`, chat, settings, and speech bubbles via a shared system-font resolver so the menu and status text now follow the same `PingFang SC` / `微软雅黑`-first display strategy as the newer UI surfaces instead of falling back to `Arial` by default.
- Started executing the runtime-shell restructure plan in code: the standalone chat launcher is no longer the default runtime entry, the right-click menu now surfaces `角色库` as a first-level command instead of a submenu, and the settings-side role-library view now consumes the real managed model library with import/apply/delete actions instead of the old recent-cache list.
- Removed the obsolete right-click `随机走动` and `应用示例移动行为` commands now that they no longer fit the new runtime-shell direction and are no longer considered user-facing entry points.
- Added a dedicated local-agent planning document for pure `OpenAI-compatible / MiniMax` style provider paths, fixing the design split between `Soul`, layered `Memory`, and durable `heartbeat/reminder` scheduling while explicitly keeping this work separate from the current `OpenClaw` transport integration.

## Known Issues

- Unity build output was previously written under `VividSoul/Builds/`; this is being moved to a root-level `Builds/VividSoul/` layout.
- Some old exported assets and screenshots were previously written directly to the repository root.
- `utsuwa` remains in-tree for reference, so project rules must keep it read-only without hiding it from search entirely.

## Next Steps

- Verify the new Unity build output path inside the editor workflow.
- Verify the new export path used by VRMA tooling and batch utilities.
- Re-test the macOS "添加角色" flow on a non-primary Space to confirm the file dialog no longer opens a frozen duplicate panel.
- Connect the runtime dialogue bubble channel to future backend-driven dialogue / structured text responses instead of only the current 7 built-in pose test lines.
- Verify the new runtime chat path against at least one real configured provider and then extend the current recent-turns only session storage toward summary memory and proactive mate messages.
- Expand the provider layer beyond `OpenAI-compatible` and `MiniMax` only after the new dedicated provider split proves stable in real usage.
- Decide whether the next AI integration milestone should prioritize the dedicated `OpenClaw` realtime gateway backend over additional direct provider adapters.
- Re-validate the updated `OpenClaw` hybrid runtime path against the shared remote gateway, especially passive `chat` / `agent` event coverage for proactive outputs, late-event deduplication after HTTP fallback, and behavior after reconnects.
- Continue reducing root-level clutter by keeping all future downloads, exports, and screenshots inside their dedicated directories.
- Turn the locked runtime interaction-shell plan into a concrete implementation breakdown, especially the unified main panel container, the first-pass right-click menu item set, and the migration path for removing the standalone chat launcher and submenu-based role/action lists.
- Define the first constrained `LLM -> ActionIntent -> Unity ActionDispatcher` contract so the future chat flow can trigger only pre-registered actions while the user-facing shell continues to simplify.
- Unify the remaining runtime UI text surfaces onto the same system-font resolution path so menus, settings, chat, and speech bubbles stop mixing `Arial` fallback-only behavior with platform-native font selection.
- Continue replacing the remaining `Modern UI Pack` hard dependencies, starting with the runtime context-menu prefab path and speech-bubble shadow asset, now that font selection is no longer coupled to the old pack defaults.
- Continue the runtime-shell migration by replacing the remaining role/action submenu flows with the planned unified main-panel structure, now that the first bridge step away from launcher-plus-submenu navigation is in place.
- Turn the new local-agent `Soul / Memory / Heartbeat` plan into an implementation breakdown for the pure local-provider path, especially the markdown-first memory storage layout, hidden thread compaction pipeline, and restart-safe reminder scheduler.
