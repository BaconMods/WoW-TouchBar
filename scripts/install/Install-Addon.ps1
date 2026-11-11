<#
.SYNOPSIS
  Copies the WoWTouchBar addon into World of Warcraft (WoW: Forever and/or Retail), or removes it (-Uninstall).
.DESCRIPTION
  Only writes inside <WoW>\<flavour>\Interface\AddOns\WoWTouchBar. Finds WoW in its default folder, or pass -WowRoot.
  Never replaces a developer link (junction) unless -Force. Restart WoW afterwards (a new addon needs a full restart).
    powershell -ExecutionPolicy Bypass -File Install-Addon.ps1
.PARAMETER Source    The addon folder to copy (default: .\WoWTouchBar next to this script).
.PARAMETER WowRoot   The World of Warcraft folder (default: C:\Program Files (x86)\World of Warcraft).
.PARAMETER Flavours  Which game folders to use if present (default: _forever_, _retail_).
#>
[CmdletBinding()]
param(
    [string]$Source,
    [string]$WowRoot = (Join-Path ${env:ProgramFiles(x86)} 'World of Warcraft'),
    [string[]]$Flavours = @('_forever_', '_retail_'),
    [switch]$Uninstall,
    [switch]$Force
)
$ErrorActionPreference = 'Stop'
if (-not $Source) { $Source = Join-Path $PSScriptRoot 'WoWTouchBar' }   # PS 5.1: $PSScriptRoot is empty in param defaults
# A folder ending in "\" before the closing quote reaches us with a stray quote (Windows command-line rules).
$WowRoot = $WowRoot.Trim().TrimEnd('"').TrimEnd('\')

# Deletes a folder and everything in it, but a link (junction / symlink) found inside is removed as a link: what it
# points to is never touched. (Remove-Item -Recurse may follow links on Windows PowerShell; .NET's Directory.Delete stops.)
function Remove-TreeNoFollow([string]$path) {
    foreach ($child in [IO.Directory]::GetFileSystemEntries($path)) {
        $attr = [IO.File]::GetAttributes($child)
        if ($attr -band [IO.FileAttributes]::ReparsePoint) {
            if ($attr -band [IO.FileAttributes]::Directory) { [IO.Directory]::Delete($child) } else { [IO.File]::Delete($child) }
        } elseif ($attr -band [IO.FileAttributes]::Directory) {
            Remove-TreeNoFollow $child
        } else {
            [IO.File]::SetAttributes($child, [IO.FileAttributes]::Normal)
            [IO.File]::Delete($child)
        }
    }
    [IO.Directory]::Delete($path)
}

function Test-Link([string]$path) {
    $i = Get-Item -LiteralPath $path -Force -ErrorAction SilentlyContinue
    return [bool]($i -and ($i.Attributes -band [IO.FileAttributes]::ReparsePoint))
}

if (-not $Uninstall -and -not (Test-Path (Join-Path $Source 'WoWTouchBar.toc'))) {
    throw "Addon not found at $Source - run this script from the unzipped addon release folder."
}
$found = @($Flavours | Where-Object { Test-Path (Join-Path $WowRoot $_) })
if ($found.Count -eq 0) { throw "No WoW game folder ($($Flavours -join ', ')) found in $WowRoot. Pass -WowRoot '<your World of Warcraft folder>'." }

foreach ($flavour in $found) {
    $addons = Join-Path $WowRoot "$flavour\Interface\AddOns"
    $dest = Join-Path $addons 'WoWTouchBar'
    # The WoW folder can be changed by every user, so never work through a folder someone turned into a link.
    $parents = (Join-Path $WowRoot $flavour), (Join-Path $WowRoot "$flavour\Interface"), $addons
    if (@($parents | Where-Object { Test-Link $_ }).Count -gt 0) {
        Write-Host "$flavour : skipped - a folder on the way to the addon is a link (not made by WoW)."
        continue
    }
    $item = Get-Item $dest -ErrorAction SilentlyContinue
    $isLink = $item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)
    if ($isLink -and -not $Force) {
        Write-Host "$flavour : skipped - $dest is a developer link (use -Force to replace it)."
        continue
    }
    if ($item) {
        # A link is removed without touching what it points to; a real folder is replaced.
        if ($isLink) { $item.Delete() } else { Remove-TreeNoFollow $dest }
    }
    if ($Uninstall) { Write-Host "$flavour : addon removed."; continue }
    New-Item -ItemType Directory -Force -Path $addons | Out-Null
    Copy-Item $Source $dest -Recurse -Force
    Write-Host "$flavour : addon installed to $dest"
}
if (-not $Uninstall) { Write-Host "Restart WoW (fully, not /reload) and enable 'WoW Touch Bar' in the AddOns list if it is off." }
