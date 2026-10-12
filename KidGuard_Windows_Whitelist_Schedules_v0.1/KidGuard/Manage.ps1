#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding()]
param(
    [ValidateSet('Status','EditWhitelist','EditSchedule','Validate','LockNow','ResumeSchedule',
        'Bonus','PauseInternet','ResumeInternet','ClearOverrides','Allow','Disallow','Blocked','Dismiss','Requests','Answer')]
    [string]$Action='Status',
    # LockNow without -Minutes locks until ResumeSchedule. Bonus/PauseInternet require it.
    [ValidateRange(1,10080)][int]$Minutes,
    # Bonus only: also allow internet during the extra time.
    [switch]$IncludeInternet,
    # Allow/Disallow/Dismiss: a hostname, or *.hostname for all subdomains.
    [string]$Domain,
    # Answer: the request id shown by -Action Requests, and the reply (allow-site allows the whole site).
    [string]$Id,
    [ValidateSet('allow','allow-site','deny')][string]$Reply
)
$ErrorActionPreference='Stop'
$root=Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'KidGuard'
$admin=Join-Path $root 'Admin'
$exe=Join-Path $root 'KidGuard.exe'
function Invoke-KidGuard {
    & $exe @args
    if ($LASTEXITCODE -ne 0) { throw "KidGuard.exe $($args -join ' ') failed." }
}
function Assert-Domain { if (-not $Domain) { throw "Use -Domain with -Action $Action." } }
# Edit a copy, validate it, and only then replace the live file. A typo never reaches the service.
function Edit-Staged([string]$Name,[string]$Check,[string]$Apply) {
    $live=Join-Path $admin $Name
    $staged=Join-Path $admin ('staging-'+$Name)
    Copy-Item -LiteralPath $live -Destination $staged -Force
    try {
        while ($true) {
            Start-Process notepad.exe -ArgumentList ('"'+$staged+'"')
            Read-Host "Edit and SAVE the file in Notepad, then press Enter here to validate and apply (Ctrl+C cancels)" | Out-Null
            & $exe $Check $staged
            if ($LASTEXITCODE -eq 0) { Invoke-KidGuard $Apply $staged; Write-Host 'Applied. The service picks it up within about 5 seconds.'; return }
            $again=Read-Host 'Not applied; the live file is unchanged. Fix it in Notepad? (Y/N)'
            if ($again -notmatch '^[Yy]') { Write-Host 'Cancelled. The live file is unchanged.'; return }
        }
    } finally { Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue }
}
switch ($Action) {
    'EditWhitelist' { Edit-Staged 'whitelist.txt' '--check-whitelist' '--apply-whitelist' }
    'EditSchedule' { Edit-Staged 'schedule.json' '--check-schedule' '--apply-schedule' }
    'Validate' { Invoke-KidGuard --validate $admin }
    'LockNow' { if ($Minutes) { Invoke-KidGuard --lock $Minutes } else { Invoke-KidGuard --lock forever } }
    'ResumeSchedule' { Invoke-KidGuard --unlock; Write-Host 'ResumeSchedule still respects the allowed hours.' }
    'Bonus' {
        if (-not $Minutes) { throw 'Use -Minutes with -Action Bonus.' }
        if ($IncludeInternet) { Invoke-KidGuard --bonus $Minutes internet } else { Invoke-KidGuard --bonus $Minutes }
    }
    'PauseInternet' { if (-not $Minutes) { throw 'Use -Minutes with -Action PauseInternet.' }; Invoke-KidGuard --pause-internet $Minutes }
    'ResumeInternet' { Invoke-KidGuard --resume-internet }
    'ClearOverrides' { Invoke-KidGuard --clear-overrides }
    'Allow' { Assert-Domain; Invoke-KidGuard --allow $Domain }
    'Disallow' { Assert-Domain; Invoke-KidGuard --disallow $Domain }
    'Dismiss' { Assert-Domain; Invoke-KidGuard --dismiss $Domain }
    'Requests' {
        $list=@(& $exe --requests)
        if ($list.Count -eq 0) { Write-Host 'The child has not asked for anything.' }
        else { Write-Host 'Asked (UTC)           Id            State     Request'; $list }
        Write-Host 'Answer with: -Action Answer -Id <id> -Reply allow|allow-site|deny'
    }
    'Answer' { if (-not $Id -or -not $Reply) { throw 'Use -Id and -Reply with -Action Answer.' }; Invoke-KidGuard --answer $Id $Reply }
    'Blocked' {
        $list=@(& $exe --blocked)
        if ($list.Count -eq 0) { Write-Host 'No blocked domains recorded.' }
        else { Write-Host 'Last seen (UTC)        Count  Domain'; $list }
        Write-Host "Allow one with: -Action Allow -Domain <name>   (or *.<name> for all subdomains)"
    }
    'Status' {
        Get-Service KidGuard
        & $exe --status
        $statusPath=Join-Path $admin 'status.json'
        if (Test-Path -LiteralPath $statusPath) {
            $s=Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
            $age=[int]((Get-Date).ToUniversalTime()-[DateTime]::Parse($s.updated,[Globalization.CultureInfo]::InvariantCulture,'AdjustToUniversal')).TotalSeconds
            Write-Host ''
            Write-Host "Service status (updated ${age}s ago, version $($s.version))"
            if ($s.failClosedReason) { Write-Warning "FAIL CLOSED: $($s.failClosedReason)" }
            if ($s.configError) { Write-Warning "Config error, running on last valid copy: $($s.configError)" }
            [pscustomobject]@{
                AccountAllowed=$s.accountAllowed; InternetAllowed=$s.internetAllowed
                NextAccountChange=$s.nextAccountChange; NextInternetChange=$s.nextInternetChange
                ChildSessions=$s.childSessions; ScheduleForceLock=$s.scheduleForceLock
                LockUntil=$s.overrides.lockUntil; BonusUntil=$s.overrides.bonusUntil
                BonusInternet=$s.overrides.bonusInternet; InternetPausedUntil=$s.overrides.internetPausedUntil
            } | Format-List
        }
        & $exe --validate $admin
        $info=Get-Content -LiteralPath (Join-Path $admin 'installation.json') -Raw | ConvertFrom-Json
        if (-not $info.kind -or $info.kind -in 'local','microsoft') { Get-LocalUser -SID $info.userSid | Select-Object -Property @('Name','Enabled','SID','PrincipalSource') }
        else {
            $denied = @(& $exe --deny-rights $info.userSid)
            Write-Host "Child: $($info.userName) ($($info.kind) account, $($info.userSid)). Sign-in currently $(if ($denied.Count) { 'blocked: ' + ($denied -join ', ') } else { 'allowed' })."
        }
        Get-ScheduledTaskInfo -TaskName 'KidGuard-Watchdog' | Select-Object -Property @('LastRunTime','LastTaskResult','NextRunTime')
        Get-Content -LiteralPath (Join-Path $admin 'kidguard.log') -Tail 12 -ErrorAction SilentlyContinue
    }
}
