# AI Domme — Cove extension

A rule-driven domme for guided edging sessions on your Cove library, with an app-wide edge tracker,
orgasm permission, games and persistent commitments. No language model is involved; speech comes
from scripted rules and text pools and can be narrated through an optional local TTS server.

> Adult content. For personal, authorized use only.

## Where things are decided

- [docs/NORTHSTAR.md](docs/NORTHSTAR.md) is the product vision and the source of truth.
- [docs/NOW.md](docs/NOW.md) is the ordered work list and the open decisions.
- [docs/CURRENT-STATE-ASSESSMENT.md](docs/CURRENT-STATE-ASSESSMENT.md) is the 2026-09-22 review of
  the code inherited from the previous iteration.

## Layout

```
Directory.Build.props / .targets   net10.0; Cove host contracts wired compile-only from ../cove
scripts/build-extension-ui.mjs      esbuild + Tailwind bundle, CSS scoped under .aidomme-* roots
scripts/stage-local-extensions.ps1  builds and copies into %LOCALAPPDATA%\cove\extensions
extensions/AIDome/
  extension.json                    manifest (id cove.community.aidome)
  AiDomeExtension.cs                entry point: services, migrations, UI manifest, HTTP API
  Tracking/                         app-wide edge/orgasm tracker (Cove tables, likes, undo)
  Sessions/                         session contract, authorities, engine, planner, games, points
  Persistence/                      guided-session storage, recovery, commitments
  Services/                         speech, punishments, homework
  ui/                               React page, companion window, settings panel
  dist/                             built bundles (checked in)
tests/AIDome.Tests                  xUnit; PostgreSQL integration tests use temporary databases
tests/ui                            Vitest component tests
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

Points, permission odds and her decision to end use the same rules in every mode, so she behaves
the same whoever holds the controls. Sessions are paused rather than ended; only debug sessions
can be ended by hand.

## Upgrading an existing install

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
