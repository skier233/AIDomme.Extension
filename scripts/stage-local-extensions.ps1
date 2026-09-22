param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [string]$DestinationRoot = (Join-Path $env:LOCALAPPDATA "cove_personal_live\extensions"),
    [string]$LegacyConfigurationPath
)

$ErrorActionPreference = "Stop"

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $scriptRoot

$extensions = @(
    "AIDome"
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

Write-Host "Building AIDome.Extension ($Configuration)..."
dotnet build (Join-Path $repoRoot "extensions\AIDome\AIDome.csproj") -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
if ([string]::IsNullOrWhiteSpace($LegacyConfigurationPath) -and -not (Test-Path -LiteralPath (Join-Path $DestinationRoot "aidomme-legacy-configuration.json"))) {
    $originalDefinitionCandidate = Join-Path (Split-Path -Parent $repoRoot) "AIDome2025\AIDome2025\AIDome2025\App_Data\commandcenter.dev.db"
    if (Test-Path -LiteralPath $originalDefinitionCandidate) { $LegacyConfigurationPath = $originalDefinitionCandidate }
}
if (-not [string]::IsNullOrWhiteSpace($LegacyConfigurationPath)) {
    $originalDefinitionSource = (Resolve-Path -LiteralPath $LegacyConfigurationPath).Path
    @{ path = $originalDefinitionSource } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $DestinationRoot "aidomme-legacy-configuration.json")
    Write-Host "Preserving original definitions/calibration from $originalDefinitionSource (read-only; usage excluded)."
}

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

    Write-Host ("Staged {0} -> {1}" -f $manifest.id, $targetDir)
}

Write-Host "Local AIDome extension is ready. Restart Cove to reload it if it is already running."
