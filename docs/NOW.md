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
4. **Delete dead persistence**: `Migrations/` (SQLite Designer files and snapshot), the
   `Microsoft.EntityFrameworkCore.Sqlite` package, the `IsNpgsql()` dual paths. Keep the legacy
   importer only until it has been confirmed to have run on the real install, then delete it too.
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
   - Mode 3 own content and methods: user picks the current method (companion picker or a
     configurable hotkey cycling the allowed set); domme sets edges, holds, stroking times, ending.
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
   mode where the domme owns the affected concern. Apply the announced stakes.
7. **Page and companion follow the contract**: the setup screen becomes "pick a mode, adjust the
   few fields the mode exposes, propose commitments, review the ledger, accept". The companion
   shows only controls for concerns the user owns.
8. Tests: deterministic engine unit tests per authority and per mode; a scripted end-to-end run
   per mode with a fake clock.

## Phase 3 — Commitments, offers and the punishment ledger (6–10 days)

1. **Structured commitment picker** on the setup screen and available to the domme as a counter:
   denial length (days), scope (this session / until date), session character (long, intense,
   torturous, mean, teasing…), combinable, optional note.
2. **Domme response rules**: accept, counter (longer or harsher), attach stakes. Stakes reference
   ledger entry types. Response and acceptance are chat messages backed by persisted rows.
3. **Offer templates** (code or data, not user-authored): trigger (beg, request after N edges,
   failed game, failed hold), grant (permission now, ruined, later), cost (owed punishment,
   punishment ending now, extra denial days, harder game). Rules instantiate an offer; the user
   accepts or declines; acceptance can create a commitment. Wire into `IPermissionAuthority` and
   `IEndingAuthority`.
4. **Ledger table**: type, severity, origin (session, commitment, rule, offer), created, due,
   status (owed / being served / served), served-in session. Enforcement: denial windows block
   permission; owed punishment sessions and punishment endings are folded into the next
   contract; counts decrement when served. Nothing auto-deletes.
5. **Breach detection from facts**: orgasm without permission (user reports accidental vs
   intentional; different consequences), early end, failed hold, broken denial window. Replace
   the three punishment implementations with the ledger; either make the confession flow real
   (set the suspicion flag on a genuinely abandoned session) or remove it.
6. **Surface**: outstanding ledger entries and commitments shown at session setup and, briefly, in
   the companion log at session start.

## Phase 4 — Session logic and speech realism (8–12 days initial, then iterative)

1. **Arc planner** replacing per-step category roulette: warm-up → build → plateau/tease →
   escalation → decision → ending, with a per-session plan drawn up front (method sequence
   skeleton, planned games, planned holds, reserved surprises) and adjusted by events.
2. **All endings reachable live**: full, ruined, denial, punishment ender; chosen by
   `IEndingAuthority` from contract, ledger, behaviour and offers.
3. **Single narration service** with persona (stable across sessions), per-session memory
   (what was said, what happened, what was promised), no-repeat selection, and references to
   concrete events. Consolidate the four text sources; delete dead pools; keep the existing lines
   as raw material.
4. **Fix the known bugs** listed in the assessment (edge-then-hold points, retaliation reason,
   never-ending request-only sessions, fabricated edge speed/hold, unguarded timer mutation).
5. **Delete** `OfflineInstructionEngine` and `MethodSelector` duplicates; if homework is still
   wanted, generate it from the planner with a non-interactive contract.
6. Measure with scripted sessions: no contradictory consecutive instructions, no repeated line
   within a session, every announced stake applied, every ledger entry either served or still owed.

## Later — Domme-initiated and mini-sessions (design only now)

Prerequisites to keep in mind during Phases 1–4: sessions creatable by the server, a message the
companion can show with no session active, a scheduler inside the extension, very short
contracts, and mode 4 as the resting state. Do not build until Phase 3 is done.

## Decisions needed from you

1. Keep the offline "homework" generator at all? (Recommendation: drop it; revisit after Phase 4.)
2. Keep a separate Tuning page, or fold method/ender editing into a tab on the AI Domme page?
   (Recommendation: one page with tabs; Cove's guidelines discourage extra nav items.)
3. Has the legacy SQLite import already run on your real Cove install? If yes, the importer and
   the SQLite package can go in Phase 0.
4. Mode 3 input for "current method": companion picker, hotkey cycle, or both?
5. Accidental vs intentional unauthorized orgasm: does the user self-report, or is it always
   treated the same with the domme deciding severity?
