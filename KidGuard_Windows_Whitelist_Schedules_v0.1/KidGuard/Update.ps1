#requires -Version 5.1
#requires -RunAsAdministrator
# Updates an installed KidGuard to the programs and scripts in this package. Settings (Admin folder) are kept.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'KidGuard'
$admin = Join-Path $root 'Admin'
if (-not (Get-Service KidGuard -ErrorAction SilentlyContinue) -or -not (Test-Path -LiteralPath (Join-Path $admin 'installation.json'))) {
    throw 'KidGuard is not installed on this PC. Use Setup.cmd instead.'
}
if (Get-Process KidGuardPanel -ErrorAction SilentlyContinue) { throw 'Close the KidGuard Parent Panel first, then run the update again.' }
$sac = 0
try { $sac = [int](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop).VerifiedAndReputablePolicyState } catch { }
if ($sac -eq 1 -and -not $env:KIDGUARD_SIGN_THUMBPRINT) { throw 'Smart App Control is on and would block the updated programs. See "Smart App Control" in README.md. Nothing was changed.' }

Write-Host 'Building and testing the new version (no changes yet)...'
& (Join-Path $PSScriptRoot 'Build.ps1')
$build = Join-Path $PSScriptRoot 'build'
$files = @(
    @{ From=(Join-Path $build 'KidGuard.exe'); To='KidGuard.exe' },
    @{ From=(Join-Path $build 'KidGuardPanel.exe'); To='KidGuardPanel.exe' },
    @{ From=(Join-Path $build 'KidGuardTray.exe'); To='KidGuardTray.exe' },
    @{ From=(Join-Path $build 'kidguard.ico'); To='kidguard.ico' }
) + (@('Manage.ps1','Uninstall.ps1','Uninstall.cmd','Registry.ps1') | ForEach-Object { @{ From=(Join-Path $PSScriptRoot $_); To=$_ } })

$backup = Join-Path $root ('backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup | Out-Null
foreach ($f in $files) { $old = Join-Path $root $f.To; if (Test-Path -LiteralPath $old) { Copy-Item -LiteralPath $old -Destination $backup } }
Write-Host "Previous version saved in $backup"

Write-Host 'Stopping KidGuard for a moment. If your child is signed in, they are signed out now (save their work first).'
$task = Get-ScheduledTask -TaskName 'KidGuard-Watchdog' -ErrorAction SilentlyContinue
if ($task) { Disable-ScheduledTask -InputObject $task | Out-Null }
$started = Get-Date
try {
    Stop-Service KidGuard
    (Get-Service KidGuard).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    try {
        foreach ($f in $files) { Copy-Item -LiteralPath $f.From -Destination (Join-Path $root $f.To) -Force }
        # New optional files are added; the parent's own versions are never overwritten.
        foreach ($name in 'bundles.txt','startpage.html') {
            $target = Join-Path $admin $name
            if (-not (Test-Path -LiteralPath $target)) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot ('config\' + $name)) -Destination $target }
        }
        Start-Service KidGuard
        $heartbeat = Join-Path $admin 'heartbeat.txt'
        $healthy = $false
        for ($i = 0; $i -lt 20; $i++) {
            if ((Test-Path -LiteralPath $heartbeat) -and (Get-Item -LiteralPath $heartbeat).LastWriteTime -gt $started) { $healthy = $true; break }
            Start-Sleep -Seconds 1
        }
        if (-not $healthy) { throw 'The updated service did not report a healthy cycle. Check Admin\kidguard.log.' }
    } catch {
        Write-Warning "Update failed: $_. Restoring the previous version."
        Stop-Service KidGuard -ErrorAction SilentlyContinue
        (Get-Service KidGuard).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
        Get-ChildItem -LiteralPath $backup -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $root -Force }
        Start-Service KidGuard
        throw
    }
} finally {
    if ($task) { Enable-ScheduledTask -InputObject $task | Out-Null }
}
$version = & (Join-Path $root 'KidGuard.exe') --version
Set-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KidGuard' -Name DisplayVersion -Value $version -ErrorAction SilentlyContinue
# Desktop and Start menu shortcuts with the shield (added if missing, e.g. after deleting the desktop one). Not fatal.
try {
    $record = Join-Path $admin 'panel-shortcut.txt'
    $known = @(if (Test-Path -LiteralPath $record) { Get-Content -LiteralPath $record | ForEach-Object { $_.Trim() } | Where-Object { $_ } })
    $shortcuts = @(& (Join-Path $root 'KidGuard.exe') --panel-shortcuts)
    if ($LASTEXITCODE -ne 0) { throw 'KidGuard.exe --panel-shortcuts failed.' }
    Set-Content -LiteralPath $record -Value (@($known) + @($shortcuts) | Select-Object -Unique) -Encoding UTF8
} catch { Write-Warning "Could not create the Parent Panel shortcuts: $_" }
Write-Host "KidGuard updated and running (version $version). Settings, schedule and allowed sites were kept."
