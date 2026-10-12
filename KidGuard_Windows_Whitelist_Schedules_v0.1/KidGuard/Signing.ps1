# Dot-sourced by Build.ps1 and Setup.ps1. Optional Authenticode signing of the programs built on this PC.
# Set KIDGUARD_SIGN_THUMBPRINT to the thumbprint of a code-signing certificate in CurrentUser\My or LocalMachine\My.
# Only a certificate from a publisher Windows trusts satisfies Smart App Control; a self-signed one does not.
function Set-KidGuardSignature([string]$Path) {
    $thumbprint = $env:KIDGUARD_SIGN_THUMBPRINT
    if (-not $thumbprint) { return }
    $cert = @(Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -CodeSigningCert | Where-Object Thumbprint -EQ $thumbprint) | Select-Object -First 1
    if (-not $cert) { throw "Code-signing certificate $thumbprint not found in CurrentUser\My or LocalMachine\My." }
    $result = Set-AuthenticodeSignature -LiteralPath $Path -Certificate $cert -HashAlgorithm SHA256 -TimestampServer 'http://timestamp.digicert.com'
    if ($result.Status -ne 'Valid') { throw "Signing $Path failed: $($result.StatusMessage)" }
    Write-Host "Signed: $Path"
}
# Runs a freshly built program; turns an application-control block into an explanation.
function Invoke-BuiltProgram([string]$Path, [string[]]$Arguments) {
    try { & $Path @Arguments }
    catch [System.Management.Automation.ApplicationFailedException] {
        throw "Windows blocked $([IO.Path]::GetFileName($Path)) ($((($_.Exception.Message -split 'At ')[0]).Trim())). Smart App Control or another application-control policy " +
            'does not allow unsigned programs built on this PC. See "Smart App Control" in README.md. No system changes were made.'
    }
}
