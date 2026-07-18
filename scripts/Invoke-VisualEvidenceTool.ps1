# Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $ToolArguments
)

$ErrorActionPreference = 'Stop'
$actionRoot = Split-Path -Parent $PSScriptRoot
$useSource = $env:VISUAL_EVIDENCE_USE_SOURCE -eq 'true'

if ($useSource) {
    & dotnet run `
        --project (Join-Path $actionRoot 'src/WoolData.VisualEvidence.Cli/WoolData.VisualEvidence.Cli.csproj') `
        --configuration Release `
        --property:RestoreLockedMode=true `
        -- @ToolArguments
    exit $LASTEXITCODE
}

$manifestPath = Join-Path $actionRoot 'tool-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or
    [string]::IsNullOrWhiteSpace($manifest.packageId) -or
    [string]::IsNullOrWhiteSpace($manifest.version) -or
    [string]::IsNullOrWhiteSpace($manifest.packageUrl) -or
    $manifest.sha256 -notmatch '^[0-9a-fA-F]{64}$') {
    throw "Invalid packaged-tool manifest: $manifestPath"
}

$cacheRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    Join-Path ([System.IO.Path]::GetTempPath()) 'wooldata-visual-evidence'
} else {
    Join-Path $env:RUNNER_TEMP 'wooldata-visual-evidence'
}
$versionRoot = Join-Path $cacheRoot $manifest.version
$packageRoot = Join-Path $versionRoot 'package'
$toolRoot = Join-Path $versionRoot 'tool'
$executableName = if ($IsWindows) { 'visual-evidence.exe' } else { 'visual-evidence' }
$executable = Join-Path $toolRoot $executableName

if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
    New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
    $packagePath = Join-Path $packageRoot "$($manifest.packageId).$($manifest.version).nupkg"
    Invoke-WebRequest -Uri $manifest.packageUrl -OutFile $packagePath
    $actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($actualHash, $manifest.sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $packagePath -Force -ErrorAction SilentlyContinue
        throw "Packaged tool SHA-256 mismatch. Expected $($manifest.sha256); received $actualHash."
    }

    Remove-Item -LiteralPath $toolRoot -Recurse -Force -ErrorAction SilentlyContinue
    & dotnet tool install $manifest.packageId `
        --tool-path $toolRoot `
        --version $manifest.version `
        --add-source $packageRoot `
        --ignore-failed-sources
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }
}

& $executable @ToolArguments
exit $LASTEXITCODE
