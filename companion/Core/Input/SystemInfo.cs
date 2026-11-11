using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WoWTouchBar.Core.Input;

/// <summary>
/// What is playing right now (any app that reports to Windows' media controls: Spotify, a browser, ...).
/// <paramref name="Art"/> is the cover, already cropped square and scaled to <see cref="CoverArt.Size"/>, if the app sends one.
/// </summary>
public sealed record NowPlaying(string Title, string Artist, bool Playing, TouchBar.PixelBuffer? Art = null);

/// <summary>Cover art sizing (pure; tested).</summary>
public static class CoverArt
{
    /// <summary>Side of the square cover on the bar, in pixels.</summary>
    public const int Size = 52;

    private static bool Dark(TouchBar.Rgb p) => p.R + p.G + p.B < 36;

    /// <summary>
    /// Finds the actual cover inside the picture a player sends. Spotify, for example, sends 300 x 300 with the cover in
    /// the top part, black bars at the sides and its logo underneath (seen 2026-11-09). Takes the first block of
    /// non-black rows from the top (stopping at the black gap before a logo) and trims black columns at its sides.
    /// If that block is under 40 % of the height (e.g. a very dark cover), the whole picture is used.
    /// </summary>
    public static (int X, int Y, int Width, int Height) ArtBounds(TouchBar.PixelBuffer img)
    {
        bool RowHasContent(int y) { for (var x = 0; x < img.Width; x++) if (!Dark(img.GetPixel(x, y))) return true; return false; }
        var top = 0;
        while (top < img.Height && !RowHasContent(top)) top++;
        var bottom = top;
        while (bottom < img.Height && RowHasContent(bottom)) bottom++;
        if (top >= img.Height || bottom - top < img.Height * 0.4) return (0, 0, img.Width, img.Height);

        bool ColHasContent(int x) { for (var y = top; y < bottom; y++) if (!Dark(img.GetPixel(x, y))) return true; return false; }
        var left = 0;
        while (left < img.Width && !ColHasContent(left)) left++;
        var right = img.Width;
        while (right > left && !ColHasContent(right - 1)) right--;
        if (right - left < img.Width * 0.3) return (0, 0, img.Width, img.Height);
        return (left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The cover for the bar: the art found by <see cref="ArtBounds"/>, cropped to a square (wide: the middle; tall: the
    /// top) and shrunk to <see cref="Size"/> by averaging (smooth, no shimmer).
    /// </summary>
    public static TouchBar.PixelBuffer Fit(TouchBar.PixelBuffer full, int size = Size)
    {
        var (bx, by, bw, bh) = ArtBounds(full);
        var side = Math.Min(bw, bh);
        var sx = bx + (bw - side) / 2;
        var sy = by; // tall: keep the top
        var outImg = new TouchBar.PixelBuffer(size, size);
        for (var oy = 0; oy < size; oy++)
            for (var ox = 0; ox < size; ox++)
            {
                int x0 = sx + ox * side / size, x1 = Math.Max(x0 + 1, sx + (ox + 1) * side / size);
                int y0 = sy + oy * side / size, y1 = Math.Max(y0 + 1, sy + (oy + 1) * side / size);
                long r = 0, g = 0, b = 0, n = 0;
                for (var y = y0; y < y1 && y < full.Height; y++)
                    for (var x = x0; x < x1 && x < full.Width; x++)
                    {
                        var p = full.GetPixel(x, y);
                        r += p.R; g += p.G; b += p.B; n++;
                    }
                if (n > 0) outImg.SetPixel(ox, oy, new TouchBar.Rgb((byte)(r / n), (byte)(g / n), (byte)(b / n)));
            }
        return outImg;
    }

    /// <summary>A PixelBuffer from 32-bit BGRA pixels (premultiplied over black, as Windows decodes them).</summary>
    public static TouchBar.PixelBuffer FromBgra(byte[] bgra, int width, int height)
    {
        var img = new TouchBar.PixelBuffer(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var i = (y * width + x) * 4;
                if (i + 2 < bgra.Length) img.SetPixel(x, y, new TouchBar.Rgb(bgra[i + 2], bgra[i + 1], bgra[i]));
            }
        return img;
    }
}

/// <summary>Where "now playing" comes from (the tray app supplies Windows' media session; tests supply fixed values).</summary>
public interface INowPlayingSource
{
    NowPlaying? Current { get; }
}

/// <summary>Things the system page shows besides buttons: clock, battery, now playing. Song titles are never logged.</summary>
public sealed record SystemInfo(DateTime Time, int? BatteryPercent, bool Charging, NowPlaying? Media,
    int? VolumePercent = null, bool Muted = false, int? BrightnessPercent = null)
{
    /// <summary>"87%" / "87% +" (charging) / "" (no battery, e.g. a desktop).</summary>
    public string BatteryText => BatteryPercent is { } p ? (Charging ? $"{p}% +" : $"{p}%") : "";
}

/// <summary>Battery state from Windows (GetSystemPowerStatus). Read-only.</summary>
[SupportedOSPlatform("windows")]
public static class Power
{
    public static (int? Percent, bool Charging) Read()
    {
        if (!Native.GetSystemPowerStatus(out var s)) return (null, false);
        var noBattery = (s.BatteryFlag & 128) != 0 || s.BatteryFlag == 255;
        int? percent = noBattery || s.BatteryLifePercent > 100 ? null : s.BatteryLifePercent;
        return (percent, s.ACLineStatus == 1 && percent is < 100);
    }

    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEM_POWER_STATUS
        {
            public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
            public int BatteryLifeTime, BatteryFullLifeTime;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
    }
}
