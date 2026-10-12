#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'KidGuard'
$admin = Join-Path $root 'Admin'
$info = Get-Content -LiteralPath (Join-Path $admin 'installation.json') -Raw | ConvertFrom-Json
$exe = Join-Path $root 'KidGuard.exe'
# Local and Microsoft accounts are local Windows accounts (a record without kind is local).
if (-not $info.kind -or $info.kind -in 'local','microsoft') {
    $child = Get-LocalUser -SID $info.userSid
    if ($child.Name -ne $info.userName) { throw 'Target account was renamed. Resolve identity before restoring settings.' }
}
$task = Get-ScheduledTask -TaskName 'KidGuard-Watchdog' -ErrorAction SilentlyContinue
if ($task) {
    Disable-ScheduledTask -InputObject $task | Out-Null
    Stop-ScheduledTask -InputObject $task
    Unregister-ScheduledTask -InputObject $task -Confirm:$false
}
$service = Get-Service KidGuard -ErrorAction SilentlyContinue
if ($service) {
    Set-Service KidGuard -StartupType Disabled
    Stop-Service KidGuard
    $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(30))
}
# Desktop shortcuts and the child-readable copy of the start page (not fatal).
& $exe --startpage-remove
if ($LASTEXITCODE -ne 0) { Write-Warning 'Could not remove the start page shortcut; delete it from the child''s desktop and C:\ProgramData\KidGuard-StartPage manually.' }
# The child's helper (next to the clock) and its folder (not fatal).
& $exe --child-remove
if ($LASTEXITCODE -ne 0) { Write-Warning 'Could not remove the child''s helper; close KidGuardTray.exe and delete C:\ProgramData\KidGuard-Child manually.' }
& $exe --remove-filter
if ($LASTEXITCODE -ne 0) { throw 'Filter removal failed. Do not delete the recovery files; rerun uninstall after fixing BFE.' }
. (Join-Path $root 'Registry.ps1')
Restore-ChildProxy -Sid $info.userSid -Backup (Join-Path $admin 'registry-backup.clixml')
# Original enabled/disabled state (local, Microsoft) or removal of KidGuard's deny-sign-in rights (Entra ID, domain).
& $exe --restore-account
if ($LASTEXITCODE -ne 0) { throw 'Restoring the child account failed. Rerun uninstall; see README "Diagnose and recover".' }
if ($service) {
    & sc.exe delete KidGuard | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service deletion failed.' }
}
Remove-Item -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KidGuard' -Recurse -Force -ErrorAction SilentlyContinue
$shortcutRecord = Join-Path $admin 'panel-shortcut.txt'
if (Test-Path -LiteralPath $shortcutRecord) {
    # Desktop and Start menu shortcuts, one per line. Only files with KidGuard's shortcut name are removed.
    foreach ($shortcut in @(Get-Content -LiteralPath $shortcutRecord | ForEach-Object { $_.Trim() } | Where-Object { $_ })) {
        if ($shortcut.EndsWith('KidGuard Parent Panel.lnk') -and (Test-Path -LiteralPath $shortcut)) { Remove-Item -LiteralPath $shortcut -Force }
    }
}
Write-Host "Protection removed and original proxy/account state restored. Child must sign out/in if still logged in."
Write-Host "Configuration and recovery files remain at $root. Archive/delete that folder before a new installation."
