param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$DestinationRoot = (Join-Path $env:LOCALAPPDATA "cove\extensions")
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

$extensions = @(
    "AIDomme"
)

Write-Host "Building extension UI bundles..."
Push-Location $repoRoot
try {
    npm run build:ui
    if ($LASTEXITCODE -ne 0) {
        throw "npm run build:ui failed with exit code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}

Write-Host "Building AIDomme.Extension ($Configuration)..."
dotnet build (Join-Path $repoRoot "extensions\AIDomme\AIDomme.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
foreach ($extension in $extensions) {
    $outputDir = Join-Path $repoRoot "extensions\$extension\bin\$Configuration\net10.0"
    $manifestPath = Join-Path $outputDir "extension.json"

    if (-not (Test-Path $manifestPath)) {
        throw "Missing manifest at $manifestPath"
    }

    $manifest = Get-Content -Raw -Path $manifestPath | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.id)) {
        throw "Manifest at $manifestPath does not define an id"
    }

    $targetDir = Join-Path $DestinationRoot $manifest.id
    # Update generated files in place. Do not delete an existing extension directory:
    # local development data/configuration may live alongside its binaries.
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    Copy-Item -Path (Join-Path $outputDir "*") -Destination $targetDir -Recurse -Force

    # Earlier builds shipped copies of host assemblies and the SQLite importer's dependencies.
    # Cove provides the host assemblies itself; remove stale copies so only the host's load.
    $obsolete = @(
        "Cove.Core.dll", "Cove.Data.dll", "Cove.Plugins.dll", "Cove.Sdk.dll",
        "Cove.Core.pdb", "Cove.Data.pdb", "Cove.Plugins.pdb", "Cove.Sdk.pdb",
        "Microsoft.EntityFrameworkCore.dll", "Microsoft.EntityFrameworkCore.Abstractions.dll",
        "Microsoft.EntityFrameworkCore.Relational.dll", "Microsoft.EntityFrameworkCore.Sqlite.dll",
        "Microsoft.Data.Sqlite.dll", "SQLitePCLRaw.batteries_v2.dll", "SQLitePCLRaw.core.dll",
        "SQLitePCLRaw.provider.e_sqlite3.dll", "Microsoft.Extensions.DependencyModel.dll", "Npgsql.dll", "Npgsql.EntityFrameworkCore.PostgreSQL.dll",
        "aidomme-legacy-configuration.json"
    )
    foreach ($name in $obsolete) {
        $stale = Join-Path $targetDir $name
        if ((Test-Path -LiteralPath $stale) -and -not (Test-Path -LiteralPath (Join-Path $outputDir $name))) {
            Remove-Item -LiteralPath $stale -Force
        }
    }
    Get-ChildItem -Path (Join-Path $targetDir "runtimes") -Recurse -Filter "e_sqlite3*" -ErrorAction SilentlyContinue |
        Remove-Item -Force

    Write-Host ("Staged {0} -> {1}" -f $manifest.id, $targetDir)
}

Write-Host "Local AIDomme extension is ready. Restart Cove to reload it if it is already running."
