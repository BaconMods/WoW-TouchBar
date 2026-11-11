# Safety and privacy

## What it touches in the game

- The **addon** only reads information the game already shows you (XP, quests, bags, gold, durability, reputation, mail / queue / ready-check status, whispers), through the normal addon functions every addon uses.
- It shows that information as a thin line of coloured dots in the top-left corner of the game. Addons can't talk to other programs, so the **app** reads those dots from the screen. It only captures that small corner, never the rest of the screen, and never saves it.
- **No memory reading, no injection, no automation.** The app **never sends anything to WoW**: no key presses, no clicks.
- **No combat information** is shown. WoW hides health, buffs and cooldowns from addons during combat, and this project doesn't try to get around that.

## Key presses outside WoW

The brightness, volume, media, F1-F12 and your own buttons only act when **another window has focus**. Each tap is one key press (or opens what you chose). The app refuses to press anything while WoW is the active window.

## Your data

- **Nothing leaves your PC.** No internet use, no telemetry, no account.
- Settings and logs stay in `%LOCALAPPDATA%\WoWTouchBar`. The logs never contain your account, character name or chat.
- The quick-add shortcut (Ctrl+Option+N) only opens the app's own box; the app doesn't read any other keys and never sends keys to WoW.
- Your notes and timers are saved on your PC only: in `%LOCALAPPDATA%\WoWTouchBar\notes.json` (the app) and in WoW's own saved-variables file for the addon. They are never logged or sent anywhere.
- **Chat** (whispers, guild lines that mention you) goes straight to the Touch Bar and is never saved by the addon or the app. Anyone looking at your laptop can read it on the bar; turn it off in the in-game options or with `/wtb chat whispers off` / `/wtb chat guild off`.

## The Touch Bar driver

The driver needs Secure Boot off and Windows test mode, which lowers your PC's boot security while it's on. It's a community driver, included built from its original source. During setup it's signed with a one-off certificate made on your PC, whose private key is deleted straight away, so nothing else can be signed with it. Uninstalling removes the driver and the certificate, and turns test mode off again unless it was already on. See [TOUCH_BAR_DRIVER.md](TOUCH_BAR_DRIVER.md).

## Blizzard's rules

This tool is not approved or endorsed by Blizzard. It isn't a bot, a cheat or a hack: it automates nothing, gives no advantage (everything shown is already on your screen), and changes nothing in the game. Still, no such tool is officially approved. **Use it at your own risk.**
