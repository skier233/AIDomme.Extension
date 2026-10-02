# Segments: what exists, and what could

A segment is a stretch of a session with one goal. The plan engine picks them from
`extensions/AIDome/Sessions/Plan/segments.json` using only the properties each declares; adding,
changing or removing one is editing that file (and giving it lines in `Speech/Lines`).

"Used" is how many of the 50 planned simulated sessions (65 minus free play) included it, from the
transcript run of 2026-10-02.

## Current segments

| Segment | What she has him do | Goal | Intensity | Gated by | Used |
|---|---|---|---|---|---|
| Slow build | Teasing grips, speed 1–3, 40–90 s steps, no edging | Warm him up slowly | 2 | she sets pace | 35 |
| Steady stroking | One good grip, speed 3–6, 45–120 s | Keep him hard and building | 4 | she sets pace | 49 |
| Edging | 1–3 edges per step on close grips, sometimes a hold | Edge, back off, again | 5 | she sets edges | 16 |
| Edge ladder | One edge per step, each step faster (speed 3 to 9), same grip | Climb | 5 | edges and pace | 19 |
| Rapid-fire edges | 1–2 edges per step at speed 7–10, swapping intense grips | No time to recover | 7 | edges and pace | 23 |
| Long hold | Reach the edge, hold 25–60 s | Stay at the edge too long | 6 | she sets edges | 20 |
| Tempo crawl | Speed 1, one stroke per beat, 1–2.5 min | Maddeningly slow | 3 | she sets pace | 30 |
| As many as you can | 1–2.5 min at speed 6–8, every edge counts | A number for her | 6 | she sets pace | 40 |
| Don't you dare | Hard grip, speed 7–9, 30–75 s, an edge fails it (and earns a punishment) | Self-control test | 7 | she sets pace | 17 |
| Torment | Torturous grips, speed 6–9, 30–90 s, rotating | Desperate to stop, takes every second | 8 | she picks grips, harshness 3+ | 25 |
| The grip he hates | His most torturous allowed grip, kept for the segment | Dread of one grip | 9 | she picks grips, harshness 5+ | 9 |
| Edge the hard way | One edge on an intense grip that barely reaches (closeness 1–4) | Earn an edge the hard way | 9 | she picks grips, harshness 4+; rare | 2 |
| Burst | Full speed 10–25 s out of nowhere | Surprise | 7 | she sets pace | 29 |
| Hands off | Hands off 30–70 s while she talks | A rest that keeps the ache | 1 | talkativeness 3+ | 18 |
| Breather | Hands off 15–40 s | A real rest | 0 | she sets pace | 1 |
| False finish | "That's it, we're done" or "you're about to cum", hands off 10–20 s, then more (must be followed by edging or torture) | Subvert the expectation | 4 | mind games 4+ | 11 |
| A game | One of her games with stakes | Gamble, compete, fear of the dice | 5 | games on | 39 |
| Punishment | Hated grip, speed 7–10, 45–90 s (only inserted after a failure or a slip) | Make him pay | 9 | not planned | 11 |
| The ending | Her verdict | How it ends | – | always last | – |

### Recommendations

| Segment | Recommendation | Why |
|---|---|---|
| Breather | Remove | Rests already come between steps (cooldowns after edges, between-step rests); it almost never gets planned. |
| Tempo crawl | Merge into Slow build | Both are slow teasing strokes; the only difference is speed 1 versus 1–3. One "Slow tease" with a crawl variant. |
| The grip he hates | Merge into Torment | Torment with "stays on one grip" as a variant; the dread line stays. |
| Steady stroking | Keep, rarer | The most used (49 of 50): it is filler without a goal. Lower its weight so goal-driven segments get the time. |
| Edging and Edge ladder | Keep both | Different shapes (repeated vs climbing). |

## Possible new segments

From your list of dominance types and psychological elements, mapped onto what the engine can do.
"Effort": **data** = a catalogue entry and lines only; **small** = a small engine addition (a step
option or one new step kind); **medium** = a new input or question; **later** = needs screen
understanding, free text or new limits.

| Segment | What she has him do | Psychological element | Effort |
|---|---|---|---|
| Hover at the edge | Edge, hold 10 s, ease off, edge, hold 15 s, … each hold a little longer | Staying near the edge, desperation | small (hold after every edge) |
| Until I say stop | Edge again and again with no count shown; she stops him at a number only she knows | Fear of the unknown | small (hidden target) |
| The countdown | "Cum on zero. Ten… nine…" at full speed, then "Stop. Hands off." | Subverting expectation, building need | small (countdown step; never ends in an orgasm) |
| Barely enough | Reach the edge on a teasing grip that hardly does anything (one finger, tip only) at a slow speed | Limited stimulation, desperation | data (reach "hard" with a teasing grip) |
| Ride it out | A heavy grip at a fixed high speed for a set time; edges don't stop it, he holds on through them | Intensity hard to bear | small (edges allowed, no pause) |
| Whiplash | Alternate 20 s full speed and 20 s crawl, several times | Cruelty, unpredictability | small (alternating speeds) |
| Dread | She names what is coming ("in two minutes, the grip you hate"), then teases slowly until it comes | Fear of the known fate | data (followUp torture plus announcement lines) |
| Freeze | At the edge: hands stay on, no movement, feel it throb for 20–40 s | Desperation, control | data (a hold variant with its own lines) |
| Look, don't touch | Hands off; she narrates what is on screen and what he is missing | Lust, wanting what you can't have | data (screen-aware lines on a hands-off step) |
| Would you refuse it? | She asks if he would turn down an orgasm for her; if he says no, she denies anyway | Manipulation, control | medium (a yes/no question) |
| Simon says | Stroke until she says a word, then stop instantly; a slip costs him | Attention challenge | medium (a game) |
| Wheel of fate | Spin for his next task; reroll at worse odds each time | Fear of random fate, gambler's fallacy | exists as the reroll game; could be a segment of its own |
| Mercy with a catch | When he begs for a change, she grants it, and the change is worse | Cruelty in granting a request | small (generalise the rest-beg rule) |
| Match the screen | Stroke in time with the action in the video | Content teasing | later (needs the video's pace) |
| Screen triggers | When the video shows a tagged moment, he edges or stops | Tasks tied to what he sees | later (time-ranged tags exist; needs a trigger loop) |
| Hands-free | Reach the edge without hands (grinding, thighs) | Limited stimulation, embarrassment | later (needs hands-free methods in the library) |
| Confession, CEI, bondage tasks | Humiliation and control tasks within his limits | Embarrassment, loss of control | later (limits exist; needs task definitions and possibly free text) |
