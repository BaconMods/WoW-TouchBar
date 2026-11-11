<#
.SYNOPSIS
  Run by the WoW Touch Bar MSI (as SYSTEM, during install). Sets up the Touch Bar display driver on THIS PC.
.DESCRIPTION
  1. Turns Windows test mode on (remembers whether it was already on, so uninstall can put it back).
  2. Makes a one-off code-signing certificate, signs the driver (.sys) and its catalog (.cat) shipped unsigned in
     <Dir>\driver, then DELETES the certificate's private key - nothing else can ever be signed with it.
  3. Trusts that certificate on this PC only, removes any older copy of the driver, and installs the driver on the
     Touch Bar (USB\VID_05AC&PID_8302) and its display part (MI_01).
  Takes effect after the restart the installer asks for. Secure Boot must be off (the MSI checks before it starts).
  Log: %windir%\Logs\WoW Touch Bar\driver-setup.log
#>
[CmdletBinding()]
# -Dir: the install folder. Defaults to the folder above this script (the MSI passes nothing: a folder ending in "\"
# followed by a quote breaks Windows' command-line parsing - the first real install failed on exactly that).
param([string]$Dir)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'TouchBarDriverCommon.ps1')
Start-DriverLog 'driver-setup.log'
try {
    if (-not $Dir) { $Dir = Split-Path -Parent $PSScriptRoot }
    # This runs as SYSTEM, and so do the uninstall scripts and (for every user) the app: refuse a folder ordinary users
    # can change (only possible if an administrator picked an unusual install folder; Program Files is fine).
    foreach ($d in $Dir, (Join-Path $Dir 'scripts'), (Join-Path $Dir 'driver')) {
        if ((Test-Path -LiteralPath $d) -and (Test-WritableByUsers $d)) { throw "Ordinary users can change files in $d. Install WoW Touch Bar into a protected folder such as Program Files." }
    }
    $src = Join-Path $Dir 'driver'
    foreach ($f in 'DFRDisplayKm.inf', 'DFRDisplayKm.sys', 'dfrdisplaykm.cat') {
        if (-not (Test-Path (Join-Path $src $f))) { throw "Missing driver file: $f" }
    }
    # Sign and install from a copy in our locked folder (SYSTEM / Administrators only), so nothing can be swapped
    # between signing and installing.
    $drv = Join-Path (Initialize-StateFolder) 'driver'
    if (Test-Path -LiteralPath $drv) { & cmd.exe /d /c rmdir /s /q "$drv" }
    New-Item -ItemType Directory -Path $drv | Out-Null
    foreach ($f in 'DFRDisplayKm.inf', 'DFRDisplayKm.sys', 'dfrdisplaykm.cat') { Copy-Item -LiteralPath (Join-Path $src $f) -Destination $drv }
    $inf = Join-Path $drv 'DFRDisplayKm.inf'

    Set-SetupStage 0   # nothing done yet: a rollback from here changes nothing
    # 1. Test mode
    $wasOn = Test-TestSigning
    # Remember the state from BEFORE the first install (an upgrade must not record our own test mode as the user's).
    New-Item -Path $StateKey -Force | Out-Null
    if ($null -eq (Get-ItemProperty -Path $StateKey -Name TestSigningWasOn -ErrorAction SilentlyContinue)) {
        Set-ItemProperty -Path $StateKey -Name TestSigningWasOn -Value ([int]$wasOn)
    }
    Set-SetupStage 1   # from here a rollback restores test mode
    if (-not $wasOn) {
        & bcdedit /set testsigning on
        if ($LASTEXITCODE -ne 0) { throw 'Could not turn on Windows test mode. Is Secure Boot still on?' }
        Write-Host 'Test mode turned on (active after restart).'
    } else { Write-Host 'Test mode was already on.' }

    # 2. One-off certificate: sign, export the public part, delete the private key, trust on this PC only
    Set-SetupStage 2   # from here a rollback removes certificates
    Remove-DriverCertificates
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $CertSubject -CertStoreLocation Cert:\LocalMachine\My `
        -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(10)
    foreach ($f in 'DFRDisplayKm.sys', 'dfrdisplaykm.cat') {
        $r = Set-AuthenticodeSignature -FilePath (Join-Path $drv $f) -Certificate $cert -HashAlgorithm SHA256
        if (-not $r.SignerCertificate) { throw "Signing $f failed: $($r.StatusMessage)" }
    }
    # The public part, kept in memory only (never a file someone else could swap before it is trusted).
    $public = New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList (, $cert.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
    Remove-Item -Path $cert.PSPath -DeleteKey
    if (Get-ChildItem Cert:\LocalMachine\My | Where-Object Subject -eq $CertSubject) { throw 'Private key could not be deleted.' }
    foreach ($storeName in 'Root', 'TrustedPublisher') {
        $store = New-Object Security.Cryptography.X509Certificates.X509Store($storeName, 'LocalMachine')
        $store.Open('ReadWrite')
        try { $store.Add($public) } finally { $store.Close() }
    }
    Write-Host 'Driver signed with a one-off certificate; its private key is deleted.'

    # 3. Driver: replace any older copy, then install on the Touch Bar and its display part
    Set-SetupStage 3   # from here a rollback removes the driver
    Remove-DriverPackages
    & pnputil /add-driver $inf
    if ($LASTEXITCODE -ne 0) { throw "Adding the driver failed (pnputil $LASTEXITCODE)." }
    if (-not (Install-OnDevice 'USB\VID_05AC&PID_8302' $inf)) {
        throw "Installing on the Touch Bar failed (Win32 error $([Runtime.InteropServices.Marshal]::GetLastWin32Error())). Is this a MacBook Pro with Touch Bar?"
    }
    Start-Sleep -Seconds 3
    & pnputil /scan-devices | Out-Null
    Start-Sleep -Seconds 2
    if (Install-OnDevice 'USB\VID_05AC&PID_8302&MI_01' $inf) { Write-Host 'Driver installed on the display part.' }
    else { Write-Host 'Display part appears after the restart; Windows attaches the driver then.' }
    & cmd.exe /d /c rmdir /s /q "$drv"   # Windows keeps its own copy in the driver store
    Write-Host 'Done. Restart Windows to finish.'
    exit 0
}
catch {
    Write-Host "FAILED: $($_.Exception.Message)"
    exit 1
}
finally { Stop-DriverLog }
