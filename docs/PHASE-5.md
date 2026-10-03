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
- **Torturous methods** often have low reach (Palm barely gets him closer), so overwhelm is its own
  axis: he can be far from the edge and still desperate for it to stop.

From these she tracks, over the session:

| State | Built by | Eased by |
|---|---|---|
| Need | Time spent close, edges without release | Only an orgasm |
| Strain | Intense or torturous stimulation | Breaks, mild methods |
| Control | Worn down by time near the edge beyond his tolerance for that method, and by holds; low control means a slip is likely | Time lower down |
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

## 5. Scheduling: intent up front, steering on demand

Two levels.

**The plan (up front).** A short list of segments by purpose, each with the parameters she intends.
A night can be a mix or a single purpose; that depends partly on length (a short session can be
about one thing; two hours of one thing with no change would be dull). Still visible in debug and
at the top of transcripts, still revised by events.

**Steering (moment to moment).** Inside a segment she does not follow a script of steps. She looks
at his estimated closeness and what she intends, and picks the next action: stroke at this speed,
edge, hold, rest this long. The break length is what she adjusts most. This is what makes the
session respond to him without rewriting the plan.

**Choosing the next purpose** uses his state, not flat weights:

| His state | She tends to |
|---|---|
| Low need, early | Build the ache |
| High need, control intact | Test control |
| High strain | Break, or starve |
| Low control | Ease off, lower his closeness, or switch to a milder method |
| Long stretch at one level | Change the level, not just the method |

Her mood, persona, the session's settings, promises and the night's emphasis bend these. The plan
shows her reason for each choice ("strain high after overwhelm: simmer").

### 5.1 Slips are his responsibility, within reason

Not going over is his job. Her side of it: she does not order things that make a slip likely. In
particular she sizes holds, and long stretches near the edge on intense methods, within his
tolerance. She never pushes past it on purpose to cause a slip.

## 5A. Room for authored content and for expansion (proposed 2026-10-03, under discussion)

Steering by purpose must not be the only way a session can run. Three things would be lost or
blocked if it were: authored chains, games, and activities that are not stroking. One contract
keeps all of them.

### 5A.1 Activities: the one thing the scheduler knows

Everything she can schedule is an **activity**. The scheduler never knows what kind it is; it only
uses what every activity declares:

- when it is allowed (authorities, limits, equipment, persona, session settings);
- its parameters and their ranges;
- what it is expected to do to his state (need, strain, control, build-up, closeness) and roughly
  how long it takes;
- how it produces its steps.

Kinds of activity, all equal to the scheduler:

| Kind | How its steps come about | Examples |
|---|---|---|
| Purpose segment | Steered moment to moment (section 5) | Build the ache, Test control, Overwhelm, Starve, Break |
| Pattern | An authored chain of other activities, with conditions | Alternate overwhelm and intense edging; mercy with a catch; dread, then the dreaded thing |
| Game | Fixed rules and steps, sized from his profile | Race to the edge, edge rush, chicken |
| Scripted module | Authored steps, written out | A set piece she uses rarely |

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
  and tests control; chicken is strain), so she picks a game that serves what she wants, not a
  random one.

### 5A.4 Beyond stroking

Nothing above the step level may assume stroking. Three rules keep that true:

- **A step is an action, not a method.** Today every step is a stroking method at a speed for a
  time or a count. The step carries an action of some kind: a stroking method, a toy, a posture
  (kneel), a task away from the session (homework, then report back), a wait. New kinds are added
  without touching the scheduler.
- **State is a set of named channels.** Closeness is one channel, fed by penile stimulation.
  Other activities declare which channels they move: a prostate section may build need and strain
  with little closeness; kneeling adds strain of its own kind. New channels can be added.
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

Points stay the session clock, but what earns them is redesigned so closeness and strain count:
time spent close, and intense or torturous stimulation, are worth more than the same seconds far
from the edge on a mild grip. Beyond a total, a session then has a profile (how much need it built,
how much strain), which lets homework, punishments and deals be shaped as well as sized: a
punishment can be "high strain" rather than merely "many points".

## 7. What changes around it

- **Speech.** New facts for lines: his closeness band, how long he has been kept high, whether the
  last gap was short, and how this edge compares with his usual ("You got back to the edge fast",
  "That was quicker than usual"). Hidden counts use the "again" lines that exist.
- **Settings.** A few baseline questions about him (2.2). What replaces the per-segment preference
  list is undecided (open question 1).
- **Simulated users** get a physiology (time to edge, recovery, tolerance by intensity, build-up)
  so the simulation reacts to gaps the way a person would, and slips happen for a reason.
- **Transcripts** gain a closeness column in the "What happened" table and a summary: time in each
  band, time near the limit of his tolerance, need and strain built.
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
5. **State-driven scheduling.** Need, strain, control, build-up; next purpose from state; reasons
   in the plan.
6. **Points.** Closeness and strain in what earns points; session profiles for homework,
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
