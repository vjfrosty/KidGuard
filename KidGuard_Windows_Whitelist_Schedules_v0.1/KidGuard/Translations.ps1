# Dot-sourced by Build.ps1. Lists English texts in the sources that lang\bg.txt does not translate yet.
# A missing translation is not an error: that text is simply shown in English.
function Get-KidGuardTextKeys([string]$root) {
    $keys = New-Object System.Collections.Generic.List[string]
    foreach ($dir in 'src','panel','setup','tray') {
        foreach ($file in Get-ChildItem -LiteralPath (Join-Path $root $dir) -Filter '*.cs' | Where-Object Name -NE 'SelfTests.cs') {
            $text = [IO.File]::ReadAllText($file.FullName)
            # L.T("...") and Parent's Done("...") calls with a literal first argument.
            foreach ($m in [regex]::Matches($text, '(?:L\.T|Done)\(\s*"((?:[^"\\]|\\.)*)"')) {
                $key = $m.Groups[1].Value -replace '\\n', "`n" -replace '\\"', '"' -replace '\\\\', '\'
                if (-not $keys.Contains($key)) { $keys.Add($key) }
            }
        }
    }
    return $keys
}
function Get-KidGuardTranslations([string]$file) {
    $table = @{}
    $en = $null
    foreach ($line in [IO.File]::ReadAllLines($file, [Text.Encoding]::UTF8)) {
        if ($line.StartsWith('en: ')) { $en = $line.Substring(4).Replace('\n', "`n") }
        elseif ($line.StartsWith('bg: ') -and $null -ne $en) { $table[$en] = $line.Substring(4).Replace('\n', "`n"); $en = $null }
    }
    return $table
}
function Test-KidGuardTranslations([string]$root) {
    $table = Get-KidGuardTranslations (Join-Path $root 'lang\bg.txt')
    $missing = @(Get-KidGuardTextKeys $root | Where-Object { -not $table.ContainsKey($_) })
    if ($missing.Count -gt 0) {
        Write-Warning "$($missing.Count) text(s) have no Bulgarian translation in lang\bg.txt and are shown in English:"
        $missing | ForEach-Object { Write-Warning ('  ' + $_.Replace("`n", '\n')) }
    }
    return $missing.Count
}
