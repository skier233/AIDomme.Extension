# AI Domme — Cove Extension

A port of the standalone **AIDome2025** Blazor app into a Cove extension.
AI Domme provides an app-wide tracker alongside a session-based interactive command center: live edge/orgasm sessions
driven by a real-time directive engine, 14 mini-game types, a punishment/anti-cheat system, and an
offline "homework" generator. Tracking, commands, countdowns and chat follow Cove browsing; the dedicated page provides setup and a larger view of the same session.

## App-wide tracker (stage 1)

- Movable floating controls, collapse/hide/reopen, and detailed counters. Position and presentation are remembered per account in this browser.
- **Close (×)** removes all floating controls and remembers that choice. Reopen with **Alt+D** or **Open tracker** on the AI Domme page. **Hide** keeps a small reopen chip; recording shortcuts remain active in either state.
- Cove-managed configurable shortcuts: **Alt+E** records an edge, **Alt+O** records an orgasm, **Alt+D** shows/hides controls. Configure them in Cove's keyboard settings. Hidden controls keep their shortcuts active.
- Standalone tracking works without a guided session. Use **New tracking session** to reset the displayed session counters while keeping recorded activity and the since-last-orgasm metric.
- **Details → Tracker settings** enables automatic content likes (off initially). Defaults are edge +0 / orgasm +1; choose edge +1 and orgasm +2 or +3 if desired.
- Image detail/lightbox and video detail/quick-view/compilation context is supplied by Cove viewers. Loading or ambiguous content is unattributed. Native fullscreen controls are portaled into the fullscreen subtree.
- Events, preferences, and exact like receipts live in extension-owned **Cove PostgreSQL tables**. Weighted likes and the event commit together; retry reuses the same event ID and original media. Undo reverses the event's own likes and recomputes counts.
- A failed request can be retried using **Retry same event**. Dismissing a request refreshes counts because a lost response may already have committed.

This stage requires the modified sibling Cove checkout: `ActiveMedia.ts`, its viewer publishers/shared runtime exports, `IUserEngagementService.DeleteLikeByIdAsync` for exact undo receipts, and shared per-user locking for native/tracker like mutations. Rebuild Cove's frontend (`npm run build` in `../cove/ui`) and backend before loading this extension. Version 1.4.2-dev.44 is the development baseline, not a claim that an unmodified build at that version supplies these additions.

## Guided storage and personal overrides (stage 2)

Guided engines now use `aidomme_guided_*` tables in Cove's PostgreSQL database. They retain their domain context, backed by the same Cove connection settings. Composite ownership keys, matching foreign keys, account query filters, and account membership tied to Cove users protect every guided record. Each account has one independent runtime and media director; refresh/navigation reconnects to it. Stage 4 adds durable paused recovery.

- Method definitions, punishment ending definitions, tuning ratings, and tuning comparisons become shared defaults. Personal definitions are effective copies: unmodified copies follow defaults; explicitly saved changes become personal overrides. Restore shared defaults without changing another account.
- **Methods and tuning** in session setup edits personal methods/endings and their multipliers. The Tuning Lab provides the same editor and the multiplier optimizer. The full pairwise comparison workflow remains a later UI task.
- The original development configuration can be selected by `AIDOMME_LEGACY_CONFIGURATION_PATH`, or `aidomme-legacy-configuration.json` (`{"path":"absolute SQLite path"}`) in Cove's extensions data directory. The staging script discovers the sibling original application's development database if no source has already been configured, or accepts `-LegacyConfigurationPath` explicitly.
- The one-time, transactional importer reads only configuration/calibration from that original file and the extension's `aidome.db`. It keeps original learned values, layers extension fields changed from the built-in seed on top, and reconciles calibration without discarding colliding source IDs. Old schemas need no in-place SQLite migration. Calibration UTC times use PostgreSQL microsecond precision.
- **No historical usage, sessions, edges, orgasms, infractions, or assigned punishments are imported.** Both SQLite inputs remain intact. Once imported, the engine works independently of those files. Previously recorded Cove tracker events remain intact.
- Guided edge/orgasm buttons and Cove tracker shortcuts use the same idempotent event path. Tracker events, likes, guided activity and delivery receipts commit in one transaction. Engine delivery follows the commit and is deduplicated; pending delivery is checked on guided requests. Passive tracking contributes to cumulative permission metrics without starting guidance.
- Undo corrects the guided activity too. If an undone event affected the currently running session, guidance stops with an explanation rather than attempting to reverse a past instruction. Corrections/system delivery failures clear abandoned-session suspicion; they do not imply a breach. A new guided session can then be started.
- The free tracker bout and guided-session identity remain separate. Starting a guided session does not reset standalone tracker bout counters; use **New tracking session** when desired.

Restart/reload the extension after staging to apply migration `002_guided` and import configuration. Startup fails clearly if an explicitly configured original source is missing; fix its path and retry. The import marker prevents later restarts from replaying old source data over new settings.

## Persistent manual metronome (stage 3)

The floating tracker now includes tempo (20–240 BPM, initially 60), Start/Pause, volume,
mute, and a visual pulse. Each tick marks one stroke. **Alt+M** starts/pauses through
Cove's configurable keyboard system, including when the tracker is hidden or closed.
The AI Domme page's End Session button pauses the metronome immediately, before its request.

- Audio and ownership live in the app-wide companion, outside its fullscreen/body portal.
  Cove route changes and tracker Hide/Close preserve playback. Closing all controls still
  removes the floating UI entirely; Alt+D or Open tracker reopens it.
- Tempo/volume/mute are remembered per account in this browser. Playback is never restored
  automatically on reload or account change; Start or the shortcut must enable audio.
- Clicks use Web Audio's audio clock with a short scheduling window, independent of tracker
  polling. A delayed scheduler skips missed beats; it does not play a catch-up burst. Mute
  keeps the visual pulse, and pause cancels queued notes immediately.
- A server lease allows only one audio owner per account across tabs/devices. A competing
  start explains where playback is already owned. Pause releases ownership; a closed/crashed
  client expires within five seconds. Renewal runs every 1.5 seconds; the client stops after
  four seconds from the last renewal request, before server ownership can expire.
- Switching away from the browser tab, closing it, losing connectivity/ownership, or browser
  audio suspension pauses playback and requires an explicit restart. Background playback
  is intentionally not promised. Manual playback does not start or advance guided sessions.

This release provides manual pacing. Following directive speed is still pending: its 1–10
scale needs configurable BPM conversion plus explicit handling of rests, holds and games
before it can safely drive beats. Traveling guidance/audio ownership is phase 4.

The scheduler uses the documented [audio clock](https://developer.mozilla.org/en-US/docs/Web/API/BaseAudioContext/currentTime).
No new Cove host modifications or database migration are required for stage 3. Restart Cove
after staging version **0.4.0** to load the metronome lease endpoints and hotkey manifest.

### Verification

```powershell
dotnet test tests/AIDome.Tests/AIDome.Tests.csproj
../cove/ui/node_modules/.bin/tsc.cmd --project tsconfig.json
../cove/ui/node_modules/.bin/vitest.cmd run --config tests/ui/vitest.config.mjs
```

UI tests use the sibling Cove checkout's test dependencies. On Node versions exposing experimental web storage, run Vitest with `NODE_OPTIONS=--no-experimental-webstorage`. Backend tests exercise a small relational fixture and the actual Cove engagement service, plus injected partial failure. Model tests generate the complete Cove PostgreSQL model without a connection.

Optional PostgreSQL integration tests read `AIDOMME_TEST_POSTGRES` (a local maintenance connection). They create uniquely named temporary databases, apply the actual migrations, test concurrent duplicate/distinct deliveries, simultaneous native likes, weighted undo, account isolation, definition migration and guided delivery, then drop only those test databases. Set `AIDOMME_TEST_LEGACY_CONFIG` to verify every scalar definition/calibration value from a real SQLite source read-only. They do not read or alter the development PostgreSQL database's users or media. Full running-app/browser verification is still separate.

Stage 2 verification: backend PostgreSQL tests include the original source's 430 ratings and 653 comparisons, source-file hash checks, personal override/reset, account foreign-key enforcement/deletion, guided deduplication, passive metrics and event/like rollback when guided persistence fails. Component tests cover configuration editing/restoration/failure and the tracker. Stage 1's 36 affected Cove UI tests passed previously; no new Cove host changes are needed for this phase.

Stage 3 adds lease isolation/renewal/expiry tests and fake-audio/component checks for clock
scheduling, missed-beat skipping, mute/pulse, tempo changes, immediate stop, closed controls,
competing ownership, canceled requests and browser departure. Browser/audio hardware
verification remains separate from these automated checks.

> Adult content. This extension is for personal, authorized use only.

## Layout

```
AIDome.Extension/
├── Directory.Build.props          # net10.0; auto-detects ../cove for SDK ProjectReference, else NuGet
├── AIDome.Extension.slnx
├── package.json                   # esbuild + @tailwindcss/cli + react (UI build)
├── scripts/
│   ├── build-extension-ui.mjs     # bundles ui/AIDome.tsx → dist/bundle.js (+ scoped bundle.css)
│   └── stage-local-extensions.ps1 # builds + copies into %LOCALAPPDATA%\cove\extensions
├── shared/theme.css               # Tailwind theme
├── extensions/AIDome/
│   ├── AIDome.csproj
│   ├── extension.json             # manifest (id: cove.community.aidome)
│   ├── AiDomeExtension.cs         # entry point: FullExtensionBase — DI, persistence, API, UI manifest
│   ├── AiDomeModels.cs            # state snapshot + request DTOs
│   ├── Data/                      # EF Core entities + DbContext (ported verbatim)
│   ├── Tracking/                  # Cove-backed tracker schema, service, and DTOs
│   ├── Migrations/                # EF migrations (ported verbatim; seed data via HasData)
│   ├── Services/                  # session/game/punishment engines (ported verbatim)
│   ├── ui/AIDome.tsx              # React UI (CommandCenterPage, AiDomeSettingsPanel, TuningLabPage)
│   ├── ui/Tracker.tsx             # app-wide tracker companion
│   ├── ui/AIDome.css              # scoped styles (ported from Home.razor.css)
│   └── dist/                      # built UI bundles (checked in)
└── tests/                        # xUnit relational/database tests + Vitest tracker tests
```

## How it maps onto Cove

| Source app | Cove extension |
|---|---|
| `Program.cs` DI | `AiDomeExtension.ConfigureServices` |
| `IHostedService` command center | singleton resolved + started in `InitializeAsync` |
| EF Core + SQLite + migrations | Cove PostgreSQL for tracking and guidance; read-only configuration import from legacy SQLite |
| `appsettings` TTS config | persisted via the extension `IExtensionStore` + settings panel |
| `Home.razor` server-push UI | React page; `/api/ext/aidome/state` polling + client-derived countdown |
| JS-interop hotkeys / TTS audio | Native Cove tracker keyboard actions; legacy narration uses authenticated extension requests → `Audio` |
| `Home.razor` / `TuningLab.razor` | `CommandCenterPage` / `TuningLabPage` React components |

The tracker integrates with Cove's media library and keyboard system. The legacy domain engines
do not participate in scans or auto-tagging and remain surfaced as a nav page until later stages.

## Build & run locally

Stage 4 (0.5.0) requires the sibling Cove frontend's `navigateToUrl` shared runtime export. Build Cove's frontend in `../cove/ui` and update the running installation's `wwwroot` assets alongside the extension. The extension staging script copies only extension files; it does not publish Cove itself. Existing hashed host assets can be retained during an in-place update.

```powershell
npm install
npm run build:ui                       # build the React/CSS bundles
dotnet build AIDome.Extension.slnx -c Debug
./scripts/stage-local-extensions.ps1   # build everything + copy into Cove's extensions dir
# restart Cove → "AI Domme" appears in the nav
```

A sibling `../cove` checkout is used for the SDK at build time (compile-only); without it the build
falls back to the `Cove.Sdk` / `Cove.Core` / `Cove.Data` NuGet packages.

## HTTP API (`/api/ext/aidome`)

`GET /state` · `GET /setup` · `GET /history` · `POST /session/start` · `/session/start-punishment`
· `/session/end` · `/session/action/{id}` · `/session/game/answer` · `/session/infraction`
· `/session/cum-request` · `/confession` · `/homework` · `/punishment-homework` · `GET|PUT /settings`
· `POST /tts`.

`GET /configuration` · `PUT /configuration/methods/{id}` · `DELETE /configuration/methods/{id}/override`
· equivalent `/configuration/enders/{id}` routes · `POST /configuration/optimize`.

`POST /tracker/metronome/lease` · `POST /tracker/metronome/release` accept a `clientId`
UUID. Ownership comes from the signed-in Cove account, never a caller-supplied user ID.

## Known gaps / follow-ups

- **Full pairwise Tuning Lab workflow remains to be ported.** Personal definition editing and the
  multiplier optimizer are available; calibration data is preserved in Cove.
- Real-time updates use polling. If Cove exposes a SignalR hub to extension frontends, that would be
  a clean upgrade over the `/state` poll.
- Multi-tab views are eventually consistent through polling; account mutations serialize server-side.
- Automated offer selection and permission-mode challenge rules remain future policy work; the phase 5 definition, offer, acceptance, progress and review infrastructure is available in 0.6.0.
- Follow-session metronome pacing still needs a speed-to-BPM conversion and rest/hold semantics.

## Traveling command/chat session (stage 4)

Version 0.5.0 uses one client polling owner for the full page and floating companion. Both show the same command, countdown, chat and game controls. Closing the companion removes its UI while guidance and Cove shortcuts remain available. New configurable shortcuts: Alt+C completes a ready step, Alt+B requests permission, Alt+S requests stopping, and Alt+P pauses/resumes guidance. Pause and End remain available while another request is pending.

Full, Simple and Permission presets configure independent guidance, pacing, media selection, permission and game settings. Simple guidance uses eligible basic hand methods; selected method IDs can narrow that pool. Permission mode has no detailed method schedule or pacing timer; its challenges and offers are deferred to phase 5. Permission being granted keeps the session active until an actual orgasm is recorded.

Before starting, choose whether session settings may change. The server enforces that decision, which cannot itself change mid-session. Allowed changes are versioned and replace obsolete directives, timers and game state without erasing recorded activity or inventing a failed challenge. Media can remain user-selected or use the engine's cues to navigate native Cove video, image and audio pages. Browser media ownership is separate from optional narration ownership.

Migration `003_companion` stores account-scoped engine checkpoints, chat, accepted policy revisions, control intents, guided event step attribution and narration receipts in Cove PostgreSQL. Server restart restores recoverable sessions **paused**, including the current game and remaining timer; Resume rearms that timer without advancing missed ticks. Browser narration and directed media must be enabled again. No narration or command is automatically replayed. If an interrupted command's effects cannot be confirmed, guidance stops for review and retains recorded facts without inferring a breach.

Tracker activity recorded while paused, or from an explicitly stale client step, remains passive tracking. A previously committed edge delivered after the step changes updates cumulative counts without advancing the new target. Orgasm authorization retains the decision applicable when its fact was committed. Retries preserve event IDs and captured media across navigation.

Narration uses one account-scoped lease and durable message claims across tabs/devices. Obsolete speech requests are canceled; a claimed message is not retried automatically after an ambiguous failure. Native audio-clock stop deadlines limit playback after lease expiry even if JavaScript is suspended. See [AudioScheduledSourceNode.stop](https://developer.mozilla.org/en-US/docs/Web/API/AudioScheduledSourceNode/stop).

Restart Cove after staging to apply the backend and migration. Durable recovery starts with sessions created by 0.5.0; a running 0.4.0 session cannot be recovered retroactively, so finish it before restarting. Existing tracker activity and imported definitions/calibration are preserved.

Verification: 27 backend tests passed using isolated PostgreSQL databases, including real HTTP endpoints, policy locks, recovery, game checkpoints, interrupted controls, late event delivery and narration claims. All 27 extension UI tests and 52 targeted Cove navigation/keyboard tests passed; TypeScript checking and the host frontend build passed. Live browser/audio hardware verification remains separate.

## Offers and cross-session commitments (stage 5)

Version 0.6.0 adds account-owned structured offer definitions without shipping any predefined terms. A definition records a name, exact terms, progress unit and target, and an optional due interval. Creating an offer snapshots those fields so later definition edits cannot rewrite a pending or accepted agreement.

Only the explicit **Accept** action creates a commitment. Declining or cancelling an offer creates none. Every mutation carries a client event ID and expected document revision: exact retries return the prior state, reused IDs with changed payloads are rejected, and stale views must refresh. Accepted commitments survive sessions, navigation, closing the companion, and Cove restarts independently of the live guidance engine.

The floating companion surfaces unresolved items on later visits. Progress is recorded separately from the eventual user report. A report records Completed, Partial, or Broken, a 0–100 degree, an optional note, and its own timestamp; disconnection or an unfinished session never creates a report automatically. Migration `004_commitments` stores the versioned document in Cove PostgreSQL under the authenticated account with account-cascade deletion.

The current deterministic system supports manually defined and proposed offers. It deliberately contains no built-in rules or terms and does not automatically select consequences. Later rule packs can propose these same structured offers through the idempotent API without changing acceptance or persistence semantics.

