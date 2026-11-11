using WoWTouchBar.Core.Protocol;

namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>
/// The Touch Bar app (Phase 3): which page is showing, what a tap does, and how each page is drawn.
/// Pure (no devices, no Windows APIs) so every layout and tap is unit-tested.
///
/// While WoW is focused: a WoW page on the left, page tabs on the right. Tapping a tab switches page;
/// tapping the quest page shows the next unfinished objective; swiping left/right flips pages.
/// Nothing is ever sent to WoW.
/// While WoW is not focused (or not running): the system page - volume, brightness, media, and a toggle
/// to Esc / F1-F12. Those taps come back as <see cref="PressKey"/> / <see cref="ChangeBrightness"/> for
/// the caller to carry out through SystemKeys / Brightness.
/// </summary>
public sealed class BarController
{
    public const int Width = DfrProtocol.TouchBarWidth, Height = DfrProtocol.TouchBarHeight;

    public PageId Page { get; private set; } = PageId.Quest;
    /// <summary>What the outside-WoW page shows: media, F1-F12, or the player's own buttons.</summary>
    public SystemMode Mode { get; private set; }
    public bool FunctionKeys => Mode == SystemMode.FunctionKeys;

    /// <summary>The player's own buttons (tray > Settings). Without any, "My keys" is skipped.</summary>
    public IReadOnlyList<Input.CustomButton> CustomButtons
    {
        get => _custom;
        set
        {
            _custom = value ?? [];
            if (Mode == SystemMode.Custom && _custom.Count == 0) Mode = SystemMode.Media;
            Version++;
        }
    }
    private IReadOnlyList<Input.CustomButton> _custom = [];

    /// <summary>Highlight colour for tabs, buttons and progress bars (tray > Colour).</summary>
    public AccentColour Accent
    {
        get => _accent;
        set { if (value == _accent || !Enum.IsDefined(value)) return; _accent = value; Version++; }
    }
    private AccentColour _accent;

    /// <summary>Icon for a "My keys" button (programs; the tray app reads them from Windows), or null for text only.</summary>
    public Func<Input.CustomButton, PixelBuffer?>? IconFor { get; set; }

    /// <summary>The mode the toggle switches to next (its label shows this).</summary>
    public SystemMode NextMode => Mode switch
    {
        SystemMode.Media => SystemMode.FunctionKeys,
        SystemMode.FunctionKeys => _custom.Count > 0 ? SystemMode.Custom : AfterKeys,
        SystemMode.Custom => AfterKeys,
        SystemMode.Notes => HasTimers ? SystemMode.Timers : SystemMode.Media,
        _ => SystemMode.Media,
    };

    /// <summary>Outside WoW the toggle offers NOTES / TIMERS (separate pages) only while there are some.</summary>
    private bool HasNotes => Pad is { } p && p.Notes.Count > 0;
    private bool HasTimers => Pad is { } p && p.Timers.Count > 0;
    private SystemMode AfterKeys => HasNotes ? SystemMode.Notes : HasTimers ? SystemMode.Timers : SystemMode.Media;

    public static string ModeLabel(SystemMode m) => m switch
    {
        SystemMode.FunctionKeys => "F1-F12",
        SystemMode.Custom => "MY KEYS",
        SystemMode.Notes => "NOTES",
        SystemMode.Timers => "TIMERS",
        _ => "MEDIA",
    };
    /// <summary>Bumped by every tap that changes what is shown, so the caller knows to redraw.</summary>
    public int Version { get; private set; }

    private int _objectiveOffset;
    private uint? _questId;
    private BarView? _last;
    private bool _lastWasWow;

    /// <summary>False on Macs with a physical Esc key (see MacHardware.HasPhysicalEscape).</summary>
    public bool ShowEscape { get; }

    public BarController(PageId page = PageId.Quest, bool showEscape = true, bool functionKeys = false,
        SystemMode? mode = null, IReadOnlyList<Input.CustomButton>? customButtons = null)
    {
        Page = Enum.IsDefined(page) ? page : PageId.Quest;
        ShowEscape = showEscape;
        _custom = customButtons ?? [];
        Mode = mode ?? (functionKeys ? SystemMode.FunctionKeys : SystemMode.Media);
        if (!Enum.IsDefined(Mode) || (Mode == SystemMode.Custom && _custom.Count == 0)) Mode = SystemMode.Media;
    }

    /// <summary>Selects a WoW page from outside the bar (tray menu).</summary>
    public void ShowPage(PageId page)
    {
        if (page == Page || !Enum.IsDefined(page)) return;
        Page = page;
        _objectiveOffset = 0;
        Version++;
    }

    private List<PageId> _visiblePages = [.. Enum.GetValues<PageId>()];

    /// <summary>Alert kind → bit in the DISPLAY message (the in-game options); everything is on until the addon says.</summary>
    public static bool AlertEnabled(DisplayMessage? d, AlertKind kind) => d is null || (d.Alerts & (1 << (int)kind)) != 0;

    /// <summary>The in-game Windows panel ("•••" on the WoW pages) is showing; <see cref="QuickKeys"/> = My keys instead of media.</summary>
    public bool QuickPanel { get; private set; }
    public bool QuickKeys { get; private set; }
    /// <summary>The in-game panel returns to the WoW pages after this long without a tap.</summary>
    public static readonly TimeSpan QuickPanelTimeout = TimeSpan.FromSeconds(15);
    private TimeSpan _quickTouched;

    private readonly AlertTracker _alerts = new();
    private readonly ChatTracker _chats = new();
    private readonly Dictionary<string, TimeSpan> _scrollStarts = []; // when each scrolling text appeared

    /// <summary>Notes and timers (Phase 7); null = none (tests, older callers).</summary>
    public Notes.Scratchpad? Pad { get; set; }
    /// <summary>Wall clock for timers (they end at a clock time); tests replace it.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    private int _noteIndex, _timerPage;
    private Notes.PadTimer? _shownTimerAlert; // the "timer done" alert on the bar right now, if any
    private (string Key, TimeSpan Since)? _timerAlertStart; // when that alert first showed (drives the flashing)
    /// <summary>How a finished timer shows (tray > Timer alert): fireworks, laser, rainbow or a plain flash.</summary>
    public AlertStyle AlertStyle { get => _alertStyle; set { if (Enum.IsDefined(value)) _alertStyle = value is AlertStyle.Rainbow or AlertStyle.Warp ? AlertStyle.Flash : value; } }
    private volatile AlertStyle _alertStyle = AlertStyle.Fireworks;
    /// <summary>"NOTE ADDED" / "TIMER STARTED" stays this long after something is added (from anywhere).</summary>
    public static readonly TimeSpan AddedTime = TimeSpan.FromSeconds(2.5);
    private int _seenAdded;
    private (string Title, string Detail, TimeSpan At)? _toast;
    private bool _shownToast;
    private bool _renderedOnce;
    /// <summary>A finished timer flashes the whole bar for <see cref="FlashFor"/>, again every <see cref="FlashEvery"/> until tapped.</summary>
    public static readonly TimeSpan FlashFor = TimeSpan.FromSeconds(4), FlashEvery = TimeSpan.FromSeconds(30);
    /// <summary>Flashes this many times (now, +30 s, +60 s), then the banner just waits for a tap.</summary>
    public const int FlashTimes = 3;
    public const double FlashesPerSecond = 2.5;

    /// <summary>Whether the bar is in its bright "flash" half right now, <paramref name="shown"/> after the alert appeared (pure; tested).</summary>
    public static bool FlashOn(TimeSpan shown)
    {
        if (shown < TimeSpan.Zero || shown >= FlashEvery * FlashTimes) return false;
        var inCycle = TimeSpan.FromTicks(shown.Ticks % FlashEvery.Ticks);
        return inCycle < FlashFor && inCycle.TotalSeconds * FlashesPerSecond % 1 < 0.5;
    }
    /// <summary>How long "REMOVED - TAP TO UNDO" stays after a note / timer is swiped away.</summary>
    public static readonly TimeSpan UndoTime = TimeSpan.FromSeconds(5);
    private (BarAction Item, TimeSpan At)? _undo; // the last thing swiped away
    private bool _shownUndo;
    private GameState? _lastState;
    private TimeSpan _lastNow;

    /// <summary>
    /// Draws the bar. <paramref name="now"/> drives alerts and the queue / ready-check timers (any steadily
    /// increasing clock; tests pass fixed values).
    /// </summary>
    public BarView Render(GameState? state, LinkStatus link, bool wowFocused, TimeSpan now = default, Input.SystemInfo? info = null)
    {
        if (state?.Quest?.QuestId != _questId) { _questId = state?.Quest?.QuestId; _objectiveOffset = 0; }
        _lastState = state;
        _lastNow = now;
        Alert? alert = null;
        ChatBanner? chat = null;
        var queueSeconds = state?.QueueSeconds(now);
        var wall = Clock();
        var pad = Pad is { } p ? new PadView(p.Notes, p.Timers, wall, _noteIndex, _timerPage) : null;
        // In-game options: hidden pages are skipped (a hidden current page moves to the first visible one).
        // NOTE / TIMER tabs only exist while there is something on them.
        var tabs = WowPages.VisibleTabs(state?.Display, pad?.Notes.Count > 0, pad?.Timers.Count > 0);
        _visiblePages = tabs.Select(t => t.Page).ToList();
        if (!_visiblePages.Contains(Page)) { Page = _visiblePages[0]; _objectiveOffset = 0; }

        if (state is not null && link is LinkStatus.Connected or LinkStatus.Stale)
        {
            _alerts.Update(state, now);
            if (!_shownUndo) alert = _alerts.Current(state, now);
            if (!_shownUndo && alert is not null && !AlertEnabled(state.Display, alert.Kind)) alert = null;
            if (!wowFocused && alert is { Persistent: false }) alert = null; // short alerts only matter in game (the undo banner is persistent)
        }
        // Just swiped something away: a few seconds to undo, before anything else.
        _shownUndo = false;
        if (_undo is { } swiped && now - swiped.At < UndoTime && now >= swiped.At)
        {
            _shownUndo = true;
            alert = new Alert(AlertKind.Removed, swiped.Item switch
            {
                RemoveNote => "NOTE REMOVED",
                _ => "TIMER REMOVED",
            }, "TAP TO UNDO", Persistent: true);
        }
        else _undo = null;
        // A timer that ended: stays until tapped, in game and outside it (game alerts come first).
        _shownTimerAlert = null;
        if (alert is null && Pad?.DueAlert(wall) is { } due)
        {
            _shownTimerAlert = due;
            if (_timerAlertStart?.Key != due.Key) _timerAlertStart = (due.Key, now);
            alert = new Alert(AlertKind.TimerDone, "TIMER DONE", due.Label, Persistent: true);
        }
        // Something was just added (tray, quick-add, in game): a short confirmation.
        if (Pad?.LastAdded is { } added && added.Seq != _seenAdded)
        {
            if (_renderedOnce) _toast = (added.Title, added.Detail, now); // not for what was there before the bar started
            _seenAdded = added.Seq;
        }
        _shownToast = false;
        if (alert is null && _toast is { } toast && now - toast.At < AddedTime && now >= toast.At)
        {
            _shownToast = true;
            alert = new Alert(AlertKind.Added, toast.Title, toast.Detail, Persistent: true);
        }
        if (state is not null && link is LinkStatus.Connected or LinkStatus.Stale)
        {
            if (alert is null)
                chat = _chats.Current(state, now, wowFocused ? WowPages.ChatChars(queueSeconds is not null, tabs.Count, state.Utility is { } u && WowPages.ShowUtility(state.Display) ? WowPages.UtilityChipWidthFor(u) : 0) : SystemPage.ChatChars);
        }
        var c = new Canvas { Now = now, ScrollStarts = _scrollStarts };
        _lastWasWow = wowFocused;
        // The in-game panel closes by itself after a while untouched, so the quest info comes back mid-fight.
        if (QuickPanel && (!wowFocused || now - _quickTouched > QuickPanelTimeout)) QuickPanel = false;
        // The last note / timer went: back to media.
        if ((Mode == SystemMode.Notes && !HasNotes) || (Mode == SystemMode.Timers && !HasTimers)) Mode = SystemMode.Media;
        if (!wowFocused) SystemPage.Draw(c, Mode, ModeLabel(NextMode), ShowEscape, alert, chat, info, _custom, IconFor, accent: _accent, pad: pad);
        else if (QuickPanel)
            SystemPage.Draw(c, QuickKeys ? SystemMode.Custom : SystemMode.Media, "WOW", ShowEscape,
                alert is { Persistent: true } ? alert : null, chat, info, _custom, IconFor, inGame: true, accent: _accent);
        else WowPages.Draw(c, state, link, Page, _objectiveOffset, alert, queueSeconds, chat, _accent, pad);
        foreach (var gone in _scrollStarts.Keys.Where(k => !c.ScrollKeys.Contains(k)).ToList()) _scrollStarts.Remove(gone);
        if (_shownTimerAlert is null) _timerAlertStart = null;
        else if (_timerAlertStart is { } fs)
        {
            // Hard to miss: the whole bar flashes (bright with the timer's name, then the normal banner), a few seconds
            // now and again every half minute until tapped. Tapping anywhere while it flashes clears it.
            var shown = now - fs.Since;
            var inCycle = TimeSpan.FromTicks(Math.Max(0, shown.Ticks) % FlashEvery.Ticks);
            if (inCycle < FlashFor && shown < FlashEvery * FlashTimes)
            {
                c.Animating = true;
                Effects.Draw(c, AlertStyle, inCycle, "TIMER DONE - " + _shownTimerAlert.Label, Palette.For(_accent));
                c.Regions.Clear();
                c.Regions.Add(new HitRegion(0, Width, new DismissAlert()));
            }
            else if (shown < FlashEvery * FlashTimes) c.Animating = true; // keep redrawing so the next flash starts on time
        }
        _renderedOnce = true;
        return _last = new BarView(c.Image, c.Regions, c.Texts, c.Animating || _shownUndo || _shownToast);
    }

    /// <summary>Handles a tap at bar pixel x. Returns an action for the caller (keys, brightness) or null.</summary>
    public BarAction? Tap(int x)
    {
        var action = _last?.HitTest(x);
        if (QuickPanel) _quickTouched = _lastNow; // any tap on the panel keeps it open
        switch (action)
        {
            case ShowPage p:
                if (p.Page != Page) { Page = p.Page; _objectiveOffset = 0; Version++; }
                return null;
            case NextObjective:
                _objectiveOffset++;
                Version++;
                return null;
            case RemoveNote or RemoveTimer:
                if (action is RemoveNote n) Pad?.Remove(n.Note);
                else if (action is RemoveTimer t) Pad?.Remove(t.Timer);
                _undo = (action, _lastNow);
                _noteIndex = 0;
                Version++;
                return null;
            case NextNote:
                _noteIndex++;
                Version++;
                return null;
            case NextTimers:
                _timerPage++;
                Version++;
                return null;
            case DismissAlert when _shownUndo && _undo is { } undo:
                if (undo.Item is RemoveNote rn) Pad?.Restore(rn.Note);
                else if (undo.Item is RemoveTimer rt) Pad?.Restore(rt.Timer);
                _undo = null;
                Version++;
                return null;
            case DismissAlert when _shownToast:
                _toast = null;
                Version++;
                return null;
            case DismissAlert:
                if (_shownTimerAlert is { } timer) { Pad?.Acknowledge(timer); _shownTimerAlert = null; }
                else if (_lastState is not null) _alerts.Dismiss(_lastState, _lastNow);
                Version++;
                return null;
            case DismissChat:
                _chats.Dismiss();
                Version++;
                return null;
            case ToggleQuickPanel:
                QuickPanel = !QuickPanel;
                QuickKeys = false;
                _quickTouched = _lastNow;
                Version++;
                return null;
            case ToggleQuickKeys:
                QuickKeys = !QuickKeys;
                _quickTouched = _lastNow;
                Version++;
                return null;
            case ToggleFunctionKeys:                Mode = NextMode;
                Version++;
                return null;
            default:
                return action; // PressKey / ChangeBrightness / null
        }
    }

    /// <summary>The volume slider under bar pixel <paramref name="x"/> in the last drawn view, if any.</summary>
    public VolumeSlider? SliderAt(int x) =>
        _last?.Regions.Where(r => r.Action is VolumeSlider && r.Distance(x) <= BarView.TapSlack).Select(r => (VolumeSlider)r.Action).FirstOrDefault();

    /// <summary>Swipe on a WoW page: +1 = next page (finger moved right), -1 = previous.</summary>
    /// <summary>A swipe switches page on the WoW pages. (<paramref name="startX"/>: where the finger went down; unused.)</summary>
    public void Swipe(int direction, int startX = -1)
    {
        if (!_lastWasWow || QuickPanel || direction == 0 || _visiblePages.Count == 0) return;
        var i = Math.Max(0, _visiblePages.IndexOf(Page));
        Page = _visiblePages[((i + direction) % _visiblePages.Count + _visiblePages.Count) % _visiblePages.Count];
        _objectiveOffset = 0;
        Version++;
    }

    /// <summary>
    /// The objective to show: the addon's current one, then (for each tap) the next unfinished one.
    /// Finished objectives are skipped while cycling; if all are finished the current one stays.
    /// </summary>
    public static ObjectiveMessage? PickObjective(GameState state, int offset)
    {
        var current = state.CurrentObjective;
        if (offset == 0 || current is null) return current;
        var open = state.Objectives.OfType<ObjectiveMessage>().Where(o => !o.Done).ToList();
        if (open.Count == 0) return current;
        var start = open.FindIndex(o => o.Index == current.Index);
        var i = start < 0 ? offset - 1 : start + offset;
        return open[((i % open.Count) + open.Count) % open.Count];
    }
}
