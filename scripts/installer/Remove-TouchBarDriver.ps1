<#
.SYNOPSIS
  Run by the WoW Touch Bar MSI (as SYSTEM) on uninstall, and with -Rollback when setup failed part-way.
  Uninstall: removes the Touch Bar display driver (Apple's own driver takes over again), the one-off certificate,
  and turns test mode off again unless it was already on before WoW Touch Bar was installed.
  Rollback: undoes only the steps setup actually got to (Setup-TouchBarDriver records them), so a driver or
  certificate that was there before setup started is left alone.
  Takes effect after a restart. Log: %windir%\Logs\WoW Touch Bar\driver-remove.log
#>
[CmdletBinding()]
param([switch]$Rollback)
$ErrorActionPreference = 'Continue'   # undo as much as possible, whatever fails
. (Join-Path $PSScriptRoot 'TouchBarDriverCommon.ps1')
Start-DriverLog 'driver-remove.log'
try {
    $stage = if ($Rollback) { Get-SetupStage } else { 3 }
    Write-Host ("{0}: undoing up to stage {1}" -f $(if ($Rollback) { 'Rollback' } else { 'Uninstall' }), $stage)
    if ($stage -ge 3) {
        Remove-DriverPackages
        & pnputil /scan-devices | Out-Null          # Apple's driver (installed by Boot Camp) takes the Touch Bar back
    }
    if ($stage -ge 2) {
        Remove-DriverCertificates
        $work = Join-Path $env:windir 'Logs\WoW Touch Bar\driver'                                                  # setup's signing copy
        if (Test-Path -LiteralPath $work) { & cmd.exe /d /c rmdir /s /q "$work" }
    }
    if ($stage -ge 1) {
        $wasOn = (Get-ItemProperty -Path $StateKey -Name TestSigningWasOn -ErrorAction SilentlyContinue).TestSigningWasOn
        if ($wasOn -eq 0) {
            & bcdedit /set testsigning off | Out-Null
            Write-Host 'Test mode turned off (after restart).'
        } else { Write-Host 'Test mode left as it was before installing.' }
    }
    Remove-ItemProperty -Path $StateKey -Name TestSigningWasOn, SetupStage -ErrorAction SilentlyContinue
    Write-Host 'Done.'
}
finally { Stop-DriverLog }
exit 0