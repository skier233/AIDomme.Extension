# AI Domme — Northstar

Last revised: 2026-09-22. This is the durable product vision. The point-in-time review of the
codebase lives in [CURRENT-STATE-ASSESSMENT.md](CURRENT-STATE-ASSESSMENT.md) and the ordered work
list lives in [NOW.md](NOW.md). When those two disagree with this file, this file wins and they
should be updated.

## 1. What AI Domme is

AI Domme is a Cove extension. It is a scripted, rule-driven domme (no language model required,
though one could be slotted in later) that runs edging / orgasm-control sessions on top of Cove's
media library, watches the user's progress through an edge tracker, speaks through TTS, and keeps a
persistent memory of agreements, infractions and owed punishments across sessions.

It is a first-class part of Cove, not an app embedded in Cove. It stores its data in Cove's
PostgreSQL database through Cove's extension data mechanism, uses Cove's design system, keyboard
system, media viewers, navigation and authenticated API surface, and follows the conventions used by
the other extensions in this workspace. It has no database of its own.

## 2. Vocabulary

These terms should be used consistently in code, UI and docs.

| Term | Meaning |
| --- | --- |
| **Session** | A bounded period of play with a start, an end and a contract. |
| **Session contract** | Everything settled before play begins: the mode, the commitments in force, intensity/length preferences, allowed toys/kinks, and whether settings may change mid-session. The contract is what the domme holds the user to. |
| **Concern** | One thing that someone must decide during a session: content, methods, pacing, edges & holds, games, permission, ending. |
| **Authority** | For each concern, who decides: the domme or the user. A mode is just a named assignment of authorities. |
| **Mode** | A preset assignment of authorities (see §3). Modes are presets, not separate engines. |
| **Content** | The Cove media the user looks at. Either user-browsed or domme-directed. |
| **Method** | A stimulation method definition from the library (name, instructions, intensity, tease, toy category, multipliers…). Editable in Tuning. Shared defaults plus personal overrides. |
| **Pacing** | Stroke speed and timing: the metronome tempo, rests, holds, "stop", "slow", "fast". |
| **Edge** | A recorded edge. The tracker (hotkey or button) is how the domme "sees" the user. |
| **Hold** | Holding at the edge for a prescribed duration. |
| **Ending** | How the session concludes: full orgasm, ruined orgasm, denial, or a punishment ending (a "punishment ender" from the library). Endings can be conditioned on behaviour during the session. |
| **Permission request** | The user asking to orgasm. The domme answers yes / no / not yet, possibly with an offer attached. |
| **Begging** | A stronger form of request. May trigger an offer. |
| **Offer (deal)** | A price the domme can ask in exchange for something the user wants. Offers are a **configurable library**, like methods and punishment endings: each offer definition has parameters the domme fills in at the moment (e.g. "edge **X** times before you may orgasm again", "**N** extra denial days", "the ending will be ruined", "you owe a punishment session"), a point value that scales with its parameters, and a flavor/style (cruel, playful, humiliating, teasing, strict…). The domme's mood biases which flavors she reaches for. The user accepts or declines. Offers are never one-off items the user writes for themselves. |
| **Commitment** | A user-proposed agreement, chosen from structured options rather than free text, for a session or a time range: e.g. "no orgasm for N days", "no orgasm this session", "a long and intense session", "a long, torturous and mean session", or a combination. The domme accepts, counters, or attaches stakes. Commitments can also be offered by the domme as deals, games, or as the price of an orgasm. |
| **Infraction** | A breach of the contract or of an instruction: leaving a session early, orgasm without permission (accidental or intentional, recorded differently), disobedience, a failed hold. |
| **Punishment ledger** | The persistent record of owed punishments. An entry is created when a commitment or rule is broken and is served in a future session. It cannot be forgotten, dismissed or lost by closing the browser. |
| **Game** | A mini-game capability (coin flip, number guess, edge endurance…) that the domme can use in any mode where she holds authority over the concern the game affects. |
| **Companion** | The small floating window that follows the user around Cove. It is a chat log / current-instruction view with the few controls the user holds authority over in the current mode. |
| **Mini-session** (future) | A short session initiated by the domme at a time of her choosing, possibly starting from a message left for the user. |

## 3. Modes: the authority matrix

Every mode is a row in this matrix. "D" means the domme decides, "U" means the user decides.

| Mode | Content | Methods | Pacing | Edges & holds | Games | Permission | Ending |
| --- | --- | --- | --- | --- | --- | --- | --- |
| **1. Full guided** (today's AI Domme page) | D | D | D | D | D | D | D |
| **2. Own content, guided** | U | D | D | D | D | D | D |
| **3. Own content and methods** | U | U | D | D | D | D | D |
| **4. Free play, domme-owned ending** | U | U | U | U (tracked) | optional | D | D |
| *(future)* **Domme-initiated / mini-session** | D or U | D | D | D | D | D | D |

Notes on the matrix:

- Mode 4 is the "always on" mode: the user does what they want, records edges with hotkeys or
  the tracker so the domme knows the current state, and asks for permission when they want to come.
  The domme answers based on history, the contract and the mood of the rules. Begging can trigger an
  offer (§5).
- Every concern the domme holds is enforced in the UI: in modes 1–3 the metronome is driven by the
  domme and its tempo controls are locked; in mode 4 the metronome is a user tool but hidden by
  default.
- Modes are presets over independent authority fields. The engine must not branch on the mode
  name; it must consult the authority for each concern. That is what lets new modes (mini-sessions,
  a "domme picks methods but user paces" variant) be added without touching the engine core.
- Whether the contract may change mid-session is part of the contract and enforced server-side.

## 4. The session lifecycle

1. **Propose.** The user opens the AI Domme page (or, in mode 4, just the companion), picks a mode,
   toys/kinks, intensity/length preferences, and optionally proposes commitments (§5).
2. **Negotiate.** The domme responds in the chat: accepts, counters, attaches stakes ("if you leave
   early you owe a punishment session"), or reminds the user of outstanding ledger entries that must
   be served first. Outstanding punishments can be folded into this session's plan.
3. **Contract.** The user accepts. The contract is persisted with a revision. Nothing in it changes
   unless the contract says changes are allowed.
4. **Play.** The engine runs the plan for the concerns it owns and listens to the tracker for the
   rest. A session moves between **phases** such as ramp-up, intensity, torture and teasing, in
   whatever order and proportion the contract and her mood call for; it is not a fixed
   warm-up/climax arc. Repetition must feel intentional: returning to a method, or alternating
   between two things, is fine and often good; accidental repeats of the same line or the same
   instruction pattern are not. The domme's speech should reference what has actually happened
   (edge count, failed holds, earlier promises, the ledger), not just fire random lines per event.
5. **Ending.** The domme decides the ending for the concerns she owns. In mode 4 this is the answer
   to the permission request. Endings are chosen from the contract, the commitments, the ledger, the
   behaviour this session, and the offer templates.
6. **Settle.** Infractions become ledger entries. Commitments are resolved (kept / partially kept /
   broken, with the user's report recorded separately from what was observed). Unfinished business
   is carried forward and surfaced next time.

Undo of a tracker event corrects the facts; it never rewrites an instruction already given.

## 5. Commitments, offers and the ledger

- **Commitments are structured, not typed.** The UI offers pickers: duration of denial (days),
  scope (this session / until date), session character (long, intense, torturous, mean, teasing…).
  Combinations are allowed. Free text is at most an optional note.
- **The domme has a say.** Rules decide whether to accept, counter (longer, harsher) or attach
  stakes. Stakes are the consequence if the commitment is broken: a ledger entry of a given
  severity, a punishment session, a punishment ending next time, extra denial days.
- **Breaking a commitment is detected from facts**: an orgasm recorded without permission, a
  session ended early, a hold failed. Accidental and intentional breaches are recorded differently
  and can carry different consequences; the user reports which it was.
- **Offers are a configurable library.** Each offer definition is edited like a method or a
  punishment ending: name, the wording the domme uses, parameters with ranges (edge count, denial
  days, hold seconds, which ending), a point value that scales with the chosen parameters, a
  flavor/style, and mood affinities. Shared defaults ship with the extension; the user can tune
  personal overrides. At the moment of a trade (the user begs, requests permission, wants a rest,
  loses a game) the domme picks an offer, weighted by mood, flavor and how much the thing being
  asked for is "worth", fills in the parameters, and presents it. The user accepts or declines.
  Accepting creates a commitment or a ledger entry with the concrete parameters snapshotted.
- **Commitments are failure- and cheat-proof.** A session or commitment that ends uncleanly
  (Cove closed, computer powered off, browser killed) is never silently dropped and never
  silently treated as kept. The next time the user opens AI Domme the domme asks what happened
  before anything else, and the answer is recorded next to the facts that were observed.
- **Unauthorized orgasms are self-reported as accidental or intentional**, either in that session
  or the next time the user sees the domme. The distinction changes the consequence.
- **The ledger is permanent.** Owed punishments live in Cove's database under the user's account
  with their origin (which session, which commitment, which rule). They are surfaced at the start
  of every session and in the companion, and are served through punishment sessions or punishment
  endings. Serving one records that it was served. Nothing in the ledger is auto-deleted.

## 6. The companion

The floating window is a **chat log with the current instruction**, sized like a chat bubble, not a
control panel. It should show:

- The domme's latest instruction and a short scrollable log.
- A countdown when the current instruction is timed.
- Edge / orgasm recording (also on hotkeys), and "request permission" / "beg" in modes where the
  user holds that authority.
- Pause / end.

It should **not** show offers and commitments management, method editing, tracker settings, or the
metronome by default. The metronome is hidden by default; when the domme owns pacing it is driven
by her and its tempo is locked. The companion must work in fullscreen viewers and stay out of the
way of the content.

## 6a. The session page

The AI Domme page, while a guided session runs, is a focused view, not a settings form:

- The media window at the top (domme-directed content, or whatever the user is browsing in modes
  where the user owns content).
- Status: time in the current instruction, edges remaining, and similar counters.
- The chat log, which is the primary surface.
- A handful of buttons, every one with a hotkey. Edge and Orgasm share the global tracker hotkeys
  because they are the same action.
- **Pause, not End.** Outside debug sessions the user cannot end a session; they can pause it.
  Leaving early is something the domme deals with, not a button.

Setup (mode, contract, commitments, ledger review) happens before the session on the same page.
Method, ending and offer libraries live on a separate Tuning page, which is useful but not a
priority.

## 7. Session logic and speech quality

The rules that pick methods, pacing, edges, holds, games, punishments and endings must produce
sessions that are **logical** (no contradictory instructions, phases that follow from each other,
consequences that follow from the contract) and **realistic** (speech with a consistent persona,
memory of the session and the ledger, escalation over time, variety without randomness that
breaks continuity, and repetition only when it reads as deliberate).

**Games must be worth playing.** A game should add tension, risk or a real decision. Games that
are pointless in practice (for example asking the user to count edges while the edge count is on
screen) should be removed, and the catalogue should grow with more imaginative ones.

Architecturally that means a **planner** behind an interface: given the contract, the history
summaries, the ledger and the current session state, it produces the next directive and the words
to say. Today the planner is deterministic rules and text pools. A language model, or richer rule
packs, can replace or augment it later without changing the tracker, persistence, UI or contract
model.

## 8. Future: domme-initiated sessions

Not built now, but every decision above must leave room for it:

- The domme chooses a time and leaves a message (morning, evening, random). When the user responds
  it starts a mini-session with its own short contract.
- Requires: sessions that can be created by the server rather than the user, a notification /
  inbox surface in Cove, a scheduler in the extension, and mode 4 as the baseline state.
- Implication now: the session model must not assume the user always initiates; the companion must
  be able to show a message when no session is active; the contract model must support very short
  sessions.

## 9. Engineering principles

1. **Cove-native or nothing.** Persistence through Cove's data extension model and Cove-owned
   migrations, Cove design-system components and tokens, Cove keyboard actions, Cove media
   viewers, Cove navigation, `extensionFetch`. No extension-owned database, no parallel design
   system, no reimplemented host features.
2. **Facts vs. interpretation.** Raw tracker events are one table. Engine decisions, permission
   answers, and infractions are separate records that point at the facts. Corrections recompute
   derived state; they never invent or erase decisions.
3. **Authorities, not modes, in the engine.** Modes are presets over independent authority fields.
4. **Contract first.** A session cannot start without a persisted contract; the contract is the
   thing the domme enforces and the thing the ledger references.
5. **Planner behind an interface.** Rules today, richer rules or an LLM later.
6. **Small, explainable rules with tests.** Each decision records its inputs and rule version so
   behaviour can be explained and regression-tested.
7. **One runtime per account, one owner per browser resource.** Audio, narration and directed
   media each have exactly one owner across tabs.
8. **Nothing forgotten.** Ledger entries, commitments and infractions survive restarts, navigation,
   closed browsers and engine failures.

## 10. Non-goals for now

- Building the 24/7 / mini-session mode (design for it, do not build it).
- A language model in the loop.
- Multi-user or shared sessions.
- Importing historical activity from the old standalone app (definitions and tuning only).

## 11. Kept capabilities

- **Homework** (offline instructions generated for later) stays. It should be produced by the same
  planner as live sessions with a non-interactive contract.
