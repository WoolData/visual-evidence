# Copyright (c) 2026 Wool Data Inc. Licensed under the MIT License.

[CmdletBinding()]
param(
    [string] $Project = 'src/WoolData.VisualEvidence.Cli/WoolData.VisualEvidence.Cli.csproj',
    [string] $OutputDirectory = 'artifacts/package',
    [int64] $MaximumPackageBytes = 40MB
)

$ErrorActionPreference = 'Stop'
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$stagingRoot = Join-Path $outputRoot ".pack-staging-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
try {
    & dotnet pack $Project --configuration Release --output $stagingRoot
    if ($LASTEXITCODE -ne 0) { throw "dotnet pack failed with exit code $LASTEXITCODE." }

    $packages = @(Get-ChildItem -LiteralPath $stagingRoot -File -Filter '*.nupkg' | Where-Object Name -NotLike '*.snupkg')
    if ($packages.Count -ne 1) { throw "Expected one tool package; found $($packages.Count)." }

    New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null
    $destination = Join-Path $outputRoot $packages[0].Name
    if (Test-Path -LiteralPath $destination) { Remove-Item -LiteralPath $destination -Force }

    Add-Type -AssemblyName System.IO.Compression
    $allowedRuntimeMarkers = @(
        '/runtimes/linux-x64/'
        '/runtimes/osx/'
        '/runtimes/win-x64/'
    )
    $source = [IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    $target = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($entry in $source.Entries) {
            if ($entry.FullName.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)) { continue }
            if ($entry.FullName -like 'tools/*/any/runtimes/*' -and
                -not ($allowedRuntimeMarkers | Where-Object { $entry.FullName.Contains($_, [StringComparison]::Ordinal) })) {
                continue
            }

            $copy = $target.CreateEntry($entry.FullName, [IO.Compression.CompressionLevel]::Optimal)
            $copy.LastWriteTime = $entry.LastWriteTime
            if ($entry.Length -gt 0) {
                $inputStream = $entry.Open()
                $outputStream = $copy.Open()
                try { $inputStream.CopyTo($outputStream) }
                finally {
                    $outputStream.Dispose()
                    $inputStream.Dispose()
                }
            }
        }
    }
    finally {
        $target.Dispose()
        $source.Dispose()
    }

    $package = Get-Item -LiteralPath $destination
    if ($package.Length -gt $MaximumPackageBytes) {
        throw "Tool package is $($package.Length) bytes; limit is $MaximumPackageBytes bytes."
    }
    $archive = [IO.Compression.ZipFile]::OpenRead($package.FullName)
    try {
        $names = @($archive.Entries.FullName)
        if ($names -match '\.pdb$') { throw 'Tool package contains debug symbols.' }
        foreach ($required in @('LICENSE', 'THIRD-PARTY-NOTICES.md', 'SkiaSharp-LICENSE.txt', 'SkiaSharp-THIRD-PARTY-NOTICES.txt')) {
            if ($required -notin $names) { throw "Tool package is missing $required." }
        }
    }
    finally { $archive.Dispose() }
    $package.FullName
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
