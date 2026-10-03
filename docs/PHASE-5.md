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

## 13. Open questions

1. **Preferences.** What replaces the 17 per-segment settings: purposes, written content, both?
2. **Tolerance setting.** Its form: plain choices ("I can stay at the edge: briefly / a while / a
   long time") with an optional exact number, or a number only.

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
- Consolidation: devices, patterns, build-up and "activity" were folded into the three terms and
  four states (section 11).
