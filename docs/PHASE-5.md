# Phase 5: what she wants him to feel

Status: **draft for discussion, second revision 2026-10-03. Nothing here is built.** The decision
log at the end records what is settled. This replaces the segment catalogue and the plan engine's
selection logic from Phase 4; the narrator, the voice scheduler, the ledger, deals and the tuning
lab stay.

## 1. Why

Phase 4's segments are defined by their mechanics (a ladder, a burst, a crawl). The scheduler can
only weigh tags, so in the middle of a session every segment is about equally likely and sessions
feel like a sampler. And nothing models the one thing a real domme watches: how close he is, and
for how long.

The change: she plans in terms of **what she wants him to feel**, tracks **how close to the edge he
is** from moment to moment, and steers mainly with **how long she lets him recover**.

## 1A. Vocabulary, and what is written where

Five terms, each a different thing:

| Term | What it is | Is it written by hand? |
|---|---|---|
| **Aim** | What she is pursuing over a longer stretch, across several segments: a purpose plus what she wants to have achieved ("a long stretch of intense edging until his control is thin"). It exists only at run time. | Never. She sets aims as she plans. |
| **Purpose** | What she wants him to feel (Build the ache, Test control, Overwhelm, Starve, Break). | A small data entry each, about ten lines. No steps, no script, no sentences. |
| **Segment** | A stretch of one session: her intention plus the parameters she chose, held for a while. It exists only at run time, in the plan. | Never. She creates segments as she plans. |
| **Device** | A twist inside or between segments: a hold, a hidden count, a false finish, a countdown. | A small data entry each (when it may be used, its ranges). |
| **Pattern** | An authored arrangement of segments: "overwhelm, then intense edging, three rounds". | Yes, as data: a short list of segments with overrides, a trigger and limits. |

**Purpose versus segment.** A purpose is a definition; there are about six and they never change
during a session. A segment is one occurrence: this purpose, tonight, with these parameters. "Test
control" is a purpose. "Minutes 12 to 17: Test control, kept above 80%, on Skin-Held Shaft Stroke,
counts hidden, 15 second holds" is a segment. The same purpose can appear as several different
segments in one night.

**Written segments.** Most segments are steered and have nothing written behind them. Where a
specific shape matters, a segment can be written instead: its steps authored, fully or in outline,
with amounts sized from his profile. Examples: an edge ladder (one edge at a time, each faster), a
countdown to an orgasm that does not come, a set piece she uses rarely, and every game. A written
segment still declares which purpose it serves and what it does to his state, so she chooses it
for a reason like any other. Writing one is always optional: it is for the cases where the shape is
the point, never a requirement for a purpose to work.

**What a purpose entry holds** (illustrative):

```
Starve
  feels like:   too little touch; desperate for more
  methods fit:  tease high, intensity low, reach low
  typical:      speed 1 to 3; closeness kept around 30 to 60%; no edges; no breaks needed
  does to him:  need up slowly, overwhelm down, control recovers
  needs:        she sets the pace
```

**What it does not hold:** steps. One shared piece of logic, the steerer, produces every step for
every purpose from three inputs: the purpose's entry, the parameters she chose for this segment,
and his state right now. Adding a purpose is adding an entry (and lines for her to say), not
writing a script.

**Speech is separate, as in Phase 4.** Lines live in `Speech/Lines` and are picked by facts: the
segment's purpose, his closeness, what just happened. A purpose has no text in it.

**Why keep segments at all, if steps are dynamic?** A segment is her commitment to one intention
for a stretch. Without it she would reconsider everything at every step and feel random. It is
also the unit the plan shows, the unit she announces ("now I want you aching"), the unit patterns
are built from, and the unit transcripts and points are grouped by.

"Activity", used in 5A, is not a fifth concept for users or writers: it is only the code-level
contract that every kind of segment (steered, game, scripted) implements so the scheduler can
treat them alike.

## 2. Closeness: the thing she watches

A running estimate, 0 to 100%, of how close he is to the edge. It is never exact; it corrects
itself every time he reports an edge (that moment is 100% by definition).

- **Stimulation raises it.** The rate comes from the method's Closeness value (already on every
  method), the speed, and his build-up (2.3); what she learns about him refines it.
- **Rest lowers it**, along his recovery curve.
- **An edge sets it to 100%.**

### 2.1 Why the gap after an edge matters

"Edge, then edge again at once" is not two edges; it is a hold. With a three-second gap it is
nearly a hold. With a minute's gap he has mostly come down and it is a fresh edge. So the gap she
allows is a choice about where she keeps him, and it is her main lever:

| She wants | She gives him |
|---|---|
| To hold him at the edge | No gap, or a few seconds |
| To keep him high (say above 75%) | Short gaps, sized from his recovery curve |
| A real, separate edge | A longer gap |
| To cool him down | A long break |

### 2.2 Good from the first session: settings first, learning second

Anything she has to learn takes sessions, may be wrong, and makes the early sessions worse. A new
user who gets a badly tuned first week leaves. So:

- **The baseline comes from defaults and from settings he can set himself**, and must give a good
  session on day one. He can tell her directly: how long he can stay at the edge, roughly how long
  an edge takes him, how fast he comes down. A few plain questions or sliders, with sensible
  defaults, on the settings page.
- **Learning only refines.** What she observes is blended in gradually, weighted by how much
  evidence there is, and never moves far from his settings on thin data. If learning were switched
  off, sessions would still be good.
- **He can see and correct it.** The tuning lab shows what she currently believes and where it
  came from (his setting, or learned from N edges), and lets him reset or override.

What she can learn, from what is already recorded (edge time, method, speed, the step it belonged
to) plus the rest before each edge:

- **Time to edge from cold**, per method and speed band.
- **Recovery.** He edged on method X, rested 10 s, then took 25 s to edge again where from cold it
  takes 70 s: after 10 s he was still about 65% of the way there. Recovery is mostly a property of
  the person, with a little variation by method.
- **Tolerance.** How long he can stay near the edge without going over. This depends strongly on
  the method: far longer on a mild method than on an intense one. So tolerance is a baseline (his
  setting) scaled by method intensity, not one number.

### 2.3 Build-up

A session changes him as it goes. After many edges he reaches the edge faster on any method and
comes down more slowly. She tracks build-up across the session (edges so far, time spent close)
and it shortens his expected time to edge and lengthens his recovery. Without it, gaps that were
right in minute five would be wrong in minute fifty.

### 2.4 Two axes, not one

Closeness alone is not the experience. The same 90% is different on a dry stroke and on an intense
grip:

- **Closeness** (how near the edge) builds **need**: the ache, the wanting.
- **Method intensity** at that closeness decides **control**: on a mild method at 90% he is aching
  but in command of himself, and can stay there a long time; on an intense one he is hanging on.
- **Torture** is a different thing from intensity and is never merged with it. An intense method
  is strong pleasure that is hard to hold back from; a torturous one is stimulation he would not
  choose and wants to stop. Torturous methods often have low reach (Palm barely gets him closer),
  so he can be far from the edge and still desperate for it to end.

Each method already carries all three as separate values (Closeness, Intensity, Torture), and each
feeds a different part of his state:

| State | Built by | Eased by |
|---|---|---|
| Need | Time spent close, edges without release | Only an orgasm |
| Control | Worn down by time near the edge beyond his tolerance, which shrinks as the method's **intensity** rises, and by holds; low control means a slip is likely | Time lower down, milder methods |
| Overwhelm | **Torturous** stimulation: how much, how fast, how long | Breaks, non-torturous methods |
| Build-up | Edges and time spent close, all session | Slowly, with long breaks |

## 3. Purposes replace segment types

A segment is a stretch with one purpose: what she wants him to feel.

| Purpose | What she is doing to him | Methods that serve it |
|---|---|---|
| Build the ache | Edging to build need | Good reach, moderate intensity; dry is fine |
| Test control | Edging where stopping is hard | High reach, high intensity |
| Overwhelm | He does it only because she ordered it | High torture |
| Starve | Too little touch; desperate for more | High tease, low intensity, slow |
| Break: cool | Bring him down | Hands off |
| Break: simmer | Hands off but kept wanting (watching, listening to her) | Hands off, content |

### 3.1 Parameters are hers to choose

A purpose does not fix how a segment runs. When she picks a segment she also chooses its
parameters, and can change them as it goes:

- where she keeps his closeness (for example "no lower than 75%"), or no target at all;
- how long it lasts;
- how she handles gaps and breaks;
- whether edges carry holds, and how long;
- whether counts are announced or hidden;
- which methods, and how many.

Each purpose has typical values (Test control usually keeps him high with short gaps; Build the
ache usually lets him come down between edges), but they are starting points for her choice, not
rules. The same purpose can run very differently on two nights.

### 3.2 Choosing methods

Methods are chosen **first for what they will make him experience** and how that serves her
purpose: their reach, intensity, tease and torture, against the purpose and the parameters she
chose. Variety is a distant third consideration. She may use one method for a whole segment or
move between three or four, as long as every one of them makes sense for what she is doing. It
must never feel like a random draw.

## 4. Devices layer on top

Not segments. They attach to a segment or sit between two.

- **Holds.** Any edge can carry one. Length comes from his tolerance for that method: comfortable,
  a stretch, or cruel, as she chooses, but always within what he can plausibly do (see 5.1).
- **Hidden counts.** By default she says "edge", then "again", and he does not know how many. An
  announced count is the exception, used when the number itself is the point.
- **Mind games that change what he does**: the false finish, the countdown to an orgasm that does
  not come, announcing something dreaded in advance. (If it only changes what she says, it is
  speech, not a device.)
- **Games.** As now, an occasional device.

## 5. Scheduling: aims up front, steering on demand

Three levels: what she is pursuing (aims), what is happening now (segments), and the next action
(steering).

### 5.0 Aims: what she is pursuing across segments

A segment ending must not end her intention. If she wants a long stretch of intense edging, a
short break in the middle is part of that stretch, not the end of it. So she holds an **aim**:

- a purpose (Test control), and
- what she wants to have achieved before she moves on: a state ("his control worn thin", "need
  high"), a duration, or both.

Segments belong to the aim they serve:

- **Advancing** segments are the aim's own purpose: the edging itself, in one long segment or
  several with different parameters or methods.
- **Supporting** segments exist to let the aim continue or to sharpen it: a short break to cool
  him just enough to keep going; a simmer; a game whose stakes are in the aim's currency (5A.3).
  When a supporting segment ends she returns to the aim, and she remembers where it stood: how long
  it has run, what his state was, what she said she would do.

An aim ends when what she wanted is achieved, when its time is up, or when events change her mind
(he slips, a deal, her mood turns). Then she picks the next aim from his state.

A session is one to a few aims: a short session can be a single aim; a long one moves through
several, and two hours on one aim with no change would be dull. The emphasis of the night is
simply its main aim. Patterns (5A.2) are authored shapes an aim can take, or a bridge between two.

Speech can use the aim: "Back to it. We are not done." after a break; "That is what I wanted" when
it is achieved.

### 5.0A The plan and the steering

**The plan (up front).** Her aims for the night, each with the segments she expects and the
parameters she intends. Still visible in debug and at the top of transcripts, still revised by
events.

**Steering (moment to moment).** Inside a segment she does not follow a script of steps. She looks
at his estimated closeness and what she intends, and picks the next action: stroke at this speed,
edge, hold, rest this long. The break length is what she adjusts most. This is what makes the
session respond to him without rewriting the plan.

**Choosing the next aim, and the next segment within one,** uses his state, not flat weights:

| His state | She tends to |
|---|---|
| Low need, early | Build the ache |
| High need, control intact | Test control |
| High overwhelm | Break, or starve |
| Low control | Ease off, lower his closeness, or switch to a milder method |
| Long stretch at one level | Change the level, not just the method |

Her mood, persona, the session's settings, promises and the night's emphasis bend these. The plan
shows her reason for each choice ("overwhelm high: simmer, then back to it").

### 5.1 Slips are his responsibility, within reason

Not going over is his job. Her side of it: she does not order things that make a slip likely. In
particular she sizes holds, and long stretches near the edge on intense methods, within his
tolerance. She never pushes past it on purpose to cause a slip.

## 5A. Room for authored content and for expansion (proposed 2026-10-03, under discussion)

Steering by purpose must not be the only way a session can run. Three things would be lost or
blocked if it were: authored chains, games, and activities that are not stroking. One contract
keeps all of them.

### 5A.1 One contract for every kind of segment

Whatever fills a segment implements one contract (in code, an "activity"; see 1A). The scheduler
never knows what kind it is; it only uses what each declares:

- when it is allowed (authorities, limits, equipment, persona, session settings);
- its parameters and their ranges;
- what it is expected to do to his state (need, control, overwhelm, build-up, closeness) and roughly
  how long it takes;
- how it produces its steps.

Kinds of segment, all equal to the scheduler:

| Kind | How its steps come about | Examples |
|---|---|---|
| Steered | The steerer, from a purpose and her parameters (section 5). Nothing scripted. | Build the ache, Test control, Overwhelm, Starve, Break |
| Game | Fixed rules and steps, sized from his profile | Race to the edge, edge rush, chicken |
| Written | Authored steps, fully or in outline, sized from his profile; declares the purpose it serves | An edge ladder, a countdown, a set piece she uses rarely |

A **pattern** is not a kind of segment: it is an authored arrangement of segments (5A.2), which the
scheduler can choose as one unit.

### 5A.2 Patterns: authored chains

A pattern is data: a short sequence (or loop) of activities with parameter overrides, plus what
triggers it and how often it may appear. For example:

- **Back and forth.** Rounds of Overwhelm, then Test control on an intense method, repeated two to
  four times. Triggered when he begs to stop during overwhelm, or chosen outright in a punishment
  session. At most once a session; rare across sessions.
- **Mercy with a catch.** He asks for a change; she grants it, and the replacement is worse.
- **Dread.** She names what is coming, starves him while he waits, then delivers it.

Patterns keep what authored modules are good at (deliberate, memorable shapes) while each part is
still steered to him. They are short, parameterised, and tracked in the usage table that already
keeps stories rare, so they do not become the repetitive modules of older programs. Phase 4's
"granted rest, swap, then back to the torture" rule is the first of these; it becomes a pattern
instead of a special case.

### 5A.3 Games

Games stay fixed sets of rules and steps. Two changes make them fit:

- **Sized from him.** Windows and targets come from his profile, not constants: a race to the edge
  is "a little under his usual time on this method", an endurance hold is sized from his
  tolerance.
- **Chosen for a reason.** Each game declares what it does to his state (an edge rush builds need
  and tests control; chicken on a torturous grip is overwhelm), so she picks a game that serves
  what she wants, not a random one.
- **Stakes in the aim's currency.** A game played inside an aim (section 5.0) takes its rewards and
  punishments from that aim. During a stretch of intense edging: win and he gets a real break or
  longer gaps for a while; lose and it is more edges, shorter gaps or a hold. The game moves the
  aim forward either way instead of being an interruption.

### 5A.4 Beyond stroking

Nothing above the step level may assume stroking. Three rules keep that true:

- **A step is an action, not a method.** Today every step is a stroking method at a speed for a
  time or a count. The step carries an action of some kind: a stroking method, a toy, a posture
  (kneel), a task away from the session (homework, then report back), a wait. New kinds are added
  without touching the scheduler.
- **State is a set of named channels.** Closeness is one channel, fed by penile stimulation.
  Other activities declare which channels they move: a prostate section may build need with
  little closeness; kneeling adds a discomfort of its own kind. New channels can be added.
- **Standing conditions.** Some things are not a stretch of the session but hold across it: "stay
  kneeling until I say", "eyes on the screen". These are conditions that sit over whatever
  activity is running, alongside devices.

None of the non-stroking activities are built in Phase 5. The check is on paper: before the
catalogue is replaced, an anal or prostate section, a kneeling condition and a mid-session
homework task are each written out as activities against this contract, to confirm they fit
without changing the scheduler. The core closeness model stays first-class and is not watered
down to be generic.

### 5A.5 What would box us in (to avoid)

- The scheduler listing purposes by name, instead of reading what activities declare.
- Closeness as the only state.
- Steps that can only be "method, speed, time or count".
- The steering controller owning every step, with no way for a pattern, game or script to take over.
- A points formula that assumes stroking.

## 6. Points

Points stay the session clock, but what earns them is redesigned so his state counts: time spent
close, time on intense methods while close, and torturous stimulation each earn more than the same
seconds far from the edge on a mild grip, and each is counted separately. Beyond a total, a session
then has a profile (how much need it built, how much it tested his control, how much overwhelm),
which lets homework, punishments and deals be shaped as well as sized: a punishment can be "high
overwhelm" rather than merely "many points".

## 7. What changes around it

- **Speech.** New facts for lines: his closeness band, how long he has been kept high, whether the
  last gap was short, and how this edge compares with his usual ("You got back to the edge fast",
  "That was quicker than usual"). Hidden counts use the "again" lines that exist.
- **Settings.** A few baseline questions about him (2.2). What replaces the per-segment preference
  list is undecided (open question 1).
- **Simulated users** get a physiology (time to edge, recovery, tolerance by intensity, build-up)
  so the simulation reacts to gaps the way a person would, and slips happen for a reason.
- **Transcripts** gain a closeness column in the "What happened" table and a summary: time in each
  band, time near the limit of his tolerance, need and overwhelm built.
- **Tuning lab** gains the view of what she believes about him.
- **Free play and modes where he sets the pace**: she still estimates closeness from his edges and
  uses it for her verdict and her words; she just does not steer.

## 8. Order of work (mini-phases, each shippable)

1. **Estimator, no behaviour change.** Closeness estimate from defaults and his settings, with
   build-up, recorded per step; closeness column and band summary in transcripts; simulated users
   get a physiology.
2. **Baseline settings and learning.** The questions about him; record the rest before each edge;
   refine time-to-edge, recovery and tolerance from evidence; the view in the lab.
3. **Purposes.** Replace the catalogue with purposes, parameters and method choice by fit.
4. **Steering.** The in-segment controller: gaps, holds within tolerance, hidden counts.
5. **State-driven scheduling.** Aims; need, control, overwhelm, build-up; next purpose from state; reasons
   in the plan.
6. **Points.** Closeness, intensity and torture in what earns points; session profiles for homework,
   punishments and deals.
7. **Content.** Lines for purposes, closeness and devices; retune from transcripts and real use.

## 9. Open questions

1. **Preferences.** The settings page lists today's 17 segment types with "how often" and
   "length". What should he be able to set instead: the purposes, the devices, both, something
   else? Undecided.
2. **Baseline questions.** Exactly which things he tells her about himself, and in what form
   (seconds, or plain choices like "I can hold an edge: briefly / a while / a long time").

## 10. Decision log

- 2026-10-03 (first draft): Agreed in principle: segments are defined by what she wants him to
  feel, not by mechanics; holds, hidden counts and mind games are devices, not segments; variety
  is not a goal in itself; the scheduler must use his state. Closeness to the edge is tracked as an
  estimate and learned from edges and the gaps between them; the gap after an edge is her main
  lever; method intensity is a second axis alongside closeness. A design doc is kept as this is
  built.
- 2026-10-03 (vocabulary): Four terms: purpose (a small data entry, no steps), segment (run-time
  only: her intention and parameters for a stretch), device, pattern. One shared steerer makes
  every step; speech stays separate. "Activity" is only the code contract behind segment kinds.
- 2026-10-03 (aims): Her intention outlives a segment. She holds an aim (a purpose plus what she
  wants achieved) across several segments; breaks and games inside it support it and she returns
  to it afterwards; a game inside an aim takes its stakes from that aim. The night's emphasis is
  its main aim. Intensity and torture are different things and are never merged: intensity wears
  control, torture builds overwhelm; "strain" is dropped as a state.
- 2026-10-03: A purpose is a definition; a segment is one occurrence of it with parameters. Written
  segments are first-class but optional: used where a specific shape is the point, each declaring
  the purpose it serves.
- 2026-10-03 (second revision): The method's Closeness value drives how fast stimulation raises
  the estimate. Sessions must be good from day one: the baseline comes from defaults and settings
  he can set (including tolerance at the edge); learning only refines and must never be required.
  Tolerance depends on method intensity. Control and slip risk are one thing. Build-up within a
  session is tracked: faster to edge, slower to recover. A purpose does not fix closeness targets:
  she picks a segment and chooses its parameters. Methods are chosen for what they make him
  experience; three or four in a segment is fine if all fit the purpose; never random-feeling.
  Speech remarks on how fast he got back to the edge. Points stay, redesigned so closeness and
  strain count, giving sessions a profile usable for homework and punishments. Recovery is mostly
  per user with a little method variation. Slips are his responsibility; she sizes holds within
  his tolerance and does not order things that overly risk one. A night can be a mix or a single
  purpose, depending partly on length.
