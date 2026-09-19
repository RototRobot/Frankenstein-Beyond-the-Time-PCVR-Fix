<#
.SYNOPSIS
    Builds both release archives.

.DESCRIPTION
    Produces the assets for a GitHub Release:

      FrankensteinPCVRFix-<ver>.zip
          Plugin + docs. No third-party binaries. User supplies BepInEx.

      FrankensteinPCVRFix-<ver>-with-BepInEx.zip
          The same, plus an unmodified copy of the official BepInEx win_x64 release laid out ready to
          drop into the game folder, with all four third-party licenses included (LGPL-2.1 for BepInEx
          requires the text to travel with the binaries).

      SHA256SUMS.txt
          Checksums for both, in the format sha256sum -c reads.

    BepInEx is NOT stored in this repository. Point -BepInExDir at an extracted copy of the official
    release; the script verifies it looks right before using it.

    Archives are written with forward-slash entry names, as the zip format specifies. Windows
    PowerShell's Compress-Archive writes backslashes, which some extractors turn into file names.

.PARAMETER Version
    Release version string. Must match the Version constant in src\FrankensteinPCVRFix\Plugin.cs.

.PARAMETER BepInExDir
    Folder containing an extracted BepInEx win_x64 release (the one with winhttp.dll at its root).
    Defaults to BepInEx_win_x64_5.4.23.5 next to the repo folder.

.PARAMETER OutDir
    Where the archives go. Defaults to dist\ in the repo, which is git-ignored.

.PARAMETER SkipBundle
    Build only the plugin-only archive.

.EXAMPLE
    .\Make-Release.ps1 -Version 1.0.0
    .\Make-Release.ps1 -Version 1.0.0 -OutDir "$env:USERPROFILE\Desktop\FrankenPCVRFix-1.0.0"
    .\Make-Release.ps1 -Version 1.1.0 -BepInExDir "C:\dl\BepInEx_win_x64_5.4.23.5"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [string] $BepInExDir,
    [string] $OutDir,
    [switch] $SkipBundle
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repo    = Split-Path $PSScriptRoot -Parent
$srcDir  = Join-Path $repo 'src\FrankensteinPCVRFix'
$distSrc = Join-Path $repo 'dist-files'
$dll     = Join-Path $repo 'build\FrankensteinPCVRFix.dll'
$name    = 'FrankensteinPCVRFix'

if (-not $OutDir)     { $OutDir = Join-Path $repo 'dist' }
if (-not $BepInExDir) { $BepInExDir = Join-Path (Split-Path $repo -Parent) 'BepInEx_win_x64_5.4.23.5' }

# The archive name and the plugin's own version must agree.
$declared = Select-String -Path (Join-Path $srcDir 'Plugin.cs') -Pattern 'const string Version = "([^"]+)"' |
    Select-Object -First 1 | ForEach-Object { $_.Matches[0].Groups[1].Value }
if ($declared -ne $Version) { throw "Plugin.cs declares version '$declared', not '$Version'. Update it and rebuild." }

# Refuse to package a build older than its source.
if (-not (Test-Path $dll)) { throw "Plugin not built. Run tools\Build.ps1 first (expected $dll)." }
$newest = Get-ChildItem $srcDir -Filter *.cs | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if ($newest.LastWriteTimeUtc -gt (Get-Item $dll).LastWriteTimeUtc) {
    throw "build\$name.dll is older than $($newest.Name). Run tools\Build.ps1 again."
}

function New-Zip([string] $sourceDir, [string] $zipPath) {
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    $root = (Resolve-Path $sourceDir).Path.TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
    try {
        Get-ChildItem $sourceDir -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
            $entry = $_.FullName.Substring($root.Length).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $_.FullName, $entry, [IO.Compression.CompressionLevel]::Optimal)
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host ("Built {0}  ({1:N0} bytes)" -f (Split-Path $zipPath -Leaf), (Get-Item $zipPath).Length) -ForegroundColor Green
}

New-Item -ItemType Directory -Force $OutDir | Out-Null

# Stage outside the repo so nothing half-built is ever committed.
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ("fbtfix-release-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
$zips = @()

# ---------------------------------------------------------------- plugin only
$s1 = Join-Path $stageRoot 'plain'
New-Item -ItemType Directory -Force $s1 | Out-Null
Copy-Item $dll                              $s1 -Force
Copy-Item (Join-Path $distSrc 'INSTALL.md') $s1 -Force
Copy-Item (Join-Path $repo 'LICENSE')       $s1 -Force
Copy-Item (Join-Path $repo 'NOTICE')        $s1 -Force

$zips += Join-Path $OutDir "$name-$Version.zip"
New-Zip $s1 $zips[-1]

# ---------------------------------------------------------------- with BepInEx
if (-not $SkipBundle) {
    $needed = @('winhttp.dll', 'doorstop_config.ini', 'BepInEx\core\BepInEx.dll', 'BepInEx\core\0Harmony.dll')
    foreach ($n in $needed) {
        if (-not (Test-Path (Join-Path $BepInExDir $n))) {
            throw "BepInExDir does not look like an extracted BepInEx win_x64 release (missing $n): $BepInExDir"
        }
    }

    $s2 = Join-Path $stageRoot 'bundle'
    New-Item -ItemType Directory -Force $s2 | Out-Null
    Copy-Item (Join-Path $BepInExDir '*') $s2 -Recurse -Force
    New-Item -ItemType Directory -Force (Join-Path $s2 'BepInEx\plugins') | Out-Null

    Copy-Item $dll (Join-Path $s2 'BepInEx\plugins') -Force
    Copy-Item (Join-Path $distSrc 'INSTALL-with-BepInEx.md') $s2 -Force
    Copy-Item (Join-Path $distSrc 'THIRD-PARTY.md')          $s2 -Force
    Copy-Item (Join-Path $distSrc 'THIRD-PARTY-LICENSES')    $s2 -Recurse -Force
    Copy-Item (Join-Path $repo 'LICENSE')                    $s2 -Force
    Copy-Item (Join-Path $repo 'NOTICE')                     $s2 -Force

    $zips += Join-Path $OutDir "$name-$Version-with-BepInEx.zip"
    New-Zip $s2 $zips[-1]
}

# Checksums, in the format sha256sum -c reads.
$sumsFile = Join-Path $OutDir 'SHA256SUMS.txt'
$zips | ForEach-Object { "{0}  {1}" -f (Get-FileHash $_ -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $_ -Leaf) } |
    Set-Content -Path $sumsFile -Encoding Ascii

Remove-Item $stageRoot -Recurse -Force

Write-Host ""
Write-Host "SHA-256 ($sumsFile):" -ForegroundColor Cyan
Get-Content $sumsFile | ForEach-Object { "  $_" }
