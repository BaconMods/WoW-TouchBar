namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>
/// The Windows controls page, in two flavours:
/// <list type="bullet">
/// <item><b>Outside WoW</b> (WoW not focused / not running): media / volume / brightness, F1-F12 (+ Esc on Macs without
/// a physical Esc key), or "My keys". The toggle at the right end cycles the modes. Volume / media / F-keys are key
/// presses (SystemKeys), which refuses anything while WoW is focused.</item>
/// <item><b>In game</b> (the "•••" panel): opened from the WoW pages while WoW stays in front.
/// Nothing here is a key press: volume / mute through Windows' audio API, media through Windows' media session,
/// brightness through WMI, and only My keys that open a program / website / folder. No F-keys. The WOW button at the
/// right end goes back.</item>
/// </list>
/// Buttons fill the bar; the right-end button always sits in the same place.
/// </summary>
public static class SystemPage
{
    public const int Margin = 8;
    public const int Gap = 8;          // between buttons
    public const int GroupGap = 28;    // between groups
    public const int ToggleWidth = 150;
    public static int ToggleX => BarController.Width - Margin - ToggleWidth;

    public static int ChatChars => Ui.Draw.ChatChars(ToggleX - GroupGap - Margin);

    public const int InfoWidth = 130;        // clock + battery, left of the right-end button(s)
    public const int NowPlayingWidth = 420;  // title / artist, left of the clock (media mode only)

    /// <summary>Clock position outside WoW (the in-game panel may move it left for its second button).</summary>
    public static int InfoX => ToggleX - Gap - InfoWidth;
    public static int NowPlayingX => InfoX - Gap - NowPlayingWidth;

    /// <summary>Something is loaded in a media player (playing or paused), so the media buttons are worth showing.</summary>
    public static bool HasMedia(Input.SystemInfo info) => info.Media is { } m && (m.Title.Length > 0 || m.Artist.Length > 0);

    /// <summary>My keys that can be used in game: the ones that open something (never the ones that press a key).</summary>
    public static bool UsableInGame(Input.CustomButton b) => b.Kind != Input.ShortcutKind.PressKey;

    public static void Draw(Canvas c, SystemMode mode, string toggleLabel, bool showEscape, Alert? alert = null, ChatBanner? chat = null,
        Input.SystemInfo? info = null, IReadOnlyList<Input.CustomButton>? custom = null, Func<Input.CustomButton, PixelBuffer?>? iconFor = null,
        bool inGame = false, AccentColour accent = AccentColour.Purple, PadView? pad = null)
    {
        var pal = Palette.For(accent);
        custom ??= [];

        // Right end: the mode toggle (outside WoW) or WOW (in game, back to the WoW pages) - always the same spot.
        // In game, a MY KEYS / MEDIA button sits left of WOW when there are buttons that open something.
        var keysButton = inGame && custom.Any(UsableInGame);
        var right = keysButton ? ToggleX - Gap - ToggleWidth : ToggleX;
        void RightButtons()
        {
            if (!inGame)
            {
                Ui.Draw.Button(c, ToggleX, ToggleWidth, toggleLabel, new ToggleFunctionKeys(), pal.Accent, pal.Text);
                return;
            }
            if (keysButton) Ui.Draw.Button(c, ToggleX - Gap - ToggleWidth, ToggleWidth, mode == SystemMode.Custom ? "MEDIA" : "MY KEYS", new ToggleQuickKeys(), pal.Button, pal.Text);
            Ui.Draw.Button(c, ToggleX, ToggleWidth, "WOW", new ToggleQuickPanel(), pal.Accent, pal.Text);
        }

        if (alert is not null || chat is not null)
        {
            // Ready check / dungeon ready / a whisper: the moment you'd otherwise miss it. Tap to hide.
            var w = right - GroupGap - Margin;
            if (alert is not null) Ui.Draw.AlertBanner(c, Margin, w, alert, pal);
            else Ui.Draw.ChatBanner(c, Margin, w, chat!, pal);
            RightButtons();
            return;
        }

        if (!inGame && pad is { } p && ((mode == SystemMode.Notes && p.Notes.Count > 0) || (mode == SystemMode.Timers && p.Timers.Count > 0)))
        {
            // Notes, or timers, outside WoW (separate modes): the clock stays, the rest of the bar is the list.
            // The clock stays exactly where it is in the other modes.
            var notesRight = right - GroupGap;
            if (info is not null)
            {
                var ix = right - Gap - InfoWidth;
                InfoBox(c, ix, info, pal);
                notesRight = ix - GroupGap;
            }
            if (mode == SystemMode.Notes) WowPages.NoteLine(c, Margin, notesRight - Margin, p, pal);
            else WowPages.TimerRow(c, Margin, notesRight - Margin, WowPages.TimersPerView, p, pal);
            RightButtons();
            return;
        }

        List<(string Label, BarAction Action)[]> groups;
        if (mode == SystemMode.Custom && custom.Count > 0)
        {
            // The player's own buttons (tray > My keys), up to 12 in one group; in game only those that open something.
            groups = [custom.Take(Input.Shortcuts.MaxButtons).Select((b, i) => (b, i))
                .Where(p => !inGame || UsableInGame(p.b))
                .Select(p => (p.b.Label, (BarAction)new RunShortcut(p.i))).ToArray()];
        }
        else if (mode == SystemMode.FunctionKeys && !inGame)
        {
            var fkeys = Enumerable.Range(0, 12).Select(i => ($"F{i + 1}", (BarAction)new PressKey(SystemKey.F1 + i)));
            groups = showEscape
                ? [[("ESC", new PressKey(SystemKey.Escape))], fkeys.Take(4).ToArray(), fkeys.Skip(4).Take(4).ToArray(), fkeys.Skip(8).ToArray()]
                : [fkeys.Take(4).ToArray(), fkeys.Skip(4).Take(4).ToArray(), fkeys.Skip(8).ToArray()];
        }
        else
        {
            var playLabel = info?.Media is { Playing: true } ? "PAUSE" : "PLAY";
            BarAction Vol(QuickKind q, SystemKey k) => inGame ? new QuickControl(q) : new PressKey(k);
            if (info is not null && !HasMedia(info))
            {
                // Nothing loaded in any player: no media buttons (they'd do nothing). In the room: a brightness slider, and
                // down / up buttons either side of both sliders.
                groups =
                [
                    [("BRT -", new ChangeBrightness(-1)), ("BRT", new VolumeSlider(0, 0, Brightness: true)), ("BRT +", new ChangeBrightness(+1))],
                    [("VOL -", Vol(QuickKind.VolumeDown, SystemKey.VolumeDown)), ("VOL", new VolumeSlider(0, 0)), ("VOL +", Vol(QuickKind.VolumeUp, SystemKey.VolumeUp)),
                     ("MUTE", new QuickControl(QuickKind.Mute))],
                ];
            }
            else
            {
                groups =
                [
                    [("BRT -", new ChangeBrightness(-1)), ("BRT +", new ChangeBrightness(+1))],
                    // Volume: a slider (Windows' audio API, no key press - fine in game too) + MUTE.
                    [("VOL", new VolumeSlider(0, 0)), ("MUTE", new QuickControl(QuickKind.Mute))],
                    [("<<", Vol(QuickKind.Previous, SystemKey.PreviousTrack)), (playLabel, Vol(QuickKind.PlayPause, SystemKey.PlayPause)), (">>", Vol(QuickKind.Next, SystemKey.NextTrack))],
                ];
            }
        }

        right -= GroupGap;
        // Clock + battery next to the right-end button(s); now playing next to that (media, while something plays).
        if (info is not null)
        {
            var infoX = right + GroupGap - Gap - InfoWidth;
            InfoBox(c, infoX, info, pal);
            right = infoX - GroupGap;
            var showMedia = mode == SystemMode.Media || (inGame && mode != SystemMode.Custom);
            if (showMedia && info.Media is { } media && (media.Title.Length > 0 || media.Artist.Length > 0))
            {
                var npX = infoX - Gap - NowPlayingWidth;
                NowPlayingBox(c, npX, media, pal, inGame ? new QuickControl(QuickKind.PlayPause) : new PressKey(SystemKey.PlayPause));
                right = npX - GroupGap;
            }
        }

        // Spread the buttons evenly over everything left of that; the last pixels go to the first buttons.
        // The volume slider has a fixed width; the buttons share what is left.
        var count = groups.Sum(g => g.Length);
        if (count == 0) { RightButtons(); return; }
        var sliders = groups.Sum(g => g.Count(i => i.Action is VolumeSlider));
        var gaps = (count - groups.Count) * Gap + (groups.Count - 1) * GroupGap;
        var sliderW = Ui.Draw.SliderWidth;
        var space = right - Margin - gaps - sliders * sliderW;
        var buttons = Math.Max(1, count - sliders);
        var width = space / buttons;
        var extra = space - width * buttons;
        var x = Margin;
        foreach (var group in groups)
        {
            foreach (var (label, action) in group)
            {
                if (action is VolumeSlider vs)
                {
                    if (vs.Brightness) Ui.Draw.BrightnessSlider(c, x, sliderW, info?.BrightnessPercent, pal);
                    else Ui.Draw.VolumeSlider(c, x, sliderW, info?.VolumePercent, info?.Muted ?? false, pal);
                    x += sliderW + Gap;
                    continue;
                }
                var w = width + (extra-- > 0 ? 1 : 0);
                if (Symbol(action, label) is { } symbol)
                {
                    // Media / volume / brightness as symbols; MUTE is highlighted while muted.
                    var on = symbol == IconKind.Mute && info?.Muted == true;
                    var fill = on ? pal.Accent : pal.Button;
                    Ui.Draw.SymbolButton(c, x, w, label, action, fill, IconArt.Render(symbol, pal.Text, fill));
                    x += w + Gap;
                    continue;
                }
                var icon = action is RunShortcut rs && iconFor is not null && rs.Index < custom.Count ? iconFor(custom[rs.Index]) : null;
                Ui.Draw.IconButton(c, x, w, label, action, pal.Button, pal.Text, icon);
                x += w + Gap;
            }
            x += GroupGap - Gap;
        }
        RightButtons();
    }

    /// <summary>Which symbol a media / volume / brightness button shows instead of its label (null = keep the label).</summary>
    public static IconKind? Symbol(BarAction action, string label) => action switch
    {
        ChangeBrightness b => b.Steps < 0 ? IconKind.BrightnessDown : IconKind.BrightnessUp,
        PressKey { Key: SystemKey.Mute } or QuickControl { Kind: QuickKind.Mute } => IconKind.Mute,
        PressKey { Key: SystemKey.VolumeDown } or QuickControl { Kind: QuickKind.VolumeDown } => IconKind.VolumeDown,
        PressKey { Key: SystemKey.VolumeUp } or QuickControl { Kind: QuickKind.VolumeUp } => IconKind.VolumeUp,
        PressKey { Key: SystemKey.PreviousTrack } or QuickControl { Kind: QuickKind.Previous } => IconKind.Previous,
        PressKey { Key: SystemKey.NextTrack } or QuickControl { Kind: QuickKind.Next } => IconKind.Next,
        PressKey { Key: SystemKey.PlayPause } or QuickControl { Kind: QuickKind.PlayPause } => label == "PAUSE" ? IconKind.Pause : IconKind.Play,
        _ => null,
    };

    private static void InfoBox(Canvas c, int x, Input.SystemInfo info, Palette pal)
    {
        var time = info.Time.ToString("HH:mm");
        var battery = info.BatteryText;
        if (battery.Length == 0)
        {
            Ui.Draw.Text(c, time, x + (InfoWidth - PixelFont.MeasureWidth(time, Ui.Draw.Small)) / 2, Ui.Draw.CenterY(Ui.Draw.Small), Ui.Draw.Small, pal.Text);
            return;
        }
        Ui.Draw.Text(c, time, x + (InfoWidth - PixelFont.MeasureWidth(time, Ui.Draw.Small)) / 2, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Text);
        var low = info.BatteryPercent is <= 15 && !info.Charging;
        Ui.Draw.Text(c, battery, x + (InfoWidth - PixelFont.MeasureWidth(battery, Ui.Draw.Small)) / 2, Ui.Draw.Line2Y, Ui.Draw.Small,
            low ? pal.Bad : pal.Dim, InfoWidth);
    }

    /// <summary>
    /// Cover, title over artist, straight on black (no box: the bar showed its near-black grey as black);
    /// tapping it plays / pauses like the PLAY button.
    /// </summary>
    private static void NowPlayingBox(Canvas c, int x, Input.NowPlaying m, Palette pal, BarAction onTap)
    {
        var textX = x + 14;
        if (m.Art is { } art)
        {
            Ui.Draw.Picture(c.Image, art, x, Ui.Draw.BoxTop, Ui.Draw.Radius, Rgb.Black); // cover, flush left, rounded corners
            textX = x + art.Width + 12;
        }
        var textW = x + NowPlayingWidth - 14 - textX;
        Ui.Draw.ScrollText(c, m.Title.Length > 0 ? m.Title : m.Artist, textX, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Text, textW);
        if (m.Title.Length > 0 && m.Artist.Length > 0)
            Ui.Draw.ScrollText(c, m.Artist, textX, Ui.Draw.Line2Y, Ui.Draw.Small, pal.Dim, textW);
        c.Regions.Add(new HitRegion(x, NowPlayingWidth, onTap));
    }
}
