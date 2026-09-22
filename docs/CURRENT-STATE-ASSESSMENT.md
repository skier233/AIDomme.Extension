# AI Domme — Current state assessment

Date: 2026-09-22. Reviewed against [NORTHSTAR.md](NORTHSTAR.md). The ordered work list derived from
this review is in [NOW.md](NOW.md). Paths are relative to `extensions/AIDome/` unless noted.

## Verification performed

| Check | Result |
| --- | --- |
| `dotnet build AIDome.Extension.slnx -c Debug` | 0 errors |
| `dotnet test tests/AIDome.Tests` | 15 passed, 12 skipped (PostgreSQL tests need `AIDOMME_TEST_POSTGRES`) |
| `vitest run --config tests/ui/vitest.config.mjs` | 27 passed |
| `tsc --project tsconfig.json` | clean |
| Host dependencies (`ActiveMedia`, `navigateToUrl`, `DeleteLikeByIdAsync`, per-user like lock, keyboard hooks) | all committed in the sibling `cove` checkout; none are uncommitted local hacks. `DeleteLikeByIdAsync` and the like-lock namespace are on the frozen `Cove.Core` ABI; `getActiveMedia`, `navigateToUrl` and the keyboard hooks are exported from the runtime barrel but not listed in the documented `v1` surface, so they could change without notice. |
| Version control | **none**. The repo root is not a git repository. |

The code compiles and the automated checks pass. What follows is about whether it is the right code.

## What the previous AI did well

1. **The tracker (stage 1) is genuinely Cove-native and is the model for everything else.**
   `Tracking/TrackerModels.cs` registers entities on the host context through `ConfigureModel`, ships
   a forward-only SQL migration, keys rows by a real `UserId` foreign key with cascade delete, and
   `Tracking/TrackerService.cs` runs inside Cove's own context, transaction, execution strategy and
   per-user advisory lock. Events carry a client-generated ID so retries never double count; likes
   are applied through Cove's engagement service with exact receipts so undo reverses only what
   the event created. Keyboard actions go through Cove's manifest and `useRegisterExtensionKeyboardActions`;
   media attribution uses `getActiveMedia`; the panel portals into the fullscreen subtree; all HTTP
   goes through `extensionFetch`. This is exactly how the rest of the extension should look.
2. **Account isolation is taken seriously.** Ownership always comes from `ICurrentPrincipalAccessor`,
   never from the client. One engine runtime per account (`Persistence/GuidedRuntimes.cs`).
3. **Durability patterns are sound.** Idempotent session controls with receipts, a recovery
   checkpoint that restores a session *paused* without replaying timers or narration, one lease
   per browser resource (metronome, narration, directed media), and the explicit rule that a
   disconnect or an engine failure is never treated as a breach. These ideas should survive the
   rewrite even where the code does not.
4. **The commitment store has the right data shape.** Offer, explicit accept, commitment, progress,
   report, all revisioned and idempotent (`Persistence/CommitmentStore.cs`). The *content* is wrong
   (see below) but the shape is the seed of the punishment ledger.
5. **The legacy import is careful.** Read-only, definitions and calibration only, runs once, keeps
   learned multipliers over seed values.
6. **Reusable domain pieces exist.** `Services/GameEngine.cs` builders and resolver,
   `StimMethodPointMath` plus `StimMultiplierOptimizer`, and the shared-default / personal-override
   definition editor (`Persistence/GuidedConfiguration.cs`, `ui/Configuration.tsx`).
7. **Build and test hygiene.** The esbuild script, Tailwind scoping pass and `shared/theme.css`
   mirror the EmpPrep reference extension. There are real tests for the hard concurrency cases.

## What missed the mark (product)

1. **"Define offers" is the wrong feature.** `ui/Commitments.tsx` lets the user author fixed offer
   definitions (name, terms, unit, target, due days) and manually propose them to themselves. The
   northstar says offers are rule-generated from templates at the moment they apply, and
   commitments are user-proposed *structured* choices that the domme answers. Nothing in the engine
   proposes, references or enforces an offer or commitment; `OriginSessionId` is never set by the
   engine. As shipped it is a to-do list with an accept button.
2. **The companion is a control panel, not a chat log.** `ui/Tracker.tsx` renders counts,
   edge/orgasm/undo, an active-media label, the full metronome (tempo, volume, mute), the whole
   `GuidedPanel` (pause/end, chat, infraction reporting, game controls, narration and directed-media
   toggles, a session-settings editor), the `CommitmentPanel`, and an expandable section with
   tracker settings and a recent-events list. The northstar wants the domme's instruction, a short
   log, a countdown, and only the controls the user holds authority over.
3. **The metronome is user-driven and always visible.** The README admits following directive
   speed "is still pending". The engine's 1–10 speed never reaches the metronome, and there is no
   locking when the domme owns pacing.
4. **The presets are not the four use-cases.** `Detailed` / `Simple` / `Permission`
   (`Services/SessionPolicy.cs`, `ui/SessionControls.tsx`) map to: full guided; full guided with a
   smaller method pool; and a static "Browse freely" directive with a coin-flip on begging. There is
   no "own content, domme picks methods" mode, no "own methods, domme picks edges/holds/ending"
   mode, and the free-play mode has no offers, never ends by itself, and ignores history, mood,
   mean-ness and the ledger when answering a request. `Pacing = Free` only rewrites directive text.
   `Media` is not read by the engine at all.
5. **Punishments never close the loop.** Three disconnected implementations exist
   (`Services/PunishmentEngine.cs`, which has no callers; the confession handler in
   `Services/CommandCenterService.cs`, which re-implements the tier formula and always issues
   denial; and in-session "retaliation", which is a few harsher steps). Persisted `PunishmentEntry`
   rows are listed on the setup screen but nothing enforces `DenialEndsAtUtc`, decrements
   `RemainingCount`, or forces a punishment session. `PunishmentEnder` definitions are only used by
   the debug homework generator. The confession prompt is unreachable because
   `SuspectedUnauthorizedOrgasm` is never set true.
6. **Naming drift.** "AI Domme" (product), `AIDome` (paths, IDs), `aidomme_*` (tables), `aidome-*`
   and `aidomme-*` (CSS), namespace `AIDome2025` (all domain code). This should be settled once.

## What needs fixing (engineering)

### A. Persistence: the "no own database" goal is only half met

- The tracker uses the sanctioned pattern. The guided domain does not. `Data/CommandCenterDbContext.cs`
  is the old app's EF context, now pointed at Cove's PostgreSQL by reading the host connection
  string and building a private `DbContextOptions` per call (`Persistence/GuidedStore.cs`). It
  bypasses the host's pooled context, execution strategy and query filters. Cove's developer docs
  prescribe registering entities on the host context via `ConfigureModel` and injecting the base
  `DbContext`; no example extension owns a context.
- The context still carries a dual provider path (`Database.IsNpgsql()` branches), the
  `Microsoft.EntityFrameworkCore.Sqlite` package (with native SQLitePCLRaw payload) is still
  shipped, and ten SQLite EF Designer migrations plus a `ModelSnapshot` (about 13,000 lines) remain
  in `Migrations/`. They are dead at runtime.
- Migration `002_guided` is not a fixed script: `GuidedSchema.CreateSql()` generates it from the EF
  model at startup via `GenerateCreateScript()`. Any later model change silently diverges from what
  the host recorded as applied. Cove migrations are meant to be literal, forward-only SQL.
- Account scoping is done with composite `(OwnerId, Id)` keys, reflection-driven query filters, and
  a copy-defaults routine that opens a transaction, takes an advisory lock and re-copies four
  definition tables **on every `CreateDbContextAsync` call** (`GuidedStore.ApplyDefaultsAsync`).
  The norm is a plain `UserId` column with a foreign key; shared defaults can be rows with a null
  user, personal overrides rows keyed by user and definition.
- `GuidedRuntime`'s constructor blocks on async work (`.GetAwaiter().GetResult()`) inside a
  `Lazy<>` in a `ConcurrentDictionary`; the engine also does sync-over-async DB calls while holding
  its lock. These are deadlock and thread-pool risks on ASP.NET.
- Recovery state and the whole commitment document are single-row JSON blobs
  (`aidomme_guided_recovery`, `aidomme_commitment_state`). A ledger has to be queryable rows.
- `AIDome.csproj`'s NuGet fallback references `Cove.Data`, which is deliberately not packaged and
  cannot restore. There is no `Directory.Build.targets` stripping host-provided assemblies from
  the publish output (EmpPrep has one).
- `extension.json` still pins `minCoveVersion` to a dev build and describes the old app.

### B. UI

- Apart from the settings panel (`Field`, `TextInput`, `NumberInput`, `SaveButton`), the UI is raw
  HTML elements with 650 lines of hand-written CSS ported from the Blazor app. Cove's guidance is
  host theme variables and the shared component barrel. The setup screen is the old form: five
  -1..10 sliders, toy and kink checkboxes, debug/homework buttons.
- Three independent pollers run at once (guided 1–2 s, tracker 5 s, commitments 10 s).
- Two nav pages are registered (`aidome`, `aidome-tuning`); tuning is also embedded on the main
  page behind a `<details>`.
- Manifest `icon` values are not ones the host recognizes.

### C. Engine (`Services/OnlineSessionEngine*.cs`, `CommandCenterService.cs`)

- **No upfront plan.** Each step is a weighted random roll over eight categories with about 250
  lines of multiplier tweaks; the ending is decided only when the points budget is met. Roughly 80
  `Random.Shared` calls and 10 `DateTime.UtcNow` calls are hardwired; there are no engine unit
  tests, and the PostgreSQL tests reach into private state by reflection.
- **Modes are string comparisons threaded through a 2,600-line partial class** with about 60
  mutable fields mirrored by hand in the recovery checkpoint. There is no notion of who decides a
  concern, so the northstar modes cannot be added without another string value and a dozen more
  scattered checks.
- **No planner seam.** Decision, state mutation, timer arming, DB writes and text composition are
  interleaved in `NextDirectiveLocked`, `RecordEdgeAsync` and `OnStepTimerElapsed`.
- **Games are half in the game engine and half in the session engine.** Announced stakes ("skip the
  next step", "ten extra edges", the Pick-Your-Poison options) are never applied; win is +5 points,
  loss is a few retaliation steps.
- **Only two endings exist live**: granted orgasm or denial. Ruined and punishment-ender endings
  appear only in text and in the homework generator.
- **Speech is random pools with no memory.** Four separate text sources (`DommeVoice`,
  `DommeTextPool`, string literals in the engine, string literals in `CommandCenterService`),
  14 of 31 pools are dead, no record of lines already used, no references to earlier events, no
  persona beyond a transient mood enum. Duplicated mood/category enums are cast by integer.
- **Duplication and dead code**: `PickMethod`, method filtering, jitter, hold constants and the
  Simple-mode filter exist two or three times across `OnlineSessionEngine`,
  `OfflineInstructionEngine` and `MethodSelector`; `PunishmentEngine`, most of `MethodSelector`,
  `Kink`, `Tenseness`, `SessionEvent.BegGranted` and several pools are unused.
- **Concrete bugs**: edge steps followed by a hold award 1 point instead of the edge points; a
  lost game sets retaliation without a reason so speech can say "you asked to cum…"; request-only
  sessions never end; the confession flow is unreachable; `EdgeEntry.Speed/HeldDuration` are
  fabricated from the intensity slider so edge history is noise; the timer callback mutates the
  engine outside the per-account gate that HTTP handlers use.

### D. Documentation

`README.md` and `INTEGRATION_PLAN.md` are long, mostly accurate about mechanics, and overclaim on
product: stage 5 is described as the permission-focused journey but shipped a content-free
definition editor; "phase 4 presets" are described as independent journeys but differ mainly by
method pool. Both should be reduced to a short accurate README once `docs/` is the source of truth.

## Bottom line

Keep: the tracker, the account/runtime model, the idempotency and recovery *patterns*, the lease
model, the game builders, the points math and optimizer, the definition editor, the build tooling,
and the commitment store's shape. Rework: guided persistence onto the host context with literal
migrations, the companion, the session model around a contract and per-concern authority, the
punishment ledger, and the engine and narration behind a planner interface. Remove: the "Define
offers" UI, the SQLite provider and its migrations, the dead engines and pools.
