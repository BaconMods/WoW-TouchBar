<#
.SYNOPSIS
  Builds and test-signs the DFRDisplayKm Touch Bar display driver from source. Changes NOTHING on the
  system except creating a local code-signing certificate in the current user's certificate store.
.DESCRIPTION
  1. Fetches github.com/imbushuo/DFRDisplayKm at a pinned commit into external/ (if missing).
  2. Builds the x64 Release driver with VS Build Tools 2022 + WDK 10.0.26100.
  3. Creates (once) a self-signed code-signing cert "CN=WoWTouchBar Local Test Signing" in Cert:\CurrentUser\My.
  4. Signs the .sys, regenerates the catalog, signs the .cat.
  Output: out/driver/ (DFRDisplayKm.inf, .sys, .cat, WoWTouchBarTestSigning.cer)
  Installing is a separate, explicit step (Install-DfrDriver.ps1).
#>
[CmdletBinding()]
[CmdletBinding()]
param(
    # Release packaging: build only, write an UNSIGNED .sys + Inf2Cat catalog to out\driver-unsigned (the MSI signs them
    # on the user's PC with a one-off certificate). No certificate is made here.
    [switch]$Unsigned
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

$PinnedCommit = '360a53fcf33e291d8047fd888d0ecc2b16d86e7b'
$CertSubject  = 'CN=WoWTouchBar Local Test Signing'
$src   = Join-Path $root 'external\DFRDisplayKm'
$out   = Join-Path $root 'out\driver'
$msb   = 'C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe'
$kits  = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0'
$signtool = Join-Path $kits 'x64\signtool.exe'
$inf2cat  = Join-Path $kits 'x86\Inf2Cat.exe'

foreach ($tool in $msb, $signtool, $inf2cat) {
    if (-not (Test-Path $tool)) { throw "Missing build tool: $tool (install VS Build Tools 2022 + WDK 10.0.26100)" }
}

# 1. Source at pinned commit
if (-not (Test-Path $src)) { git clone -q https://github.com/imbushuo/DFRDisplayKm.git $src }
git -C $src fetch -q origin 2>$null
git -C $src checkout -q $PinnedCommit
Write-Host "Source: DFRDisplayKm @ $($PinnedCommit.Substring(0,12))"

# The linker embeds the full path of the debug file (.pdb) in the driver, e.g. C:\Users\<name>\...: keep only the file name.
$env:LINK = '/PDBALTPATH:%_PDB%'
# 2. Build (signing + INF verifier disabled here; we sign ourselves below)
& $msb "$src\src\DFRDisplayKm\DFRDisplayKm.vcxproj" /t:Rebuild /p:Configuration=Release /p:Platform=x64 `
    "/p:SolutionDir=$src\" /p:SignMode=Off /p:SkipPackageVerification=true /m /nologo /v:minimal
if ($LASTEXITCODE -ne 0) { throw "Driver build failed ($LASTEXITCODE)" }

$pkg = Join-Path $src 'build\DFRDisplayKm\x64\Release\DFRDisplayKm'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$pkg\DFRDisplayKm.inf", "$pkg\DFRDisplayKm.sys" $out

if ($Unsigned) {
    $rel = Join-Path $root 'out\driver-unsigned'
    if (Test-Path $rel) { Remove-Item $rel -Recurse -Force }
    New-Item -ItemType Directory -Force $rel | Out-Null
    Copy-Item "$pkg\DFRDisplayKm.inf", "$pkg\DFRDisplayKm.sys" $rel
    & $inf2cat /driver:$rel /os:10_X64 /uselocaltime | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Inf2Cat failed' }
    Get-ChildItem $rel | Select-Object Name, Length | Format-Table -AutoSize
    Write-Host "Unsigned driver package ready in $rel (signed later by the installer)."
    return
}

# 3. Local test-signing certificate (current user only; private key never leaves this machine)
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $CertSubject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $CertSubject -CertStoreLocation Cert:\CurrentUser\My `
        -KeyUsage DigitalSignature -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(2)
    Write-Host "Created certificate $($cert.Thumbprint)"
}
Export-Certificate -Cert $cert -FilePath (Join-Path $out 'WoWTouchBarTestSigning.cer') | Out-Null

# 4. Sign driver, build catalog, sign catalog
& $signtool sign /fd sha256 /sha1 $cert.Thumbprint /s My "$out\DFRDisplayKm.sys"
if ($LASTEXITCODE -ne 0) { throw "Signing .sys failed" }
& $inf2cat /driver:$out /os:10_X64 /uselocaltime | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Inf2Cat failed" }
& $signtool sign /fd sha256 /sha1 $cert.Thumbprint /s My "$out\DFRDisplayKm.cat"
if ($LASTEXITCODE -ne 0) { throw "Signing .cat failed" }

Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize
Write-Host "Driver package ready in $out (not installed)."
