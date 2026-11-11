// WoW Touch Bar - tray app. Runs the companion loop in the background with a tray icon:
// status, page choice, My keys, pause, logs, Touch Bar guide, quit. No admin rights, nothing sent over the network.
namespace WoWTouchBar.App;

internal static class Program
{
    /// <summary>
    /// [STAThread] is required for Windows Forms: the file / folder pickers (Browse...) and other common dialogs only
    /// work on an STA thread. Without it, Browse... froze the app.
    /// </summary>
    [STAThread]
    private static int Main(string[] args)
    {
        // Developer aid: `WoWTouchBar.exe --icon-preview <png>` writes the tray glyph at every size (dark + light) and exits.
        if (args.Length == 2 && args[0] == "--icon-preview")
        {
            TrayIcon.SavePreview(args[1], lightTaskbar: false);
            TrayIcon.SavePreview(Path.ChangeExtension(args[1], ".light.png"), lightTaskbar: true);
            return 0;
        }

        // Developer aid: `WoWTouchBar.exe --selftest-mykeys <file>` types into the My keys window and writes PASS / FAIL.
        if (args.Length == 2 && args[0] == "--selftest-mykeys")
        {
            ApplicationConfiguration.Initialize();
            File.WriteAllText(args[1], MyKeysForm.SelfTest());
            return 0;
        }

        // Developer aid: `WoWTouchBar.exe --preview-mykeys <png>` draws the My keys window at this screen's scaling and exits.
        if (args.Length == 2 && args[0] == "--preview-mykeys")
        {
            ApplicationConfiguration.Initialize();
            using var form = new MyKeysForm([new("BNET", Core.Input.ShortcutKind.OpenWebsite, "https://battle.net"), new("WOWHEAD", Core.Input.ShortcutKind.OpenWebsite, "https://www.wowhead.com")])
                { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            form.Show();
            Application.DoEvents();
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }

        // Developer aid: `WoWTouchBar.exe --preview-mykeys-bar <png>` draws the Touch Bar's My keys page with the saved buttons.
        if (args.Length == 2 && args[0] == "--preview-mykeys-bar")
        {
            var saved = Core.Host.Settings.Load();
            IReadOnlyList<Core.Input.CustomButton> buttons = saved.CustomButtons.Count > 0 ? saved.CustomButtons :
            [
                new("BNET", Core.Input.ShortcutKind.OpenProgram, @"C:\Program Files (x86)\Battle.net\Battle.net Launcher.exe"),
                new("NOTEPAD", Core.Input.ShortcutKind.OpenProgram, @"C:\Windows\notepad.exe"),
                new("WOWHEAD", Core.Input.ShortcutKind.OpenWebsite, "https://www.wowhead.com"),
            ];
            var bar = new Core.TouchBar.Ui.BarController(mode: Core.TouchBar.Ui.SystemMode.Custom, customButtons: buttons)
                { IconFor = new ButtonIcons().For };
            var view = bar.Render(null, Core.TouchBar.LinkStatus.WaitingForWow, wowFocused: false);
            using var bmp = new Bitmap(view.Image.Width, view.Image.Height);
            for (var y = 0; y < view.Image.Height; y++)
                for (var x = 0; x < view.Image.Width; x++)
                {
                    var p = view.Image.GetPixel(x, y);
                    bmp.SetPixel(x, y, Color.FromArgb(p.R, p.G, p.B));
                }
            bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            return 0;
        }

        // Developer aid: `WoWTouchBar.exe --check-sta <file>` writes the main thread's apartment (must be STA).
        if (args.Length == 2 && args[0] == "--check-sta")
        {
            File.WriteAllText(args[1], Thread.CurrentThread.GetApartmentState().ToString());
            return 0;
        }

        using var single = new Mutex(initiallyOwned: true, @"Local\WoWTouchBar.App", out var first);
        if (!first)
        {
            // Started at sign-in (--autostart) while a copy already runs: leave quietly, no message box at login.
            if (args.Contains("--autostart")) return 0;
            MessageBox.Show("WoW Touch Bar is already running - look for its icon in the tray (bottom right).", "WoW Touch Bar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 1;
        }
        ApplicationConfiguration.Initialize();

        using var tray = new TrayApp();
        Application.Run();
        return 0;
    }
}
