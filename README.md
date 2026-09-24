# AI Domme — Cove extension

A rule-driven domme for guided edging sessions on your Cove library, with an app-wide edge tracker,
orgasm permission, games and persistent commitments. No language model is involved: she plans each
session from a catalogue of segments, and a rule-based narrator speaks for her from line modules
that can be narrated through an optional local TTS server.

> Adult content. For personal, authorized use only.

## Where things are decided

- [docs/NORTHSTAR.md](docs/NORTHSTAR.md) is the product vision and the source of truth.
- [docs/NOW.md](docs/NOW.md) is the ordered work list and the open decisions.
- [docs/CURRENT-STATE-ASSESSMENT.md](docs/CURRENT-STATE-ASSESSMENT.md) is the 2026-09-22 review of
  the code inherited from the previous iteration.
- [docs/PHASE-4.md](docs/PHASE-4.md) is the design of her plan and her voice, and what landed.

## Layout

```
Directory.Build.props / .targets   net10.0; Cove host contracts wired compile-only from ../cove
scripts/build-extension-ui.mjs      esbuild + Tailwind bundle, CSS scoped under .aidomme-* roots
scripts/stage-local-extensions.ps1  builds and copies into %LOCALAPPDATA%\cove\extensions
extensions/AIDome/
  extension.json                    manifest (id cove.community.aidome)
  AiDomeExtension.cs                entry point: services, migrations, UI manifest, HTTP API
  Tracking/                         app-wide edge/orgasm tracker (Cove tables, likes, undo)
  Sessions/                         session contract, authorities, engine, planner, games, points,
                                    offers, the ledger and promise rules, what she knows, simulation
  Sessions/Plan/                    segment catalogue (segments.json), plan engine, ending lean
  Speech/                           utterances, grammar, narrator, voice scheduler
  Speech/Lines/                     her line modules (JSON, embedded)
  Persistence/                      guided-session storage, recovery, ledger, recall, on-screen media
  Services/                         TTS and method point calibration
  ui/                               React page, companion window, settings panel
  dist/                             built bundles (checked in)
tests/AIDome.Tests                  xUnit; PostgreSQL integration tests use temporary databases
tests/ui                            Vitest component tests
.claude/skills/aidomme-lines        Claude skill for drafting her lines and segment types
```

## Build, stage and run

Requires a sibling Cove checkout at `../cove`: the extension compiles against `Cove.Data`, which
Cove does not publish as a package.

```powershell
npm install
npm run build:ui
dotnet build AIDome.Extension.slnx -c Debug
./scripts/stage-local-extensions.ps1   # then restart Cove; "AI Domme" appears in the nav
```

## Session modes

A session starts from a contract that says who decides each concern. The four presets:

| Mode | Content | Methods | Pacing | Edges and holds | Permission and ending |
|---|---|---|---|---|---|
| Full guided | domme | domme | domme | domme | domme |
| Own content | you | domme | domme | domme | domme |
| Own content and methods | you | you | domme | domme | domme |
| Free play | you | you | you | you | domme |

Points, permission odds, the price of her deals and her decision to end use the same rules in
every mode, so she behaves the same whoever holds the controls. Sessions are paused rather than
ended; only debug sessions can be ended by hand.

## Promises, deals and the ledger

Before a session you can promise her something: no orgasm, and/or a long, intense, torturous, mean
or teasing session, for the next session or for some days. She accepts or asks for more, and names
the stakes. During a session she may offer a deal when you beg, ask for a rest or lose a game:
edges now, edges owed, denial days, a punishment session or ending, or a ruined orgasm. Her deals
come from a library you can tune in Cove's settings (Settings, Extensions, AI Domme).

What you owe is kept in a permanent ledger and folded into your next sessions. Breaches are found
from what the tracker recorded, even when AI Domme was closed. An orgasm that breaks a promise or a
session, or a session that stopped with nobody there, becomes a question she asks before anything
else.

## Her plan and her voice

Each session she plans a series of segments (edging, a slow build, torment, a game, a false
finish...) and revises the plan as you behave: begging, failing a challenge or cumming without
permission all change what comes next. She leans toward an ending early and may hint at it, or lie
about it. Free play has no segments, only her verdict.

Everything you set once is in Cove's settings, under its own AI Domme tab (the AI Domme page links
to it): her persona (harshness, humiliation, mind games, talkativeness, what she calls you, how much
she varies from session to session), how your sessions end (how much denial, whether and when she
ruins, roughly how many days between orgasms), how many minutes a length 1, 5 and 10 session means
to you, how often she uses each kind of segment and how long it runs, her methods, punishment
endings and deals, and the narration voice. Each session's own settings (Denial, limits such as
"no ruined orgasm tonight") apply on top. Sessions still run on points (what you do), so the minutes are a target: she converts them
at your pace, which she learns from the sessions you finish. Session settings decide how strict a session is;
the persona decides how she talks.

She has two streams of speech: orders, on time, and chatter in the quiet between them (remarks,
teasing, stories, comments on what is on screen). Stock lines don't repeat within a session; stories
are tracked across sessions so they stay rare. A debug session shows her plan and how it changed.

## Upgrading an existing install

Version 0.12.0 moves the Tuning page into Cove's settings (an AI Domme tab) and adds ending
preferences. No migration.

Version 0.11.0 adds migration `009_pace`: one column recording minutes of play per session, so she
can learn your pace. It only adds a column.

Version 0.10.0 adds migration `008_voice`: her persona and plan settings, the plan kept with each
session, and a record of which of her lines have been used. It only adds tables and a column. It was
run against a copy of the real install's tables.

Version 0.9.0 adds migration `007_ledger`: the punishment ledger, structured promises, the offer
library and her follow-up questions. Earlier punishments become ledger entries. Earlier free-form
offers and commitments are kept under `aidomme_legacy_*` names if there are any.

Version 0.8.0 adds migration `006_session_contracts` (session mode and contract columns; the policy
table becomes `aidomme_session_contracts`). A session still running or paused under 0.7.0 cannot be
continued afterwards: its chat log is kept and the page says it could not be resumed. Finish it
before upgrading.

Version 0.7.0 adds migration `005_guided_v2`, which moves AI Domme's guided data onto plain
per-user tables in Cove's database and drops the previous tables in the same transaction. It was
verified against a copy of a real install, but it is a one-way move, so take a copy of the AI Domme
tables first:

```powershell
& "$env:LOCALAPPDATA\cove_personal_live\pgsql\bin\pg_dump.exe" -h 127.0.0.1 -p 5430 -U postgres -d cove -t 'aidomme_*' -f aidomme-backup.sql
```

Then stage, and restart Cove so the migration runs. Staging also removes assemblies that earlier
versions shipped but Cove provides itself.

## Verify

```powershell
dotnet test tests/AIDome.Tests/AIDome.Tests.csproj
../cove/ui/node_modules/.bin/tsc.cmd --project tsconfig.json
$env:NODE_OPTIONS="--no-experimental-webstorage"; ../cove/ui/node_modules/.bin/vitest.cmd run --config tests/ui/vitest.config.mjs
```

PostgreSQL integration tests are skipped unless `AIDOMME_TEST_POSTGRES` names a maintenance
connection. Cove's managed PostgreSQL listens on 127.0.0.1:5430 with local trust authentication:

```powershell
$env:AIDOMME_TEST_POSTGRES="Host=127.0.0.1;Port=5430;Username=postgres;Database=postgres"
```

Each test creates a uniquely named `aidomme_*_test_*` database, applies the real migrations, and
drops it afterwards. Tests never read or modify the `cove` database.

Simulated sessions (every mode, with users who obey, beg, fail, slip or only edge) can be written
out as readable transcripts, with her plan and each revision:

```powershell
$env:AIDOMME_TRANSCRIPTS="$env:TEMP\aidomme-transcripts"; dotnet test tests/AIDome.Tests --filter "FullyQualifiedName~TranscriptTests"
```
