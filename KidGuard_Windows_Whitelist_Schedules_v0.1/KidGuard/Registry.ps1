# Dot-sourced by installer/uninstaller only. Writes only selected values in the child's hive.
function Use-ChildHive {
    param([string]$Sid, [scriptblock]$Action)
    $mount = $Sid
    $loadedHere = $false
    $probe = [Microsoft.Win32.Registry]::Users.OpenSubKey($Sid)
    if ($null -ne $probe) { $probe.Dispose() }
    else {
        $childProfile = Get-CimInstance Win32_UserProfile | Where-Object SID -EQ $Sid
        if (-not $childProfile -or -not (Test-Path -LiteralPath (Join-Path $childProfile.LocalPath 'NTUSER.DAT'))) {
            throw 'Sign in to the child account once to create its profile, then sign out and retry.'
        }
        $mount = 'KidGuard_' + [Guid]::NewGuid().ToString('N')
        & reg.exe load "HKU\$mount" (Join-Path $childProfile.LocalPath 'NTUSER.DAT') | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Unable to load the child registry hive.' }
        $loadedHere = $true
    }
    $hive = $null
    try {
        $hive = [Microsoft.Win32.Registry]::Users.OpenSubKey($mount, $true)
        if ($null -eq $hive) { throw 'Unable to open child hive.' }
        & $Action $hive
    }
    finally {
        if ($null -ne $hive) { $hive.Dispose() }
        if ($loadedHere) {
            [GC]::Collect(); [GC]::WaitForPendingFinalizers()
            & reg.exe unload "HKU\$mount" | Out-Null
            if ($LASTEXITCODE -ne 0) { Write-Warning "Registry hive HKU\$mount could not be unloaded. Reboot before the child signs in." }
        }
    }
}
function Set-ChildProxy {
    param([string]$Sid, [string]$Backup)
    $entries = [System.Collections.Generic.List[object]]::new()
    function Entry($Path,$Name,$Value,$Kind) { $entries.Add([pscustomobject]@{Path=$Path;Name=$Name;Value=$Value;Kind=$Kind}) }
    $inet = 'Software\Microsoft\Windows\CurrentVersion\Internet Settings'
    Entry $inet 'ProxyEnable' 1 'DWord'
    Entry $inet 'ProxyServer' '127.0.0.1:8877' 'String'
    Entry $inet 'ProxyOverride' '' 'String'
    Entry $inet 'AutoConfigURL' '' 'String'
    Entry $inet 'AutoDetect' 0 'DWord'
    foreach ($browser in @('Google\Chrome','Microsoft\Edge')) {
        $path = "Software\Policies\$browser"
        Entry $path 'ProxyMode' 'fixed_servers' 'String'
        Entry $path 'ProxyServer' '127.0.0.1:8877' 'String'
        Entry $path 'ProxyBypassList' '<-loopback>' 'String'
        Entry $path 'DnsOverHttpsMode' 'off' 'String'
        Entry $path 'QuicAllowed' 0 'DWord'
        Entry $path 'EncryptedClientHelloEnabled' 0 'DWord'
    }
    $ff = 'Software\Policies\Mozilla\Firefox'
    Entry "$ff\Proxy" 'Mode' 'manual' 'String'
    Entry "$ff\Proxy" 'HTTPProxy' '127.0.0.1:8877' 'String'
    Entry "$ff\Proxy" 'SSLProxy' '127.0.0.1:8877' 'String'
    Entry "$ff\Proxy" 'UseHTTPProxyForAllProtocols' 1 'DWord'
    Entry "$ff\Proxy" 'Passthrough' '' 'String'
    Entry "$ff\Proxy" 'Locked' 1 'DWord'
    Entry $ff 'DisableEncryptedClientHello' 1 'DWord'
    Entry "$ff\DNSOverHTTPS" 'Enabled' 0 'DWord'
    Entry "$ff\DNSOverHTTPS" 'Locked' 1 'DWord'
    foreach ($pref in @('network.dns.echconfig.enabled','network.dns.http3_echconfig.enabled','network.http.http3.enable','security.tls.ech.grease_probability')) {
        Entry "$ff\Preferences\$pref" 'Value' 0 'DWord'
        Entry "$ff\Preferences\$pref" 'Status' 'locked' 'String'
    }
    Use-ChildHive $Sid {
        param($hive)
        # Persist the complete original value set BEFORE the first mutation.
        $old = foreach ($entry in $entries) {
            $key = $hive.OpenSubKey($entry.Path)
            try {
                $exists = $null -ne $key -and $key.GetValueNames() -contains $entry.Name
                [pscustomobject]@{Path=$entry.Path;Name=$entry.Name;Exists=$exists;
                    Value=$(if($exists){$key.GetValue($entry.Name,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)}else{$null});
                    Kind=$(if($exists){$key.GetValueKind($entry.Name).ToString()}else{'String'})}
            } finally { if($null -ne $key){$key.Dispose()} }
        }
        $old | Export-Clixml -LiteralPath $Backup
        foreach ($entry in $entries) {
            $key = $hive.CreateSubKey($entry.Path)
            try { $key.SetValue($entry.Name,$entry.Value,([Microsoft.Win32.RegistryValueKind]$entry.Kind)) }
            finally { $key.Dispose() }
        }
    }
}
function Restore-ChildProxy {
    param([string]$Sid,[string]$Backup)
    if (-not (Test-Path -LiteralPath $Backup)) { return }
    $entries = Import-Clixml -LiteralPath $Backup
    Use-ChildHive $Sid {
        param($hive)
        foreach ($entry in $entries) {
            $key = $hive.CreateSubKey($entry.Path)
            try {
                if ($entry.Exists) { $key.SetValue($entry.Name,$entry.Value,([Microsoft.Win32.RegistryValueKind]$entry.Kind)) }
                else { $key.DeleteValue($entry.Name,$false) }
            } finally { $key.Dispose() }
        }
    }
}
