<#
.SYNOPSIS
  Run by the WoW Touch Bar MSI as the installing user. Removes an older per-user copy of the app (installed by the
  earlier PowerShell installer into %LOCALAPPDATA%\Programs\WoWTouchBar): its start-with-Windows entry, Start menu
  shortcut and folder. Otherwise both copies start at sign-in. Settings and logs are kept (the new app uses them).
  Only touches things that point into that one folder.
#>
[CmdletBinding()]
param()
$ErrorActionPreference = 'Continue'
$old = Join-Path $env:LOCALAPPDATA 'Programs\WoWTouchBar'
$run = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'

$value = (Get-ItemProperty -Path $run -Name WoWTouchBar -ErrorAction SilentlyContinue).WoWTouchBar
if ($value -and $value -like "*$old*") {
    Remove-ItemProperty -Path $run -Name WoWTouchBar
    Write-Host "Removed the old start-with-Windows entry ($value)."
}

$lnk = Join-Path ([Environment]::GetFolderPath('Programs')) 'WoW Touch Bar.lnk'   # per-user Start menu, not the all-users one
if (Test-Path $lnk) {
    $target = (New-Object -ComObject WScript.Shell).CreateShortcut($lnk).TargetPath
    if ($target -like "$old*") { Remove-Item $lnk -Force; Write-Host 'Removed the old Start menu shortcut.' }
}

if (Test-Path $old) {
    Get-Process WoWTouchBar -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$old*" } | Stop-Process -Force -ErrorAction SilentlyContinue
    Remove-Item $old -Recurse -Force -ErrorAction SilentlyContinue
    Write-Host "Removed the old per-user copy ($old)."
}
exit 0
