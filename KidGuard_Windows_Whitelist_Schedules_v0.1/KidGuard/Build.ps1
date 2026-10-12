#requires -Version 5.1
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess -or $env:OS -ne 'Windows_NT') { throw 'Use 64-bit Windows PowerShell 5.1 on Windows 11 x64.' }
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) { throw '.NET Framework C# compiler not found. Enable/install .NET Framework 4.8 first.' }
$out = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $out -Force | Out-Null
$exe = Join-Path $out 'KidGuard.exe'
$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
. (Join-Path $PSScriptRoot 'Signing.ps1')
. (Join-Path $PSScriptRoot 'Assets.ps1')
. (Join-Path $PSScriptRoot 'Translations.ps1')
$icon = Join-Path $out 'kidguard.ico'
New-KidGuardIcon (Join-Path $PSScriptRoot 'assets\kidguard-icon.png') $icon
# Bulgarian texts (lang\bg.txt) are embedded; KidGuard.exe, the Parent Panel, the child's helper and setup read them.
$language = "/resource:$(Join-Path $PSScriptRoot 'lang\bg.txt'),KidGuard.bg.txt"
& $csc /nologo /target:exe /platform:x64 /langversion:5 /optimize+ /warn:4 "/out:$exe" "/win32icon:$icon" $language /reference:System.ServiceProcess.dll /reference:System.Runtime.Serialization.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed. No system changes were made.' }
Set-KidGuardSignature $exe
Invoke-BuiltProgram $exe @('--self-test')
if ($LASTEXITCODE -ne 0) { throw 'Self-tests failed. No system changes were made.' }
Invoke-BuiltProgram $exe @('--validate', (Join-Path $PSScriptRoot 'config'))
if ($LASTEXITCODE -ne 0) { throw 'Configuration validation failed.' }
Invoke-BuiltProgram $exe @('--check-bundles', (Join-Path $PSScriptRoot 'config\bundles.txt'))
if ($LASTEXITCODE -ne 0) { throw 'Site list (bundles.txt) validation failed.' }
Invoke-BuiltProgram $exe @('--startpage-check', (Join-Path $PSScriptRoot 'config\startpage.html'))
if ($LASTEXITCODE -ne 0) { throw 'Start page (config\startpage.html) validation failed.' }
Test-KidGuardTranslations $PSScriptRoot | Out-Null
# Parent Panel: references KidGuard.exe for all policy logic, so both must sit in the same folder.
$panel = Join-Path $out 'KidGuardPanel.exe'
$panelSources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'panel') -Filter '*.cs' | ForEach-Object FullName)
& $csc /nologo /target:winexe /platform:x64 /langversion:5 /optimize+ /warn:4 /codepage:65001 "/out:$panel" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'panel\app.manifest')" "/win32icon:$icon" "/reference:$exe" `
    "/resource:$icon,KidGuard.Icon.ico" "/resource:$(Join-Path $PSScriptRoot 'assets\kidguard-logo.png'),KidGuard.Logo.png" `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.ServiceProcess.dll /reference:System.Runtime.Serialization.dll $panelSources
if ($LASTEXITCODE -ne 0) { throw 'Parent Panel compilation failed. No system changes were made.' }
Set-KidGuardSignature $panel
# The child's helper runs as the child, who cannot read the KidGuard folder: it is self-contained (no KidGuard.exe reference).
$tray = Join-Path $out 'KidGuardTray.exe'
$traySources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'tray') -Filter '*.cs' | ForEach-Object FullName) +
    @((Join-Path $PSScriptRoot 'src\ChildData.cs'), (Join-Path $PSScriptRoot 'src\Text.cs'))
& $csc /nologo /target:winexe /platform:x64 /langversion:5 /optimize+ /warn:4 "/out:$tray" `
    "/win32manifest:$(Join-Path $PSScriptRoot 'tray\app.manifest')" "/win32icon:$icon" "/resource:$icon,KidGuard.Icon.ico" $language `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll $traySources
if ($LASTEXITCODE -ne 0) { throw 'Child helper compilation failed. No system changes were made.' }
Set-KidGuardSignature $tray
# "Install KidGuard" / "Update KidGuard" shortcuts with the shield next to Setup.cmd and Update.cmd. Not fatal.
try {
    . (Join-Path $PSScriptRoot 'Shortcuts.ps1')
    $shippedIcon = Join-Path $PSScriptRoot 'assets\kidguard.ico'
    if (-not (Test-Path -LiteralPath $shippedIcon)) { Copy-Item -LiteralPath $icon -Destination $shippedIcon }
    Update-KidGuardLaunchers $PSScriptRoot
} catch { Write-Warning "Could not create the Install/Update shortcuts: $_" }
Write-Host "Built and tested: $exe"
Write-Host "Built: $panel"
Write-Host "Built: $tray"
