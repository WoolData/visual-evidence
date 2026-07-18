# Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PublishDirectory
)

$ErrorActionPreference = 'Stop'
$publishRoot = [IO.Path]::GetFullPath($PublishDirectory)
if (-not (Test-Path -LiteralPath $publishRoot -PathType Container)) {
    throw "Native publish directory does not exist: $publishRoot"
}

Get-ChildItem -LiteralPath $publishRoot -Recurse -File -Filter '*.pdb' |
    Remove-Item -LiteralPath { $_.FullName } -Force

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$files = @{
    'WOOLDATA-LICENSE.txt' = Join-Path $repositoryRoot 'LICENSE'
    'THIRD-PARTY-NOTICES.md' = Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md'
    'SKIASHARP-LICENSE.txt' = Join-Path $repositoryRoot 'licenses/SkiaSharp-LICENSE.txt'
    'SKIASHARP-THIRD-PARTY-NOTICES.txt' = Join-Path $repositoryRoot 'licenses/SkiaSharp-THIRD-PARTY-NOTICES.txt'
}

$dotnetRoots = @(
    $env:DOTNET_ROOT
    $env:DOTNET_ROOT_X64
    (Split-Path (Get-Command dotnet -ErrorAction Stop).Source -Parent)
) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
$dotnetRoot = $dotnetRoots | Where-Object {
    (Test-Path -LiteralPath (Join-Path $_ 'LICENSE.txt') -PathType Leaf) -and
    (Test-Path -LiteralPath (Join-Path $_ 'ThirdPartyNotices.txt') -PathType Leaf)
} | Select-Object -First 1
if (-not $dotnetRoot) {
    throw 'Could not locate the .NET distribution license and third-party notices.'
}
$files['DOTNET-LICENSE.txt'] = Join-Path $dotnetRoot 'LICENSE.txt'
$files['DOTNET-THIRD-PARTY-NOTICES.txt'] = Join-Path $dotnetRoot 'ThirdPartyNotices.txt'

foreach ($entry in $files.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
        throw "Required distribution notice is missing: $($entry.Value)"
    }
    Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $publishRoot $entry.Key) -Force
}

if (Get-ChildItem -LiteralPath $publishRoot -Recurse -File -Filter '*.pdb') {
    throw 'Native distribution still contains debug symbols.'
}
