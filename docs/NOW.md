# AI Domme — What needs to happen now

Date: 2026-09-22. Derived from [NORTHSTAR.md](NORTHSTAR.md) and
[CURRENT-STATE-ASSESSMENT.md](CURRENT-STATE-ASSESSMENT.md). Phases are ordered; each one leaves the
extension working. Estimates are rough working-days for one focused implementer (human or AI).

## Phase 0 — Housekeeping and removing what missed the mark (2–3 days)

1. **Put the repo under git** and commit the current state before anything else is touched.
2. **Remove the "Define offers" feature.** Delete the definition editor and manual "Create offer"
   from `ui/Commitments.tsx`; remove `save-definition`, `delete-definition` and user-initiated
   `propose` from `CommitmentStore`. Keep accept / decline / report and the persisted rows; the
   ledger in Phase 3 builds on them.
3. **Shrink the companion to a chat log** (`ui/Tracker.tsx`, `ui/SessionControls.tsx`):
   - Keep: latest instruction, scrollable log, countdown, Edge / Orgasm, Request permission / Beg,
     Pause / End, Hide / Close, drag, fullscreen portal, hotkeys.
   - Remove from the companion: offers and commitments panel, tracker settings, recent-events list,
     infraction reporting, game answer controls (move to the page, or show only while a game is
     active), narration and directed-media toggles (page or a small menu), session settings editor.
   - Metronome: hidden by default behind a menu; tempo, volume and start controls locked when the
     domme owns pacing (until Phase 2 wires her in, "locked" simply means hidden in guided modes).
   - **End session is only offered in debug sessions.** Everywhere else the user gets Pause.
     (Until Phase 3 adds the leave-early flow, a non-debug session ends through the domme's
     ending, or through a recorded orgasm in permission mode.)
4. **Delete dead persistence**: `Migrations/` (SQLite Designer files and snapshot), the
   `Microsoft.EntityFrameworkCore.Sqlite` package, the `IsNpgsql()` dual paths, and the legacy
   SQLite importer (confirmed: it has already run on the real install).
5. **Fix packaging**: drop the unrestorable `Cove.Data` package fallback, add a
   `Directory.Build.targets` that strips host-provided assemblies (copy EmpPrep's), set
   `extension.json` description and `minCoveVersion` deliberately.
6. **Settle naming**: namespace `AIDome2025` → `AIDome`; one CSS prefix; keep `cove.community.aidome`,
   `/api/ext/aidome` and `aidomme_*` table names for compatibility.
7. **Reduce docs**: trim `README.md` to build/run/verify and point at `docs/`; delete
   `INTEGRATION_PLAN.md` or mark it historical.

## Phase 1 — Finish Cove-native persistence (5–8 days)

Goal: no extension-owned DbContext, literal migrations, plain user scoping, queryable rows.

1. Register the guided entities on the host context via `ConfigureModel` (as the tracker already
   does). Inject the base `DbContext` and `ICurrentPrincipalAccessor` into services; delete
   `CommandCenterDbContext`, `GuidedStore.Context`, `BoundGuidedFactory`, `GuidedModelCacheKeyFactory`.
2. Write **literal** SQL migration `005_guided_v2` that creates the new tables and copies data from
   the current `aidomme_guided_*` tables, then drops them. New shape:
   - Every account-owned table has `UserId integer NOT NULL REFERENCES users("Id") ON DELETE CASCADE`
     and its own serial `Id`. No composite owner keys, no query-filter expressions built by
     reflection.
   - Definitions: `aidomme_methods` / `aidomme_enders` shared rows (`UserId NULL`) plus
     `aidomme_method_overrides` / `aidomme_ender_overrides` keyed by `(UserId, DefinitionId)`.
     "Effective definition" is a query, not a per-request copy. Remove `ApplyDefaultsAsync`.
   - Calibration (`ratings`, `comparisons`) keyed by `UserId`.
   - Sessions, edges, orgasms, requests, infractions, game challenges, steps: keyed by `UserId`
     with the tracker event ID as the link to facts.
   - Replace the JSON blob tables (`aidomme_guided_recovery`, `aidomme_commitment_state`) with rows:
     `aidomme_sessions` gets `ContractJson`, `CheckpointJson`, `Revision`; commitments and ledger
     entries become tables (Phase 3 defines them; create them here to avoid a second data move).
3. Make the per-account runtime async-initialized (no `.GetAwaiter().GetResult()` in constructors;
   no DB calls under the engine lock). Route the timer callback through the same per-account gate
   the HTTP handlers use.
4. Keep `GET /configuration` and the definition editor working on the new shape.
5. Tests: the existing PostgreSQL tests are the acceptance suite; document how to set
   `AIDOMME_TEST_POSTGRES` locally and run them in CI-like fashion before and after the move.

## Phase 2 — Session contract and per-concern authority (10–15 days)

Goal: the four modes exist, as presets over independent authorities, on a rewritten engine core.

1. **Typed `SessionContract`** replacing `SessionPolicy` and `SessionParameters`:
   mode preset name, one authority field per concern (content, methods, pacing, edges & holds,
   games, permission, ending), toys/kinks, intensity/length/denial/mean preferences, commitments
   in force, ledger entries folded in, `CanChangeDuringSession`, revision. Persisted on the
   session row before play starts. Server validates that a locked contract cannot change.
2. **Authority interfaces** chosen by the contract, not by string checks:
   `IContentAuthority`, `IMethodAuthority`, `IPacingAuthority`, `IEdgePolicy`, `IGameAuthority`,
   `IPermissionAuthority`, `IEndingAuthority`. Each has a domme implementation and a user
   implementation (user implementations read what the user reports or selects).
3. **Engine as a reducer**: `(state, event) -> (state', directives, narration events)` over typed
   events: edge, orgasm, timer, game answer, request, beg, method chosen, pause, resume, end.
   Timer arming, persistence and TTS live outside the reducer. Inject `IRandom` and `IClock`.
   This is the planner seam: the rules are one `IPlanner`; a richer rule pack or an LLM is another.
4. **Modes as presets**:
   - Mode 1 full guided: all domme.
   - Mode 2 own content: content = user; rest domme. Directed media off; everything else as mode 1.
   - Mode 3 own content and methods: the user strokes however they like; domme sets edges, holds,
     stroking times, ending. Whether she needs to know the current method is open decision 4
     below; the default is method-agnostic instructions.
   - Mode 4 free play: user controls everything; tracker records edges; permission and ending are
     domme's; begging can trigger an offer (Phase 3 supplies the templates).
   Retire `Simple` as a mode; "restricted method pool" becomes an allowed-methods filter any mode
   can use.
5. **Metronome driven by pacing authority**: directives carry a tempo (speed 1–10 → BPM table,
   plus explicit rest / hold / stop). When the domme owns pacing the companion metronome follows
   the directive and its controls are locked; when the user owns pacing it is a hidden-by-default
   tool.
6. **Games as a capability**: extract a `GameSession` state machine (start / on edge / on timer /
   on answer → outcome with applied consequences) owned by the games module and usable by any
   mode where the domme owns the affected concern. Apply the announced stakes. Cut games that
   are pointless in practice (asking the user to count edges that are already on screen, and any
   other game whose "answer" is visible or trivially known) and add more imaginative ones:
   games with real risk, decisions under pressure, or stakes that carry into the ledger.
7. **Redesign the AI Domme page.** Setup becomes "pick a mode, adjust the few fields the mode
   exposes, propose commitments, review the ledger, accept". The running session becomes: media
   window at the top, status (time left in the instruction, edges remaining, and so on), the chat
   log as the main surface, and a few buttons, each with a hotkey. Edge and Orgasm reuse the
   global tracker hotkeys. Pause instead of End outside debug sessions. The companion shows only
   controls for concerns the user owns. Built with Cove's shared components and theme variables.
8. Tests: deterministic engine unit tests per authority and per mode; a scripted end-to-end run
   per mode with a fake clock.

## Phase 3 — Commitments, offers and the punishment ledger (6–10 days)

1. **Structured commitment picker** on the setup screen and available to the domme as a counter:
   denial length (days), scope (this session / until date), session character (long, intense,
   torturous, mean, teasing…), combinable, optional note.
2. **Domme response rules**: accept, counter (longer or harsher), attach stakes. Stakes reference
   ledger entry types. Response and acceptance are chat messages backed by persisted rows.
3. **Offer library**, configurable like methods and punishment endings (shared defaults plus
   personal overrides, editable on the Tuning page): name, domme wording with parameter slots,
   parameters with ranges (edge count, denial days, hold seconds, ending type, owed punishment
   severity), a point value that scales with the chosen parameters, a flavor/style, and mood
   affinities. When the user asks for something (permission, a rest, mercy after a lost game),
   `IPermissionAuthority` / `IEndingAuthority` values what is asked for, picks an offer weighted by
   mood and flavor, fills parameters to roughly match that value, and presents it. Acceptance
   snapshots the concrete parameters into a commitment or ledger entry. Replaces the removed
   user-authored "offer definitions".
4. **Ledger table**: type, severity, origin (session, commitment, rule, offer), created, due,
   status (owed / being served / served), served-in session. Enforcement: denial windows block
   permission; owed punishment sessions and punishment endings are folded into the next
   contract; counts decrement when served. Nothing auto-deletes.
5. **Breach detection from facts**: orgasm without permission, early end, failed hold, broken
   denial window. The user self-reports whether an unauthorized orgasm was accidental or
   intentional, in that session or the next time they open AI Domme; the answer changes the
   consequence. Replace the three punishment implementations with the ledger.
6. **Unclean endings are never lost.** A session with no clean end (Cove closed, machine powered
   off, browser killed) is marked as interrupted at the next startup or visit. Before anything
   else the domme asks what happened, and the answer is recorded next to what was observed.
   Commitments in force at that moment are evaluated from the answer. This replaces the current
   confession flow, which is unreachable.
7. **Surface**: outstanding ledger entries and commitments shown at session setup and, briefly, in
   the companion log at session start.

## Phase 4 — Session logic and speech realism (8–12 days initial, then iterative)

1. **Phase planner** replacing per-step category roulette. A session moves between phases such
   as ramp-up, intensity, torture and teasing, in an order and proportion drawn from the
   contract and her mood, with a rough plan made up front (phase sequence, planned games and
   holds, reserved surprises) and adjusted by events. Not a fixed warm-up-to-climax arc.
   Repetition is deliberate or absent: returning to a method or alternating between two things is
   a valid choice the planner can make; repeating a line or an instruction pattern by accident is
   a defect. Track recent history to tell the two apart.
2. **All endings reachable live**: full, ruined, denial, punishment ender; chosen by
   `IEndingAuthority` from contract, ledger, behaviour and offers.
3. **Single narration service** with persona (stable across sessions), per-session memory
   (what was said, what happened, what was promised), no-repeat selection, and references to
   concrete events. Consolidate the four text sources; delete dead pools; keep the existing lines
   as raw material.
4. **Fix the known bugs** listed in the assessment (edge-then-hold points, retaliation reason,
   never-ending request-only sessions, fabricated edge speed/hold, unguarded timer mutation).
5. **Homework stays.** Rebuild it on the planner with a non-interactive contract, then delete the
   `OfflineInstructionEngine` and `MethodSelector` duplicates.
6. Measure with scripted sessions: no contradictory consecutive instructions, no repeated line
   within a session, every announced stake applied, every ledger entry either served or still owed.

## Later — Domme-initiated and mini-sessions (design only now)

Prerequisites to keep in mind during Phases 1–4: sessions creatable by the server, a message the
companion can show with no session active, a scheduler inside the extension, very short
contracts, and mode 4 as the resting state. Do not build until Phase 3 is done.

## Decisions

Answered 2026-09-22:

1. **Homework stays.** Rebuild it on the planner in Phase 4.
2. **Tuning page**: useful, keep it as its own page, low priority.
3. **Legacy SQLite import has run** on the real install. Importer and SQLite package removed in
   Phase 0.
5. **Unauthorized orgasms are self-reported** as accidental or intentional, in that session or the
   next time the user opens AI Domme.

Still open:

4. **Mode 3 (user picks their own methods): does the domme need to know which method the user is
   using?** She does not need it to run edges, holds and timing. Knowing it would let her react
   ("switch to something gentler") and would make the recorded edges meaningful for tuning. Options:
   (a) she never knows, and instructions stay method-agnostic ("edge twice, however you like");
   (b) the user optionally tells her, with a picker in the companion or a hotkey that cycles the
   allowed methods; (c) she asks at each phase change. Default if unanswered: (a), with (b) added
   later if it turns out to be wanted.
