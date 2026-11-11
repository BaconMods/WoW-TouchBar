# Troubleshooting

Start with the **tray icon** (bottom right; click **^** if hidden). Its first menu line says what the app sees. The logs are under *Open logs folder*.

| Tray / bar says | What it means | Fix |
|---|---|---|
| **Touch Bar driver not active** | The app can't find the Touch Bar display driver. | Did you restart after installing? Restart once more. Check with `C:\Program Files\WoW Touch Bar\scripts\Get-TouchBarState.ps1` (right-click > *Run with PowerShell*). Still nothing: uninstall (Settings > Apps), restart, and install again. See also [TOUCH_BAR_DRIVER.md](TOUCH_BAR_DRIVER.md). |
| **Waiting for WoW** | WoW isn't running. | Start the game. Outside WoW the bar shows brightness, volume and media, which is normal. |
| **WoW found - waiting for the addon** | WoW runs, but the app can't see the line of dots. | Is the addon ticked under *AddOns* on the character screen? Installed while WoW was open: restart WoW fully. Is something covering the top-left corner of the game (another window, an overlay)? |
| **WoW strip not visible** / bar greyed out | The dots were readable, but not for a few seconds. | Usually a loading screen or a window over the corner. It recovers by itself. |
| **No signal from WoW** | Nothing readable for over a minute. | As above, then `/reload` in game. |
| **Addon and app versions don't match** | The addon and the app come from different releases. | Install both from the same release. |
| **WoW is minimised** | Windows doesn't draw a minimised game. | Restore the window. |
| **Paused** | You chose *Pause Touch Bar*. | Untick it in the tray menu. |

## Other problems

- **Wrong quest, or "WAITING FOR QUEST DATA".** The bar follows your tracked quest. In game, `/wtb` shows what the addon picked, `/wtb pin` pins the current quest, `/wtb policy watched` uses only watched quests.
- **A count shows "1/1" without a name.** Right after login the game hasn't loaded the item name yet. It fills in by itself.
- **Setup says Secure Boot is on.** Turn it off first: [INSTALL.md → Step 1](INSTALL.md#step-1-turn-secure-boot-off-mac-side-about-5-minutes).
- **"Windows protected your PC" when opening the installer.** Normal for installers without a paid certificate: *More info* > *Run anyway*.
- **The addon didn't appear in WoW.** Setup copies it into `C:\Program Files (x86)\World of Warcraft`. If your game is elsewhere, reinstall with `msiexec /i WoWTouchBar-<version>.msi WOWROOT="<your WoW folder>"`, or copy `C:\Program Files\WoW Touch Bar\addon\WoWTouchBar` into your game's `Interface\AddOns` folder. Then restart WoW fully.
- **Taps do nothing.** The app must be running (tray icon). Start it from the Start menu.
- **Volume or F-keys don't work in the game.** On purpose: the app never sends anything to WoW. Use **•••** for volume and brightness while in game, or use them when another window has focus.
- **Whispers / guild chat.** Change them in the in-game options, or with `/wtb chat whispers on|off` and `/wtb chat guild off|mentions|all`. Tap a message to skip it. `/wtb testchat` shows two pretend messages to check it works; nothing is sent in game.
- **The line of dots is in the way.** It's 1-2 pixels tall in the top-left corner. It can't be made smaller and still be read.
- **"Already running."** Only one copy can run. Use the tray icon, or end `WoWTouchBar.exe` in Task Manager.
- **I want the normal Touch Bar back.** Settings > Apps > WoW Touch Bar > Uninstall, then restart. Details: [TOUCH_BAR_DRIVER.md → Undo](TOUCH_BAR_DRIVER.md#undo-get-the-normal-touch-bar-back).

## Reporting a problem

The logs never contain your account, character name or chat. Include `app.log` from the logs folder and what the tray status line says.
