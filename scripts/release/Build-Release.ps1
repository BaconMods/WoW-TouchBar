<#
.SYNOPSIS
  Builds the release zips from the current checkout into dist\ (no network, no signing, nothing installed).
.DESCRIPTION
  dist\WoWTouchBar-<v>-win-x64.zip        WoWTouchBar.exe (self-contained: no .NET install needed),
                                           Install-/Uninstall-Companion.ps1, user guides, scripts\touchbar\ (driver build / install / undo)
  dist\SHA256SUMS.txt                      checksums of both zips
  The Touch Bar driver itself is not included (it must be built locally - see docs\11_TOUCHBAR_DRIVER_GUIDE.md).
.PARAMETER Output  Output folder (default: dist).
#>
[CmdletBinding()]
param([string]$Output)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not $Output) { $Output = Join-Path $root 'dist' }   # PS 5.1: $PSScriptRoot is empty in param defaults
$version = (Get-Content (Join-Path $root 'VERSION') -Raw).Trim()
# Zip entries must use '/' (Compress-Archive on PowerShell 5.1 writes '\', which other unzip tools misread).
Add-Type -AssemblyName System.IO.Compression.FileSystem
function Zip([string]$from, [string]$to) {
    # Entries written one by one with '/' separators (PowerShell 5.1's .NET would otherwise store '\').
    Add-Type -AssemblyName System.IO.Compression
    $base = (Resolve-Path $from).Path.TrimEnd('\') + '\'
    $zip = [IO.Compression.ZipFile]::Open($to, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($f in Get-ChildItem $from -Recurse -File) {
            $name = $f.FullName.Substring($base.Length).Replace('\', '/')
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.FullName, $name, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
    } finally { $zip.Dispose() }
}

if ($version -notmatch '^\d+\.\d+\.\d+$') { throw "VERSION must be x.y.z, got '$version'" }

$ReleaseDriverScripts = 'Build-DfrDriver.ps1', 'Enable-TestSigning.ps1', 'Install-DfrDriver.ps1', 'Uninstall-DfrDriver.ps1', 'Get-TouchBarState.ps1'

. (Join-Path $PSScriptRoot 'ReleaseChecks.ps1')   # Assert-NoNames

# The zips are for users: no development docs, links or history in any shipped .md file.
function Assert-PublicText([string]$dir) {
    $dev = '0\d_[A-Z_]+\.md|1\d_[A-Z_]+\.md|02_phases|FEASIBILITY|PHASE_\d|research_notes|CLAUDE\.md'
    $hits = Get-ChildItem $dir -Recurse -File -Include *.md | Where-Object { (Get-Content $_.FullName -Raw) -match $dev }
    if ($hits) { throw "Development references in release docs: $($hits.FullName -join ', ')" }
}

$stage = Join-Path $Output 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force -Confirm:$false }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

# ---- what the MSI installs, staged in one folder ---------------------------------------------------------
$msiStage = Join-Path $stage 'msi'
New-Item -ItemType Directory -Force -Path $msiStage | Out-Null

# App: one self-contained exe (no .NET install needed)
$publish = Join-Path $stage 'publish'
& dotnet publish (Join-Path $root 'companion\App\WoWTouchBar.App.csproj') -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Deterministic=true "-p:PathMap=$root=." -o $publish --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
$exe = Join-Path $publish 'WoWTouchBar.exe'
if (-not (Test-Path $exe)) { throw "publish produced no WoWTouchBar.exe" }
$fileVersion = (Get-Item $exe).VersionInfo.ProductVersion
if (-not $fileVersion.StartsWith($version)) { throw "exe version $fileVersion != $version" }
Copy-Item $exe $msiStage

# Addon (copied into WoW by the installer)
$addonDir = New-Item -ItemType Directory -Force -Path (Join-Path $msiStage 'addon')
Copy-Item (Join-Path $root 'addon\WoWTouchBar') $addonDir -Recurse
foreach ($toc in Get-ChildItem (Join-Path $addonDir 'WoWTouchBar') -Filter *.toc) {
    if ((Get-Content $toc.FullName -Raw) -notmatch "## Version: $([regex]::Escape($version))") { throw "$($toc.Name) is not version $version" }
}

# Touch Bar driver: built from source but UNSIGNED; the installer signs it on the user's PC (one-off certificate).
$unsigned = Join-Path $root 'out\driver-unsigned'
foreach ($f in 'DFRDisplayKm.inf', 'DFRDisplayKm.sys', 'dfrdisplaykm.cat') {
    if (-not (Test-Path (Join-Path $unsigned $f))) { throw "Missing $f - run scripts\touchbar\Build-DfrDriver.ps1 -Unsigned first (needs VS Build Tools + WDK)." }
}
$drvDir = New-Item -ItemType Directory -Force -Path (Join-Path $msiStage 'driver')
Copy-Item (Join-Path $unsigned '*') $drvDir
if ((Get-AuthenticodeSignature (Join-Path $drvDir 'DFRDisplayKm.sys')).SignerCertificate) { throw 'Driver in out\driver-unsigned is signed; rebuild with -Unsigned.' }

# Scripts the installer runs (as SYSTEM): driver setup / removal, addon copy; plus the state check for troubleshooting
$scr = New-Item -ItemType Directory -Force -Path (Join-Path $msiStage 'scripts')
foreach ($s in 'Setup-TouchBarDriver.ps1', 'Remove-TouchBarDriver.ps1', 'TouchBarDriverCommon.ps1', 'Remove-OldUserInstall.ps1') { Copy-Item (Join-Path $root "scripts\installer\$s") $scr }
Copy-Item (Join-Path $root 'scripts\install\Install-Addon.ps1'), (Join-Path $root 'scripts\touchbar\Get-TouchBarState.ps1') $scr

# Docs: ONLY the public user docs in public\ (never the development docs).
# In the private repo the user docs are in public\; in the public repo they ARE the README and docs\.
$pub = if (Test-Path (Join-Path $root 'public\README.md')) { Join-Path $root 'public' } else { $root }
Copy-Item (Join-Path $pub 'README.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'THIRD_PARTY_NOTICES.txt') $msiStage   # MIT: the driver's notice must ship with it
Copy-Item (Join-Path $pub 'docs') $msiStage -Recurse
Assert-PublicText $msiStage
Assert-NoNames $msiStage

# Licence as RTF for the wizard's licence page: ours, then the third-party notices (the driver's MIT notice)
$lic = ((Get-Content (Join-Path $root 'LICENSE') -Raw) + "`n`n" + (Get-Content (Join-Path $root 'THIRD_PARTY_NOTICES.txt') -Raw)) -replace '\\', '\\' -replace '\{', '\{' -replace '\}', '\}'
$rtf = '{\rtf1\ansi\deff0{\fonttbl{\f0 Segoe UI;}}\fs18 ' + (($lic -split "`r?`n") -join '\par ') + '}'
Set-Content -Path (Join-Path $msiStage 'LICENSE.rtf') -Value $rtf -Encoding ascii

# ---- the MSI -------------------------------------------------------------------------------------------
& dotnet build (Join-Path $root 'installer\WoWTouchBar.Installer.wixproj') -c Release --nologo -v q `
    "-p:StageDir=$msiStage" "-p:ProductVersion=$version" "-o:$(Join-Path $stage 'msiout')"
if ($LASTEXITCODE -ne 0) { throw "MSI build failed" }
$msi = Join-Path $Output "WoWTouchBar-$version.msi"
if (Test-Path $msi) { Remove-Item $msi -Force -Confirm:$false }
Copy-Item (Get-ChildItem (Join-Path $stage 'msiout') -Recurse -Filter *.msi | Select-Object -First 1).FullName $msi
Assert-NoNames $msi

# ---- checksum -------------------------------------------------------------------------------------------
"{0}  {1}" -f (Get-FileHash $msi -Algorithm SHA256).Hash.ToLowerInvariant(), (Split-Path $msi -Leaf) |
    Set-Content -Path (Join-Path $Output 'SHA256SUMS.txt') -Encoding ascii
Remove-Item $stage -Recurse -Force -Confirm:$false

Write-Host "Release $version"
Get-ChildItem $Output -File | ForEach-Object { "  {0,-40} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }