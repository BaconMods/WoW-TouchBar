namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>WoW pages, switched by the tabs on the right of the bar.</summary>
public enum PageId { Quest, Xp, Bags, Reputation, Notes, Timers }

/// <summary>Keys the system page can press (only while WoW is not focused - see SystemKeys).</summary>
public enum SystemKey
{
    Escape, F1, F2, F3, F4, F5, F6, F7, F8, F9, F10, F11, F12,
    VolumeDown, VolumeUp, Mute, PreviousTrack, PlayPause, NextTrack,
}

/// <summary>What a tap asks for. Page / objective / layout changes stay inside the companion.</summary>
public abstract record BarAction;
public sealed record ShowPage(PageId Page) : BarAction;
public sealed record NextObjective : BarAction;
/// <summary>NOTE page: show the next note. TIMER page: show the next few timers.</summary>
public sealed record NextNote : BarAction;
public sealed record NextTimers : BarAction;
/// <summary>The small X beside a note / timer: deletes it (a few seconds to undo).</summary>
public sealed record RemoveNote(WoWTouchBar.Core.Notes.PadNote Note) : BarAction;
public sealed record RemoveTimer(WoWTouchBar.Core.Notes.PadTimer Timer) : BarAction;
public sealed record ToggleFunctionKeys : BarAction;
public sealed record DismissAlert : BarAction;
public sealed record DismissChat : BarAction;
/// <summary>Runs the player's own button number <paramref name="Index"/> (tray > Settings); done by the caller via Shortcuts.Run.</summary>
public sealed record RunShortcut(int Index) : BarAction;

/// <summary>In-game "•••" panel: Windows controls that are not key presses (Core Audio volume, media session).</summary>
public enum QuickKind { VolumeDown, VolumeUp, Mute, PlayPause, Next, Previous }
public sealed record QuickControl(QuickKind Kind) : BarAction;
/// <summary>The "•••" button on WoW pages / the WOW button on the panel: switch between them.</summary>
public sealed record ToggleQuickPanel : BarAction;
/// <summary>The volume slider: drag or tap along the track (<paramref name="TrackLeft"/>, <paramref name="TrackWidth"/>) to set the volume.</summary>
/// <paramref name="Brightness"/>: the brightness slider (shown when nothing is playing) instead of volume.
public sealed record VolumeSlider(int TrackLeft, int TrackWidth, bool Brightness = false) : BarAction
{
    /// <summary>Volume % for a finger at bar pixel <paramref name="x"/> (clamped to the track; pure, tested).</summary>
    public int PercentAt(int x) => TrackWidth <= 0 ? 0 : Math.Clamp((int)Math.Round((x - TrackLeft) * 100.0 / TrackWidth), 0, 100);
}
/// <summary>On the in-game panel: media controls ↔ your program / website / folder buttons.</summary>
public sealed record ToggleQuickKeys : BarAction;

/// <summary>What the outside-WoW page shows; the toggle cycles Media → F1-F12 → My keys (only if there are any).</summary>
public enum SystemMode { Media, FunctionKeys, Custom, Notes, Timers }
public sealed record PressKey(SystemKey Key) : BarAction;
public sealed record ChangeBrightness(int Steps) : BarAction;

/// <summary>A tappable span of the bar (full height).</summary>
public readonly record struct HitRegion(int X, int Width, BarAction Action)
{
    public bool Contains(int x) => x >= X && x < X + Width;
    public int Distance(int x) => Contains(x) ? 0 : Math.Min(Math.Abs(x - X), Math.Abs(x - (X + Width - 1)));
}

/// <summary>A rendered bar, the regions a tap can hit, and the texts shown (in drawing order).</summary>
/// <param name="Animating">Something is moving (scrolling text): redraw often until a render says false.</param>
public sealed record BarView(PixelBuffer Image, IReadOnlyList<HitRegion> Regions, IReadOnlyList<string> Texts, bool Animating = false)
{
    /// <summary>All texts joined with " | " - compact for tests and logs.</summary>
    public string Summary => string.Join(" | ", Texts);

    /// <summary>Fingers are wide: a tap up to this many pixels outside a button still counts.</summary>
    public const int TapSlack = 12;

    public BarAction? HitTest(int x)
    {
        HitRegion? best = null;
        foreach (var r in Regions)
            if (r.Distance(x) <= TapSlack && (best is null || r.Distance(x) < best.Value.Distance(x))) best = r;
        return best?.Action;
    }
}
