<#
.SYNOPSIS
  READ-ONLY. Reports Secure Boot, Windows test-signing and which Touch Bar driver is active.
  Safe to run as a normal user at any time. Changes nothing.
.OUTPUTS
  A summary object; also prints a readable table. Use -Json for machine-readable output.
#>
[CmdletBinding()]
param([switch]$Json)

$ErrorActionPreference = 'Stop'

function Get-SecureBoot {
    try { if (Confirm-SecureBootUEFI) { 'On' } else { 'Off' } }
    catch { 'Unknown (needs admin or unsupported)' }
}

function Get-TestSigning {
    # SystemStartOptions holds the boot options Windows started with; readable without admin.
    $opts = (Get-ItemProperty 'HKLM:\SYSTEM\CurrentControlSet\Control' -Name SystemStartOptions -ErrorAction SilentlyContinue).SystemStartOptions
    if ($null -eq $opts) { return 'Unknown' }
    if ($opts -match 'TESTSIGNING') { 'On' } else { 'Off' }
}

# -PresentOnly: after a driver swap the old Apple device node lingers as a non-present "ghost".
$touchBar = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match '^USB\\VID_05AC&PID_8302\\' } | Select-Object -First 1

$driver = $null
if ($touchBar) {
    $driver = Get-CimInstance Win32_PnPSignedDriver -ErrorAction SilentlyContinue |
        Where-Object { $_.DeviceID -eq $touchBar.InstanceId } | Select-Object -First 1
}

$display = Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue |
    Where-Object { $_.InstanceId -match '^USB\\VID_05AC&PID_8302&MI_01\\' -and $_.FriendlyName -match 'Touch Bar Display|iBridge' } |
    Select-Object -First 1

$mode = if (-not $touchBar) { 'Not found' }
        elseif ($driver -and $driver.DriverProviderName -match 'Apple') { 'Stock Apple driver (F-keys mode)' }
        elseif ($driver -and $driver.DeviceName -match 'libusb0') { 'libusb0 setup (display mode, Microsoft-signed driver)' }
        elseif ($driver -and $driver.DeviceName -match 'WinUSB') { 'WinUSB setup (display mode, Windows drivers only)' }
        elseif ($driver -and $driver.DeviceName -match 'usbprint') { 'usbprint setup (display mode, Windows printer class driver)' }
        elseif ($driver -and $driver.DeviceName -match 'iBridge') { 'Experimental DFR driver (display mode)' }
        else { 'Unknown driver' }

# Which driver runs the display interface: WinUSB (Microsoft) or DFRDisplayKm (custom, needs test mode).
$displayService = $null
if ($display) {
    $displayService = (Get-PnpDeviceProperty -InstanceId $display.InstanceId -KeyName 'DEVPKEY_Device_Service' -ErrorAction SilentlyContinue).Data
}

$state = [ordered]@{
    Timestamp        = (Get-Date).ToString('s')
    SecureBoot       = Get-SecureBoot
    TestSigning      = Get-TestSigning
    TouchBarMode     = $mode
    TouchBarStatus   = if ($touchBar) { $touchBar.Status } else { $null }
    DriverName       = if ($driver) { $driver.DeviceName } else { $null }
    DriverProvider   = if ($driver) { $driver.DriverProviderName } else { $null }
    DriverVersion    = if ($driver) { $driver.DriverVersion } else { $null }
    DriverInf        = if ($driver) { $driver.InfName } else { $null }
    DisplayInterface = if ($display) { "$($display.FriendlyName) ($($display.Status))" } else { 'Not present' }
    DisplayDriver    = $displayService
}

if ($Json) { $state | ConvertTo-Json } else { [pscustomobject]$state | Format-List }
