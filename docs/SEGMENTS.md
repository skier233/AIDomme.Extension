# Purposes, aims and segments: what she schedules

Phase 5 replaced the catalogue of mechanical segment types. The design is in
[PHASE-5.md](PHASE-5.md); this page is the reference for what exists now and where to change it.

## The three terms

| Term | What it is | Where it lives |
|---|---|---|
| **Purpose** | What she wants him to feel. Five exist. | `Purpose` in `Sessions/Plan/Segments.cs` |
| **Aim** | A purpose plus what she wants achieved, pursued across several segments. Set when the session starts, revised to his state as each begins. | `Aim`, made by `PlanEngine.Make` and `Reconsider` |
| **Segment** | A few minutes with one purpose and the parameters she chose for it. Chosen one at a time, from his state. | `PlannedSegment`, chosen by `PlanEngine.ChooseSegment` |

## Purposes

| Purpose | He feels | Steps the steerer makes | Methods that fit |
|---|---|---|---|
| Build the ache | Wanting it more and more | Steady stroking kept just under the edge, or unhurried single edges with a long gap after each | Bring him close, pleasant: high closeness, low torture |
| Test control | At the edge and struggling to stay there | One edge at a time, a gap she sizes, holds within his tolerance | Reach the edge and are hard to hold back from: high closeness and intensity |
| Overwhelm | Desperate for it to stop | Timed stimulation on torturous methods at speed | Torture 5 or more |
| Starve | Desperate for more | The lightest touch at a crawl, or hands off while she talks | Teasing, low closeness, low intensity |
| Cool | A real break | Hands off, long enough to bring him down | None |

Cool is never an aim; it only supports one.

## How she chooses

**Aims** (`PlanEngine.Weigh`): his state leads. Low need favours the ache; high need with control
intact favours testing it; overwhelm already high makes more of it less likely and starving him
more likely; her mood, persona, the session's sliders, a punishment and owed traits lean on that.
A session has about one aim per eight minutes. Each records why she chose it and why it ended
(she got what she wanted, it ran its share of the points, its time was up, or an event).

**Segments inside an aim** (`PlanEngine.ChooseSegment`), in this order:

1. What his state asks for: control under 25% → ease off (cool, or hands-off starve); overwhelm
   at 90% → a short break; five minutes over 80% → change the level.
2. Something written, when it is allowed, fits the aim, is fresh and its turn comes up (about a
   third of the time after two improvised segments).
3. A side of something else (torment in the middle of edging, more likely when she is cruel,
   annoyed or punishing; a few edges in the middle of overwhelm).
4. Otherwise more of the aim, with fresh parameters.

Each segment has a **role** in its aim: advancing (the aim's own purpose), supporting (a break, a
game whose stakes come from the aim) or side.

**Parameters** (`PlanEngine.Improvise`) are chosen per segment, not fixed per purpose: how long;
how far he comes down between edges (the closeness she lets him back in at; 100% means no gap);
how often an edge is held and how much of what he has left a hold spends; whether the count is
hidden ("edge", then "again", the default) or announced; the speed range; and one to four methods
that all fit the purpose (`MethodFit`).

**Steps** (`SegmentPlanner.Steer`) come from those parameters and his state as she estimates it.
The gap after an edge is her main lever: none keeps him at the edge, a short one keeps him high, a
long one makes the next edge a fresh climb. She never asks for an edge, or any stroking straight
after one, from someone whose control is nearly gone; a slip is still his to answer for.

## Names for the shapes she improvises

Improvised segments are not templates. The catalogue only names the shapes that come out, so the
debug view and her lines have something to call them (`"written": false` in
`Sessions/Plan/segments.json`).

| Name | Purpose | When her parameters make it |
|---|---|---|
| Slow build | Ache | Steady stroking at the start of a session |
| Steady stroking | Ache | Steady stroking later on |
| Slow edges | Ache | Single edges with long gaps |
| Edging | Control | Edges with gaps |
| Rapid-fire edges | Control | He is let back in at 85% or more, or not let down at all |
| Holding at the edge | Control | Every edge is held |
| Torment | Overwhelm | Several torturous methods |
| The grip he hates | Overwhelm | One torturous method |
| Punishment | Overwhelm | Overwhelm an event asked for (a failed challenge, a slip) |
| Barely touching | Starve | Minimal touch |
| Hands off | Starve | No touch, she talks |
| Breather | Cool | A real break |

## Written segments and aims

Written where a specific shape is the point (`"written": true`; steps from `step`):

| Segment | Shape | May appear in |
|---|---|---|
| Edge ladder | One edge at a time, each faster | Ache, Control |
| Countdown | A number of edges announced, counted down | Ache, Control |
| As many as you can | Edges against the clock | Control |
| Don't you dare | Stroke hard without edging; an edge fails it | Ache, Control |
| Edge the hard way | An edge on an intense grip that barely gets him there | Overwhelm, Control |
| Burst | Full speed, very short | Ache, Starve |
| False finish | She lets him think it is over (needs mind games 4) | Any |
| A game | Stakes in the aim's currency, times from his profile | Any |

Written aims (`"aims"` in the same file): **Back and forth** (overwhelm, then fast edges, then
overwhelm again), **Dread** (hands off while she tells him what is coming, then it comes), and
**Mercy with a catch** (he begs for a rest during overwhelm: a break, then edges instead, then it
carries on; only this event brings it on).

## Changing things

- A new written segment or aim: an entry in `segments.json` and a `SegmentStart` line module
  (`"when": ["#segment:<id>"]`). No code unless it needs a new step kind.
- A new purpose, or a change to what a purpose's steps look like: `Purpose`, `PlanEngine.Weigh`
  and `Improvise`, `MethodFit.Score`, `SegmentPlanner.Steer`.
- How his body is modelled (closeness, need, control, overwhelm, what an edge teaches her):
  `Sessions/Body.cs`.
- Check with the transcripts: every session lists its aims and segments with parameters, reasons
  and his state per step. `SimulatedSessions_KeepHerInvariants_AndCoverTheCatalogue` and
  `BodyTests` hold the invariants.

## Ideas not built

From the earlier list of dominance types, still open: hover at the edge with lengthening holds,
"ride it out" (edges do not stop a timed step), whiplash (alternating full speed and crawl), freeze
at the edge, "would you refuse it?", Simon says, and everything that needs the screen's pace or new
method kinds (match the screen, screen triggers, hands-free, tasks). Most are now a written segment
entry plus lines; the ones that need a new input or screen understanding are larger.
