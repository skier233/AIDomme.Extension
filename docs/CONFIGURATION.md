# Configuring AI Domme

There are two layers. **Your settings** are per account and live in Cove's database; you change
them in the UI and they apply from your next session. **The extension's defaults** are what every
account starts with; they live in this repository, and changing them means editing a file, running
the tests, building and staging (see the README).

Everything below is relative to `extensions/AIDome/`.

## Your settings (the UI)

| What | Where in Cove | Stored as |
|---|---|---|
| Her persona, how sessions end, session lengths, how long you can stay at the edge | Settings, Extensions, AI Domme | `aidomme_domme_settings` (one JSON row per account) |
| What she believes about your body (pace, recovery, tolerance): see, fix or reset | Tuning lab, Your body tab | overrides in the same settings row; what she has learned in its `Learned` column |
| Methods, punishment endings, deals: edit, add, remove | Tuning lab, Edit tab (from the settings tab) | personal rows (`UserId` set) next to the shared defaults |
| Calibrating their points by comparison | Tuning lab, Compare tab | `aidomme_tuning_comparisons`; Apply writes personal overrides |
| Narration voice | Settings, Extensions, AI Domme | extension settings |
| One session's sliders and limits | The AI Domme page, before starting | the session's contract |

A personal row overrides the shared default with the same id (`BaseId`), or is a definition of your
own (`BaseId` empty). "Restore shared default" deletes the override.

## The extension's defaults (the repository)

### Data files (embedded; edit, rebuild, restage)

| What | File | Notes |
|---|---|---|
| Written segments and aims, the names of the shapes she improvises, how each mood leans on the purposes, what a punishment session must include (see [SEGMENTS.md](SEGMENTS.md)) | `Sessions/Plan/segments.json` | Header comment lists every property. |
| Everything she says | `Speech/Lines/*.json` | `commands` (orders), `reactions`, `session` (openers, segment starts, endings, hints), `chatter` (asides, stories), `games`, `promises`. The `aidomme-lines` Claude skill in `.claude/skills` explains the format. |

`TheShippedLinesAndSegmentsAreValid` checks both on every test run.

### Seeded libraries (in the database: change with a new migration)

Methods, punishment endings and deals are shared rows (`UserId IS NULL`) created by migrations, so
existing installs only change through a new migration that updates those rows (see `010_offer_values`
for the pattern: update only where the row still has its original value, so nobody's tuning is lost).

| What | Seeded by | Table |
|---|---|---|
| Methods (40) | `Persistence/Sql/005_guided_v2.sql` | `aidomme_methods` |
| Punishment endings (14) | `005_guided_v2.sql`, `007_ledger.sql` (EndsInOrgasm) | `aidomme_enders` |
| Deals (10) | `007_ledger.sql`, repriced in `010_offer_values.sql` | `aidomme_offer_definitions` |

Migrations must not contain `{` or `}` (Cove runs them as raw SQL with format placeholders), and are
registered in `AiDomeExtension.cs` (`DefineMigrations`).

### Rules in code

| What | Where |
|---|---|
| Points: per edge, per second, per hold; what his closeness adds; default pace (5.5 a minute) and how the budget follows his pace within a session; the budget from Length | `Sessions/Points.cs` |
| His body: how stimulation, rest and holds move closeness, need, control and overwhelm; what an edge teaches her; tolerance defaults | `Sessions/Body.cs` |
| Her odds of letting him cum (denial slider, edges, mood, cadence, preferences) | `Sessions/Authorities.cs`, `DommePermission` |
| Ending lean, hints, ruin chance | `Sessions/Plan/Ending.cs` |
| Which aim and which segment she chooses, and the parameters she gives a segment | `Sessions/Plan/PlanEngine.cs` (`Weigh`, `ChooseSegment`, `Improvise`) |
| Which methods fit a purpose | `MethodFit` in `Sessions/Plan/PlanEngine.cs` |
| Steps inside a segment (gaps, holds, hidden counts), written segments' templates | `Sessions/Planner.cs` (`Steer`, `Build`) |
| Mood: start, drift, what moves it | `Mood` in `Sessions/Planner.cs` |
| Deals: how a request is valued, which deal she picks, raising the price after a no | `Sessions/Offers.cs` |
| Games: which ones, their stakes | `Sessions/Games.cs` (`Choose`, `Start`) |
| Promises: her counters and stakes | `Sessions/Ledger.cs` (`Respond`) |
| Session modes (who decides what) | `SessionContract.Preset` in `Sessions/Contract.cs`; names in `ModePresets`, `AiDomeExtension.cs` |
| Defaults for a new account's settings (persona 5/3/5/5, lengths 10/35/90 minutes, endings, tolerance "a while") | `Sessions/Knowledge.cs` (`PersonaSettings`, `SessionLengths`, `EndingPreferences`, `ProfileSettings`) |
| When she talks between orders, stories, bridges | `Speech/Voice.cs` |
| The exact wording of instructions (method, pace, time) | `Speech/Grammar.cs`, `Speech/GameGrammar.cs` |

## Checking a change

```powershell
dotnet test tests/AIDome.Tests
$env:AIDOMME_TRANSCRIPTS="$env:TEMP\aidomme-transcripts"; dotnet test tests/AIDome.Tests --filter "FullyQualifiedName~TranscriptTests"
```

Add `$env:AIDOMME_SETTINGS_FROM="Host=127.0.0.1;Port=5430;Username=postgres;Database=cove"` to run the
transcripts with your own settings and library (read-only). Then build and stage (README).
