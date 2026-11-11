# Install

Two steps: turn **Secure Boot off** on the Mac side, then run **one installer** that sets up everything in Windows. (Prefer to build it yourself from the source, with or without an installer? See [BUILDING.md](BUILDING.md).)

## Before you start

- An **Intel MacBook Pro with Touch Bar and T2 chip** (2018-2020), running **Windows 10** through **Boot Camp**.
- **BitLocker off**, or your BitLocker recovery key at hand: changing Secure Boot makes BitLocker ask for it at the next start.
- Your **macOS user password**.
- Read [TOUCH_BAR_DRIVER.md](TOUCH_BAR_DRIVER.md) once, so you know what the installer changes and how to undo it.

## Step 1: Turn Secure Boot off (Mac side, about 5 minutes)

The Touch Bar driver only loads with Secure Boot off. This setting lives in the Mac's firmware, so Windows can't change it for you.

1. Save your work. In Windows: Start > Power > **Restart**.
2. As soon as the screen goes black, **hold ⌘ Command + R** until the Apple logo or a spinning globe appears (macOS Recovery).
3. If asked, pick your macOS user and enter its password.
4. Menu bar: **Utilities > Startup Security Utility**. Enter the password if asked.
5. Under **Secure Boot**, choose **No Security**. Leave *External Boot* as it is.
6. Apple menu > **Startup Disk** > **Boot Camp Windows** > **Restart** (or restart holding **⌥ Option** and pick Windows).

## Step 2: Run the installer

1. Double-click **`WoWTouchBar-<version>.msi`**.
2. Windows may show a blue **"Windows protected your PC"** screen, because the installer isn't signed with a paid certificate. Click **More info**, then **Run anyway**.
3. Follow the wizard. The **Before you install** page checks Secure Boot (if it's still on, it tells you how to turn it off and won't let you continue) and test mode (if it's off, setup turns it on for you). On **Custom Setup** you can untick:
   - **WoW addon**: copies the addon into World of Warcraft (in `C:\Program Files (x86)\World of Warcraft`, for WoW: Forever and Retail);
   - **Start with Windows**.
4. Click **Yes** when Windows asks for permission (setup changes a driver, so it needs administrator rights).
5. When setup finishes, **restart Windows**. The Touch Bar driver starts after the restart.

If your WoW is somewhere else, install from a command prompt instead:
`msiexec /i WoWTouchBar-<version>.msi WOWROOT="D:\Games\World of Warcraft"`


## Step 3: Check it works

- The **tray icon**, a small bar outline, appears at the bottom right of the taskbar (click **^** if it's hidden).
- **Start WoW** (fully, not just `/reload`). On the character screen, under *AddOns*, make sure **WoW Touch Bar** is ticked. In game you'll see a very thin line of coloured dots in the top-left corner; that's how the addon talks to the app, so leave it visible.
- The Touch Bar shows your tracked quest. Tap the tabs on the right for **XP** and **Rep**. Bag space, durability and gold sit in a small box beside the tabs.
- Switch to another window: the bar shows brightness, volume and media. The button on the right switches to **F1-F12**, and to **MY KEYS** once you've added your own buttons.

## Make it yours

- **Options in game:** Esc > Options > AddOns > **WoW Touch Bar** (or type `/wtb options`). Choose which tabs and alerts show, whispers and guild chat, which quest is followed, and whether to hide some of the game's own displays. `/wtb help` lists the chat commands.
- **Your own buttons:** tray icon > **My keys...** Up to 12 buttons that open a program, website or folder, or press a key. They show on the bar outside WoW.
- **Windows controls while in game:** the **•••** button at the right end of the bar shows volume, brightness, media and your own buttons without leaving WoW. **WOW** goes back (or it returns by itself after 15 seconds).
- **Colour:** tray icon > **Colour**.
- **Tray icon:** show a page, pause the bar, open the logs folder, quit.

## Uninstall

**Settings > Apps > WoW Touch Bar > Uninstall**, then **restart**. That removes the app, the addon, the driver and the certificate it made, turns Windows test mode off again (unless it was already on before you installed), and gives you the normal Touch Bar back.

Afterwards you can turn **Secure Boot** back on: repeat Step 1 and choose **Full Security**. Only do this **after** uninstalling.

Not working? See [TROUBLESHOOTING.md](TROUBLESHOOTING.md).
