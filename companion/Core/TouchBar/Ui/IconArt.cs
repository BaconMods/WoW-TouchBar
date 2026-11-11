namespace WoWTouchBar.Core.TouchBar.Ui;

public enum IconKind { BrightnessDown, BrightnessUp, Speaker, Mute, Previous, Next, Play, Pause, Bag, Anvil, Coin, Hourglass, Close, VolumeDown, VolumeUp }

/// <summary>
/// Media / volume / brightness symbols for the Windows pages (icons instead of text labels).
/// Each icon is a shape defined on a 0..1 square, rendered with 4 x 4 samples per pixel for smooth edges and blended
/// onto the button colour. Results are cached (a handful of sizes and colours).
/// </summary>
public static class IconArt
{
    public const int Size = 30;
    private static readonly Dictionary<(IconKind, int, Rgb, Rgb), PixelBuffer> Cache = [];
    private static readonly object CacheLock = new();

    public static PixelBuffer Render(IconKind kind, Rgb fg, Rgb bg, int size = Size)
    {
        lock (CacheLock)
        {
            if (Cache.TryGetValue((kind, size, fg, bg), out var hit)) return hit;
            var img = new PixelBuffer(size, size);
            for (var py = 0; py < size; py++)
                for (var px = 0; px < size; px++)
                {
                    var inside = 0;
                    for (var sy = 0; sy < 4; sy++)
                        for (var sx = 0; sx < 4; sx++)
                            if (Inside(kind, (px + (sx + 0.5) / 4) / size, (py + (sy + 0.5) / 4) / size)) inside++;
                    var a = inside / 16.0;
                    byte Mix(byte f, byte b) => (byte)Math.Round(f * a + b * (1 - a));
                    img.SetPixel(px, py, new Rgb(Mix(fg.R, bg.R), Mix(fg.G, bg.G), Mix(fg.B, bg.B)));
                }
            return Cache[(kind, size, fg, bg)] = img;
        }
    }

    /// <summary>Whether point (x, y) in the unit square is part of the icon (pure; tested via rendering).</summary>
    public static bool Inside(IconKind kind, double x, double y) => kind switch
    {
        IconKind.BrightnessDown => Sun(x, y, core: 0.16, rayFrom: 0.26, rayTo: 0.36, ray: 0.07),
        IconKind.BrightnessUp => Sun(x, y, core: 0.20, rayFrom: 0.30, rayTo: 0.48, ray: 0.08),
        IconKind.Speaker => SpeakerBody(x, y) || Ring(x, y, 0.52, 0.5, 0.16, 0.22, 50) || Ring(x, y, 0.52, 0.5, 0.30, 0.36, 50),
        IconKind.Mute => SpeakerBody(x, y) || Segment(x, y, 0.64, 0.36, 0.88, 0.64, 0.06) || Segment(x, y, 0.64, 0.64, 0.88, 0.36, 0.06),
        IconKind.Play => Triangle(x, y, 0.30, 0.18, 0.30, 0.82, 0.84, 0.5),
        IconKind.Pause => Box(x, y, 0.27, 0.20, 0.42, 0.80) || Box(x, y, 0.58, 0.20, 0.73, 0.80),
        IconKind.Next => Triangle(x, y, 0.14, 0.24, 0.14, 0.76, 0.46, 0.5) || Triangle(x, y, 0.44, 0.24, 0.44, 0.76, 0.76, 0.5) || Box(x, y, 0.76, 0.24, 0.85, 0.76),
        IconKind.Previous => Inside(IconKind.Next, 1 - x, y), // mirror of next
        // Bags / durability / gold box on the WoW pages (always shown, instead of a Bags tab)
        // Picked from a sheet of options: a treasure chest, an anvil (WoW's repair symbol), two coins.
        // Chest: rounded lid, a gap between lid and body, and a clasp (hollow frame with a solid centre).
        IconKind.Bag => ((RoundedBox(x, y, 0.08, 0.18, 0.92, 0.50, 0.14) && y <= 0.44) || Box(x, y, 0.08, 0.50, 0.92, 0.86))
                && !Box(x, y, 0.08, 0.44, 0.92, 0.50) && !RoundedBox(x, y, 0.42, 0.40, 0.58, 0.62, 0.03)
            || RoundedBox(x, y, 0.45, 0.43, 0.55, 0.59, 0.02),
        // Anvil: face with a horn to the left, waist, flared foot and base.
        IconKind.Anvil => Box(x, y, 0.24, 0.26, 0.92, 0.42) || Triangle(x, y, 0.02, 0.26, 0.24, 0.26, 0.24, 0.40)
            || Box(x, y, 0.42, 0.42, 0.72, 0.78) || Triangle(x, y, 0.42, 0.62, 0.30, 0.78, 0.42, 0.78)
            || Triangle(x, y, 0.72, 0.62, 0.72, 0.78, 0.84, 0.78) || Box(x, y, 0.24, 0.78, 0.90, 0.88),
        IconKind.Hourglass => Box(x, y, 0.20, 0.06, 0.80, 0.14) || Box(x, y, 0.20, 0.86, 0.80, 0.94)
            || Triangle(x, y, 0.27, 0.14, 0.73, 0.14, 0.5, 0.52) || Triangle(x, y, 0.5, 0.48, 0.27, 0.86, 0.73, 0.86),
        // Two coins: the back one cut away around the front one, the front one with a rim groove.
        IconKind.Coin => (Annulus(x, y, 0.38, 0.40, 0, 0.29) && !Annulus(x, y, 0.62, 0.62, 0, 0.35))
            || (Annulus(x, y, 0.62, 0.62, 0, 0.29) && !Annulus(x, y, 0.62, 0.62, 0.15, 0.20)),
        // Volume down / up: the speaker with a minus / plus beside it.
        IconKind.VolumeDown => SpeakerBody(x, y) || Box(x, y, 0.62, 0.46, 0.90, 0.54),
        IconKind.VolumeUp => SpeakerBody(x, y) || Box(x, y, 0.62, 0.46, 0.90, 0.54) || Box(x, y, 0.72, 0.36, 0.80, 0.64),
        IconKind.Close => Segment(x, y, 0.28, 0.28, 0.72, 0.72, 0.07) || Segment(x, y, 0.28, 0.72, 0.72, 0.28, 0.07), // remove a note / timer
        _ => false,
    };

    private static bool Annulus(double x, double y, double cx, double cy, double r0, double r1)
    {
        var d = (x - cx) * (x - cx) + (y - cy) * (y - cy);
        return d >= r0 * r0 && d <= r1 * r1;
    }

    private static bool Disc(double x, double y, double r) => (x - 0.5) * (x - 0.5) + (y - 0.5) * (y - 0.5) <= r * r;

    private static bool Box(double x, double y, double x0, double y0, double x1, double y1) => x >= x0 && x <= x1 && y >= y0 && y <= y1;

    private static bool RoundedBox(double x, double y, double x0, double y0, double x1, double y1, double r)
    {
        if (!Box(x, y, x0, y0, x1, y1)) return false;
        var cx = Math.Clamp(x, x0 + r, x1 - r);
        var cy = Math.Clamp(y, y0 + r, y1 - r);
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r;
    }

    private static bool Triangle(double x, double y, double ax, double ay, double bx, double by, double cx, double cy)
    {
        double S(double px, double py, double qx, double qy, double rx, double ry) => (px - rx) * (qy - ry) - (qx - rx) * (py - ry);
        var d1 = S(x, y, ax, ay, bx, by);
        var d2 = S(x, y, bx, by, cx, cy);
        var d3 = S(x, y, cx, cy, ax, ay);
        var neg = d1 < 0 || d2 < 0 || d3 < 0;
        var pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static bool Segment(double x, double y, double ax, double ay, double bx, double by, double halfWidth)
    {
        double dx = bx - ax, dy = by - ay;
        var t = Math.Clamp(((x - ax) * dx + (y - ay) * dy) / (dx * dx + dy * dy), 0, 1);
        double px = ax + t * dx - x, py = ay + t * dy - y;
        return px * px + py * py <= halfWidth * halfWidth;
    }

    /// <summary>A ring between radii r0..r1 around (cx, cy), only the part within ±<paramref name="halfAngle"/> degrees of pointing right.</summary>
    private static bool Ring(double x, double y, double cx, double cy, double r0, double r1, double halfAngle)
    {
        double dx = x - cx, dy = y - cy;
        var r = Math.Sqrt(dx * dx + dy * dy);
        return r >= r0 && r <= r1 && dx > 0 && Math.Abs(Math.Atan2(dy, dx)) <= halfAngle * Math.PI / 180;
    }

    private static bool SpeakerBody(double x, double y) =>
        Box(x, y, 0.10, 0.38, 0.28, 0.62)                                  // the box
        || Triangle(x, y, 0.27, 0.38, 0.52, 0.16, 0.52, 0.84)           // the cone, as two triangles
        || Triangle(x, y, 0.27, 0.38, 0.52, 0.84, 0.27, 0.62);

    private static bool Sun(double x, double y, double core, double rayFrom, double rayTo, double ray)
    {
        double dx = x - 0.5, dy = y - 0.5;
        var r = Math.Sqrt(dx * dx + dy * dy);
        if (r <= core) return true;
        if (r < rayFrom || r > rayTo) return false;
        for (var k = 0; k < 8; k++)
        {
            var a = k * Math.PI / 4;
            // distance of the point from the ray's centre line
            var across = Math.Abs(-dx * Math.Sin(a) + dy * Math.Cos(a));
            var along = dx * Math.Cos(a) + dy * Math.Sin(a);
            if (along > 0 && across <= ray / 2) return true;
        }
        return false;
    }
}
