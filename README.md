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
  Persistence/                      guided-session storage, recovery, commitments
  Services/                         session engine, games, punishments, speech
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
