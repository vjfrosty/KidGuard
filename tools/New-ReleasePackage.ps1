#requires -Version 5.1
# Makes the release download in dist\: KidGuard-v<version>.zip (one "KidGuard" folder with Setup.cmd at the top).
# Changes nothing outside dist. See RELEASING.md.
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

Write-Host "Checked $($files.Count - 1) files against SHA256SUMS.txt"
Write-Host "Made: $zip"
