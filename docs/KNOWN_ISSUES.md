# Known issues and limits

| Issue | Why | Workaround |
|---|---|---|
| "Windows protected your PC" when starting the installer | The installer isn't signed with a paid certificate. | *More info* > *Run anyway*. |
| Needs Secure Boot off and Windows test mode | The Touch Bar display driver has no official signature. Windows' own USB drivers and Apple's Boot Camp drivers can't show pictures on this Touch Bar. | None. Fully undoable by uninstalling, see [TOUCH_BAR_DRIVER.md](TOUCH_BAR_DRIVER.md#undo-get-the-normal-touch-bar-back). |
| "Test Mode" text on the desktop | Windows shows it while test mode is on. | Goes away when the driver is removed. |
| Some anti-cheat software won't run in test mode | Those games check for it. | Uninstall WoW Touch Bar while playing them. |
| Apple's Touch Bar keys are gone while the driver is on | The driver turns the Touch Bar into a screen. | The app draws brightness, volume, media and F-keys, but only while it runs. |
| WoW Classic untested | Tested on WoW: Forever and WoW Retail only. Classic may call the addon out of date. | Tick *Load out of date AddOns* and try; some pages may stay empty. |
| Only tested on one model | 13" MacBook Pro 2020 (MacBookPro16,2), Windows 10 22H2. | Other T2 Touch Bar models should work but are untested. |
| The line of dots must stay visible | It's how the addon talks to the app. Anything covering the top-left corner (other windows, overlays, a minimised game) pauses updates. | Updates resume by themselves. |
| A screen-wide colour-blind filter may stop the dots being read | The default line uses red, green and blue. | Untested. `/wtb strip 2 353 dark` switches to a grey (slightly larger) line. |
| No health, buffs or cooldowns in combat | WoW hides them from addons during combat. | By design. |
| No distance or travel time to quests | Not shown. | |
| Chat in other alphabets shows as blanks | The bar's font only has A-Z, 0-9 and basic punctuation (accents are simplified). | |
| Battle.net friends show as "BATTLE.NET FRIEND" | WoW hides their real names from addons. | By design. |
| macOS not supported | Windows (Boot Camp) only. | |
