#requires -Version 5.1
# End-to-end test of the KidGuard filter with real browsers, without installing anything.
# Runs the proxy from build\KidGuard.exe on a spare port with config\ rules, then opens pages in headless Chrome/Edge.
# Needs internet. Uses the whitelist from config\ with internet allowed all day. Run after Build.ps1.
[CmdletBinding()]
param(
    [int]$Port = 18878,
    # Instead of the fixed cases: open every link of config\startpage.html (Chrome only), with its sites allowed as "Turn on" would.
    [switch]$StartPage
)
$ErrorActionPreference = 'Stop'
$package = Split-Path $PSScriptRoot -Parent
[void][Reflection.Assembly]::LoadFrom((Join-Path $package 'build\KidGuard.exe'))
$work = Join-Path ([IO.Path]::GetTempPath()) ('KidGuardBrowserTest-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
[KidGuard.Log]::DirectoryPath = $work
$config = Join-Path $package 'config'
$whitelist = [IO.File]::ReadAllText((Join-Path $config 'whitelist.txt'))
$html = $null
if ($StartPage) {
    $html = [IO.File]::ReadAllText((Join-Path $config 'startpage.html'))
    $rules = [KidGuard.StartPage]::Rules([KidGuard.StartPage]::LinkHosts($html), [KidGuard.Bundle]::Parse([IO.File]::ReadAllText((Join-Path $config 'bundles.txt'))))
    $whitelist += "`n" + ($rules -join "`n")
}
# The filter is tested, not the clock: internet allowed all day (config\schedule.json may be outside its hours now).
$days = '["Monday","Tuesday","Wednesday","Thursday","Friday","Saturday","Sunday"]'
$allDay = '{"timeZone":"UTC","forceLock":false,"accountHours":[{"days":' + $days + ',"start":"00:00","end":"24:00"}],"internetHours":[{"days":' + $days + ',"start":"00:00","end":"24:00"}]}'
$policy = New-Object KidGuard.Policy ($allDay, $whitelist)
# Proxy threads call this delegate; bind it to a .NET getter (a PowerShell script block cannot run on those threads).
$holder = [Tuple]::Create($policy)
$get = [Delegate]::CreateDelegate([Func[KidGuard.Policy]], $holder, 'get_Item1')
$proxy = New-Object KidGuard.Proxy ($get, $Port)
$proxy.Start()
$browsers = @(
    @{ Name='Chrome'; Path="$env:ProgramFiles\Google\Chrome\Application\chrome.exe" },
    @{ Name='Edge'; Path="${env:ProgramFiles(x86)}\Microsoft\Edge\Application\msedge.exe" }
) | Where-Object { Test-Path $_.Path }
$cases = @(
    @{ Url='https://www.wikipedia.org/'; Expect=$true },
    @{ Url='https://en.wikipedia.org/wiki/Bulgaria'; Expect=$true },
    @{ Url='https://www.khanacademy.org/'; Expect=$true },
    @{ Url='https://example.com/'; Expect=$false }
)
if ($StartPage) {
    $browsers = @($browsers | Where-Object Name -EQ 'Chrome')
    $seen = @{}
    $cases = @([regex]::Matches($html, '<a\b[^>]*?\bhref\s*=\s*["''](https?://[^"'']+)["'']') | ForEach-Object { [Net.WebUtility]::HtmlDecode($_.Groups[1].Value) } |
        Where-Object { $h = ([Uri]$_).Host; if ($seen[$h]) { $false } else { $seen[$h] = $true; $true } } | ForEach-Object { @{ Url=$_; Expect=$true } })
}
$failed = 0
try {
    foreach ($b in $browsers) {
        $version = (Get-Item $b.Path).VersionInfo.ProductVersion
        foreach ($c in $cases) {
            $profile = Join-Path $work ('profile-' + [Guid]::NewGuid().ToString('N'))
            $out = Join-Path $work 'out.html'; $err = Join-Path $work 'err.txt'
            $p = Start-Process $b.Path -PassThru -WindowStyle Hidden -RedirectStandardOutput $out -RedirectStandardError $err -ArgumentList @(
                '--headless=new','--disable-gpu','--no-first-run','--no-default-browser-check',"--user-data-dir=$profile",
                "--proxy-server=127.0.0.1:$Port",'--disable-quic','--timeout=20000','--dump-dom',$c.Url)
            if (-not $p.WaitForExit(60000)) { $p.Kill() }
            $html = "$(Get-Content $out -Raw -ErrorAction SilentlyContinue)"
            $loadError = [regex]::Match("$(Get-Content $err -Raw -ErrorAction SilentlyContinue)", 'net::ERR_[A-Z_]+').Value
            # KidGuard blocks show up as network errors (tunnel refused, connection closed, certificate). A short page without
            # an error means the site itself answered (some refuse automated browsers, e.g. HTTP 403); that is not a KidGuard block.
            $opened = $html.Length -gt 0 -and -not $loadError
            $ok = $opened -eq $c.Expect
            if (-not $ok) { $failed++ }
            '{0,-4} {1,-7} {2,-62} {3}' -f $(if ($ok) { 'PASS' } else { 'FAIL' }), $b.Name, $c.Url, $(if ($opened) { if ($html.Length -lt 500) { "passed KidGuard; the site answered with a short page ($($html.Length) chars, e.g. bot protection)" } else { "opened ($($html.Length) chars)" } } else { "blocked $loadError" })
            Remove-Item $profile -Recurse -Force -ErrorAction SilentlyContinue
        }
        "        ($($b.Name) $version)"
    }
} finally {
    $proxy.Dispose()
}
$log = Join-Path $work 'kidguard.log'
if (Test-Path $log) { 'Proxy log (first lines):'; Get-Content $log | Select-Object -First 12 | ForEach-Object { '  ' + $_ } }
Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
if ($failed) { throw "$failed browser check(s) failed." }
'All browser checks passed.'
