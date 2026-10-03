# Phase 5: what she wants him to feel

Status: **draft for discussion, 2026-10-03. Nothing here is built.** The decision log at the end
records what is settled. This replaces the segment catalogue and the plan engine's selection logic
from Phase 4; the narrator, the voice scheduler, the ledger, deals and the tuning lab stay.

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

- **Stimulation raises it**, at a rate set by the method, the speed, and how long that method
  takes him to edge from cold.
- **Rest lowers it**, along his own recovery curve.
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

### 2.2 Learning his physiology

Everything needed is already recorded (edge time, method, speed, the step it belonged to), or
needs one small addition (the rest before it). From his history she learns:

- **Time to edge from cold**, per method and speed band.
- **Recovery curve.** He edged on method X, rested 10 s, then took 25 s to edge again, where from
  cold it takes 70 s: so after 10 s he was still about 65% of the way there. Many such points give
  his curve (a half-life, to start with).
- **Tolerance.** How long he has stayed above about 90% without going over, and what was happening
  when he did go over (slips, failed holds). Some users can sit at 90% for minutes; she learns
  which kind he is.

New accounts start from defaults and the estimate tightens as edges are recorded. The profile is
visible in the tuning lab ("what she has learned about you") and can be reset.

### 2.3 Two axes, not one

Closeness alone is not the experience. The same 90% is different on a dry stroke and on an intense
grip:

- **Closeness** (how near the edge) builds **need**: the ache, the wanting.
- **Method intensity** at that closeness decides **control**: on a mild method at 90% he is aching
  but in command of himself; on an intense one he is hanging on.
- **Torturous methods** often have low reach (Palm barely gets him closer), so overwhelm is its own
  axis: he can be far from the edge and still desperate for it to stop.

From these she tracks, over the session:

| State | Built by | Eased by |
|---|---|---|
| Need | Time spent close, edges without release | Only an orgasm |
| Strain | Intense or torturous stimulation | Breaks, mild methods |
| Control left | Worn by holds and long stretches above his tolerance | Time lower down |
| Slip risk | High closeness on intense methods beyond his tolerance | Backing off |

These also give a truer measure of how intense a session was than points alone (see open questions).

## 3. Purposes replace segment types

A segment is a stretch with one purpose. Each purpose declares where she wants his closeness, which
methods serve it, and how she handles breaks.

| Purpose | What she is doing to him | Closeness she aims for | Methods | Breaks |
|---|---|---|---|---|
| Build the ache | Edging to build need | 60 to 100%, coming down between edges | Good reach, moderate intensity; dry is fine | Medium: real edges |
| Test control | Edging where stopping is hard | 80 to 100%, kept high | High reach, high intensity | Short: little recovery |
| Overwhelm | He does it only because she ordered it | Wherever it lands | High torture | Few, short |
| Starve | Too little touch; desperate for more | 30 to 60%, never enough to edge | High tease, low intensity, slow | None needed |
| Break: cool | Bring him down | Falling to a floor she picks | Hands off | The break is the point |
| Break: simmer | Hands off but kept wanting (watching, listening to her) | Falling slowly, held above a floor | Hands off, content | The break is the point |

A segment stays on one or two methods. Variety is not a goal in itself; her words are the variety.

Each segment gets its targets when planned, for example "Test control: keep him above 80% for about
five minutes on Skin-Held Shaft Stroke".

## 4. Devices layer on top

Not segments. They attach to a segment or sit between two.

- **Holds.** Any edge can carry one. Length comes from his tolerance: comfortable, a stretch, or
  cruel, as she chooses. It starts modest and grows as he proves he can take it.
- **Hidden counts.** By default she says "edge", then "again", and he does not know how many. An
  announced count is the exception, used when the number itself is the point.
- **Mind games that change what he does**: the false finish, the countdown to an orgasm that does
  not come, announcing something dreaded in advance. (If it only changes what she says, it is
  speech, not a device.)
- **Games.** As now, an occasional device.

## 5. Scheduling: intent up front, steering on demand

Two levels.

**The plan (up front).** A short list of segments by purpose, with targets, and one purpose
emphasised for the night ("tonight is about control"). Still visible in debug and at the top of
transcripts, still revised by events.

**Steering (moment to moment).** Inside a segment she does not follow a script of steps. She looks
at his estimated closeness and the segment's target and picks the next action: stroke at this
speed, edge, hold, rest this long. The break length is what she adjusts most. This is what makes
the session respond to him without rewriting the plan.

**Choosing the next purpose** uses his state, not flat weights:

| His state | She tends to |
|---|---|
| Low need, early | Build the ache |
| High need, control intact | Test control |
| High strain | Break, or starve |
| Control nearly gone | Ease off if she wants him denied; push if she is cruel or a slip serves her |
| Long stretch at one level | Change the level, not just the method |

Her mood, persona, the session's settings, promises and the night's emphasis bend these. The plan
shows her reason for each choice ("strain high after overwhelm: simmer").

## 6. What changes around it

- **Speech.** New facts for lines: his closeness band, how long he has been kept high, whether the
  last gap was short. Hidden counts need "again" lines, which exist.
- **Simulated users** get a physiology (time to edge, recovery, tolerance) so the simulation
  reacts to gaps the way a person would, and slips happen for a reason.
- **Transcripts** gain a closeness column in the "What happened" table and a summary: time in each
  band, time above tolerance, longest stretch above 80%.
- **Tuning lab** gains the profile view.
- **Free play and modes where he sets the pace**: she still estimates closeness from his edges and
  uses it for her verdict and her words; she just does not steer.

## 7. Order of work (mini-phases, each shippable)

1. **Estimator, no behaviour change.** Closeness estimate from defaults, recorded per step;
   closeness column and band summary in transcripts; simulated users get a physiology.
2. **Learning.** Record the rest before each edge; fit time-to-edge and recovery per user; profile
   view in the lab.
3. **Purposes.** Replace the catalogue with purposes and method profiles; map old preferences.
4. **Steering.** The in-segment controller: gaps, holds from tolerance, hidden counts.
5. **State-driven scheduling.** Need, strain, control; next purpose from state; night emphasis;
   reasons in the plan.
6. **Content.** Lines for purposes, closeness and devices; retune from transcripts and real use.

## 8. Open questions

1. **Points.** Keep points as the session clock for now, or move to a measure built from closeness
   and intensity over time? (Proposal: keep points through steps 1 to 5, compare both in
   transcripts, decide after.)
2. **Recovery model.** Start with one half-life per user, or per method from the start? (Proposal:
   one per user, split later if the data shows it.)
3. **Slips.** May she ever push past his tolerance on purpose, risking a slip? (Proposal: only
   when she is cruel and the session settings allow, and never when he has asked not to be ruined.)
4. **Night emphasis.** A mix with one purpose emphasised (proposed), or sometimes a single-purpose
   night?
5. **Segment preferences.** The settings page lists today's 17 segment types. Replace it with the
   six purposes and the devices?

## 9. Decision log

- 2026-10-03: Agreed in principle: segments are defined by what she wants him to feel, not by
  mechanics; holds, hidden counts and mind games are devices, not segments; variety is not a goal
  in itself; the scheduler must use his state. Closeness to the edge is tracked as an estimate and
  learned per user from edges and the gaps between them; the gap after an edge is her main lever;
  method intensity is a second axis alongside closeness. A design doc is kept as this is built.
