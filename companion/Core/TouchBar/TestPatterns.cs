namespace WoWTouchBar.Core.TouchBar;

/// <summary>Images used to prove the Touch Bar works (Phase 0, Step 1).</summary>
public static class TestPatterns
{
    private static readonly Rgb[] Bars = [Rgb.Red, Rgb.Green, Rgb.Blue, Rgb.Yellow, Rgb.Cyan, Rgb.Magenta, Rgb.White];

    /// <summary>Seven vertical colour bars with a white frame — easy to recognise and shows orientation problems.</summary>
    public static PixelBuffer ColourBars(int width, int height)
    {
        var buffer = new PixelBuffer(width, height);
        var barWidth = width / Bars.Length;
        for (var i = 0; i < Bars.Length; i++)
        {
            var x = i * barWidth;
            var w = i == Bars.Length - 1 ? width - x : barWidth;
            buffer.FillRect(x, 0, w, height, Bars[i]);
        }
        // Red marker in the top-left corner reveals flips/rotation.
        buffer.FillRect(0, 0, Math.Min(40, width), Math.Min(height / 2, height), Rgb.Black);
        buffer.FillRect(4, 4, Math.Min(32, width - 4), Math.Max(1, height / 2 - 8), Rgb.Red);
        return buffer;
    }

    /// <summary>A progress bar filled to <paramref name="fraction"/> (0..1) — the shape of the future XP bar.</summary>
    public static PixelBuffer ProgressBar(int width, int height, double fraction)
    {
        fraction = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        var buffer = new PixelBuffer(width, height);
        buffer.Fill(Rgb.Black);
        var margin = Math.Max(1, height / 6);
        var inner = width - 2 * margin;
        buffer.FillRect(margin, margin, inner, height - 2 * margin, new Rgb(40, 40, 40));
        buffer.FillRect(margin, margin, (int)Math.Round(inner * fraction), height - 2 * margin, new Rgb(140, 60, 220));
        return buffer;
    }
}
