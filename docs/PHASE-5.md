# Phase 5: what she wants him to feel

Status: **design, consolidated 2026-10-03. Nothing here is built.** Replaces the segment catalogue
and the plan engine's selection logic from Phase 4. The narrator, voice scheduler, ledger, deals
and tuning lab stay.

## 1. Why

Phase 4's segments are defined by mechanics (a ladder, a burst, a crawl). The scheduler can only
weigh tags, so mid-session every segment is about equally likely and sessions feel like a sampler.
Nothing models what a real domme watches: how close he is, and for how long.

The change: she plans by **what she wants him to feel**, tracks **how close to the edge he is**, and
steers mainly with **how long she lets him recover**.

## 2. Concepts

Three terms. Everything else in this doc is a property of one of them.

| Term | What it is | Lives |
|---|---|---|
| **Purpose** | What she wants him to feel. Five exist (section 4). | A small data entry each. No steps, no sentences. |
| **Aim** | What she is pursuing over a longer stretch: a purpose plus what she wants achieved ("a long stretch of intense edging"). Spans several segments. | Run time only, in her plan. |
| **Segment** | What is happening now: one purpose with the parameters she chose, for a few minutes. | Run time only, in her plan. |

A purpose is a definition; aims and segments are occurrences of it. "Test control" is a purpose.
"Tonight's main aim: test his control for about fifteen minutes" is an aim. "Minutes 12 to 17, kept
above 80% on Skin-Held Shaft Stroke, counts hidden" is a segment.

Everything that happens inside an aim is a segment, whether improvised or written. Most segments
are **improvised**: nothing is written for them, and the steerer makes their steps. A new segment
begins whenever she changes what she is doing: a different purpose, a different set of methods, a
different way of handling gaps, a break. A segment (or a whole aim) can instead be **written** where
a specific shape is the point (section 7). Speech is separate, as in Phase 4:
lines are picked by facts (the purpose, his state, what just happened).

## 3. His state

Four numbers she tracks through a session. Each has one job.

| State | What it means | Raised by | Lowered by |
|---|---|---|---|
| **Closeness** | How near the edge he is right now, 0 to 100%. An estimate, corrected to 100% at every edge he reports. | Stimulation, at a rate from the method's Closeness value and the speed | Rest, along his recovery curve |
| **Need** | The ache built up over the session. Also makes him quicker to edge and slower to come down as it rises. | Time spent close; edges without release | Only an orgasm |
| **Control** | How much margin he has before a slip. | Time lower down, milder methods | Time near the edge beyond his tolerance; holds. Tolerance shrinks as the method's **Intensity** rises |
| **Overwhelm** | How much he wants it to stop. | **Torturous** stimulation: how much, how fast, how long | Breaks, non-torturous methods |

Intensity and torture are different things and are never merged. An intense method is strong
pleasure that is hard to hold back from; it wears control. A torturous one is stimulation he would
not choose; it builds overwhelm, and often happens far from the edge. Methods already carry
Closeness, Intensity and Torture as separate values.

### 3.1 The gap after an edge is her main lever

"Edge, then edge again at once" is a hold, not two edges. Three seconds later it is nearly a hold.
A minute later it is a fresh edge. So the gap she allows decides where she keeps him:

| She wants | She gives him |
|---|---|
| To hold him at the edge | No gap, or a few seconds |
| To keep him high (say above 75%) | Short gaps, sized from his recovery |
| A real, separate edge | A longer gap |
| To cool him down | A long break |

### 3.2 His profile: settings first, learning second

The estimate needs three things about him:

1. **Pace:** how quickly he reaches an edge (one personal factor on what each method's Closeness
   value predicts).
2. **Recovery:** how fast he comes down (one half-life; mostly a property of the person).
3. **Tolerance:** how long he can stay near the edge (a baseline, scaled down by method intensity).

Sessions must be good on day one. How each value is found differs:

- **Pace and recovery are learned, quickly.** Every edge is evidence: how long it took, and how
  long the rest before it was. A handful of edges in the first session is enough to move off the
  defaults, and she measures these better than he could describe them. He is not asked to set
  them.
- **Tolerance is his setting.** She can only observe "too much" when he slips, which may take
  hours, and a slip does not say why. So he tells her (one question, with a sensible default).
  Learning adjusts it slowly and mostly upward from what he demonstrably survives: long holds and
  long stretches near the edge without a slip are clear evidence; a single slip is weak evidence
  and moves it little.

The tuning lab shows each value and where it came from, and lets him reset or override any of
them.

Per-method learning (he is unusually quick on one grip) can come later if the data shows it is
needed. It is not in this phase.

## 4. Purposes

| Purpose | What she is doing to him | Methods that serve it |
|---|---|---|
| Build the ache | Edging to build need | Good reach, moderate intensity; dry is fine |
| Test control | Edging where stopping is hard | High reach, high intensity |
| Overwhelm | He does it only because she ordered it | High torture |
| Starve | Too little touch, or none; desperate for more | High tease, low intensity, slow; or hands off while he watches and listens |
| Cool | Bring him down: a real break | Hands off |

A purpose entry holds: which method properties fit, typical parameter values, what it does to his
state, and who must hold the controls. For example:

```
Starve
  methods fit:  tease high, intensity low, reach low
  typical:      speed 1 to 3; closeness around 30 to 60%; no edges
  does to him:  need up slowly, overwhelm down, control recovers
  needs:        she sets the pace
```

Starve covers both minimal touch and none at all (what earlier drafts called "simmer"): the purpose
is the same, to make him desperate for stimulation, and how much touch he gets is a parameter. Cool
is different in kind: its purpose is to bring him down.

**Parameters are hers to choose** for each segment, starting from the purpose's typical values:
where she keeps his closeness (or no target), how long, the gaps, whether edges carry holds and how
long, whether counts are announced or hidden, which methods and how many. Hidden counts are the
default: she says "edge", then "again". The same purpose can run very differently on two nights.

**Methods are chosen for what they make him experience**: reach, intensity, tease and torture
against the purpose and her parameters. Variety is a distant third. One method or three or four is
fine as long as each makes sense for what she is doing. It must never feel like a random draw.

## 5. Aims and segments

**An aim** is a purpose plus what she wants achieved before moving on: a state, a duration, or
both. Her intention survives the end of a segment: a break in the middle of a long stretch of
intense edging is part of that stretch.

An aim gives the stretch a focus; it does not forbid everything else. Segments inside an aim are:

- **Advancing:** the aim's own purpose. Most of the aim's time.
- **Supporting:** there so the aim can continue or bite harder. A short cool-down, a hands-off
  starve, a game whose stakes come from the aim.
- **Side:** another purpose where it makes sense, such as a burst of torment inside an intense
  edging aim. More likely when she is cruel or it is a punishment session; rare otherwise.

After a supporting or side segment she returns to the aim and remembers where it stood. An aim
ends when what she wanted is achieved, when its time is up, or when events change her mind (a
slip, a deal, her mood). A session is one to a few aims: a short one can be a single aim; two
hours on one aim would be dull.

## 6. Scheduling and steering

**The plan (up front).** Her aims for the night, each with the segments she expects. Visible in
debug and transcripts, revised by events.

**Choosing** the next aim, and the next segment inside one, uses his state:

| His state | She tends to |
|---|---|
| Low need, early | Build the ache |
| High need, control intact | Test control |
| High overwhelm | Cool, or starve |
| Low control | Ease off: lower his closeness, lengthen gaps, or a milder method |
| Long stretch at one level | Change the level, not just the method |

Her mood, persona, the session's settings, promises and punishment status bend these. Every choice
records a one-line reason.

**Steering (moment to moment).** Improvised segments have no script. One shared piece of logic,
the steerer, picks each next action (stroke at this speed, edge, hold, rest this long) from the
purpose, her parameters and his state. Gap length is what it adjusts most.

**Slips are his responsibility.** Her side: she sizes holds, and long stretches near the edge on
intense methods, within his tolerance. She never pushes past it to cause one.

## 7. Written content

Where a specific shape is the point, an aim or a segment is written instead of improvised. Written
content still declares the purpose it serves and what it does to his state, so she chooses it for a
reason; its amounts are sized from his profile, not constants; and it is tracked in the usage table
that keeps stories rare, so it does not become repetitive. Writing is always optional.

| Written | Examples |
|---|---|
| Segments | Games (fixed rules); an edge ladder; a countdown to an orgasm that does not come; a false finish |
| Aims | Back and forth: rounds of overwhelm, then intense edging. Dread: she names what is coming, starves him while he waits, then delivers it. Mercy with a catch: he begs for a change and gets a worse one |

### 7.1 When she chooses something written

Improvising is the default. At each point where she decides what comes next, written segments and
aims are candidates beside "improvise the next stretch", and one is picked only when all of these
hold:

- **It is allowed:** his limits and settings, who holds the controls, her persona (a false finish
  needs her to play mind games; games must be switched on).
- **It serves what she is doing:** its declared purpose and effect fit the current aim, as an
  advancing, supporting or side segment.
- **It is fresh:** not used too recently, by the usage table (within the session and across
  sessions).
- **Its turn comes up:** each has a base rarity, bent by her persona and mood (playful: more
  games) and by his preference for it.

Some are also offered by events rather than by her schedule: he begs to stop during overwhelm
(mercy with a catch, or the back and forth), he fails a challenge, a deal.

Written content is the seasoning: a bounded share of a session, so most of the time is improvised
and steered to him.

**Games** change in two ways. Their windows and targets come from his profile ("a little under his
usual time on this method"). And a game inside an aim takes its stakes from that aim: during
intense edging, a win earns a real break or longer gaps; a loss means more edges, shorter gaps or
a hold. The game moves the aim forward either way.

## 8. Points

Points stay the session clock. What earns them changes so his state counts: time spent close, time
on intense methods while close, and torturous stimulation each earn more than the same seconds far
from the edge on a mild grip, counted separately. A session then has a profile (need built, control
tested, overwhelm), so homework, punishments and deals can be shaped as well as sized.

## 9. Debugging: what transcripts and the debug view must show

A requirement of this phase, built alongside each step, not after. If a concept is not visible
here it cannot be tuned.

- **His profile** as she used it: pace, recovery, tolerance, and whether each came from his
  settings, defaults or learning.
- **Aims:** purpose, what she wanted, why she chose it, when and why it ended.
- **Segments:** purpose, improvised or written, role in the aim (advancing, supporting, side), the
  parameters she chose, and her reason.
- **Every step:** the action, the gap before it, and closeness, need, control and overwhelm at its
  start and end.
- **Estimator check:** for each edge, when she expected it against when it came. In simulation the
  simulated user's true closeness is known, so her estimate is shown beside the truth.
- **Summary:** time in each closeness band, time per purpose, peaks of each state, time near his
  tolerance, points with their breakdown.
- Every decision carries its one-line reason, and a session replays exactly from its seed.

## 10. Keeping the door open

Not built in this phase. Three constraints so that later additions (anal or prostate sections,
postures such as kneeling, homework mid-session) fit without touching the scheduler:

- The scheduler only uses what a segment declares (when allowed, parameters, effect on state, how
  it makes steps), never what kind it is.
- A step is an action (a stroking method, a toy, a posture, a task, a wait), not always "method,
  speed, time".
- State is a set of named values. Closeness belongs to stroking; other activities move the others,
  and new ones can be added.

Before the old catalogue is removed, an anal section, a kneeling condition and a homework task are
each sketched on paper against these to confirm they fit.

## 11. What this revision simplified

Reviewing the design as a whole, five things were more complicated than they needed to be:

| Was | Now | Why |
|---|---|---|
| Five states, including "build-up" beside "need" | Four: need does both jobs | Both were built by the same things; one number is easier to reason about and debug |
| "Devices" as a separate concept | Gone. Holds and hidden counts are segment parameters; false finish and countdown are written segments | They were already described both ways |
| "Patterns" as a separate concept | Gone. They are written aims | A pattern was an authored multi-segment shape, which is what an aim is |
| Learning time-to-edge per method and speed | Three personal numbers | Far less to learn, wrong less often, good from settings alone |
| "Activity" as a named concept | A constraint in section 10 | Only code needs it |

What remains is: three terms, four states, three profile numbers, one steerer, one chooser.

## 12. Order of work (each step shippable, each with its transcript output)

1. **Estimator, no behaviour change.** The four states from defaults; simulated users get a
   physiology; transcripts show states per step and the estimator check.
2. **Profile.** Pace and recovery learned from edges; his tolerance setting; the view in the lab.
3. **Purposes and method choice.** Purposes replace the catalogue; methods chosen by fit.
4. **Steering.** Gaps, holds within tolerance, hidden counts.
5. **Aims and choosing from state.** Aims, segment roles, reasons.
6. **Written content.** Games sized from the profile with stakes from the aim; ladder, countdown,
   false finish; the first written aims.
7. **Points.** State in what earns points; session profiles.
8. **Lines.** For purposes, aims and his state ("That was quicker than usual"); retune from
   transcripts and real use.

## 13. Settings

Few, and high level. A setting exists only if he can answer it without knowing how she works.

- **Removed:** the per-segment "how often" and "length" list. Nothing replaces it; what she does
  comes from his state, the session's sliders and her persona.
- **Added:** one question for tolerance, as a plain choice ("I can stay at the edge: briefly / a
  while / a long time"), with an exact number available in the tuning lab for those who want it.
- **Her persona stays a handful of high-level dials** (harshness, humiliation, mind games,
  talkativeness, variety). Anything finer, such as whether she is honest about the ending tonight,
  how often she uses a false finish, or how she sizes a hold, is derived from those dials, her
  mood and his state. It is never a setting. It is visible in the debug view and transcripts, so
  it can be understood and tuned in the design, not by the user.

No open questions remain.

## 14. Decision log

All 2026-10-03.

- Segments are defined by what she wants him to feel, not by mechanics. Variety is not a goal in
  itself. The scheduler uses his state.
- Closeness is an estimate corrected at each edge; the method's Closeness value drives its rise;
  the gap after an edge is her main lever.
- Sessions must be good from day one: the baseline is defaults and his own settings; learning only
  refines. Tolerance depends on method intensity.
- Intensity and torture are never merged: intensity wears control, torture builds overwhelm.
- Need also makes him quicker to edge and slower to recover as a session goes on.
- A purpose does not fix parameters; she chooses them per segment. Methods are chosen for what
  they make him experience; several are fine if all fit.
- Her intention outlives a segment: she holds an aim. An aim focuses a stretch without excluding
  other things that make sense (torment inside an intense edging aim, by mood or punishment).
- Written aims and segments are optional and first-class. Games are sized from his profile and
  take their stakes from the aim.
- Points stay, redesigned so state counts. Slips are his responsibility; she sizes holds within
  his tolerance. A night can be one aim or several, depending partly on length.
- Speech remarks on his state and on how an edge compares with his usual.
- Transcripts and the debug view must show every concept in this doc.
- Pace and recovery are learned quickly from edges and are not user settings; tolerance is his
  setting, adjusted slowly and mainly from what he survives.
- Simmer is folded into Starve (how much touch is a parameter); five purposes.
- Improvised stretches are segments too. Improvising is the default; written content is chosen
  only when allowed, fitting the aim, fresh and due, or when an event calls for it.
- Per-segment preferences are removed with nothing in their place. Tolerance is asked as a plain
  choice. User-facing settings stay few and high level: fine-grained behaviour (honesty about the
  ending, how often a device is used) is derived, shown in debug, and never a knob.
- Consolidation: devices, patterns, build-up and "activity" were folded into the three terms and
  four states (section 11).

## 15. What landed (2026-10-03, version 0.16.0)

All eight steps are built. Reference for what exists now: [SEGMENTS.md](SEGMENTS.md).

**His state and profile** (`Sessions/Body.cs`). Four states, 0 to 1, advanced second by second
from what he is doing (stroking a method at a speed, holding, resting). One addition to the design:
while he strokes, some of his closeness leaks away, so a weak method at a crawl levels off below
the edge and a strong one gets there; this is what makes "starve" and "edge the hard way" behave.
Each reported edge sets closeness to 100%, settles what the climb cost his control (a slow climb
hovers under the edge and costs far more than a quick one), and teaches her his pace and recovery
together. In simulation she is within about 10% on pace and recovery after six to eight edges. Her
control estimate carries a 20% margin, because being wrong in the other direction is a slip.
Tolerance is his setting; it moves up 4% only when he gets through what she reckoned was nothing
left, and down 7% on a slip she did not see coming, within 0.6 to 2 times what he set.

**Scheduling** (`Sessions/Plan/PlanEngine.cs`, `Sessions/Planner.cs`). Aims at the start (about
one per eight minutes), each reconsidered against his real state as it begins; segments chosen one
at a time; parameters per segment; one steerer. Every aim records why it was chosen and why it
ended; every segment records its purpose, role, parameters, reason and why it ended.

**Written content.** Edge ladder, countdown, as many as you can, don't you dare, edge the hard way,
burst, false finish, games; written aims back and forth, dread, mercy with a catch. Games take
their stakes from the aim (a loss during a control aim is more edges; during overwhelm, more of
that) and their times from how fast he reaches the edge.

**Points.** A second of stimulation is worth more the closer he is, and more again on an intense
method while close (about 1.0 for a middling method at a typical closeness, so a session's pace is
what it was). Plain stimulation, torturous stimulation, what closeness added, edges and holds are
counted separately, so a session has a profile: need built, control tested, overwhelm reached. The
same seconds earn the same in every mode.

**Length.** The points budget follows his pace tonight: it starts from the pace she assumed (the
default of 5.5 points a minute, or what his recorded sessions show) and is pulled toward the points
a minute he is actually earning, half and half after eight minutes and up to four fifths. A Length
therefore means roughly its minutes whoever he is (simulated sessions at Length 5 with his settings
run 34 to 36 minutes against 40), while a session full of edges still ends a little sooner than one
full of slow teasing. Bonuses and penalties from games are not pace and still move the ending.

**Lines.** She sometimes says what she is after when a new aim begins (about three times in ten;
mostly she just does it), sometimes says when she comes back to one after a break or a game, eases
off in words when his control is thin, and remarks when he reaches the edge much faster or slower
than she expected. A hidden count is "edge", then "Again." New facts for line conditions:
`purpose`, `aim`, `role`, `closeness`, `need`, `control`, `overwhelm` (0 to 100); new tags:
`#aim-start`, `#back`, `#purpose:<id>`, `#role:<role>`, `#gap`, `#again`, `#pace` with `#quick`,
`#quick-back` or `#slow`.

**Settings.** The per-segment preferences are gone. Cove's settings ask one thing about his body
(how long he can stay at the edge); the tuning lab's "Your body" tab shows pace, recovery and
tolerance with where each came from, lets him fix a value, and forgets what she learned. What she
learns is stored apart from his settings (migration `012_profile`).

**Debugging.** Transcripts show, per step: the gap before, her estimate of closeness and control
beside the simulated truth, need and overwhelm; then time by closeness band (estimate and truth),
peaks, the points breakdown, the session profile, his profile with sources beside the truth, the
estimator check (expected against actual seconds for each edge), every aim and segment with
parameters and reasons, time by purpose, how the plan changed, and the seed. The debug view of a
live session shows his state, his profile, the aims and the segments with her reasons.

**Simulated users** have a true pace, recovery and tolerance, edge when their body gets there and
slip when their control runs out. With the default tolerance setting: users whose true tolerance is
at or above it almost never slip; the "fails and slips" user (true tolerance 18 seconds against her
45) slips in most sessions, which is what setting "briefly" is for.

### Checking the door is still open (section 10)

Sketched against the code as built, before the old catalogue was removed:

- **An anal or prostate section** is a purpose-carrying segment whose steps are actions that are
  not "method, speed, time". The chooser only reads what a segment declares (purpose, needs,
  persona, fits, role); the steerer is the only place that assumes stroking, so such a segment is
  written (its own step kinds) and declares which states it moves. `Body.Advance` would need a
  mode beside stroke, hold and rest that builds need without closeness. No scheduler change.
- **A kneeling condition** is not a segment but a standing condition over several. It fits as a
  written aim's parameter or a consequence with a duration; it adds a named state (discomfort)
  beside the four. The state is a class of named values, so adding one touches `BodyState`, the
  transcript columns and whatever chooses by it, not the existing four.
- **Homework mid-session** is a written segment whose single step is a task with a completion
  input. The plan already runs written segments by steps; the engine already has "done" steps.

Nothing in the scheduler asks what kind a segment is.

### Known limits

- Her estimate between edges cannot be checked in real use; only the edges correct it. In timed
  stroking she assumes he keeps himself just under the edge.
- When an edge takes far longer than she expected she does not yet react mid-step (speed up, or
  ask); she only notices afterwards.
- A session he buys his way out of with a deal (he asks to cum, accepts her price and pays it) ends
  when he has paid, however early that is. That is the deal working, not the clock.
- The session profile (need built, control tested, overwhelm) is recorded and shown but nothing
  yet shapes homework, punishments or deals by it.

### After first feedback (2026-10-03)

- Transcripts were hard to read at a glance, and much of what they showed (her estimates against
  a simulated user, edge timing, slips) says nothing about real sessions. They now show what a
  session was (`Sessions/Report.cs`): the settings it started from, what it came to, the methods
  used with time and edges on each, the flow (aims, the segments inside each, every step) and the
  chat. A run also writes `_SUMMARY.md`, one row per session. Tables are fixed-width text, so they
  read the same in a plain editor. `AIDOMME_TRANSCRIPT_DETAIL=1` adds the model detail (states per
  step, estimates beside the simulated truth, the estimator check, flags) for debugging `Body.cs`.
- She no longer announces every aim: stating it each time made her predictable. An aim's opening
  line is now occasional, and so is "back to it" after a break.
- Sessions ran at about half the minutes the Length setting means, because the budget assumed a
  pace measured under the old scheduling. The budget now follows his pace within the session (see
  Length above) and the default pace is 5.5 points a minute. Transcripts show the minutes a Length
  means beside how long the session ran.

### After the first real session (2026-10-05, version 0.16.1)

From his notes and the log of a 40-minute punishment session (his own grip, her pace and edges):

- **After a hold, always a rest.** An edge during timed stroking used to be followed by a hold or
  a three-second pause and then straight back to stroking, which is only more hold. Now an edge in
  a timed step is followed by hands off sized to bring him down (8 to 45 seconds, longer after a
  hold), and no stroking of any kind follows a hold without one.
- **Edges shorten the time left.** Each edge he reaches in a timed step halves what was left to
  serve; with under 15 seconds left the step is over.
- **Speeds.** The beat for each speed is faster (speed 5 is 120 a minute, 7 is 170, 10 is 250; it
  was 90, 120, 180), and it is scaled by the method: an intense grip needs fewer strokes a minute to
  be "fast" than a mild one.
- **Begging.** An orgasm she has honestly told him he is not getting is not for sale. One she had
  decided against costs 1.8 times as much, one in a punishment 1.6 times, and she never sells one
  for much less than it is worth: a deal that cannot cost enough is not offered, and when none can
  she says no. Begging lengthens a session at most twice.
- **The ache uses milder methods.** Intense methods belong to testing his control; building the
  ache now prefers the milder ones that still get him there.
- **Stay away from the edge.** A new shape of the ache, used sparingly: timed stroking with an
  order not to reach the edge; reaching it costs him.
- **She does not wait for ever.** In the log single edges took two to four minutes at "medium
  pace" with nothing from her. Past about twice what she expected she now tells him to go faster
  and raises the beat, up to three times.
- **She does not learn his pace on his own grip.** When the grip is his choice she cannot know
  which he is using, and he can change it as he goes, so those edges teach her nothing about his
  pace or recovery (briefly tried, and taken out again). There she works from what she already
  believes and relies on telling him to go faster.
- **One climb cannot empty his control.** A slow climb was reckoned as minutes spent hovering under
  the edge, which put her estimate of his control at zero and had her easing off again and again.
  The cost of a single climb is now capped.
