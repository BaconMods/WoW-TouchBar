# Building it yourself

Most people should use the installer from the Releases page. This guide is for when you'd rather not run a downloaded installer: you build everything from this source code on your own PC, and either:

- **[Route A](#route-a-build-your-own-installer)**: build your own copy of the installer (`.msi`) and run that, or
- **[Route B](#route-b-no-installer-at-all)**: skip the installer entirely and set up each part by hand.

Both routes end up the same as the official installer: same driver, same app, same addon. Route B is just more steps.

> **Same requirements as the installer.** An Intel MacBook Pro with Touch Bar and T2 chip, Windows 10 in Boot Camp, and **Secure Boot set to *No Security*** (restart holding *Command + R*, then *Utilities > Startup Security Utility*). The driver only loads with Secure Boot off and Windows test mode on, however it's installed. See [TOUCH_BAR_DRIVER.md](TOUCH_BAR_DRIVER.md).

## What's in the source

| Folder | What it is |
|---|---|
| `addon/WoWTouchBar` | The WoW addon (Lua). Reads quest / XP / bag / reputation / chat info through normal addon functions and draws it as a thin line of coloured dots. |
| `companion/Core` | The app's logic: reads the dots from the screen, decodes them, draws the Touch Bar pages, handles touch. |
| `companion/App` | The tray app (`WoWTouchBar.exe`). |
| `installer` | The Windows Installer package (`.msi`), built with WiX. |
| `scripts/installer` | What the installer runs: driver setup / removal, clean-up of older copies. You can run these yourself (Route B). |
| `scripts/install` | Copies the addon into WoW. |
| `scripts/touchbar` | Building the Touch Bar driver from its original source, and checking its state. |
| `scripts/release` | The release build. |
| `site` | The project website. |

The Touch Bar driver is [DFRDisplayKm](https://github.com/imbushuo/DFRDisplayKm) by Bingxing Wang (MIT licence). It isn't stored here: the build script downloads its source from that GitHub repo at a fixed commit and builds it unchanged.

## Tools you need

Install these once (all free, all from Microsoft):

1. **[.NET 10 SDK](https://dotnet.microsoft.com/download)**, for the app and the installer.
2. **[Git](https://git-scm.com/)**, to download the source and the driver source.
3. For the driver: **[Visual Studio Build Tools 2022](https://visualstudio.microsoft.com/downloads/#build-tools-for-visual-studio-2022)** with
   - the *Desktop development with C++* workload,
   - *MSVC v143 Spectre-mitigated libs (x64/x86)*,
   - *Windows 11 SDK (10.0.26100)*,

   and then the **[Windows Driver Kit (WDK) 10.0.26100](https://learn.microsoft.com/windows-hardware/drivers/download-the-wdk)**.

Then get the source:

```powershell
git clone https://github.com/BaconMods/WoW-TouchBar.git
cd WoW-TouchBar
```

All commands below run in **PowerShell** from that folder.

## Route A: build your own installer

```powershell
# 1. The Touch Bar driver, built unsigned (the installer signs it on your PC)
powershell -ExecutionPolicy Bypass -File scripts\touchbar\Build-DfrDriver.ps1 -Unsigned

# 2. The app, then the .msi (WiX is fetched by dotnet automatically)
powershell -ExecutionPolicy Bypass -File scripts\release\Build-Release.ps1
```

You get `dist\WoWTouchBar-<version>.msi` (and `dist\SHA256SUMS.txt`). Run it like the official one; [INSTALL.md](INSTALL.md) has the rest. Uninstall from *Settings > Apps* as usual.

## Route B: no installer at all

Each step is one of the scripts the installer would run, run by you instead.

### 1. Build the driver

```powershell
powershell -ExecutionPolicy Bypass -File scripts\touchbar\Build-DfrDriver.ps1 -Unsigned
```

The result is in `out\driver-unsigned` (`DFRDisplayKm.inf`, `.sys`, `.cat`).

### 2. Build the app

```powershell
dotnet publish companion\App\WoWTouchBar.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o "$env:LOCALAPPDATA\Programs\WoW Touch Bar"
```

That puts `WoWTouchBar.exe` in `%LOCALAPPDATA%\Programs\WoW Touch Bar` (any folder works). It is self-contained: no .NET install needed to run it.

### 3. Set up the driver (needs administrator)

Open PowerShell **as administrator** in the source folder, then:

```powershell
$own = "$env:ProgramFiles\WoW Touch Bar driver"     # a protected folder (only administrators can change it)
New-Item -ItemType Directory -Force "$own\driver" | Out-Null
Copy-Item out\driver-unsigned\* "$own\driver"
powershell -ExecutionPolicy Bypass -File scripts\installer\Setup-TouchBarDriver.ps1 -Dir "$own"
```

This does exactly what the installer does:

1. turns Windows test mode on (and remembers if it was already on);
2. makes a one-off certificate, signs the driver with it, **deletes the certificate's private key** (nothing else can ever be signed with it), and trusts it on this PC only;
3. installs the driver on the Touch Bar.

It prints `Done. Restart Windows to finish.` A log is kept in `C:\Windows\Logs\WoW Touch Bar\driver-setup.log`. Setup signs and installs from its own protected copy, so `C:\Program Files\WoW Touch Bar driver` can be deleted afterwards. The driver is set up with full system rights, so setup refuses a folder that anyone but administrators could change - including folders in your own user folder (your everyday programs could change those) and folders made directly under `C:\`.

### 4. Put the addon into WoW

```powershell
powershell -ExecutionPolicy Bypass -File scripts\install\Install-Addon.ps1 -Source addon\WoWTouchBar
```

It copies the addon into WoW: Forever and / or Retail, whichever it finds in `C:\Program Files (x86)\World of Warcraft`. If WoW is somewhere else, add `-WowRoot "D:\Games\World of Warcraft"`. If the folder needs administrator rights, run this from the administrator window too.

### 5. Restart, then start the app

Restart Windows. Start `WoWTouchBar.exe`; its icon appears in the tray. To start it at sign-in, press *Win + R*, type `shell:startup`, and put a shortcut to `WoWTouchBar.exe` in that folder, with `--autostart` added to its *Target*.

In WoW, tick *WoW Touch Bar* under *AddOns*. Check the driver at any time with:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\touchbar\Get-TouchBarState.ps1
```

### Undo Route B

As administrator, in the source folder:

```powershell
powershell -ExecutionPolicy Bypass -File scripts\installer\Remove-TouchBarDriver.ps1
powershell -ExecutionPolicy Bypass -File scripts\install\Install-Addon.ps1 -Uninstall
```

The first removes the driver and the certificate and turns test mode off again (unless it was on before you started). The second removes the addon. Then quit the app from the tray, delete its folder and the `shell:startup` shortcut, and restart Windows. Your settings are in `%LOCALAPPDATA%\WoWTouchBar`; delete that too if you like. You can then set Secure Boot back to *Full Security*.

## Rules this project follows

- The addon only reads; it never acts in the game. The app never sends input to WoW; its key buttons only work while another window has focus.
- No memory reading, injection or automation, and no attempt to read values the game hides during combat.
- Nothing is sent over the network.
