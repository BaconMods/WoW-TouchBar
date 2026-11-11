using System.Diagnostics;
using System.Drawing.Drawing2D;
using WoWTouchBar.Core.Host;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.App;

/// <summary>Tray icon + menu around <see cref="CompanionHost"/>, which runs on its own thread.</summary>
internal sealed class TrayApp : IDisposable
{
    public const string QuitEventName = @"Local\WoWTouchBar.Quit";
    private readonly FileLog _log = new("app.log");
    private readonly CompanionHost _host;
    private readonly WindowsNowPlaying _nowPlaying;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _worker;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _pause = new("Pause Touch Bar") { CheckOnClick = false };
    private readonly Dictionary<PageId, ToolStripMenuItem> _pages = new();
    private readonly Dictionary<AccentColour, ToolStripMenuItem> _colours = new();
    private readonly System.Windows.Forms.Timer _refresh = new() { Interval = 1000 };
    private bool _warnedNoDriver;

    public TrayApp()
    {
        _log.Write("app start");
        _nowPlaying = new WindowsNowPlaying(_log);
        _host = new CompanionHost(_log, nowPlaying: _nowPlaying, buttonIcons: new ButtonIcons().For, media: _nowPlaying);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_status);
        menu.Items.Add(new ToolStripSeparator());
        var pages = new ToolStripMenuItem("Show page");
        foreach (var (page, label) in new[] { (PageId.Quest, "Quest"), (PageId.Xp, "XP"), (PageId.Reputation, "Reputation") })
        {
            var item = new ToolStripMenuItem(label);
            item.Click += (_, _) => _host.ShowPage(page);
            _pages[page] = item;
            pages.DropDownItems.Add(item);
        }
        menu.Items.Add(pages);
        var colours = new ToolStripMenuItem("Colour");
        foreach (var accent in Enum.GetValues<AccentColour>())
        {
            var item = new ToolStripMenuItem(accent.ToString(), Swatch(accent));
            item.Click += (_, _) => _host.SetAccent(accent);
            _colours[accent] = item;
            colours.DropDownItems.Add(item);
        }
        menu.Items.Add(colours);
        menu.Items.Add("My keys (your own buttons)...", null, (_, _) => OpenMyKeys());
        menu.Items.Add("Notes and timers...", null, (_, _) => OpenNotes());
        _quickAddItem = new ToolStripMenuItem("Quick add a note or timer...", null, (_, _) => OpenQuickAdd());
        menu.Items.Add(_quickAddItem);
        var styles = new ToolStripMenuItem("Timer alert");
        foreach (var (style, label) in new[] { (AlertStyle.Fireworks, "Fireworks"), (AlertStyle.Laser, "Laser"), (AlertStyle.Shockwave, "Shockwave"), (AlertStyle.Flash, "Flash") })
        {
            var item = new ToolStripMenuItem(label) { Checked = _host.Bar.AlertStyle == style };
            item.Click += (_, _) =>
            {
                _host.Bar.AlertStyle = style;
                foreach (ToolStripMenuItem other in styles.DropDownItems.OfType<ToolStripMenuItem>()) other.Checked = other == item;
                _host.Pad.StartTimer("Test", TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow); // show it straight away
                _log.Write($"timer alert={style}");
            };
            styles.DropDownItems.Add(item);
        }
        styles.DropDownItems.Add(new ToolStripSeparator());
        styles.DropDownItems.Add("Try it (a 2-second test timer)", null, (_, _) => _host.Pad.StartTimer("Test", TimeSpan.FromSeconds(2), DateTimeOffset.UtcNow));
        menu.Items.Add(styles);
        _hotkey.Pressed += OpenQuickAdd;
        if (_hotkey.Set(WoWTouchBar.Core.Notes.Hotkey.Parse(_host.QuickAddKey)) is { } why) _log.Write($"quick-add shortcut: {why}");
        _quickAddItem.ShortcutKeyDisplayString = _host.QuickAddKey;
        _pause.Click += (_, _) => _host.SetPaused(!_host.Paused);
        menu.Items.Add(_pause);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open logs folder", null, (_, _) => Open(FileLog.DefaultDirectory));
        menu.Items.Add("Touch Bar guide (how to restore the normal Touch Bar)", null, (_, _) => OpenGuide());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());
        menu.Opening += (_, _) => UpdateMenu();

        _icon = new NotifyIcon { Icon = CurrentIcon(), ContextMenuStrip = menu, Visible = true, Text = "WoW Touch Bar" };
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnThemeChanged;
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowMenu(); };

        _worker = new Thread(() =>
        {
            try { _host.Run(_cts.Token); }
            catch (Exception ex) { _log.Write($"host crashed: {ex}"); }
        }) { IsBackground = true, Name = "companion" };
        _worker.Start();

        _refresh.Tick += (_, _) => UpdateMenu();
        _refresh.Start();

        // Install / uninstall scripts ask a running copy to quit cleanly through this named event.
        var ui = SynchronizationContext.Current;
        new Thread(() =>
        {
            using var quit = new EventWaitHandle(false, EventResetMode.AutoReset, QuitEventName);
            if (WaitHandle.WaitAny([quit, _cts.Token.WaitHandle]) == 0) ui?.Post(_ => Quit(), null);
        }) { IsBackground = true, Name = "quit-listener" }.Start();
    }

    /// <summary>A small square of the colour for the Colour menu.</summary>
    private static Bitmap Swatch(AccentColour accent)
    {
        var c = Palette.Tones(accent).Accent;
        var bmp = new Bitmap(16, 16);
        using var g = Graphics.FromImage(bmp);
        using var brush = new SolidBrush(Color.FromArgb(c.R, c.G, c.B));
        g.FillRectangle(brush, 1, 1, 14, 14);
        return bmp;
    }

    private void UpdateMenu()
    {
        var text = _host.StatusText;
        _status.Text = text;
        _icon.Text = Truncate($"WoW Touch Bar - {text}", 127);
        _pause.Checked = _host.Paused;
        if ((_iconLight, _iconDimmed) != (TrayIcon.TaskbarIsLight(), _host.Paused)) SetIcon();
        foreach (var (page, item) in _pages) item.Checked = _host.Bar.Page == page;
        foreach (var (accent, item) in _colours) item.Checked = _host.Bar.Accent == accent;
        if (!_warnedNoDriver && _host.State != "starting" && !_host.TouchBarFound)
        {
            _warnedNoDriver = true;
            _icon.ShowBalloonTip(8000, "WoW Touch Bar", "The Touch Bar display driver is not active, so nothing can be shown on the bar. " +
                "Open the Touch Bar guide from the tray menu.", ToolTipIcon.Warning);
        }
    }

    private MyKeysForm? _myKeys;

    /// <summary>Opens the "My keys" window (only one at a time); Save hands the buttons to the host, which saves them.</summary>
    private NotesForm? _notesForm;
    private QuickAddForm? _quickAdd;
    private readonly HotkeyListener _hotkey = new();
    private readonly ToolStripMenuItem _quickAddItem;

    /// <summary>The quick-add box (the shortcut or the tray menu): pops up over anything, WoW included.</summary>
    private void OpenQuickAdd()
    {
        if (_quickAdd is { IsDisposed: false }) { _quickAdd.Activate(); return; }
        _quickAdd = new QuickAddForm(_host.Pad);
        _quickAdd.FormClosed += (_, _) => _quickAdd.Dispose();
        _quickAdd.Show();
    }

    /// <summary>New quick-add shortcut from the Notes window ("" = off). Returns null, or why it can't be used.</summary>
    private string? SetQuickAddKey(string key)
    {
        var hotkey = WoWTouchBar.Core.Notes.Hotkey.Parse(key);
        if (key.Length > 0 && hotkey is null) return "That shortcut can't be used.";
        var why = _hotkey.Set(hotkey);
        if (why is not null) { _hotkey.Set(WoWTouchBar.Core.Notes.Hotkey.Parse(_host.QuickAddKey)); return why; }
        _host.QuickAddKey = key;
        _quickAddItem.ShortcutKeyDisplayString = key;
        _log.Write($"quick-add shortcut={(key.Length > 0 ? key : "off")}");
        return null;
    }

    /// <summary>Tray > Notes and timers: edits the pad directly (changes show on the bar at once).</summary>
    private void OpenNotes()
    {
        if (_notesForm is { IsDisposed: false }) { _notesForm.Activate(); return; }
        _notesForm = new NotesForm(_host.Pad, _host.QuickAddKey, SetQuickAddKey);
        _notesForm.FormClosed += (_, _) => _notesForm.Dispose();
        _notesForm.Show();
        _notesForm.Activate();
    }

    private void OpenMyKeys()
    {
        if (_myKeys is { IsDisposed: false }) { _myKeys.Activate(); return; }
        _myKeys = new MyKeysForm(_host.CustomButtons);
        _myKeys.FormClosed += (_, _) =>
        {
            if (_myKeys.DialogResult == DialogResult.OK) _host.SetCustomButtons(_myKeys.Result);
            _myKeys.Dispose();
        };
        _myKeys.Show();
        _myKeys.Activate();
    }

    private void ShowMenu()
    {
        // NotifyIcon only opens its menu on right-click; reuse the private helper for left-click too.
        typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(_icon, null);
    }

    /// <summary>Opens the Touch Bar guide: the repo copy when running from the repo, else explains where it is.</summary>
    private void OpenGuide()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "docs", "11_TOUCHBAR_DRIVER_GUIDE.md"))) dir = dir.Parent;
        if (dir is not null) { Open(Path.Combine(dir.FullName, "docs", "11_TOUCHBAR_DRIVER_GUIDE.md")); return; }
        MessageBox.Show("The guide is docs\\11_TOUCHBAR_DRIVER_GUIDE.md in the WoW Touch Bar download. " +
            "It explains how to put the normal Apple Touch Bar back.", "WoW Touch Bar", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { _log.Write($"open failed {path}: {ex.Message}"); }
    }

    private void Quit()
    {
        if (_cts.IsCancellationRequested) return;
        _log.Write("app quit");
        _refresh.Stop();
        _cts.Cancel();
        _worker.Join(TimeSpan.FromSeconds(3));
        _icon.Visible = false;
        Application.Exit();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private bool _iconLight, _iconDimmed;

    /// <summary>Flat Windows 10 style glyph matching the taskbar theme; dimmed while paused.</summary>
    private Icon CurrentIcon()
    {
        _iconLight = TrayIcon.TaskbarIsLight();
        _iconDimmed = _host.Paused;
        return TrayIcon.Create(_iconLight, _iconDimmed);
    }

    private void SetIcon()
    {
        var old = _icon.Icon;
        _icon.Icon = CurrentIcon();
        old?.Dispose();
    }

    private void OnThemeChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category is Microsoft.Win32.UserPreferenceCategory.General or Microsoft.Win32.UserPreferenceCategory.VisualStyle) SetIcon();
    }

    public void Dispose()
    {
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        _refresh.Dispose();
        _hotkey.Dispose();
        _nowPlaying.Dispose();
        _icon.Dispose();
        _cts.Dispose();
    }
}
