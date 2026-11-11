# The Touch Bar driver: what setup changes, and how to undo it

Under Windows, Apple's Touch Bar driver can only show Apple's own keys. To show pictures, the Touch Bar needs the community driver [DFRDisplayKm](https://github.com/imbushuo/DFRDisplayKm) by Bingxing Wang ([@imbushuo](https://github.com/imbushuo)), MIT licence. The installer includes it built unchanged from the original source; its licence is in THIRD_PARTY_NOTICES.txt, next to the app. It has no official Microsoft signature, so Windows only loads it with **Secure Boot off** and **test mode on**.

> **At your own risk.** Everything below is undone by uninstalling (Settings > Apps) and, if you like, turning Secure Boot back on.

## What the installer does to your PC

1. **Checks** that this is a Mac and that Secure Boot is off. If not, it stops before changing anything.
2. **Turns on Windows test mode.** After the restart, "Test Mode" shows in the bottom-right corner of the desktop. The installer remembers whether test mode was already on, so uninstalling puts it back the way it was.
3. **Signs the driver on your PC.** The driver is included already built from its original source code, but unsigned. Setup makes a one-off certificate, signs the driver with it, then **deletes the certificate's private key**, so nothing else can ever be signed with it. Only the public part is kept, and it is trusted on this PC only.
4. **Installs the driver** on the Touch Bar only, replacing Apple's Touch Bar driver for as long as WoW Touch Bar is installed. Apple's driver stays on your PC.
5. Asks you to **restart**. The driver starts after the restart.

Logs of these steps: `C:\Windows\Logs\WoW Touch Bar\driver-setup.log` and, on uninstall, `driver-remove.log`. (Version 1.0.0 kept them in `C:\ProgramData\WoW Touch Bar`; that old folder only holds logs and can be deleted.)

## While the driver is on

- Apple's own Touch Bar keys are gone, because the bar becomes a screen. WoW Touch Bar draws brightness, volume, media and F-keys instead, while it runs.
- Keyboard and trackpad are not affected, nor is the physical Esc key on 2019-2020 models.
- "Test Mode" shows on the desktop.
- Some games' anti-cheat software refuses to run while Windows is in test mode.

## Undo: get the normal Touch Bar back

1. **Settings > Apps > WoW Touch Bar > Uninstall**, then **restart**. This removes the driver (Apple's own driver takes over again), the certificate, the app and the addon, and turns test mode off unless it was on before.
2. **Optional: Secure Boot back to full strength.** Restart holding **⌘ Command + R** > **Utilities > Startup Security Utility** > **Full Security**. Only after step 1: with the driver still installed, Full Security stops it loading.

### If the normal keys don't come back

1. Restart once more. The Touch Bar sometimes only re-attaches on a fresh start.
2. Device Manager (right-click Start > *Device Manager*) > *View* > *Show hidden devices*. Under *Universal Serial Bus devices*, find **iBridge Display Composition Device** / **Touch Bar Display**: right-click > *Uninstall device* > tick *Delete the driver software for this device* > OK. Then *Action* > *Scan for hardware changes*. **Apple Touch Bar** should reappear.
3. Still not right: run the Boot Camp drivers installer again (*Apple Boot Camp Support Software*).
4. Last resort: System Restore (Start > type *Create a restore point* > *System Restore*), to a point from before you installed.

### If Windows won't start properly

Hold the power button to turn off, then start again. Windows offers *Automatic Repair > System Restore*; pick a restore point from before the install.

### Checking the driver

`C:\Program Files\WoW Touch Bar\scripts\Get-TouchBarState.ps1` (right-click > *Run with PowerShell*) shows whether Secure Boot and test mode are on and which driver runs the Touch Bar.
