# Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

[CmdletBinding()]
param(
    [Parameter(ValueFromRemainingArguments)]
    [string[]] $ToolArguments
)

$ErrorActionPreference = 'Stop'
$actionRoot = Split-Path -Parent $PSScriptRoot
if ($env:VISUAL_EVIDENCE_USE_SOURCE -eq 'true') {
    & dotnet run `
        --project (Join-Path $actionRoot 'src/WoolData.VisualEvidence.Cli/WoolData.VisualEvidence.Cli.csproj') `
        --configuration Release `
        --property:RestoreLockedMode=true `
        -- @ToolArguments
    exit $LASTEXITCODE
}

$rid = if ($IsWindows -and [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64') {
    'win-x64'
} elseif ($IsLinux -and [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'X64') {
    'linux-x64'
} elseif ($IsMacOS -and [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq 'Arm64') {
    'osx-arm64'
} else {
    throw "No packaged Visual Evidence tool supports this runner: $([System.Runtime.InteropServices.RuntimeInformation]::OSDescription) $([System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture)."
}

$manifestPath = Join-Path $actionRoot 'tool-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$artifact = $manifest.artifacts.$rid
if ($manifest.schemaVersion -ne 2 -or
    $manifest.version -notmatch '^\d+\.\d+\.\d+$' -or
    $manifest.repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
    $null -eq $artifact -or
    [string]::IsNullOrWhiteSpace($artifact.archive) -or
    [string]::IsNullOrWhiteSpace($artifact.executable)) {
    throw "Invalid packaged-tool manifest for ${rid}: $manifestPath"
}

$cacheRoot = if ([string]::IsNullOrWhiteSpace($env:RUNNER_TEMP)) {
    Join-Path ([System.IO.Path]::GetTempPath()) 'wooldata-visual-evidence'
} else {
    Join-Path $env:RUNNER_TEMP 'wooldata-visual-evidence'
}
$versionRoot = Join-Path $cacheRoot "$($manifest.version)-$rid"
$toolRoot = Join-Path $versionRoot 'tool'
$executable = Join-Path $toolRoot $artifact.executable
$verifiedMarker = Join-Path $versionRoot 'attestation-verified'

if (-not (Test-Path -LiteralPath $executable -PathType Leaf) -or
    -not (Test-Path -LiteralPath $verifiedMarker -PathType Leaf)) {
    New-Item -ItemType Directory -Path $versionRoot -Force | Out-Null
    $archivePath = Join-Path $versionRoot $artifact.archive
    $checksumPath = "$archivePath.sha256"
    $releaseRoot = "https://github.com/$($manifest.repository)/releases/download/v$($manifest.version)"
    Invoke-WebRequest -Uri "$releaseRoot/$($artifact.archive)" -OutFile $archivePath
    Invoke-WebRequest -Uri "$releaseRoot/$($artifact.archive).sha256" -OutFile $checksumPath

    $expectedHash = (Get-Content -LiteralPath $checksumPath -Raw).Trim().Split(' ')[0]
    $actualHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$' -or
        -not [string]::Equals($actualHash, $expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Packaged tool SHA-256 mismatch. Expected $expectedHash; received $actualHash."
    }

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI is required to verify the packaged tool attestation.'
    }
    & gh attestation verify $archivePath --repo $manifest.repository | Out-Null
    if ($LASTEXITCODE -ne 0) {
        exit $LASTEXITCODE
    }

    if (Test-Path -LiteralPath $toolRoot) {
        Remove-Item -LiteralPath $toolRoot -Recurse -Force
    }
    Expand-Archive -LiteralPath $archivePath -DestinationPath $toolRoot
    if (-not $IsWindows) {
        & chmod +x $executable
        if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    }
    if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
        throw "Verified archive omitted the expected executable: $($artifact.executable)"
    }
    Set-Content -LiteralPath $verifiedMarker -Value $actualHash -NoNewline
}

& $executable @ToolArguments
exit $LASTEXITCODE
