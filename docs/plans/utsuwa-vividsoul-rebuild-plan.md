# Utsuwa -> VividSoul Rebuild Plan

## Document Info

- Project: `VividSoul`
- Baseline: `utsuwa`
- Purpose: turn the current open-source AI companion app into an open-model, desktop-first 3D AI companion platform aligned with `../prd.md`
- Status: working draft
- Date: 2026-04-04

## 1. Executive Summary

`Utsuwa` is a strong implementation base for `VividSoul`, but it is not already the target product.

Its strengths match our technical needs:

- modern desktop-friendly stack: `SvelteKit + Svelte 5 + TypeScript + Three.js + Threlte + @pixiv/three-vrm + Tauri`
- existing VRM rendering, avatar display, chat UI, TTS, lip-sync, overlay window, and provider integrations
- an already working end-to-end product, which reduces time-to-MVP

Its core product philosophy does not match our target:

- `Utsuwa` is centered on a local-first AI companion / dating-sim experience
- `VividSoul` needs to be centered on open model support, desktop-pet interaction, and a backend-driven "soul engine"

Conclusion:

- keep the shell, rendering stack, desktop foundation, and voice pipeline
- replace or heavily refactor the AI contract, state model, product information architecture, and desktop interaction model
- treat this effort as a "rebuild on top of an existing shell", not as a light re-theme

## 2. Product Direction From PRD

The PRD defines `VividSoul` as a lightweight, desktop-first, AI-driven 3D companion product with the following core principles:

1. We do not create the model. We provide the "soul".
2. Any user-provided `VRM` or future `glTF` model should be usable.
3. Frontend and backend should be clearly separated.
4. The backend LLM should output structured control JSON, not only dialogue text.
5. The desktop experience must feel native: transparent overlay, always-on-top, drag, scale, hotkeys, click-through, tray/background behavior.
6. The platform must be NSFW-tolerant at the model level and configuration level.

This means the target product is not "an AI girlfriend app with one built-in worldview". It is a general-purpose AI desktop companion engine that can animate arbitrary imported characters.

## 3. What Utsuwa Is Today

### 3.1 Overall Positioning

`Utsuwa` is an open-source AI companion app with:

- VRM avatar loading and rendering
- chat with multiple LLM providers
- TTS and lip-sync
- local memory and relationship simulation
- event-driven companion progression
- a desktop overlay mode through `Tauri`

### 3.2 Existing Technical Strengths

The current codebase already gives us several important foundations:

- `src/lib/components/vrm/`
  - scene graph, VRM model loading, animation, rendering integration
- `src/routes/app/+page.svelte`
  - the main orchestration page for chat, state, memory, TTS, and avatar behavior
- `src/routes/overlay/+page.svelte`
  - a desktop overlay route with avatar + compact interaction model
- `src/lib/services/chat/client-chat.ts`
  - direct provider streaming for Tauri builds
- `src/routes/api/chat/+server.ts`
  - web-side chat proxy route
- `src/lib/services/platform/`
  - desktop abstractions for hotkeys, window positioning, dragging, and visibility
- `src-tauri/`
  - a working desktop shell with overlay window support

### 3.3 Current Product Assumptions

The existing app makes several assumptions that are useful for a companion app, but limiting for our target:

- the AI is modeled as a single companion with emotional stats
- relationship progression and dating-sim mechanics are first-class concerns
- memory is designed around long-term relationship continuity
- the LLM output is interpreted mainly as dialogue plus stat deltas
- the app is local-first and can call providers directly from desktop builds

## 4. Product Gap Analysis

This is the main difference between the baseline and target direction.

### 4.1 Core Product Lens

`Utsuwa`:

- "AI companion app with VRM avatar"

`VividSoul`:

- "Open 3D desktop companion engine powered by backend intelligence"

This is the biggest conceptual shift. The companion should become model-agnostic and protocol-driven.

### 4.2 AI Output Contract

Current state:

- LLM output is parsed into dialogue and state changes such as mood, affection, trust, intimacy, and memory suggestions

Target state:

- LLM output should become a structured behavior protocol, for example:

```json
{
  "text": "Good morning. You look busy today.",
  "emotion": "playful",
  "expression": "smile_soft",
  "action": "wave",
  "pose": "lean_forward",
  "look_at": "cursor",
  "intensity": 0.45,
  "physics_profile": "gentle_idle",
  "voice": {
    "style": "warm",
    "speed": 1.0
  },
  "metadata": {
    "adult_tone": true
  }
}
```

This is a foundational rebuild item.

### 4.3 Architecture Direction

Current state:

- web mode uses a SvelteKit route as a proxy
- desktop mode can talk directly to model providers
- the app is designed to be usable without our own backend

Target state:

- frontend desktop app should be a client
- backend should own LLM orchestration, prompt policy, model routing, and response schema normalization
- communication should be over `WebSocket` or streaming `HTTP`

This implies we should remove "provider-specific logic" from the client over time.

### 4.4 Desktop Experience

Current state:

- transparent overlay and always-on-top exist
- hotkey support exists
- dragging exists
- click-through is present at the API level but intentionally disabled because UI hit-testing is not solved cleanly
- system tray is still planned, not complete

Target state:

- desktop overlay should feel like a real desktop pet
- click-through should be robust
- window dragging, scaling, edge snapping, hide/show hotkeys, tray/background behavior, and quick summon should be dependable

### 4.5 Model System

Current state:

- strong VRM support already exists
- there is not yet a first-class model mapping system for arbitrary imported character behavior

Target state:

- every imported model should have a configurable behavior mapping layer
- emotional states should map to expressions
- command keywords should map to animation groups
- physics and visual reactions should be configurable per model

This mapping layer is essential for supporting community models without hardcoding assumptions into the app.

## 5. Keep / Refactor / Remove

### 5.1 Keep With Minimal Changes

These parts are already aligned or close to aligned:

- `Three.js + Threlte + @pixiv/three-vrm` rendering base
- `Tauri` desktop shell
- overlay window concept
- TTS pipeline
- lip-sync pipeline
- general settings infrastructure
- provider UI as a temporary compatibility layer during migration

### 5.2 Keep But Refactor Heavily

These parts are valuable, but their current shape does not match the target product:

- `src/routes/app/+page.svelte`
  - too much orchestration in one page
  - should be broken into services and controllers
- `src/routes/overlay/+page.svelte`
  - currently duplicates major behavior from the main app
  - should reuse a shared companion runtime
- chat transport
  - currently split between web proxy and direct desktop fetch
  - should converge on a single backend client
- avatar state handling
  - should shift from relationship-centric fields to action/expression/behavior state
- import flow
  - should become model-centric, not companion-card-centric

### 5.3 Remove or Deprioritize

These areas do not belong in MVP unless we explicitly decide otherwise:

- dating-sim progression as the core product frame
- relationship stage engine as a primary mechanism
- event content pipeline focused on romance progression
- "single built-in companion personality" assumptions
- marketing/docs/blog content as a near-term engineering priority

Some of these may remain temporarily in code during transition, but they should not define the product direction.

## 6. Proposed Target Architecture

### 6.1 High-Level Architecture

```text
+------------------------------+
| Desktop Client (Tauri)       |
| Svelte + Three + VRM         |
| - rendering                  |
| - input / overlay UI         |
| - hotkeys / window control   |
| - TTS playback / lip-sync    |
| - model config application   |
+--------------+---------------+
               |
               | WebSocket / streaming HTTP
               |
+--------------v---------------+
| Soul Backend Service         |
| - prompt orchestration       |
| - LLM routing                |
| - structured response schema |
| - session/context handling   |
| - optional memory layer      |
| - policy/config controls     |
+--------------+---------------+
               |
               | provider adapters
               |
+--------------v---------------+
| Model Providers              |
| OpenAI / Claude / Grok /     |
| Gemini / Ollama / others     |
+------------------------------+
```

### 6.2 Frontend Responsibilities

The desktop client should focus on:

- rendering and runtime playback
- local window and overlay behavior
- avatar expression/action execution
- audio playback and lip-sync
- model import and local model config
- sending user input and environment context to the backend

The desktop client should not remain responsible for:

- provider-specific prompt quirks
- provider-specific streaming formats
- core conversation policy
- model routing and fallback logic

### 6.3 Backend Responsibilities

The backend should own:

- a unified request/response contract
- provider abstraction
- model selection
- prompt templates and role setup
- safety policy decisions according to our product rules
- normalization of LLM output into a stable control schema
- optional long-term memory, if we keep that feature

### 6.4 Companion Runtime Layer

We should introduce a shared runtime layer in the client, for example:

- `runtime/session`
- `runtime/behavior`
- `runtime/avatar`
- `runtime/audio`
- `runtime/overlay`

This runtime should be reusable by both:

- full app mode
- overlay mode

That will eliminate duplicated logic now spread across `app/+page.svelte` and `overlay/+page.svelte`.

## 7. Proposed Client-Side Domain Model

The current data model is too relationship-centric. For `VividSoul`, the center of the client should become:

### 7.1 Model Profile

Represents imported character assets and configuration:

- model path / source
- model type: `vrm`, later `gltf`
- default scale / camera framing
- expression map
- animation map
- optional body-zone interaction map
- optional NSFW capability flags

### 7.2 Runtime State

Represents current presentation state:

- active expression
- active animation
- active pose
- look-at target
- speaking state
- current text bubble content
- current physics preset
- overlay visibility and interactivity state

### 7.3 Session State

Represents current interaction session:

- backend connection state
- recent messages
- streaming response state
- active voice input state
- optional memory summary

### 7.4 User Preferences

Represents environment and UX settings:

- hotkeys
- overlay behavior
- click-through behavior
- voice provider config
- backend endpoint config
- scaling and desktop behavior settings

## 8. MVP Scope Recommendation

The MVP should be narrower than the PRD's full vision.

### 8.1 MVP Goal

Users can import a `VRM` model, connect to our backend, talk to the companion, and see backend-driven expression/action changes inside a desktop overlay.

### 8.2 MVP In-Scope

- `VRM` import from local file
- desktop overlay window
- drag and reposition
- always-on-top
- global hotkeys for show/hide and focus chat
- chat input
- backend streaming connection
- structured LLM response contract
- expression switching
- a small set of mapped actions and poses
- TTS and lip-sync
- basic model config file

### 8.3 MVP Out of Scope

- full community model marketplace
- multi-character display
- advanced body-part interaction system
- full local model support for all logic
- complex long-term memory graph
- fully polished tray/background experience on day one
- broad `glTF` compatibility beyond carefully chosen subsets

## 9. Recommended Delivery Phases

### Phase 0: Architecture Cleanup

Goal:

- make the codebase safe to modify

Tasks:

- extract shared companion runtime from `app/+page.svelte` and `overlay/+page.svelte`
- isolate transport logic behind a single client interface
- separate avatar control from relationship/stat logic
- define a stable internal action/expression API

### Phase 1: Product Reframe

Goal:

- remove the strongest product-level mismatch

Tasks:

- de-emphasize dating-sim language and flows
- replace relationship-centric defaults with model-centric defaults
- introduce model profile and mapping configuration
- create a backend endpoint configuration page

### Phase 2: Backend Protocol

Goal:

- move from provider client to soul client

Tasks:

- define request schema from desktop client to backend
- define streaming behavior response schema
- implement a client transport service
- make existing provider settings optional or fallback-only

### Phase 3: Desktop Pet Runtime

Goal:

- make overlay mode feel like the main product

Tasks:

- robust overlay mode entry/exit
- click-through design and hit-test strategy
- drag/scale/snap behavior
- stronger hotkey handling
- background-friendly desktop lifecycle

### Phase 4: Model Ecosystem Layer

Goal:

- support arbitrary imported characters better

Tasks:

- create model mapping JSON format
- emotion -> expression mapping
- action key -> animation mapping
- optional physics behavior presets
- importer UX for validating config completeness

## 10. Biggest Engineering Risks

### 10.1 Utsuwa Logic Is Too Page-Centric

Much of the current logic lives directly inside route components. This is fast for iteration but bad for a rebuild. If we keep adding features without extracting a runtime layer, development speed will degrade quickly.

### 10.2 Desktop and Web Transport Are Diverged

There are currently separate chat pathways. That creates maintenance risk and makes backend migration harder unless we standardize early.

### 10.3 Click-Through Is Not Finished

The app already exposes APIs for cursor-ignore behavior, but the hard part is correct hit-testing between 3D model regions and HTML overlay controls.

### 10.4 Existing State Model Can Mislead Future Development

If we keep fields like affection/trust/intimacy as the central model, new work will continue drifting toward the old product. We should introduce the new model early, even if old fields remain temporarily for compatibility.

### 10.5 NSFW Support Needs Product-Level Decisions

The PRD is explicit about permissive model support. That affects:

- backend prompting strategy
- config UX
- model metadata
- desktop interaction design
- future policy boundaries

This needs deliberate product rules before implementation spreads across the codebase.

## 11. Immediate Recommendations

These are the most important next steps before writing large amounts of code.

1. Freeze the target client/backend protocol in a draft spec.
2. Decide whether memory is part of MVP or postponed.
3. Decide whether relationship stats survive as an optional persona layer or are removed from the product core.
4. Extract a shared companion runtime from the two main route pages.
5. Define a first version of the model mapping JSON.

## 12. Suggested Near-Term Work Order

If implementation starts immediately, the recommended order is:

1. Write protocol spec for backend responses.
2. Design the new client-side runtime state.
3. Refactor shared app/overlay orchestration into reusable services.
4. Replace direct provider logic with backend transport abstraction.
5. Build the first model profile + mapping flow.
6. Rebuild overlay interactions around the new runtime.

## 13. Open Questions

These decisions will affect architecture and scope:

- Is backend deployment single-user, self-hosted, or cloud-managed?
- Is long-term memory in MVP, or should it be stubbed out?
- Do we keep a web app at all, or focus on desktop-first delivery?
- Is `glTF` support a true MVP requirement, or a post-MVP extension after VRM is stable?
- How explicit should body-zone interactions be in v1?
- Should provider settings remain visible to advanced users, or be fully abstracted behind our backend?

## 14. Final Recommendation

Use `Utsuwa` as the implementation foundation, but do not treat its current product logic as the target.

The recommended strategy is:

- inherit the rendering shell
- inherit the desktop shell
- inherit the audio pipeline
- replace the AI contract
- replace the core domain model
- progressively replace local provider-centric assumptions with a backend-driven architecture

In practical terms, `VividSoul` should become:

- a desktop companion runtime
- plus a model configuration layer
- plus a backend "soul engine"

That is the cleanest path from current baseline to the PRD vision.

## 15. References

- PRD: `../prd.md`
- Repo root: `utsuwa/`
- Main app route: `utsuwa/src/routes/app/+page.svelte`
- Overlay route: `utsuwa/src/routes/overlay/+page.svelte`
- Web chat route: `utsuwa/src/routes/api/chat/+server.ts`
- Desktop chat transport: `utsuwa/src/lib/services/chat/client-chat.ts`
- Platform helpers: `utsuwa/src/lib/services/platform/`
- Tauri config: `utsuwa/src-tauri/tauri.conf.json`
- Upstream repository: [The-Lab-by-Ordinary-Company/utsuwa](https://github.com/The-Lab-by-Ordinary-Company/utsuwa)
