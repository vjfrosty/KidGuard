#requires -Version 5.1
# Makes the download for parents: dist\KidGuard-v<version>.zip (one "KidGuard" folder with Setup.cmd at the top)
# and dist\KidGuard-v<version>.zip.sha256. Changes nothing on this PC except the dist folder. See RELEASING.md.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+(\.\d+)?$')][string]$Version,
    [string]$Package
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
# Windows PowerShell 5.1 leaves $PSScriptRoot empty in parameter defaults, so the default is set here.
if (-not $Package) { $Package = Join-Path $PSScriptRoot '..\KidGuard_Windows_Whitelist_Schedules_v0.1\KidGuard' }

$Package = (Resolve-Path -LiteralPath $Package).Path
$sums = Join-Path $Package 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $sums)) { throw "SHA256SUMS.txt not found in $Package" }

# The zip holds exactly the files listed in SHA256SUMS.txt (plus that file), and every hash must match.
# build\ and the .lnk shortcuts are left out: Setup.cmd compiles the programs on the parent's PC.
$files = @()
foreach ($line in Get-Content -LiteralPath $sums) {
    if (-not $line.Trim()) { continue }
    if ($line -notmatch '^([0-9a-f]{64})\s+(.+)$') { throw "Bad line in SHA256SUMS.txt: $line" }
    $expected = $Matches[1]; $relative = $Matches[2].Trim()
    $path = Join-Path $Package $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Listed in SHA256SUMS.txt but missing: $relative" }
    $actual = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "Checksum mismatch: $relative. Update SHA256SUMS.txt before releasing." }
    $files += $relative
}
$files += 'SHA256SUMS.txt'
$unlisted = @(Get-ChildItem -LiteralPath $Package -Recurse -File |
    ForEach-Object { $_.FullName.Substring($Package.Length + 1).Replace('\', '/') } |
    Where-Object { $_ -notlike 'build/*' -and $_ -notlike '*.lnk' -and $files -notcontains $_ })
if ($unlisted) { throw "Files not in SHA256SUMS.txt (add them or remove them): $($unlisted -join ', ')" }

$dist = Join-Path $PSScriptRoot '..\dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$dist = (Resolve-Path -LiteralPath $dist).Path
# Start empty, so an upload of everything in dist never includes files from another version.
Get-ChildItem -LiteralPath $dist -File | Remove-Item
$zip = Join-Path $dist "KidGuard-v$Version.zip"

$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($relative in $files) {
        # Forward slashes, so every unzip tool (and Windows Explorer) recreates the folders.
        $entry = 'KidGuard/' + $relative.Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $Package $relative), $entry, [IO.Compression.CompressionLevel]::Optimal)
    }
} finally { $archive.Dispose() }

$hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText("$zip.sha256", "$hash  KidGuard-v$Version.zip`n")
Write-Host "Checked $($files.Count - 1) files against SHA256SUMS.txt"
Write-Host "Made: $zip"
Write-Host "SHA256: $hash"

# Ready-built programs, attached to the release next to the zip. Run Build.ps1 first so they match the sources.
$programs = @('KidGuard.exe', 'KidGuardPanel.exe', 'KidGuardTray.exe')
$build = Join-Path $Package 'build'
$newest = (Get-ChildItem -LiteralPath $Package -Recurse -File | Where-Object { $_.FullName -notlike "$build\*" -and $_.Extension -ne '.lnk' } |
    Measure-Object LastWriteTimeUtc -Maximum).Maximum
$lines = @()
foreach ($name in $programs) {
    $source = Join-Path $build $name
    if (-not (Test-Path -LiteralPath $source)) { throw "build\$name not found. Run Build.ps1 first." }
    if ((Get-Item -LiteralPath $source).LastWriteTimeUtc -lt $newest) { throw "build\$name is older than the sources. Run Build.ps1 again." }
    Copy-Item -LiteralPath $source -Destination (Join-Path $dist $name) -Force
    $lines += '{0}  {1}' -f (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant(), $name
}
$programSums = Join-Path $dist "KidGuard-v$Version-programs.sha256"
[IO.File]::WriteAllText($programSums, ($lines -join "`n") + "`n")
Write-Host "Copied programs: $($programs -join ', ')"
Write-Host "Made: $programSums"

# Logo and icon from Media\, renamed without spaces (GitHub turns spaces in asset names into dots).
$media = Join-Path $PSScriptRoot '..\Media'
foreach ($pair in @(@('KidGuard guardian shield logo.png', 'KidGuard-logo.png'), @('KidGuard shield checkmark icon.png', 'KidGuard-icon.png'))) {
    Copy-Item -LiteralPath (Join-Path $media $pair[0]) -Destination (Join-Path $dist $pair[1]) -Force
}
Write-Host 'Copied media: KidGuard-logo.png, KidGuard-icon.png'
