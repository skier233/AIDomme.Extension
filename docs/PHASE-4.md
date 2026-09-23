# Phase 4 — Session logic and speech: design

Status: **signed off 2026-09-23; in implementation.** The decision log at the end records what was
settled in each round.

"Speech" throughout means the domme's sentences and language, never TTS.

## 1. What this phase must achieve

- Sessions that feel steered by a person: a logical flow with a shape, adaptable to what the
  user does, and different from the last one.
- Speech with one consistent persona that knows what has happened (this session and before),
  reacts to what the user does, fills the quiet moments, and never repeats a story by accident.
- The same domme in every mode and in homework.
- No structural choice that blocks the northstar: free-text messages from the user later, a
  language model as a narrator (or an authoring aid) later, domme-initiated mini-sessions later.

Constraints carried over: the engine stays deterministic (injected random and clock); the
contract, ledger, tracker and persistence models stay as they are; the checkpoint must capture
everything she "knows" and the whole plan, so a restart never makes her repeat or forget.

## 2. Three layers: decide, know, speak

| Layer | Owns | Produces |
| --- | --- | --- |
| **Decide** (the plan engine) | the session plan, its revision after events, step choice within a segment, the ending verdict | `Directive` + an *utterance spec* (what to say, why), never text |
| **Know** (recall) | what she knows: facts from the database, this session's state, what is on screen | a `Knowledge` object the other two read |
| **Speak** (narrator) | persona, the two speech streams, text tiers, usage tracking | the words |

The seam that matters: **decision code never contains sentences.** Today `RulePlanner`,
`SessionEngine`, `DommeVoice`, `DommeTextPool` and `OfflineInstructionEngine` all make text. After
this phase only the narrator does. `Transition.Domme` becomes a list of utterance specs; the host
narrates them into chat messages.

### 2.1 Who changes the plan

The plan engine has two jobs, both in the Decide layer:

- **Make** the plan at session start (§3.3) from the contract, persona, mood and recall.
- **Revise** it when something happens. The existing `SessionEngine` stays the mechanical
  reducer (timers, points, permission gating, ledger effects); on each event it asks the plan
  engine, which applies **adaptation rules** that edit the *remaining* plan (§3.4). The engine
  then takes the next step from the plan.

The plan is plain data inside `SessionState`, so it is checkpointed, shown in the debug view and
printed at the top of every transcript. Because the session is "run the plan, revise it on
events" rather than a roll per step, homework is the same code with no events, and tests can
assert on plans directly.

### 2.2 Alternatives considered

| Approach | How it works | Trade-offs |
| --- | --- | --- |
| A. Text made where decisions are made (today) | planner builds directive and its sentence together | simple; every voice change touches decision code; no memory; blocks any other narrator |
| B. Structured intents + narrator (base) | planner emits what to say and why; narrator writes it | clean seam; text can be improved in isolation; risk of stilted "assembled" sentences if the narrator is only a grammar |
| C. Script modules drive both (Tease AI) | text modules carry instructions and speech together | most natural, story-like text; flow logic hidden inside text; long modules are obvious when repeated; hard to adapt mid-session; every mood/variant needs a rewrite |
| D. B, with story modules attached to segments (proposed) | decisions are structured; a segment may carry a written multi-line module that fits its goal; usage is tracked across sessions | keeps the seam and the adaptability of B, gets the coherence of C where it matters; needs a usage record so stories are not repeated |

**D is the proposal.** Details in §5.

## 3. The session plan: segments with goals

### 3.1 Segments

There is no mandatory warm-up and no 30-minute "torture phase". The unit is a **segment**: a
stretch of the session with one goal, what she is trying to make the user do or feel. This is
how a person would think about it. Examples of segment goals:

- build him up without letting him edge
- rapid-fire edges on intense methods
- one long hold
- slow, maddening stroking at tempo 1
- as many edges as he can at a set tempo
- stroke for a set time without reaching an edge
- torment with a method he hates
- let him think he's done, then more
- punish the beg
- a game, with its stakes
- a rest that isn't really a rest (teasing while hands off)
- the ending (§7)

A segment is a **type** from a catalogue plus **parameters** the plan fills in:

- **length**: a points share or a step count, from a range the type allows. The same goal can be
  very short or very long; the plan (and user settings, §3.5) decide. "Rapid-fire edges" might be
  three edges or twenty minutes; either way it is the thing she wants him to experience right now.
- **method policy**: one method throughout; rotate among methods with a quality (intense,
  teasing, hated, a favourite); swap on every edge; "whatever he was using"; a specific method
  as a callback.
- **pacing policy**, **edge and hold policy**: what today's step builders take as inputs.
- **narration theme**: what the command stream says while this goal runs (§5.1).
- **presentation**: what she lets on about it (§4).

Within a segment the step builders that exist today produce the actual directives, constrained
by the segment's policies. A segment can vary its methods mid-way; variety comes from the
policy, not from a roll per step.

### 3.2 Plan modes

- **Guided modes** (she sets pacing or edges): a full plan of segments.
- **Free play**: no plan of segments. Her job is the **verdict**: whether, when and how the
  session ends, on what terms, from what he has done (edges, stroke time, total time, what he has
  watched, edges since his last orgasm and today, promises and debts) and her mood. The verdict
  is the same ending authority the plan's ending segment uses (§7). Her chatter (§5.1) still
  runs, so she is present.
- **Homework**: a plan with no events, usually shorter, not always.
- **Mini-sessions** (later): a plan of one or two segments.

### 3.3 Making the plan: order and chaining

Inputs: the contract (length, intensity, meanness, denial, traits from promises, punishment,
limits, authorities); persona and mood (§4); recall (§6): what the last sessions were like so
this one differs, what is owed, which methods work on him, how he behaves; user settings (§3.5).

The order matters as much as the segments. A plan is never a uniform draw from the catalogue;
that would feel random. The plan engine works one level above segments and chooses each one
conditioned on what came before, where in the session it sits, and what is left.

**Everything below is expressed as properties that each segment type declares, not as rules
written for particular segments.** A segment type carries tags (for example `setup`, `payoff`,
`torture`, `tease`, `edging`, `rest`, `game`), an intensity score, the tags it likes to follow
and to lead into, where it may sit, its length range and whether it needs a follow-up. The plan
engine only reads those properties, so adding a segment type is adding a catalogue entry. Rules
that name a specific segment are a rare exception and are listed as such.

- **Transition affinities**, from each type's tag preferences. "Build him up without an edge" leads
  naturally into "rapid-fire edges" (setup and payoff); "one long hold" leads into a rest or a
  tease; a game's follow-up depends on its result; "let him think he's done" must be followed by
  more. Some pairs are near-forbidden (two tortures back to back unless the plan means it).
- **Chains**: a segment type may declare the follow-up it needs ("let him think he's done" needs
  more), so combinations that only make sense together arise from properties. Adaptation rules
  (§3.4) produce the rest, such as "mercy with a twist": granted rest, rapid-fire edges, back to
  the torture.
- **An escalation curve**: every segment type has an intensity score; the plan aims at an
  overall rise with deliberate dips, shaped by length, meanness and mood. It never plateaus by
  accident and never opens at the top unless the session is a punishment.
- **Position rules**: what can open a session (depends on persona and settings, not a fixed
  warm-up), what belongs in the middle, what must precede the ending segment.
- **Budget**: segment lengths are chosen so the plan fits the points budget, with the ending
  placed last and its lean already chosen (§7).
- **Variety across sessions** from recall: not the same opening twice running, not the same
  chain as last time, callbacks to methods that worked before.

Keeping this in the plan engine, and the step-level detail in the segments, keeps each part
small enough to test and tune on its own: the plan engine can be judged from plan dumps alone,
segments from their steps. Both will be revised from transcripts; the split is what makes that
cheap.

### 3.4 Revising the plan

Events edit the remaining plan through adaptation rules, for example:

- begging to stop during a torturous segment → she may grant it as a swap into a rapid-fire
  edge segment, then swing back to the torture, and may alternate the two;
- a lost game → a punishing segment is inserted;
- pestering for permission → the ending moves further away, or gets meaner;
- an orgasm without permission → the rest of the plan is rewritten;
- her mood turning (he made her mad) → remaining segments get longer or harder;
- a low-rate change of mind with no visible cause, so the plan is not a contract.

Worst case a rule replaces everything that remains; usually it inserts, removes, reorders or
re-parameterises one or two segments.

### 3.5 User settings that shape the plan

To be decided in detail (§12). The likely shape: per segment type, *never / rarely / normally /
often*, and a preferred length range; plus things she should learn from facts rather than be
told. The persona (§4) is separate: it is how she talks, not what she plans.

### 3.6 Debug view

The whole plan is visible up front in a debug session and at the top of every transcript
(§10): segments, their parameters, the ending she is leaning toward, and, as the session runs,
which rule changed what. This is how the plan engine gets tuned without playing sessions.

## 4. Persona and mood

**Persona** is set once by the user and shapes how she talks: what she calls him, how harsh her
tone is at neutral mood, how much humiliation she uses, how much psychological manipulation
(mind games, false promises, lying about the ending), how terse or talkative she is, her
register and rules of style, her signature phrases. One persona ships as data; the settings that
matter most are exposed on the settings panel in this phase, the rest later.

**Session severity** is not the persona. How torturous or mean a session is comes from the
contract (preferences and traits) and the plan. A gentle-tongued persona can run a brutal
session; a vicious one can run a mild one.

**Mood** is dynamic, one state, read by both layers: whether she is pleased, bored, annoyed or
angry, moving for reasons (events this session, how the last sessions went) more than by drift,
so the narrator can name the reason. Mood changes the plan (annoyed: the rest gets longer or
harder) and the speech (tone shifts from the persona's baseline).

**Intent vs. presentation.** The plan knows the truth (for example the ending). What she lets on
is separate: honest foreshadowing, misdirection ("you'll get to cum tonight" when she has decided
denial), or silence. How often she lies is the persona's manipulation setting, sharpened by
mood; the narrator writes to the presentation, never the truth.

## 5. Speech

### 5.1 Two streams

- **Commands**: what she orders and what he is doing. Tied to segments and steps: the
  instruction itself, reactions to his inputs (edge, orgasm, beg, rest, done, offers, game
  answers), segment changes, the ending. Its content follows the plan; its tone follows persona
  and mood.
- **Chatter**: what she says when she is not giving orders. Psychological play, teasing,
  humiliation, stories, remarks about what is on screen, callbacks to earlier sessions, idle
  threats. It fills timed steps, holds, rests and free play. Its tone is the persona's, coloured
  by mood; it is **not** tied to how severe the session is. It can reference the current
  segment (a theme) but need not.

The streams are separate in what they say and where it comes from, but **one scheduler owns
the timing of both**, so they never talk over each other:

- commands are always on time; chatter yields. A story is only started when the quiet stretch
  is long enough to finish it (the scheduler can see the step timer); if a command interrupts
  anyway, the story's remaining lines are dropped or resumed later with a bridge, never spoken
  into the command.
- a minimum gap between any two lines, and no chatter within a few seconds before a command the
  scheduler knows is coming (a timer about to fire, an edge target one away).
- a **bridge** when a command follows chatter that needs one ("Enough of that. Edge."); some
  contexts need none and the module says which.
- a talkativeness budget from the persona and the session (§4, §12), and silence as a valid
  choice that cruel moods use.

Commands can borrow from chatter's context: a command module may have conditions on what is on
screen or on the story in progress ("Edge right now, for [performer]"). That is a command
micro-module with extra conditions, not a third stream.

### 5.2 Three tiers of text

| Tier | What | Repeats |
| --- | --- | --- |
| **Core** | the instruction itself: method, speed, count, hold, time. Generated from a small grammar so it is always exact. | freely ("stroke" is "stroke") |
| **Stock** | short reactions and colour: openers, praise, threats, edge remarks, denials, segment changes, one-line chatter. Written lines with slots, tagged. | not within a session (exact line); fragments cool down |
| **Story** | multi-line modules: a mini-story, a running bit across a segment, a lie about the ending, "let me tell you about a boy who made me mad last weekend". | rarely: tracked per user across sessions with cooldown tiers (*once ever*, *rare*, *occasional*) |

This is the answer to the Tease AI lesson (longer text is more natural and more obviously
repeated): long text stays rare and tracked, short text stays plentiful and free, and the joins
are made natural with glue that uses memory.

### 5.3 Modules

A module is data (embedded JSON, validated by a test, editable without code):

- id, tier, stream (command or chatter), act (instruct / react-to-beg / react-to-edge /
  announce segment / foreshadow / callback / interjection / story / ending / out-of-session
  message);
- conditions: persona settings (humiliation on, manipulation ≥ n), mood, segment goal, facts
  ("begged ≥ 3", "owes edges", "failed a hold", "last session denied", "method needs lube",
  "on screen: …");
- lines with slots (`[method]`, `[count]`, `[edges]`, `[days]`, `[name]`, …) and optional
  branches on the user's next event (a story can react: "still going? good");
- for story modules, where each line lands (start, mid, end of a segment; or across several
  quiet moments).

The narrator picks a module for a spec by conditions and weights, renders the core exactly,
and records the usage (session memory for stock; the usage table for story, §6).

### 5.4 What makes it sound like her

- **Callbacks** from memory: "third time asking", "you held that one; last time you didn't", "you
  still owe me two edges from Tuesday", "you didn't last a minute on this grip last week".
- **Reactions are the dialogue.** His inputs are few; each gets a specific, in-context reaction.
- **Terseness.** "Again." can be the instruction. The persona's talkativeness and the mood set
  the budget.
- **Chatter that knows the room**: what is playing, what segment this is, what he did last time.
- **Deliberate repetition** is tagged by the plan and voiced as such ("Same grip. Again.").
- **Presentation** lets her lie, tease with a false promise, or say nothing about the ending.

## 6. Knowledge (recall)

The database already holds the facts: sessions, contracts, edges (method, speed), orgasms,
requests, games, offers, commitments, ledger, tracker events. Nothing new needs to be stored to
know them; the work is a **recall** step that turns them into what she knows:

- last N sessions: mode, outcome, points, edges, requests, games, offers, how she ended it;
- streaks: denials in a row, days since the last orgasm, edges since, edges today;
- what he responds to: methods with the most edges per minute, methods he never edged on;
- what he does: begs early, begs often, fails holds, walks out, takes offers;
- promises and debts (from the ledger);
- **what is on screen** now, and what he has watched: Cove holds detailed watch and engagement
  data and extensions can read it (and hooks can be added to Cove where something is missing).
  The page reports the active media with its polls; recall reads titles, performers, tags and
  watch time. Inputs for chatter, for command micro-modules, and for the free play verdict.

Session knowledge is the engine state plus a small event log inside it (edges, holds, begs,
denials, offers, games, mood changes, segment history, lines used, plan revisions).

The **one new table**: narration usage: module id, user, session id, when. A session id is
enough to place a use; no other structure. Whether a short written summary per session is worth
storing is open; facts come first.

## 7. Endings

An ending authority chooses among full, ruined, denied and a punishment ending from the
contract, promises and the ledger (a no-orgasm promise or a denial window settles it), this
session's behaviour, mood and recall (not five denials in a row unless denial is high). In a
plan she **leans** toward an ending when the plan is made, finalises it in the ending segment,
and chooses a presentation: honest foreshadowing, a lie, or nothing. In free play the same
authority gives the verdict at the moment it is asked for or when she decides it is time.
Ruined and punishment endings become her own choices, not only owed ones. Offers keep working
as they do now.

## 8. Homework

Homework is the plan engine run with no events: a plan of segments narrated by the narrator,
with counts instead of live edge feedback, usually shorter. `OfflineInstructionEngine` and
`MethodSelector` are deleted.

## 9. Room for later

**User messages.** A free-text message would be an event ("the user said …") like a beg or an
edge. Rules can classify it (beg, complaint, question, backtalk) and the plan engine and
narrator react through the same specs. Nothing in this design assumes buttons only.

**A language model.** Not in this phase. Two doors stay open:

1. *Runtime narrator*: an `INarrator` that renders specs through a model, with the core
   instruction still generated by rules, a style validator (reject em-dashes, lists, "not X but
   Y", length limits), and fallback to the rule narrator. Whether a local model can hold the
   persona and the NSFW register is unknown; the door costs nothing.
2. *Authoring aid*: use a model offline to draft story modules and stock lines in the persona's
   voice, for review and editing before they enter the data files. This addresses the real
   bottleneck (writing many original modules) without a model in the loop or on user hardware.

**Mini-sessions.** Short plans and an out-of-session message act are part of the vocabulary.

## 10. Measuring it

- **Simulated users** in tests: obedient; begs to cum every minute; begs to stop during
  torture; fails holds; only edges, nothing else; takes every offer; declines every offer;
  different game results. Seeded, per mode, with a fake clock.
- **Invariants** asserted automatically: no exact line repeated in a session; no story module
  repeated across simulated sessions within its tier's cooldown; no contradictory consecutive
  instructions; every announced stake applied; every segment type reachable; every ending
  reachable; plan revisions only through rules; the checkpoint round trip keeps the plan and
  narrator memory.
- **Transcripts** written to files: the plan up front, then the session as chat, with the
  simulated user's actions inline, the true intent beside what she said, and each plan revision
  noted where it happened. Reading these is the iteration loop for both the plan engine and the
  writing.

## 11. Order of work (proposed)

1. **The seam**: utterance specs, `INarrator`, knowledge object, session event log, transcript
   harness. Existing lines move over unchanged so nothing regresses.
2. **The plan engine**: segment catalogue, plan making, adaptation rules, ending authority
   (shared with the free play verdict), mood with causes, the chatter scheduler's tick, debug
   plan view.
3. **The voice**: persona settings, module format and validator, usage table, rewriting the
   pools into stock lines, first story modules and chatter, callbacks, on-screen context.
   Transcript rounds.
4. **Cleanup**: homework on the plan engine; delete `OfflineInstructionEngine`,
   `MethodSelector`, `DommeVoice`, `DommeTextPool`; confirm the assessment's listed bugs are gone
   (most fell with the Phase 2 rewrite; each is checked).

Step 3 is where the effort goes and where the transcript rounds live. Step 1 is the part that,
done wrong, becomes the bottleneck.

## 12. Open questions

1. ~~Persona settings~~ settled: anything psychological belongs to the persona (and mood can
   make her more manipulative one night); anything about commands belongs to the session.
2. ~~Plan settings~~ settled for now: per segment type *never / rarely / normally / often* plus
   a length preference; expected to be tuned and redone.
3. ~~On-screen context~~ settled: in this phase; Cove exposes what is needed.
4. ~~Story usage~~ settled: one usage table is the only new storage for narration.
5. ~~The authoring aid~~ settled: a Claude skill in the repo for drafting modules, no model or
   UI in the product.
6. ~~Chatter frequency~~ settled: persona talkativeness plus a per-session option.
7. ~~Per-session talkativeness~~ settled: each persona dial (talkativeness, humiliation,
   manipulation, harshness) varies per session around the user's setting, partly at random and
   partly from memory of past sessions. The session's values are recorded with the session.
8. ~~On-screen data~~ settled: first what is happening on screen now (the tags of the current
   video segment, and performers), so she can comment on it naturally. Letting her pick videos by
   tags, and hooking into recommendation extensions, are later.

## 13. Decision log

- 2026-09-23 — First draft discussed. Agreed in principle: decisions must not contain text; the
  domme's knowledge comes from facts already in the database plus session state; terseness is a
  tool; stock phrasing may repeat, stories must not; a rough plan with adaptation; endings can
  be known early and foreshadowed or lied about; homework runs on the same plan; simulated
  transcripts are the measure. Rejected: mandatory phases such as a warm-up. Deferred: LLMs.
- 2026-09-23 — Second round. Settled: segments with goals are the unit, with configurable
  lengths and method variation inside a segment; the plan engine both makes and revises the
  plan, the reducer stays mechanical; speech has two streams, commands (tied to the plan) and
  chatter (tied to the persona), and session severity is not the persona; persona is a one-time
  user setting for how she talks, mood is dynamic and moves both plan and speech; free play has
  no plan of segments, only the verdict and chatter; commitments feed the ending; homework is
  usually shorter.
- 2026-09-23 — Third round. Settled: the plan engine chooses segments by transition affinities,
  chains, an escalation curve and position rules, never a uniform draw; one scheduler owns the
  timing of both speech streams, chatter yields to commands, bridges join them; command modules
  may reference on-screen content and the story in progress; psychological traits are persona
  (mood can sharpen them), command traits are the session; on-screen and watch data come from
  Cove in this phase, with hooks added to Cove if needed; the authoring aid is a Claude skill in
  the repo, not a product feature; talkativeness is a persona setting plus a per-session option.
- 2026-09-23 — Signed off. Settled: segment ordering is driven by properties each segment type
  declares (tags, intensity, affinities, positions, follow-ups), so the catalogue scales without
  per-segment rules; persona dials vary per session around the user's settings, partly random and
  partly from memory; on-screen context starts with the current video segment's tags and
  performers; choosing videos by tags and recommendation hooks are out of scope.
