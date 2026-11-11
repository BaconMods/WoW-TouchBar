namespace WoWTouchBar.Core.TouchBar;

/// <summary>One finger slot of a Touch Bar touch report.</summary>
public readonly record struct Contact(int Id, bool Touching, int RawX)
{
    /// <summary>Horizontal position in Touch Bar pixels (0 = left edge).</summary>
    public int PixelX(int barWidth = DfrProtocol.TouchBarWidth) =>
        (int)Math.Round((double)RawX * (barWidth - 1) / TouchReport.MaxRawX);
}

/// <summary>
/// Parses the Touch Bar digitizer's 57-byte input report (measured 2026-11-09, see docs/04_FEASIBILITY.md W8):
/// byte 0 = report id (0); then 11 slots of 4 bytes [flags, X lo, X hi, Y], where flags low nibble = contact id,
/// 0x10 / 0x20 = touch / in range; then 12 vendor bytes (ignored). X is 0..32767 left to right.
/// Untouched slots keep their last position, so only the flags say whether a finger is down.
/// </summary>
public static class TouchReport
{
    public const int Length = 57;
    public const int Slots = 11;
    public const int MaxRawX = 32767;
    private const byte TouchFlags = 0x30;

    public static IReadOnlyList<Contact>? Parse(ReadOnlySpan<byte> report)
    {
        if (report.Length < 1 + Slots * 4) return null;
        var contacts = new List<Contact>(Slots);
        for (var s = 0; s < Slots; s++)
        {
            var o = 1 + s * 4;
            var flags = report[o];
            var x = report[o + 1] | report[o + 2] << 8;
            contacts.Add(new Contact(flags & 0x0F, (flags & TouchFlags) != 0, Math.Min(x, MaxRawX)));
        }
        return contacts;
    }
}

/// <summary>A finished touch: where it started and ended (bar pixels) and how long it took.</summary>
public readonly record struct Gesture(int StartX, int EndX, TimeSpan Duration)
{
    public bool IsTap => Math.Abs(EndX - StartX) <= TapDetector.MaxTapMovement && Duration <= TapDetector.MaxTapDuration;
    public int SwipeDirection => IsTap || Math.Abs(EndX - StartX) < TapDetector.MinSwipeDistance ? 0 : Math.Sign(EndX - StartX);
}

/// <summary>Turns a stream of touch reports into finished gestures (finger down → finger up), per contact id.</summary>
public sealed class TapDetector
{
    public const int MaxTapMovement = 40;            // px on the 2008 px bar
    public static readonly TimeSpan MaxTapDuration = TimeSpan.FromMilliseconds(800);
    public const int MinSwipeDistance = 150;

    private readonly Dictionary<int, (int StartX, int LastX, TimeSpan Since)> _down = new();
    private readonly int _barWidth;

    public TapDetector(int barWidth = DfrProtocol.TouchBarWidth) => _barWidth = barWidth;

    /// <summary>Fingers currently down: where each started and where it is now (for dragging the volume slider).</summary>
    public IReadOnlyList<(int Id, int StartX, int X)> Active => _down.Select(d => (d.Key, d.Value.StartX, d.Value.LastX)).ToList();

    /// <summary>Feeds one report; returns gestures whose finger lifted in it.</summary>
    public IReadOnlyList<Gesture> Feed(IReadOnlyList<Contact> contacts, TimeSpan now)
    {
        var done = new List<Gesture>();
        foreach (var c in contacts)
        {
            var x = c.PixelX(_barWidth);
            if (c.Touching)
            {
                _down[c.Id] = _down.TryGetValue(c.Id, out var d) ? (d.StartX, x, d.Since) : (x, x, now);
            }
            else if (_down.Remove(c.Id, out var d))
            {
                done.Add(new Gesture(d.StartX, d.LastX, now - d.Since));
            }
        }
        return done;
    }
}
