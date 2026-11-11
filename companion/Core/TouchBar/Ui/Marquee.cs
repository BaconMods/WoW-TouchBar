namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>
/// Gentle scrolling for text too long for its space (instead of cutting it with "..."). The text shows
/// its start for <see cref="HoldStart"/>, glides to its end at <see cref="PixelsPerSecond"/>, rests there for
/// <see cref="HoldEnd"/>, then starts over. Pure; tested.
/// </summary>
public static class Marquee
{
    public static readonly TimeSpan HoldStart = TimeSpan.FromSeconds(2.5), HoldEnd = TimeSpan.FromSeconds(2);
    public const double PixelsPerSecond = 40;
    /// <summary>Width of the soft fade at an edge where text is cut off.</summary>
    public const int Fade = 18;

    /// <summary>How far (pixels) text that is <paramref name="overflow"/> pixels too wide is scrolled after <paramref name="shown"/>.</summary>
    public static int Offset(int overflow, TimeSpan shown)
    {
        if (overflow <= 0) return 0;
        var glide = overflow / PixelsPerSecond;
        var period = HoldStart.TotalSeconds + glide + HoldEnd.TotalSeconds;
        var t = Math.Max(0, shown.TotalSeconds) % period - HoldStart.TotalSeconds;
        return t <= 0 ? 0 : t >= glide ? overflow : (int)Math.Round(t * PixelsPerSecond);
    }
}
