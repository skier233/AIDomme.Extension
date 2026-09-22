# AI Domme / Cove integration plan

Source review: 2026-09-16. The initial planning pass reviewed the extension, the original application's README and selected source, and the sibling Cove checkout. Stages 1–5 have since been implemented as described below. Follow-session metronome pacing and automated policy rules remain proposed work.

## Product direction

Make AI Domme an optional companion throughout Cove. A compact tracker should work without starting a guided session. The same persistent panel can expand into the existing rule-driven command/chat experience. Keep a dedicated page for session setup, history, detailed controls, and eventually tuning.

Stage 1 implementation: the app-wide free tracker uses Cove-owned PostgreSQL tables, authenticated APIs, native keyboard registrations, image/video viewer context, optional like weights, exact undo receipts, and a movable/collapsible/closable panel. Supporting hooks and exact like deletion were added to the sibling Cove checkout.

Stage 2 implementation (2026-09-18): guided domain persistence now uses account-scoped `aidomme_guided_*` tables in Cove PostgreSQL, with composite ownership keys/foreign keys and independent account runtimes. Shared definition/calibration defaults are combined with personal overrides and reset controls. The importer preserves original calibration (430 ratings / 653 comparisons in the inspected development source), merges later extension definition edits, reads SQLite only as configuration input and imports no historical usage/assignments. Guided buttons and tracker shortcuts share transactional activity/like receipts and deduplicated engine delivery. Undo corrects activity and stops affected guidance; live timer/chat recovery remains phase 4. PostgreSQL tests use isolated temporary databases and check real source parity read-only. This phase adds no new Cove host modifications.

Use Cove's normal media browsing and playback. Make automatic media selection an explicit session option instead of the default prerequisite for receiving commands.

### User journeys and independent controls

The experience is broader than a switch between tracking and full guidance. Offer approachable presets backed by independent settings:

| Preset | Guidance | Media selection | Persistent behavior |
| --- | --- | --- | --- |
| Full guidance | Detailed method selection, pacing, session progression, and optional games | User browsing or engine selection | Guided session plus activity history |
| Simple guidance | A restricted method set, pacing, timed stages, and optional games/challenges | User browsing or engine selection | Guided session plus activity history |
| Permission-focused | Minimal interruption; permission requests and optional high-level prompts | User browsing or engine selection | Decisions, optional offers, and accepted future commitments |
| Tracking only | No instructions or permission decisions | Normal Cove browsing | Activity history available for later sessions |

Full guidance with engine-selected media preserves the original fully directed experience. Full guidance with user-selected media is equally valid. Media authority must not be inferred from guidance intensity.

Represent configuration as separate fields: guidance level, allowed method set, pacing control, media control, permission policy, and optional games/offers. Presets initialize those fields rather than creating four separate engines. Before starting, the user chooses whether settings may change during the upcoming session. Persist the accepted configuration and this change policy; enforce a locked policy on the server as well as in the UI. When changes are allowed, record them without restarting the session and stop obsolete timers/prompts. Pause and stop remain separate from configuration editing.

Longer term, pre-session setup can collect subjective readiness, desired effort through a qualitative slider rather than a numeric activity target, and desired activity categories. A configuration flow or rule-driven dialogue proposes terms; explicit acceptance records a versioned commitment. A locked mode keeps the accepted settings fixed for that session. Slider-to-target calibration is later policy work, not part of the tracker release.

The companion adapts to the current journey: a small counter in tracking mode, a compact permission/status surface for minimal guidance, and an expandable command/chat panel for guided sessions. Metronome availability does not imply that every mode prescribes a precise tempo.

### Shared history, decisions, and future commitments

Tracking is a foundation used by every journey. Record each observed activity once, then let an active session consume that event when applicable. Keep factual activity separate from engine interpretations and permission decisions. Unobserved activity is unknown, not evidence that it did not happen. Corrections must update derived summaries used by later decisions.

Use historical summaries as inputs to the existing rule-based engine. Record the rule/version, relevant input snapshot, and decision outcome so behavior is explainable and testable without introducing a language model. Passive tracking must not start a guided session or silently assign a permission outcome.

Model optional deals as structured offers rather than chat text alone. An offer has terms, origin session, status, and an explicit acceptance event. Only explicit agreement creates a commitment. An accepted offer may create a future commitment with its own identity, progress, and lifecycle. Repeated requests must not accept an offer twice or create duplicate commitments. Closing the browser, leaving, or stopping an unfinished session does not erase accepted terms. Persist unresolved commitments and ask on a later visit whether they were completed or broken and to what degree. Record the user's response separately from observed progress; consequences may follow from the configured rules. Disconnection alone is not proof of a breach. Cancellation or expiry must be explicit policy outcomes, never an automatic cleanup of unfinished records. Future sessions can surface outstanding commitments without confusing them with existing punishment records.

History inputs explicitly include activity counts within the current session, edge count since the latest recorded orgasm, and elapsed time since it. Later personalization can use typical session totals and intervals between edges to calibrate task/session difficulty and offer cost. Keep raw events, derived metrics, and decision rules separate. An unknown last event yields an unknown elapsed time, not an invented duration. Corrections must recompute affected metrics. Longer-term interval statistics must distinguish pauses and inactive gaps from measured task time. Newly collected tracking-only activity contributes to these summaries; importing old activity history is not required.

Games, challenges, and offers should be reusable capabilities selected by session policy. Minimal guidance must not force the detailed-method engine merely to offer a game or evaluate a permission request. Define pause, mode-change, and session-end behavior for an in-progress challenge and keep user controls available in every presentation.

### Naming

The user-facing name is **AI Domme**. Correct labels, navigation, settings, documentation, and voice-test text. Preserve existing `AIDome` paths, extension ID, API routes, assembly names, storage keys, and database filename for compatibility; a technical rename would be a separate migration.

## What exists

- `extensions/AIDome/AiDomeExtension.cs` creates `aidome.db` in the extension data directory, applies SQLite EF migrations, and starts singleton session engines. Only TTS settings currently use Cove's `IExtensionStore`.
- `extensions/AIDome/ui/AIDome.tsx` contains page-owned polling, keyboard listeners, TTS, and a custom player. Navigating away unmounts these features, while the server engine has an independent lifetime.
- The existing engine, interaction recording, games, history, and offline instructions are substantial reusable domain code. Tuning has backend pieces but a placeholder frontend.
- Edge and orgasm records require a session and contain neither Cove user ownership nor media attribution. The orgasm action is ignored without an active online engine session; it is not a standalone tracker API.
- The original application's shared-singleton design survives in the port. Cove users do not currently get distinct engine instances or user-scoped domain records.
- The UI uses bare `fetch` for extension requests and copies token handling for media requests. Cove now provides an authenticated extension fetch surface.
- Existing tests cover SQLite migration/seeding and recording one edge. They do not establish host integration, user isolation, route persistence, or duplicate-event handling.
- This workspace did not report a Git repository at its root. Establish version control before implementation.

## Host capabilities verified in the local Cove checkout

| Need | Existing support | Remaining work |
| --- | --- | --- |
| Persistent companion | `ui/src/components/AppFloatingUI.tsx`: `app-floating-ui`, mounted above page routing | Register a contribution; implement compact/expanded states and shared runtime |
| Configurable hotkeys | `UIManifestBuilder.AddKeyboardAction`, `ExtensionKeyboardActions.tsx` | Register tracker commands through Cove and verify typing, conflicts, and repeat behavior |
| Video attribution | `MediaPlayerExtension.ts`: player actions/overlay slots with video ID, surface, time, and playback state | Bridge player context into companion state; arbitrate multiple players |
| Image attribution | No unified app-wide active-media contract established in this review | Inspect image detail, gallery viewer, and quick-view surfaces; add a small host contract where needed |
| Likes | `IUserEngagementService` has video/image increment methods | Use authenticated user scope, handle retries/undo, and refresh displayed counts |
| Cove-owned persistence | `IDataExtension.ConfigureModel`, named SQL migrations, and `CoveContext` model contributions | Implement extension-owned tables and repositories; validate supported data access dependencies |
| Settings | `IExtensionStore`, backed by Cove's `extension_data` table | Retain small settings here; explicitly scope personal preferences by user |
| Authenticated API access | `@cove/runtime/api` / `extensionFetch` | Replace copied auth conventions and bare extension fetch calls |

Cove uses PostgreSQL (`Cove.Data/DataServiceExtensions.cs`). Existing SQLite migrations cannot simply be pointed at Cove. The floating slot currently receives an empty context and sits outside `VideoQueueProvider`; its presence alone does not supply current-media information. Player context currently declares video only.

## Recommended sequence

### 1. Foundation and standalone tracker

Deliver the first useful vertical slice: record an edge or orgasm from a floating panel or Cove shortcut while browsing, without a guided session.

- Create a user-owned activity event model with event ID, kind, occurrence/receipt timestamps, optional guided-session ID, optional media type/ID and playback position, input source, and correction/void metadata. Do not invent a guided-session permission result for free tracking.
- Put new records in extension-owned Cove tables, with names that cannot collide with core or other extension tables. Keep preferences in the extension store. Introduce repositories so engines do not depend directly on the old context throughout the codebase.
- Resolve ownership from Cove's authenticated principal. Scope active engines and history by user. Define one active guided session per user initially, with explicit control ownership across tabs/devices.
- Add a tracker endpoint that accepts a client-generated idempotency key. Both button and hotkey invoke the same command. Repeated delivery of one event must not create another count.
- Register the companion in `app-floating-ui`. Include counts, record actions, recent-event feedback, undo, collapse, and hide/reopen controls. Make it movable, remember its position, and support expansion into chat. Hiding does not pause or end a session or disable shortcuts. Keep it usable on narrow screens and in fullscreen.
- Register configurable shortcuts through Cove's existing keyboard actions and extension hooks; do not create a parallel extension-only binding system. Initial scope is the focused Cove app, not operating-system-wide hotkeys.
- Add a media-context bridge. Prefer the focused quick-view/fullscreen item, then the active detail viewer; do not attribute to a grid thumbnail or stale previous page. Capture context at button press. With no unambiguous item, save an unattributed event.
- Provide auto-like enablement and per-event like weights. Initial default is edge +0 / orgasm +1; also support edge +1 / orgasm +2 and edge +1 / orgasm +3. Repeated distinct events on the same item each apply their configured weight. Capture the applicable weight with the event, so later preference changes do not alter retry or undo behavior. Call the engagement service rather than writing a media counter directly. Verify authorization against that item. No unambiguous media means no like effect.
- Establish an atomic event/like transaction if the service boundaries support it. Otherwise add a durable effect receipt and an idempotent host operation before enabling retries. Two sequential HTTP requests alone are insufficient. Undo must reverse only the effect created by this event, not overwrite unrelated likes.

Acceptance: normal video detail and image viewing, no-media browsing, navigation during request completion, Cove shortcut customization and typing/repeat behavior, duplicate delivery, failed/partially applied weighted likes, weighted undo after settings change, separate users, and multi-tab operation. Verify fullscreen explicitly: a root floating layer may be outside the browser's fullscreen subtree, requiring the player overlay slot. A weight of three must not produce six likes after retry or lose track of partial application.

### 2. Preserve configuration and move session persistence

The user does not require old sessions, edges, orgasms, or activity history. Preserve method definitions, punishment definitions, and their tuning/configuration. In the current schema this includes `StimMethods` (including point multipliers), `PunishmentEnders`, and `TuningRatings`. Keep tuning comparisons where needed for recalibration/reproducibility; these are calibration data rather than activity history. Inspect optimizer dependencies before excluding them. Reconcile seeded reference records rather than replacing customized values with defaults.

Create a resumable selective import with source identity and stable key/ID mappings. Determine which original-app or extension database contains the desired configuration; do not blindly merge overlapping copies. Assign personal tuning to the intended Cove account. Verify preserved values and relationships, record a migration receipt, and retain the source database as a backup. No source database deletion is authorized by the decision to skip history. Cut over engine repositories only after validation; avoid indefinite dual writes. Test interruption and rerunning the import.

Confirmed by the user: preserve only definitions and tuning. Historical assigned `PunishmentEntry` records and all other historical usage are unnecessary because this application has only been used during development. Newly accepted commitments and assignments must persist going forward.

### 3. Persistent metronome

Implemented 2026-09-18 in version 0.4.0: manual tempo (20–240 BPM), Start/Pause, volume/mute and an audio-clock visual pulse in the app-wide companion. Cove-managed Alt+M starts/pauses while controls are closed too. Route changes and Hide/Close preserve playback; page End Session pauses it immediately. Audio initialization requires a gesture. Tab departure, browser audio suspension, account change, reload, connection/lease loss or client shutdown stops playback without automatic resumption. Preferences are personal to this browser/account; playback state is transient.

Authenticated server leases allow one audio owner per Cove account across tabs/devices. The client deadline is conservative relative to the five-second server expiry; failed/canceled starts cannot resume audio, and late lease grants are released. Beat scheduling skips missed ticks rather than catching up. No new database migration or Cove host edits are needed. Lease and fake-audio/component tests cover ownership and timing behavior; live browser/hardware verification remains separate.

Pending follow-session capability: existing directive speed is a 1–10 scale, not BPM. Define configurable conversion and explicit rest/hold/game semantics before enabling it. Manual playback does not imply that a guided session prescribes a tempo, and does not alter the engine.

### 4. Traveling command/chat session

Implemented 2026-09-18 in version 0.5.0. The full page and floating panel share polling, versioned state, controls and narration. Independent Full/Simple/Permission presets support native user-selected or engine-directed media. Server-enforced locked/editable session policies are accepted before starting, recorded by revision, and replace obsolete commands without resetting activity. Permission mode currently omits games and offers; permission granted is distinct from an actual recorded orgasm.

Migration `003_companion` persists typed engine/game checkpoints, chat, policy revisions, command intents, event-step attribution and narration claims in Cove PostgreSQL. Restart restores paused without replaying timers, controls or speech. Interrupted controls with uncertain effects stop guidance for review without inferring a breach. Narration and directed media have independent browser ownership. A single host frontend export, `navigateToUrl`, enables native Cove navigation. All 26 backend, 26 extension UI and 52 targeted host navigation/keyboard tests passed; live audio/browser checks remain separate.

The following describes the phase's design rationale. Cross-session commitments and permission-mode challenges remain stage 5; follow-session metronome pacing remains a separate pending capability.

Extract shared session state, polling/subscription, TTS queue, and commands from `CommandCenterPage`. The floating panel and full page become views of one runtime. Keep one audio owner and one stream of event IDs to avoid duplicate narration or actions.

Expand the companion to show the latest command, countdown, chat history, and relevant actions while Cove browsing continues. Preserve the existing scripted engine; no language model is needed. Keep setup, history, and complex games available in the full page or expanded panel.

Separate browsing freely from optional engine-directed media selection. Replace the custom embedded player with host player/navigation integration where supported. Separate accounts have separate state, with one active guided session per account and synchronized controls across tabs/devices, but only one audio owner. Refresh reconnects to the session; server restart offers recovery in a paused state. Persist enough state to recover coherently without advancing missed timers or replaying actions. Unfinished commitments survive independently of the live engine.

Start with one shared polling owner and versioned snapshots. Push updates can follow after the lifecycle is correct; SignalR is not a prerequisite for a persistent companion.

Implement the independent session settings and presets above as part of this extraction. Verify both user-selected and engine-selected media with full and simple guidance, and verify that permission-focused mode can run without a pacing timer or detailed method schedule. Test permitted mode changes without losing history or leaving obsolete commands active, and reject forbidden configuration changes for locked sessions across all clients.

### 5. Permission-focused policies and cross-session continuity

Implemented in version 0.6.0 as a generic, account-owned definition/offer/commitment system. Definitions contain exact versionable terms, a progress target/unit and optional due interval. Proposed offers snapshot those terms. Only an explicit, idempotent acceptance event creates one commitment. Progress and user reports are separate; reports preserve Completed/Partial/Broken, degree, note and time. Unresolved records persist across sessions and restarts and appear in the traveling companion. Disconnects never infer a breach. Migration `004_commitments` uses Cove PostgreSQL and account cascade ownership.

No built-in terms or automatic consequence rules ship with this infrastructure. Deterministic rule packs can later propose the same structured offers while reusing its acceptance, audit and retry guarantees.

Build the minimal-guidance journey on the shared tracker and companion. Add persisted permission requests/decisions and structured offers with explicit acceptance and future commitments. Reuse games through a capability interface rather than tying them to full guidance. Start with a small rule set and deterministic tests for history inputs, corrected events, offer acceptance, progress attribution, unfinished-session follow-up, reported breach degree, and resumed sessions. Exact thresholds, costs, and consequence rules remain a later policy design task.

The event model and session configuration in phase 1 should leave room for this journey, even though richer policies and offers ship later. Historical data from tracking-only use must remain available to these policies under the same user's account.

## First implementation scope

Start with phase 1 and enough phase 2 infrastructure to avoid adding more SQLite dependence. Prove one complete path: browse media, press the Cove-registered tracker shortcut, persist a user-owned event in Cove, apply the configured like weight exactly once, navigate elsewhere, and undo the event correctly. Preserve existing guided behavior during this incremental integration. Then migrate remaining domain storage and selected configuration before expanding session features.

Defer tuning UI, new games, elaborate media direction, and a general engine rewrite. They do not resolve the current integration problem.

## Handoff readiness and remaining decisions

The first milestone's product direction is settled. A bounded implementation agent should work on the tracker and its Cove integration, not implement all later policy features. Before assigning implementation, pin the concrete host data-access contract, active-media contract for image/video/fullscreen surfaces, and atomic/idempotent weighted-like operation in a technical specification. Provide a compact companion layout and executable acceptance scenarios. These are engineering discovery tasks, not additional product questions for the user.

Before selective import, identify the source database/account. Preserve definitions and tuning only. Exact decision formulas, qualitative-slider calibration, deal terms, expiry rules, and consequence severity can be settled before the later policy milestone. Do not invent these behaviors as incidental choices during tracker work.
