# WoW Touch Bar

Your World of Warcraft progress on the Touch Bar of an Intel MacBook Pro running Windows (Boot Camp).

**In WoW** the Touch Bar shows:
- your tracked quest objective with a progress bar (tap to see the next objective);
- XP and rested XP;
- your watched reputation;
- bag space, durability and gold;
- short alerts (quest complete, bags full, repair soon, level up, new mail, ready check, dungeon ready) and the dungeon-queue timer;
- whispers, and guild lines that mention you;
- your own **notes** (e.g. coordinates) and **timers**, with an alert when a timer ends.

**Outside WoW** it gives you brightness, volume, media controls with what's playing, the time, battery, F1-F12 and buttons of your own. When nothing is playing, the media buttons step aside: brightness gets a slider like volume, with down / up buttons either side of both.

## Notes and timers

Add them in game, from the tray icon (*Notes and timers...*), or anywhere with the **quick-add shortcut Ctrl+Option+N** (the Option key is called Alt in Windows; works in WoW too): type `buy flasks` for a note or `10m tea` for a timer and press Enter. Change or turn off the shortcut in the tray's *Notes and timers* window. They show on the NOTE and TIMER tabs.

| In game | Does |
|---|---|
| `/wtb note buy flasks` | adds a note (up to 8, 80 letters each) |
| `/wtb note here rare spawn` | adds your zone and map coordinates, then your text |
| `/wtb timer 10m tea` | starts a timer (`90s`, `10m`, `1h30m`, `1:30`; up to 8, up to 24 hours) |
| `/wtb notes` | a small window to add both |

Outside WoW, tap the toggle at the right end of the bar until it says **NOTES** (then **TIMERS**) to see them next to the clock. Tap the small **×** beside a note or timer to remove it ("REMOVED - TAP TO UNDO" gives you 5 seconds to change your mind). Each timer has a bar that empties as time runs out. Each time you add one, the bar confirms it for a moment. When a timer ends, the whole bar lights up - **fireworks**, a **laser** sweep, a **shockwave** or a plain **flash**, both in your bar colour (tray > *Timer alert*, with *Try it*) - three times, half a minute apart, and the banner stays until you tap it, in game or not. Wherever you added them, they end up in one list: remove them on the bar or in the tray window.

## Is this for me?

You need:
- an **Intel MacBook Pro with Touch Bar and T2 chip** (2018-2020), running **Windows 10** through **Boot Camp**;
- **WoW: Forever** and **WoW Retail**;
- to be comfortable turning **Secure Boot off** and putting Windows in **test mode**. The Touch Bar can only show pictures under Windows with a community driver that has no official signature. Every step can be undone.

## Get started

1. Turn **Secure Boot off** on the Mac side, then run **`WoWTouchBar-<version>.msi`**: one installer sets up the app, the WoW addon and the Touch Bar driver. Step by step: [docs/INSTALL.md](docs/INSTALL.md).
2. What the installer changes and how to undo it: [docs/TOUCH_BAR_DRIVER.md](docs/TOUCH_BAR_DRIVER.md). Uninstalling (Settings > Apps) reverses everything.
3. Problems: [docs/TROUBLESHOOTING.md](docs/TROUBLESHOOTING.md) and [docs/KNOWN_ISSUES.md](docs/KNOWN_ISSUES.md).
4. Rather not run a downloaded installer? Build it yourself from this source, with or without an installer: [docs/BUILDING.md](docs/BUILDING.md).

## Safe by design

- The addon only reads what the game already shows you, through normal addon functions. The app only reads a thin line of coloured dots the addon draws in the corner of the game.
- No memory reading, no injection, no automation. The app **never sends anything to WoW**.
- Nothing leaves your PC: no internet, no telemetry. Chat is never saved or logged.

More in [docs/SAFETY_AND_PRIVACY.md](docs/SAFETY_AND_PRIVACY.md). Not affiliated with or endorsed by Blizzard Entertainment or Apple. Use at your own risk.

## Credits

The Touch Bar display driver is **[DFRDisplayKm](https://github.com/imbushuo/DFRDisplayKm) by Bingxing Wang ([@imbushuo](https://github.com/imbushuo))**, MIT licence. It is what lets Windows draw pictures on the Touch Bar; this project wouldn't exist without it. The installer includes it built unchanged from the original source and signs it on your own PC during setup. Its licence and the other third-party notices are in [THIRD_PARTY_NOTICES.txt](THIRD_PARTY_NOTICES.txt).

## Licence

MIT, see the LICENSE file.
