# Shared by Setup-TouchBarDriver.ps1 and Remove-TouchBarDriver.ps1 (dot-sourced). Run as SYSTEM by the MSI.

$CertSubject = 'CN=WoW Touch Bar driver (this PC only)'
$OldCertSubjects = @('CN=WoWTouchBar Local Test Signing')   # left by the manual (script) install, if any
$StateKey = 'HKLM:\SOFTWARE\WoW Touch Bar'

# How far setup got (1 test mode, 2 certificates, 3 driver), so a rollback undoes exactly that and nothing it didn't do.
function Set-SetupStage([int]$n) {
    New-Item -Path $StateKey -Force | Out-Null
    Set-ItemProperty -Path $StateKey -Name SetupStage -Value $n
}
function Get-SetupStage { [int](Get-ItemProperty -Path $StateKey -Name SetupStage -ErrorAction SilentlyContinue).SetupStage }

# Accounts trusted to own / change the install folder: Windows itself and the Administrators group only. Not the person
# running setup: their everyday (non-elevated) programs run as the same account. SIDs, so any Windows language works.
$TrustedSids = @('S-1-5-18', 'S-1-5-32-544', 'S-1-5-80-956008885-3418522649-1831038044-1853292631-2271478464')  # SYSTEM, Administrators, TrustedInstaller

function Get-OwnerSid([string]$path) {
    try { (Get-Acl -LiteralPath $path).GetOwner([Security.Principal.SecurityIdentifier]).Value } catch { '' }
}

# True if anyone other than the trusted accounts could change $path or swap it out: they own it, have a right to change
# it, or own / can delete-in / re-permission a folder above it. (An allow-list: unusual accounts count as unsafe too.)
function Test-WritableByUsers([string]$path) {
    $change = [Security.AccessControl.FileSystemRights]'WriteData, AppendData, WriteExtendedAttributes, WriteAttributes, Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership'
    $swap = [Security.AccessControl.FileSystemRights]'Delete, DeleteSubdirectoriesAndFiles, ChangePermissions, TakeOwnership'
    $current = (Resolve-Path -LiteralPath $path).ProviderPath
    $rights = $change
    while ($current) {
        # A link anywhere on the way would make the checks above it look at the wrong folders: refuse.
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { return $true }
        if ($TrustedSids -notcontains (Get-OwnerSid $current)) { return $true }
        foreach ($rule in (Get-Acl -LiteralPath $current).GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier])) {
            if ($rule.AccessControlType -ne 'Allow' -or ($rule.PropagationFlags -band [Security.AccessControl.PropagationFlags]::InheritOnly)) { continue }
            $sid = $rule.IdentityReference.Value
            if ($TrustedSids -contains $sid -or $sid -eq 'S-1-3-0') { continue }   # CREATOR OWNER: the owner, already checked
            if ($rule.FileSystemRights -band $rights) { return $true }
        }
        $rights = $swap                                    # folders above: only what lets someone replace the one below
        $current = Split-Path -Parent $current
    }
    return $false
}

# Logs and the signing copy live in C:\Windows\Logs\WoW Touch Bar. Only Windows and administrators can create anything
# in C:\Windows\Logs (users may read), so - unlike a folder in ProgramData - nobody else can make or swap it first, and it
# inherits safe rights. A link there could only have been made by an administrator; refuse it anyway.
function Initialize-StateFolder {
    $dir = Join-Path $env:windir 'Logs\WoW Touch Bar'
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Path $dir | Out-Null }
    if ((Get-Item -LiteralPath $dir -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "$dir is a link - not using it." }
    return $dir
}
function Start-DriverLog([string]$name) {
    $dir = Initialize-StateFolder
    Start-Transcript -Path (Join-Path $dir $name) -Append | Out-Null
}
function Stop-DriverLog { try { Stop-Transcript | Out-Null } catch { } }

# Test mode as configured for the next boots (bcdedit), or as active right now.
function Test-TestSigning {
    $bcd = (& bcdedit /enum '{current}') -join "`n"
    if ($bcd -match '(?im)^\s*testsigning\s+Yes') { return $true }
    $opts = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control' -Name SystemStartOptions -ErrorAction SilentlyContinue).SystemStartOptions
    return [bool]($opts -match 'TESTSIGNING')
}

# Every copy of the DFRDisplayKm driver in Windows' driver store (oemNN.inf names).
function Get-DriverPackages {
    $blocks = ((& pnputil /enum-drivers) -join "`n") -split "(?m)^\s*$"
    foreach ($b in $blocks) {
        if ($b -match '(?im)^\s*Original Name:\s*dfrdisplaykm\.inf' -and $b -match '(?im)^\s*Published Name:\s*(oem\d+\.inf)') { $Matches[1] }
    }
}

function Remove-DriverPackages {
    foreach ($oem in Get-DriverPackages) {
        Write-Host "Removing driver package $oem"
        & pnputil /delete-driver $oem /uninstall /force | Out-Null
    }
}

# Our certificate (and the manual install's), from the trust stores and the machine's personal store.
function Remove-DriverCertificates {
    foreach ($store in 'My', 'Root', 'TrustedPublisher') {
        Get-ChildItem "Cert:\LocalMachine\$store" |
            Where-Object { $_.Subject -eq $CertSubject -or $OldCertSubjects -contains $_.Subject } |
            ForEach-Object { Remove-Item -Path $_.PSPath -DeleteKey:($store -eq 'My') }
    }
}

Add-Type -Namespace WTBSetup -Name NewDev -MemberDefinition @'
[System.Runtime.InteropServices.DllImport("newdev.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
public static extern bool UpdateDriverForPlugAndPlayDevicesW(System.IntPtr hwnd, string hardwareId, string infPath, uint flags, out bool rebootRequired);
'@

function Install-OnDevice([string]$hardwareId, [string]$inf) {
    $reboot = $false
    return [WTBSetup.NewDev]::UpdateDriverForPlugAndPlayDevicesW([IntPtr]::Zero, $hardwareId, $inf, 1, [ref]$reboot) # 1 = force
}
