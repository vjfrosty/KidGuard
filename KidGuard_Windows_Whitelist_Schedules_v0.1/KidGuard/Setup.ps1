#requires -Version 5.1
#requires -RunAsAdministrator
# Started by Setup.cmd. Builds and tests KidGuard (no system changes), then opens the setup wizard.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
try {
    . (Join-Path $PSScriptRoot 'Signing.ps1')
    # Smart App Control blocks unsigned programs built on this PC, including the wizard itself: explain instead of failing silently.
    $sac = 0
    try { $sac = [int](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop).VerifiedAndReputablePolicyState } catch { }
    if ($sac -eq 1 -and -not $env:KIDGUARD_SIGN_THUMBPRINT) {
        Write-Host ''
        Write-Host 'Smart App Control is ON on this computer.' -ForegroundColor Yellow
        Write-Host 'It blocks programs that are not signed by a publisher Windows trusts. KidGuard is built on this'
        Write-Host 'computer and is not signed, so Windows would block it - sometimes at once, sometimes later (for'
        Write-Host 'example after a restart). Then your child''s account would silently stop being locked.'
        Write-Host ''
        Write-Host 'Setup has stopped. Nothing was changed. Your options are in README.md, section "Smart App Control".'
        exit 1
    }
    Write-Host 'KidGuard setup: building and testing on this computer (about 20 seconds)...'
    & (Join-Path $PSScriptRoot 'Build.ps1')
    # The wizard runs from a temporary folder: Install.ps1 rebuilds the package's build folder while it runs.
    $work = Join-Path ([IO.Path]::GetTempPath()) ('KidGuardSetup-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    $wizard = Join-Path $work 'KidGuardSetup.exe'
    & $csc /nologo /target:winexe /platform:x64 /langversion:5 /optimize+ /warn:4 /codepage:65001 "/out:$wizard" `
        "/win32manifest:$(Join-Path $PSScriptRoot 'setup\app.manifest')" "/win32icon:$(Join-Path $PSScriptRoot 'build\kidguard.ico')" `
        "/resource:$(Join-Path $PSScriptRoot 'build\kidguard.ico'),KidGuard.Icon.ico" "/resource:$(Join-Path $PSScriptRoot 'assets\kidguard-icon.png'),KidGuard.Shield.png" `
        "/resource:$(Join-Path $PSScriptRoot 'lang\bg.txt'),KidGuard.bg.txt" `
        /reference:System.Windows.Forms.dll /reference:System.Drawing.dll `
        /reference:System.Runtime.Serialization.dll (Join-Path $PSScriptRoot 'setup\SetupWizard.cs') (Join-Path $PSScriptRoot 'src\Text.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Setup wizard compilation failed. No system changes were made.' }
    Set-KidGuardSignature $wizard
    Write-Host 'Opening the setup wizard...'
    try { Start-Process -FilePath $wizard -ArgumentList ('--package "' + $PSScriptRoot + '"') -Wait }
    catch { throw "Windows did not allow the setup wizard to start ($($_.Exception.Message.Trim())). See ""Smart App Control"" in README.md." }
    Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
}
catch {
    Write-Host ''
    Write-Host "Setup could not start: $_" -ForegroundColor Red
    Write-Host 'No system changes were made by this step.'
    exit 1
}
