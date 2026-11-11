using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.Versioning;
using WoWTouchBar.Core.Bridge;
using WoWTouchBar.Core.Capture;
using WoWTouchBar.Core.Input;
using WoWTouchBar.Core.Protocol;
using WoWTouchBar.Core.TouchBar;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.Core.Host;

/// <summary>
/// The companion's main loop, shared by the tray app and the console bridge:
/// WoW strip → protocol v1 → game state → Touch Bar pages; Touch Bar taps → pages / system keys.
/// Runs on the calling thread until cancelled; touch input is read on a background thread.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class CompanionHost
{
    public const int SampleIntervalMs = 150;
    public const int AnimationFrameMs = 50;           // ~20 redraws a second, only while text is scrolling
    private bool _animating;
    private SystemInfo? _lastInfo;          // ~6-7 captures per second (addon holds each frame 200 ms)
    public const double StaleAfterSeconds = 3;        // grey out
    public const double NoSignalAfterSeconds = 60;    // stop showing old data
    private const int SnapshotMinTimingRuns = 40;

    private readonly FileLog _log;
    private readonly bool _trace;
    private readonly string _settingsPath;
    private readonly BridgeSession _session = new();
    private readonly ConcurrentQueue<Gesture> _gestures = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly INowPlayingSource? _nowPlaying;
    private readonly IMediaControl? _media;
    private ITouchBarDisplay? _touchBar;
    private CancellationToken _ct;
    private Settings _saved;
    private string? _lastDrawn;
    private bool? _wasForeground;

    public BarController Bar { get; }
    /// <summary>Notes and timers (Phase 7): the tray window edits it, the bar shows it; saved as notes.json next to settings.json.</summary>
    public Notes.Scratchpad Pad { get; }
    private NotesMessage? _gameNotes;
    private TimersMessage? _gameTimers;
    public bool TouchBarFound => _touchBar is not null;
    public bool Paused { get => _paused; private set => _paused = value; }
    private volatile bool _paused;
    /// <summary>Short machine-readable state ("connected", "waiting-for-wow", ...), also logged.</summary>
    public string State { get; private set; } = "starting";
    /// <summary>Plain-language status for the tray tooltip / menu.</summary>
    public string StatusText => Describe(State, TouchBarFound, Paused);
    public event Action? StatusChanged;

    public CompanionHost(FileLog log, bool trace = false, string? settingsPath = null, INowPlayingSource? nowPlaying = null,
        Func<CustomButton, PixelBuffer?>? buttonIcons = null, IMediaControl? media = null)
    {
        _log = log;
        _trace = trace;
        _nowPlaying = nowPlaying;
        _settingsPath = settingsPath ?? Settings.DefaultPath;
        _saved = Settings.Load(_settingsPath, out var warning);
        if (warning is not null) _log.Write($"warning: {warning}");
        Paused = _saved.Paused;
        _quickAddKey = _saved.QuickAddKey;
        var model = MacHardware.ReadModel();
        Bar = new BarController(_saved.Page, showEscape: !MacHardware.HasPhysicalEscape(model), mode: _saved.EffectiveMode, customButtons: _saved.CustomButtons) { Accent = _saved.Accent };
        Bar.IconFor = buttonIcons;
        Bar.AlertStyle = _saved.AlertStyle;
        Pad = Notes.Scratchpad.Load(Path.Combine(Path.GetDirectoryName(_settingsPath)!, "notes.json"));
        Bar.Pad = Pad;
        _media = media;
        _log.Write($"model={model ?? "unknown"} physicalEsc={!Bar.ShowEscape} page={Bar.Page} paused={Paused}");
    }

    /// <summary>Plain-language status (pure; tested).</summary>
    public static string Describe(string state, bool touchBarFound, bool paused)
    {
        if (!touchBarFound) return "Touch Bar driver not active - see the Touch Bar guide";
        if (paused) return "Paused";
        return state switch
        {
            "connected" => "Connected to WoW",
            "waiting-for-wow" => "Waiting for WoW",
            "wow-minimized" => "WoW is minimised",
            "wow-found-no-strip" => "WoW found - waiting for the addon",
            "stale" => "WoW strip not visible",
            "no-signal" => "No signal from WoW",
            "addon-incompatible" => "Addon and app versions don't match - update both",
            _ => "Starting",
        };
    }

    public void SetPaused(bool paused)
    {
        Paused = paused;
        _lastDrawn = null;
        _log.Write($"paused={paused}");
        _wake.Set();
        StatusChanged?.Invoke();
    }

    /// <summary>Thread-safe page request (tray menu); applied by the loop, which owns the BarController.</summary>
    public void ShowPage(PageId page) { _requestedPage = (int)page; _wake.Set(); }
    private volatile int _requestedPage = -1;

    /// <summary>Thread-safe colour change (tray > Colour); applied and saved by the loop.</summary>
    public void SetAccent(AccentColour accent) { _requestedAccent = (int)accent; _wake.Set(); }
    private volatile int _requestedAccent = -1;

    /// <summary>Thread-safe: new custom buttons from the Settings window; applied and saved by the loop.</summary>
    public void SetCustomButtons(IReadOnlyList<CustomButton> buttons) { _requestedButtons = buttons.Take(Shortcuts.MaxButtons).ToList(); _wake.Set(); }
    private volatile IReadOnlyList<CustomButton>? _requestedButtons;
    /// <summary>The saved custom buttons (for the Settings window to start from).</summary>
    public IReadOnlyList<CustomButton> CustomButtons => Bar.CustomButtons;

    /// <summary>The quick-add shortcut as text ("Ctrl+Alt+N"; "" = off). The tray registers it; saved in settings.json.</summary>
    public string QuickAddKey { get => _quickAddKey ?? Notes.Hotkey.Default.ToString(); set => _quickAddKey = value; }
    private volatile string? _quickAddKey;

    private void SetState(string value, string? detail = null)
    {
        if (value == State) return;
        State = value;
        _log.Write($"status={value}{(detail is null ? "" : " " + detail)}");
        StatusChanged?.Invoke();
    }

    public void Run(CancellationToken ct)
    {
        _ct = ct;
        WindowsScreenCapture.EnablePerMonitorDpiAwareness();
        _touchBar = TouchBarDisplays.OpenWindows(_log.Write); // WinUSB first, then the old driver
        StatusChanged?.Invoke();

        var reader = _session.Reader;
        _log.Write($"bridge start capture={reader.CaptureWidth}x{reader.CaptureHeight} interval={SampleIntervalMs}ms protocol=v{ProtocolV1.Version}");
        if (_touchBar is not null) new Thread(ReadTouches) { IsBackground = true, Name = "touch" }.Start();

        var clock = _clock; // one clock for frames, status timers and alerts
        var process = Process.GetCurrentProcess();
        var cpuAt = process.TotalProcessorTime;
        var statsAt = clock.Elapsed;
        var traceAt = clock.Elapsed;
        int traceImages = 0, traceFrames = 0, traceRejected = 0;
        var snapshotDir = Path.Combine(FileLog.DefaultDirectory, "captures");
        var snapshots = 0;
        var lastSnapshotAt = TimeSpan.FromSeconds(-10);
        var lastGeometry = "";
        if (_trace) { Directory.CreateDirectory(snapshotDir); foreach (var old in Directory.GetFiles(snapshotDir, "*.bmp")) File.Delete(old); }

        while (!ct.IsCancellationRequested)
        {
            if (Paused)
            {
                while (_gestures.TryDequeue(out _)) { } // touches do nothing while paused
                DrawBlank();
                Sleep(500);
                continue;
            }
            HandleGestures();
            SaveSettingsIfChanged();
            var window = WindowsScreenCapture.FindWowWindow();
            if (window is null) { SetState("waiting-for-wow"); Draw(LinkStatus.WaitingForWow, wowFocused: false); Pause(1000, LinkStatus.WaitingForWow, false); continue; }
            if (WindowsScreenCapture.IsMinimized(window)) { SetState("wow-minimized"); Draw(LinkStatus.Stale, wowFocused: false); Pause(1000, LinkStatus.Stale, false); continue; }

            var now = clock.Elapsed;
            var image = WindowsScreenCapture.CaptureClientTopLeft(window, reader.CaptureWidth, reader.CaptureHeight);
            var framesBefore = _session.Frames;
            var message = image is null ? null : _session.Feed(image, now);
            if (_trace && image is not null && snapshots < 80)
            {
                // Local-only snapshots of the captured corner to diagnose geometry problems (only when strip-like).
                var ok = _session.Frames > framesBefore;
                var geometry = $"{reader.LastColumns}-{reader.LastPitch:0.0}-{reader.LastContrast / 100}";
                var stripLike = reader.LastTimingRuns >= SnapshotMinTimingRuns;
                if ((ok && geometry != lastGeometry) || (!ok && stripLike && (now - lastSnapshotAt).TotalSeconds >= 1))
                {
                    var name = $"{DateTime.Now:HHmmss.fff}-{(ok ? "ok-" + geometry : "fail-" + _session.LastError)}.bmp";
                    try { image.SaveBmp(Path.Combine(snapshotDir, name)); snapshots++; lastSnapshotAt = now; } catch (IOException) { }
                    if (ok) lastGeometry = geometry;
                }
            }
            if (message is HelloMessage h) _log.Write($"hello flavour={h.Flavour} interface={h.Interface} addon={h.AddonVersion}");
            if (message is not null) LogChanges();
            // Notes / timers added in game: the pad takes each new id in once (contents never logged).
            if (_session.State.Notes is { } notes && !ReferenceEquals(notes, _gameNotes)) { Pad.ImportGameNotes(notes.Notes); _gameNotes = notes; }
            if (_session.State.Timers is { } timers && !ReferenceEquals(timers, _gameTimers)) { Pad.ImportGameTimers(timers.Timers); _gameTimers = timers; }

            var age = _session.LastFrameAt is { } at ? (now - at).TotalSeconds : double.PositiveInfinity;
            LinkStatus link;
            if (_session.AddonIncompatible && age > StaleAfterSeconds) { SetState("addon-incompatible", $"addon protocol v{_session.OtherVersion}, app v{ProtocolV1.Version}"); link = LinkStatus.AddonIncompatible; }
            else if (age <= StaleAfterSeconds) { SetState("connected", $"flavour={window.Flavour}"); link = LinkStatus.Connected; }
            else if (_session.LastFrameAt is null) { SetState("wow-found-no-strip"); link = LinkStatus.LookingForAddon; }
            else if (age <= NoSignalAfterSeconds) { SetState("stale", "(strip not visible)"); link = LinkStatus.Stale; }
            else { SetState("no-signal"); link = LinkStatus.NoSignal; }
            // Game data only while WoW has focus; the strip is still read in the background so switching back is instant.
            var foreground = WindowsScreenCapture.IsForeground(window);
            if (foreground != _wasForeground) { _log.Write($"focus={(foreground ? "wow" : "other")}"); _wasForeground = foreground; }
            Draw(link, foreground);

            if (_trace && (now - traceAt).TotalSeconds >= 1)
            {
                _log.Write($"trace images={_session.Images - traceImages} frames={_session.Frames - traceFrames} rejected={_session.Rejected - traceRejected} " +
                    $"cols={reader.LastColumns} pitch={reader.LastPitch:0.000} contrast={reader.LastContrast} runs={reader.LastTimingRuns} lastError={_session.LastError}");
                traceImages = _session.Images; traceFrames = _session.Frames; traceRejected = _session.Rejected;
                traceAt = now;
            }
            if ((now - statsAt).TotalSeconds >= 30)
            {
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var cpuPct = (cpu - cpuAt).TotalMilliseconds / (now - statsAt).TotalMilliseconds / Environment.ProcessorCount * 100;
                _log.Write($"stats images={_session.Images} frames={_session.Frames} rejected={_session.Rejected} malformed={_session.Malformed} " +
                    $"duplicates={_session.Reassembler.Duplicates} cols={reader.LastColumns} pitch={reader.LastPitch:0.00} contrast={reader.LastContrast} cpu={cpuPct:0.0}% " +
                    $"mem={process.WorkingSet64 / 1024 / 1024}MB lastError={_session.LastError}");
                _session.ResetCounters();
                traceImages = traceFrames = traceRejected = 0;
                cpuAt = cpu;
                statsAt = now;
            }
            Pause(SampleIntervalMs, link, foreground);
        }

        SaveSettingsIfChanged();
        _log.Write("bridge stop");
        try { _touchBar?.Clear(); } catch (Exception) { /* device gone */ }
        _touchBar?.Dispose();
    }

    /// <summary>
    /// Waits <paramref name="ms"/>, but while text is scrolling redraws every <see cref="AnimationFrameMs"/> so it glides.
    /// A tap ends the wait early (back to the loop, which handles it).
    /// </summary>
    private void Pause(int ms, LinkStatus link, bool wowFocused)
    {
        var until = _clock.Elapsed + TimeSpan.FromMilliseconds(ms);
        while (_animating && !_ct.IsCancellationRequested)
        {
            var left = until - _clock.Elapsed;
            if (left <= TimeSpan.Zero) return;
            if (WaitHandle.WaitAny([_wake, _ct.WaitHandle], (int)Math.Min(AnimationFrameMs, left.TotalMilliseconds)) != WaitHandle.WaitTimeout) return;
            Draw(link, wowFocused, reuseInfo: true);
        }
        var rest = until - _clock.Elapsed;
        if (rest > TimeSpan.Zero) Sleep((int)Math.Ceiling(rest.TotalMilliseconds));
    }

    private void Draw(LinkStatus link, bool wowFocused, bool reuseInfo = false)
    {
        if (_touchBar is null) return;
        // Scrolling frames between loop passes reuse the last battery / volume / song (no need to ask Windows 20 times a second).
        var info = reuseInfo && _lastInfo is { } last ? last with { Time = DateTime.Now } : ReadSystemInfo(wowFocused);
        _lastInfo = info;
        var view = Bar.Render(_session.State, link, wowFocused, _clock.Elapsed, info);
        _animating = view.Animating;
        // Redraw when anything visible changed (the picture itself, not just its text: the slider knob has none).
        var key = $"{wowFocused}|{link}|{Bar.Version}|{view.Image.ContentHash():X16}";
        if (key == _lastDrawn) return;
        try { _touchBar.Draw(view.Image); _lastDrawn = key; }
        catch (Exception ex) { _log.Write($"touchbar=draw-error {ex.Message}"); }
    }

    private SystemInfo ReadSystemInfo(bool wowFocused)
    {
        var (battery, charging) = Power.Read();
        // Volume for the slider, only while a Windows page is showing (no need to ask Windows otherwise).
        var windowsPage = !wowFocused || Bar.QuickPanel;
        var volume = windowsPage ? Volume.Read() : null;
        // Brightness for its slider: asking Windows (WMI) is slow, so at most every 3 s (and straight after the slider sets it).
        if (windowsPage && _clock.Elapsed - _brightnessReadAt > TimeSpan.FromSeconds(3))
        {
            _brightness = Brightness.Current();
            _brightnessReadAt = _clock.Elapsed;
        }
        return new SystemInfo(DateTime.Now, battery, charging, _nowPlaying?.Current, volume?.Percent, volume?.Muted ?? false, _brightness);
    }

    private void DrawBlank()
    {
        if (_touchBar is null || _lastDrawn == "paused") return;
        try { _touchBar.Draw(new PixelBuffer(DfrProtocol.TouchBarWidth, DfrProtocol.TouchBarHeight)); _lastDrawn = "paused"; }
        catch (Exception ex) { _log.Write($"touchbar=draw-error {ex.Message}"); }
    }

    private void SaveSettingsIfChanged()
    {
        if (!Pad.SaveIfChanged()) _log.Write("warning: notes.json could not be saved");
        var now = _saved with { Page = Bar.Page, Mode = Bar.Mode, FunctionKeys = false, Paused = Paused, CustomButtons = Bar.CustomButtons, Accent = Bar.Accent, QuickAddKey = _quickAddKey, AlertStyle = Bar.AlertStyle };
        if (now == _saved) return;
        if (now.Save(_settingsPath)) _saved = now;
    }

    // Touch input: read-only HID reads from the Touch Bar digitizer (reconnects if it goes away).
    private void ReadTouches()
    {
        var detector = new TapDetector();
        var clock = _clock; // one clock for frames, status timers and alerts
        var buffer = new byte[64];
        while (!_ct.IsCancellationRequested)
        {
            var path = HidTouch.FindPaths().FirstOrDefault();
            if (path is null) { _ct.WaitHandle.WaitOne(5000); continue; }
            try
            {
                using var handle = HidTouch.OpenRead(path);
                _log.Write("touch=reading");
                using var stop = _ct.Register(() => handle.Dispose());
                while (!_ct.IsCancellationRequested)
                {
                    var n = HidTouch.Read(handle, buffer);
                    if (n <= 0) break;
                    if (TouchReport.Parse(buffer.AsSpan(0, n)) is not { } contacts) continue;
                    foreach (var g in detector.Feed(contacts, clock.Elapsed)) { _gestures.Enqueue(g); _wake.Set(); }
                    // A finger still down: remember where (latest only) so the volume slider can follow a drag.
                    var active = detector.Active;
                    if (active.Count > 0)
                    {
                        var (_, startX, x) = active[0];
                        var last = _drag;
                        if (last is null || Math.Abs(last.Value.X - x) >= 3) { _drag = (startX, x); _wake.Set(); }
                    }
                }
            }
            catch (Exception ex) { _log.Write($"touch=error {ex.Message}"); }
            _ct.WaitHandle.WaitOne(2000);
        }
    }

    private volatile StrongBox<(int StartX, int X)>? _dragBox;
    private (int StartX, int X)? _drag { get => _dragBox?.Value; set => _dragBox = value is { } v ? new StrongBox<(int, int)>(v) : null; }
    private int? _lastSliderPercent;
    private int? _brightness;
    private TimeSpan _brightnessReadAt = TimeSpan.FromSeconds(-10);

    /// <summary>A slider moved: volume (Windows' audio API) or brightness (WMI). Neither is a key press.</summary>
    private string SetSlider(VolumeSlider slider, int x)
    {
        var percent = slider.PercentAt(x);
        if (!slider.Brightness) return Volume.Set(percent) is { } v ? $"volume {v}%" : "volume unavailable";
        return SetBrightness(percent);
    }

    /// <summary>The BRT - / BRT + buttons: one step from what the bar shows, through the same quick path as the slider.</summary>
    private string StepBrightness(int steps) =>
        (_brightness ?? Brightness.Current()) is { } now ? SetBrightness(Brightness.Next(now, steps)) : "brightness unsupported";

    /// <summary>The knob moves at once; Windows is told in the background (WMI is slow), only the latest value.</summary>
    private string SetBrightness(int percent)
    {
        _brightness = percent;
        _brightnessReadAt = _clock.Elapsed;
        Volatile.Write(ref _pendingBrightness, percent);
        if (_brightnessWorker is null)
        {
            _brightnessWorker = new Thread(ApplyBrightness) { IsBackground = true, Name = "brightness" };
            _brightnessWorker.Start();
        }
        _brightnessWake.Set();
        return $"brightness {percent}%";
    }

    private int _pendingBrightness = -1;
    private Thread? _brightnessWorker;
    private readonly AutoResetEvent _brightnessWake = new(false);

    /// <summary>Background: applies the newest requested brightness; reads it back once things go quiet (keeps the tracker right).</summary>
    private void ApplyBrightness()
    {
        while (!_ct.IsCancellationRequested)
        {
            if (WaitHandle.WaitAny([_brightnessWake, _ct.WaitHandle]) != 0) return;
            int last = -1;
            while (Interlocked.Exchange(ref _pendingBrightness, -1) is var p && p >= 0)
            {
                Brightness.Set(p, readBack: false);
                last = p;
            }
            // Quiet for a moment (finger lifted or still): one read-back with the final value.
            if (last >= 0 && !_brightnessWake.WaitOne(250) && Volatile.Read(ref _pendingBrightness) < 0) Brightness.Set(last, readBack: true);
            else if (last >= 0) _brightnessWake.Set(); // more came in: go round again
        }
    }

    /// <summary>A finger dragging on a slider sets the volume / brightness as it moves (no key press).</summary>
    private void HandleDrag()
    {
        if (Interlocked.Exchange(ref _dragBox, null) is not { } box) return;
        var (startX, x) = box.Value;
        if (Bar.SliderAt(startX) is not { } slider) return;
        var percent = slider.PercentAt(x);
        if (percent == _lastSliderPercent) return;
        _lastSliderPercent = percent;
        SetSlider(slider, x);
    }

    private void HandleGestures()
    {
        HandleDrag();
        var requested = Interlocked.Exchange(ref _requestedPage, -1);
        if (requested >= 0) { Bar.ShowPage((PageId)requested); _log.Write($"tray page={Bar.Page}"); }
        var accent = Interlocked.Exchange(ref _requestedAccent, -1);
        if (accent >= 0) { Bar.Accent = (AccentColour)accent; _log.Write($"colour={Bar.Accent}"); }
        if (Interlocked.Exchange(ref _requestedButtons, null) is { } buttons) { Bar.CustomButtons = buttons; _log.Write($"custom buttons={buttons.Count}"); }
        while (_gestures.TryDequeue(out var g))
        {
            if (Bar.SliderAt(g.StartX) is { } slider)
            {
                // Tap or drag on a slider: volume / brightness follows the finger; no page swipe / tap action.
                var outcome = SetSlider(slider, g.EndX);
                _lastSliderPercent = null;
                _log.Write($"slider {outcome}");
                continue;
            }
            if (g.IsTap)
            {
                var action = Bar.Tap(g.EndX);
                var outcome = action switch
                {
                    PressKey { Key: SystemKey.PlayPause or SystemKey.NextTrack or SystemKey.PreviousTrack } k => $"{k.Key} {(SystemKeys.Press(k.Key, out var mr) ? mr : mr)}{RefreshMedia()}",
                    PressKey k => $"{k.Key} {(SystemKeys.Press(k.Key, out var r) ? r : r)}",
                    ChangeBrightness c => StepBrightness(c.Steps),
                    QuickControl q => Quick(q.Kind),
                    RunShortcut s when s.Index < Bar.CustomButtons.Count => $"my key {s.Index + 1}: {Shortcuts.Run(Bar.CustomButtons[s.Index])}",
                    _ => $"page={Bar.Page} fkeys={Bar.FunctionKeys}",
                };
                _log.Write($"tap x={g.EndX} {outcome}");
            }
            else if (g.SwipeDirection != 0)
            {
                Bar.Swipe(g.SwipeDirection, g.StartX);
                _log.Write($"swipe {(g.SwipeDirection > 0 ? "right" : "left")} page={Bar.Page}");
            }
        }
    }

    private readonly Dictionary<string, string> _lastLines = [];

    /// <summary>Ask for the playing state again right away, so play / pause flips without waiting for the next poll.</summary>
    private string RefreshMedia() { _media?.Refresh(); return ""; }

    /// <summary>In-game panel controls: Windows' audio / media APIs, never a key press (safe while WoW is in front).</summary>
    private string Quick(QuickKind kind)
    {
        switch (kind)
        {
            case QuickKind.VolumeUp: return Volume.Change(+1) is { } up ? $"volume {up}%" : "volume unavailable";
            case QuickKind.VolumeDown: return Volume.Change(-1) is { } down ? $"volume {down}%" : "volume unavailable";
            case QuickKind.Mute: return Volume.ToggleMute() is { } m ? (m ? "muted" : "unmuted") : "volume unavailable";
            case QuickKind.PlayPause: _media?.PlayPause(); break;
            case QuickKind.Next: _media?.Next(); break;
            case QuickKind.Previous: _media?.Previous(); break;
        }
        RefreshMedia();
        return _media is null ? "media control unavailable" : $"media {kind}";
    }

    private void LogChanges()
    {
        foreach (var (key, line) in StateLines(_session.State))
            if (!_lastLines.TryGetValue(key, out var last) || last != line) { _log.Write(line); _lastLines[key] = line; }
    }

    /// <summary>
    /// The state lines the log may contain (pure; tested). Deliberately limited to player / quest / utility /
    /// status / reputation: chat text and senders are never logged.
    /// </summary>
    public static IEnumerable<(string Key, string Line)> StateLines(GameState st)
    {
        if (st.Player is { } p)
            yield return ("player", $"player level={p.Level} xp={p.Xp}/{p.XpMax} ({p.XpPercent:0.0}%) rested={p.Rested} max={p.MaxLevel}");
        if (st.Quest is { } q)
        {
            var o = st.CurrentObjective;
            var objectives = string.Join(" | ", st.Objectives.Select(x => x is null ? "?" : x.Numeric ? $"{x.Have}/{x.Need} {x.Label}" : x.Label));
            yield return ("quest", q.None ? "quest none" : $"quest id={q.QuestId} \"{q.Title}\" source={q.Source} ready={q.ReadyForTurnIn} current={o?.Index} [{objectives}]");
        }
        if (st.Utility is { } u)
            yield return ("utility", $"utility money={u.Money} bags={u.BagsFree}/{u.BagsTotal} durability={u.DurabilityLowest}");
        if (st.Status is { } s)
            yield return ("status", $"status mail={s.NewMail} readyCheck={s.ReadyCheck} proposal={s.Proposal} queued={s.Queued}");
        if (st.Reputation is { } r)
            yield return ("reputation", r.None ? "reputation none" : $"reputation \"{r.Name}\" {r.Standing} {r.Value}/{r.Max}");
    }
    /// <summary>Waits up to ms, waking early on a touch / tray action (so taps feel instant) or on cancel.</summary>
    private void Sleep(int ms) => WaitHandle.WaitAny([_wake, _ct.WaitHandle], ms);
}
