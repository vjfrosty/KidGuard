#requires -Version 5.1
#requires -RunAsAdministrator
[CmdletBinding(DefaultParameterSetName='Install')]
param(
    # The child's account: a local or Microsoft account name ("Viktor"), DOMAIN\name or AzureAD\name. Or use -ChildSid.
    [Parameter(ParameterSetName='Install')][string]$ChildUser,
    # The child's account by security ID (the setup wizard uses this).
    [Parameter(ParameterSetName='Install')][string]$ChildSid,
    # A folder left by a previous uninstall is renamed to KidGuard.previous-<timestamp> instead of blocking setup.
    [Parameter(ParameterSetName='Install')][switch]$ArchivePrevious,
    # With -ArchivePrevious: start from the previous whitelist.txt and schedule.json if they are valid.
    [Parameter(ParameterSetName='Install')][switch]$ReuseSettings,
    # Put the kids' start page (config\startpage.html) on the child's desktop and allow every site it links to.
    [Parameter(ParameterSetName='Install')][switch]$StartPage,
    # Language of the Parent Panel and of everything the child sees (en or bg). Default: the Windows display language.
    [Parameter(ParameterSetName='Install')][ValidateSet('en','bg')][string]$Language,
    # Print JSON describing the machine and every account's eligibility (used by the setup wizard). Changes nothing.
    [Parameter(Mandatory=$true,ParameterSetName='List')][switch]$ListCandidates
)
$ErrorActionPreference = 'Stop'
if (-not [Environment]::Is64BitProcess) { throw 'Use 64-bit Windows PowerShell.' }
$root = Join-Path ([Environment]::GetFolderPath('CommonApplicationData')) 'KidGuard'
$admin = Join-Path $root 'Admin'
$currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value

# Direct members of the local Administrators group. ADSI instead of Get-LocalGroupMember,
# which fails when the group contains Entra ID or orphaned members.
function Get-AdminMemberSids {
    $name = (New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')).Translate([Security.Principal.NTAccount]).Value.Split('\')[-1]
    $group = [ADSI]"WinNT://./$name,group"
    return @($group.psbase.Invoke('Members') | ForEach-Object {
        try { (New-Object Security.Principal.SecurityIdentifier($_.GetType().InvokeMember('objectSid','GetProperty',$null,$_,$null),0)).Value } catch { }
    })
}
# local / microsoft: local Windows accounts (locked with the account-disabled flag).
# entra / domain: no local account object (locked with deny-sign-in rights).
function Get-Kind([string]$sid, $localUser) {
    if ($localUser) { if ("$($localUser.PrincipalSource)" -eq 'MicrosoftAccount') { return 'microsoft' } else { return 'local' } }
    if ($sid -like 'S-1-12-1-*') { return 'entra' }
    return 'domain'
}
function Get-KindNote([string]$kind) {
    switch ($kind) {
        'microsoft' { return 'Microsoft account. If it is in a Microsoft Family group, turn off Family screen time and web filters for it so they do not conflict with KidGuard.' }
        'entra' { return 'Work or school (Entra ID) account. Sign-in is blocked with the Windows deny-sign-in policy; organisation policies (Intune) may override it. Administrator rights through groups are checked when the child signs in.' }
        'domain' { return 'Domain account. Sign-in is blocked with the Windows deny-sign-in policy; domain Group Policy may override it. Administrator rights through groups are checked when the child signs in.' }
    }
    return $null
}
# Why this account cannot be the protected child account, or $null if it can.
function Get-ChildIssue($c) {
    $sid = $c.sid
    if ($sid.EndsWith('-500')) { return 'Built-in Administrator account.' }
    if ($sid -eq $currentSid) { return 'This is your own administrator account. The child needs a separate account.' }
    if ($adminSids -contains $sid) {
        if ($c.kind -in 'local','microsoft') { return 'This account is an administrator. Make it a Standard account first (Settings > Accounts > Other users > Change account type).' }
        return 'This account is a member of the Administrators group on this PC. Remove it from that group first.'
    }
    $p = $profiles | Where-Object SID -EQ $sid
    if (-not $p) { return 'This account has never signed in on this PC. Sign in to it once, then sign out.' }
    if ($p.Loaded) { return 'This account is signed in right now. Sign it out completely (switching user is not enough).' }
    return $null
}
# Every account that could be the child: local and Microsoft accounts, plus work/school and domain
# accounts that have signed in on this PC (they are known only through their profile).
function Get-Candidates {
    $list = @()
    foreach ($u in $localUsers) {
        $list += [pscustomobject]@{ name=$u.Name; fullName=[string]$u.FullName; sid=$u.SID.Value; kind=(Get-Kind $u.SID.Value $u); enabled=[bool]$u.Enabled; issue=$null; note=$null }
    }
    $known = @($list | ForEach-Object { $_.sid })
    foreach ($p in $profiles) {
        if ($p.Special -or $known -contains $p.SID -or $p.SID -notmatch '^S-1-(5-21|12-1)-') { continue }
        if ($machineDomain -and $p.SID.StartsWith($machineDomain + '-')) { continue }   # profile of a deleted local account
        $name = $null
        try { $name = (New-Object Security.Principal.SecurityIdentifier($p.SID)).Translate([Security.Principal.NTAccount]).Value } catch { }
        if (-not $name) { $name = Split-Path -Leaf $p.LocalPath }
        $list += [pscustomobject]@{ name=$name; fullName=''; sid=$p.SID; kind=(Get-Kind $p.SID $null); enabled=$true; issue=$null; note=$null }
    }
    foreach ($c in $list) { $c.issue = Get-ChildIssue $c; $c.note = Get-KindNote $c.kind }
    return $list
}
function Get-InstallState {
    $service = [bool](Get-Service KidGuard -ErrorAction SilentlyContinue)
    $task = [bool](Get-ScheduledTask -TaskName 'KidGuard-Watchdog' -ErrorAction SilentlyContinue)
    if ($service -or $task) { return 'installed' }
    if (Test-Path -LiteralPath $root) { return 'leftover' }
    return 'none'
}
# Smart App Control: 0 off, 1 on, 2 evaluation (Windows may switch it on by itself later).
function Get-SmartAppControlState {
    try { return [int](Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control\CI\Policy' -ErrorAction Stop).VerifiedAndReputablePolicyState } catch { return 0 }
}
# True when the built programs carry a valid signature from a publisher Windows trusts (see Build.ps1 signing).
function Test-TrustedBuild {
    $exe = Join-Path $PSScriptRoot 'build\KidGuard.exe'
    return (Test-Path -LiteralPath $exe) -and (Get-AuthenticodeSignature -LiteralPath $exe).Status -eq 'Valid'
}
function Get-MachineIssues {
    $issues = @()
    if ((Get-Service BFE).Status -ne 'Running') { $issues += 'The Windows Base Filtering Engine (BFE) service is not running.' }
    if (Get-NetTCPConnection -LocalPort 8877 -State Listen -ErrorAction SilentlyContinue) { $issues += 'TCP port 8877 is already used by another program.' }
    if ((Get-SmartAppControlState) -eq 1 -and -not (Test-TrustedBuild)) {
        $issues += 'Smart App Control is on. It blocks programs that are not signed by a publisher Windows trusts, including KidGuard built on this PC. ' +
            'Windows would then stop KidGuard at random, for example after a restart, and the child account would no longer be locked. See "Smart App Control" in README.md for the options.'
    }
    return ,$issues
}
function Get-MachineWarnings {
    $warnings = @()
    if ((Get-SmartAppControlState) -eq 2 -and -not (Test-TrustedBuild)) {
        $warnings += 'Smart App Control is in evaluation mode. If Windows switches it on later, it will block KidGuard and the child account will no longer be locked. ' +
            'Check the Parent Panel regularly, or turn Smart App Control off (see README.md).'
    }
    return ,$warnings
}

# Guest, DefaultAccount and WDAGUtilityAccount are Windows system accounts, not people.
$localUsers = @(Get-LocalUser | Where-Object { $_.SID.Value -notmatch '-(501|503|504)$' })
$machineDomain = if ($localUsers.Count -gt 0) { $localUsers[0].SID.AccountDomainSid.Value } else { $null }
$profiles = @(Get-CimInstance Win32_UserProfile)
$adminSids = Get-AdminMemberSids

if ($ListCandidates) {
    $accounts = @(Get-Candidates | ForEach-Object {
        # Disabled local accounts are usually Windows/system accounts; -ChildUser still accepts one deliberately.
        if (-not $_.issue -and -not $_.enabled) { $_.issue = 'This account is turned off (disabled). If it is your child''s account, enable it first.' }
        $_
    })
    [pscustomobject]@{ state=(Get-InstallState); root=$root; machineIssues=(Get-MachineIssues); machineWarnings=(Get-MachineWarnings); accounts=$accounts } | ConvertTo-Json -Depth 4 -Compress
    return
}

$state = Get-InstallState
if ($state -eq 'installed') { throw 'KidGuard is already installed. Uninstall it first (Apps & features, or C:\ProgramData\KidGuard\Uninstall.ps1).' }
if ($state -eq 'leftover' -and -not $ArchivePrevious) { throw "Existing folder $root from a previous installation. Run setup with -ArchivePrevious to move it aside; do not overwrite it." }
if ($ReuseSettings -and $state -ne 'leftover') { $ReuseSettings = $false }
if (-not $ChildUser -and -not $ChildSid) { throw 'Use -ChildUser <name> or -ChildSid <sid>. -ListCandidates shows the accounts on this PC.' }
if (-not $ChildSid) {
    $match = $localUsers | Where-Object Name -EQ $ChildUser
    if ($match) { $ChildSid = $match.SID.Value }
    else { $ChildSid = (New-Object Security.Principal.NTAccount($ChildUser)).Translate([Security.Principal.SecurityIdentifier]).Value }
}
$child = Get-Candidates | Where-Object sid -EQ $ChildSid
if (-not $child) { throw "No account with a Windows profile on this PC matches $ChildSid. Sign in to the child's account once, then sign out." }
if ($child.issue) { throw "$($child.name): $($child.issue)" }
$sid = $child.sid
if ($child.note) { Write-Host "Note: $($child.note)" }
foreach ($machineIssue in (Get-MachineIssues)) { throw $machineIssue }
foreach ($machineWarning in (Get-MachineWarnings)) { Write-Warning $machineWarning }
& (Join-Path $PSScriptRoot 'Build.ps1')
$sourceExe = Join-Path $PSScriptRoot 'build\KidGuard.exe'
# All checks passed: only now move a previous installation's folder aside (a rename, kept for reference).
$previous = $null
if ($state -eq 'leftover') {
    $previous = $root + '.previous-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    Move-Item -LiteralPath $root -Destination $previous
    Write-Host "Moved the previous KidGuard folder to $previous"
}
# Create a dedicated administrator-only directory; never reuse an existing directory/junction.
New-Item -ItemType Directory -Path $root | Out-Null
$acl = New-Object Security.AccessControl.DirectorySecurity
$acl.SetAccessRuleProtection($true,$false)
$adminSid = New-Object Security.Principal.SecurityIdentifier('S-1-5-32-544')
$systemSid = New-Object Security.Principal.SecurityIdentifier('S-1-5-18')
$acl.SetOwner($adminSid)
foreach ($identity in @($adminSid,$systemSid)) {
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($identity,'FullControl','ContainerInherit,ObjectInherit','None','Allow')
    $acl.AddAccessRule($rule)
}
Set-Acl -LiteralPath $root -AclObject $acl
New-Item -ItemType Directory -Path $admin | Out-Null
Copy-Item -LiteralPath $sourceExe -Destination (Join-Path $root 'KidGuard.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\KidGuardPanel.exe') -Destination (Join-Path $root 'KidGuardPanel.exe')
# The child's helper; the service copies it to a folder the child can read (C:\ProgramData\KidGuard-Child).
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\KidGuardTray.exe') -Destination (Join-Path $root 'KidGuardTray.exe')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\kidguard.ico') -Destination (Join-Path $root 'kidguard.ico')
foreach ($file in @('Uninstall.ps1','Uninstall.cmd','Registry.ps1','Manage.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination $root }
Copy-Item -Path (Join-Path $PSScriptRoot 'config\*') -Destination $admin
if ($ReuseSettings -and $previous) {
    foreach ($pair in @(@('schedule.json','--check-schedule'),@('whitelist.txt','--check-whitelist'))) {
        $old = Join-Path $previous ('Admin\' + $pair[0])
        if (-not (Test-Path -LiteralPath $old)) { continue }
        & $sourceExe $pair[1] $old | Out-Null
        if ($LASTEXITCODE -eq 0) { Copy-Item -LiteralPath $old -Destination $admin -Force; Write-Host "Reused previous $($pair[0])." }
        else { Write-Warning "Previous $($pair[0]) is invalid; using the default one instead." }
    }
}
if ($Language) {
    [IO.File]::WriteAllText((Join-Path $admin 'settings.json'), ('{"language":"' + $Language + '","hideChildHelper":false}'), (New-Object Text.UTF8Encoding $false))
}
# Deny-sign-in rights the account already has are recorded so uninstall never removes them.
$deniedBefore = @()
if ($child.kind -in 'entra','domain') {
    $deniedBefore = @(& $sourceExe --deny-rights $sid)
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the child account''s sign-in rights.' }
}
[pscustomobject]@{ userName=$child.name; userSid=$sid; originallyEnabled=[bool]$child.enabled; kind=$child.kind; deniedBefore=$deniedBefore } |
    ConvertTo-Json | ForEach-Object { [IO.File]::WriteAllText((Join-Path $admin 'installation.json'), $_, (New-Object Text.UTF8Encoding $false)) }
$exe = Join-Path $root 'KidGuard.exe'
try {
    & $exe --close-gate
    if ($LASTEXITCODE -ne 0) { throw 'Could not block the child account''s sign-in.' }
    . (Join-Path $root 'Registry.ps1')
    Set-ChildProxy -Sid $sid -Backup (Join-Path $admin 'registry-backup.clixml')
    New-Service -Name KidGuard -BinaryPathName ('"' + $exe + '"') -DisplayName 'KidGuard parental controls' -StartupType Automatic -DependsOn BFE -Description 'Local child-account web whitelist and scheduled sign-out.' | Out-Null
    & $exe --install-filter $sid
    if ($LASTEXITCODE -ne 0) { throw 'WFP installation failed.' }
    & sc.exe failure KidGuard reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service recovery configuration failed.' }
    & sc.exe failureflag KidGuard 1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Service failure flag configuration failed.' }
    $action = New-ScheduledTaskAction -Execute $exe -Argument '--guard'
    $repeat = New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
    $boot = New-ScheduledTaskTrigger -AtStartup
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -ExecutionTimeLimit (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName 'KidGuard-Watchdog' -Action $action -Trigger @($repeat,$boot) -Principal $principal -Settings $settings | Out-Null
    Start-Service KidGuard
    $healthy = $false
    for ($i=0; $i -lt 15; $i++) {
        if (Test-Path -LiteralPath (Join-Path $admin 'heartbeat.txt')) { $healthy=$true; break }
        Start-Sleep -Seconds 1
    }
    if (-not $healthy) { throw 'Service did not report a healthy enforcement cycle. Check Admin\kidguard.log.' }
    & $exe --status
    if ($LASTEXITCODE -ne 0) { throw 'WFP post-install verification failed.' }
    if ($StartPage) {
        & $exe --startpage-enable
        if ($LASTEXITCODE -ne 0) { Write-Warning 'The start page could not be turned on; use the Start page tab in the Parent Panel.' }
    }
    # Desktop and Start Menu shortcuts (with the shield) for the installing administrator only. Not fatal: protection is already active.
    try {
        $shortcuts = @(& $exe --panel-shortcuts)
        if ($LASTEXITCODE -ne 0) { throw 'KidGuard.exe --panel-shortcuts failed.' }
        Set-Content -LiteralPath (Join-Path $admin 'panel-shortcut.txt') -Value $shortcuts -Encoding UTF8
        Write-Host "Parent Panel: desktop and Start menu > KidGuard Parent Panel ($root\KidGuardPanel.exe)"
    } catch { Write-Warning "Could not create the Parent Panel shortcuts: $_. Start $root\KidGuardPanel.exe directly." }
    # Apps & features entry, so KidGuard can be removed like any other program. Not fatal.
    try {
        $arp = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\KidGuard'
        New-Item -Path $arp -Force | Out-Null
        $values = @{ DisplayName='KidGuard parental controls'; DisplayVersion=(& $exe --version); Publisher='KidGuard (built on this computer)';
            InstallLocation=$root; DisplayIcon=(Join-Path $root 'KidGuardPanel.exe'); UninstallString=('"' + (Join-Path $root 'Uninstall.cmd') + '"');
            Comments="Protects the Windows account $($child.Name)." }
        foreach ($name in $values.Keys) { Set-ItemProperty -Path $arp -Name $name -Value $values[$name] }
        Set-ItemProperty -Path $arp -Name NoModify -Value 1 -Type DWord
        Set-ItemProperty -Path $arp -Name NoRepair -Value 1 -Type DWord
    } catch { Write-Warning "Could not add KidGuard to Apps & features: $_" }
    Write-Host "Installed for $($child.Name) ($sid)."
    Write-Host "Whitelist: $admin\whitelist.txt"
    Write-Host "Schedule:  $admin\schedule.json"
    Write-Host 'Run the acceptance checklist in tests\WINDOWS-ACCEPTANCE.md before relying on protection.'
}
catch {
    $originalError = $_
    Write-Warning 'Installation failed. Attempting rollback.'
    try { & (Join-Path $root 'Uninstall.ps1') } catch { Write-Warning "Rollback incomplete: $_. Use the recovery instructions in README.md." }
    throw $originalError
}
