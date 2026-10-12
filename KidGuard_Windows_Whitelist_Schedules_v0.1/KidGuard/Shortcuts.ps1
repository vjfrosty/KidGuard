# Dot-sourced by Build.ps1. Windows shortcuts with the KidGuard shield.
function New-KidGuardShortcut([string]$Path, [string]$Target, [string]$Icon, [string]$Description, [string]$WorkingDirectory) {
    $link = (New-Object -ComObject WScript.Shell).CreateShortcut($Path)
    $link.TargetPath = $Target
    $link.WorkingDirectory = $WorkingDirectory
    $link.Description = $Description
    if ($Icon -and (Test-Path -LiteralPath $Icon)) { $link.IconLocation = "$Icon,0" }
    $link.Save()
}
# A .cmd file cannot carry its own icon, so "Install KidGuard" and "Update KidGuard" shortcuts with the shield
# sit next to Setup.cmd and Update.cmd. They hold full paths, so every build writes them again.
function Update-KidGuardLaunchers([string]$Package) {
    $icon = Join-Path $Package 'assets\kidguard.ico'
    New-KidGuardShortcut (Join-Path $Package 'Install KidGuard.lnk') (Join-Path $Package 'Setup.cmd') $icon 'Install KidGuard on this computer' $Package
    New-KidGuardShortcut (Join-Path $Package 'Update KidGuard.lnk') (Join-Path $Package 'Update.cmd') $icon 'Update the installed KidGuard to this version' $Package
}

