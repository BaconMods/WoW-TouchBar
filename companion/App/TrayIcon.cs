using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using Microsoft.Win32;

namespace WoWTouchBar.App;

/// <summary>
/// Windows 10 style tray glyph, drawn in code: a flat single-colour outline of the Touch Bar with a progress bar
/// inside. White on the dark taskbar, black on the light one. Every size Windows may ask for (16-64 px, i.e. all
/// DPI scales) is drawn pixel-aligned and packed into one .ico, so it stays sharp instead of being scaled.
/// </summary>
internal static class TrayIcon
{
    private static readonly int[] Sizes = [16, 20, 24, 32, 40, 48, 64];

    public static bool TaskbarIsLight()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("SystemUsesLightTheme") is int v && v == 1;
    }

    public static Icon Create(bool lightTaskbar, bool dimmed = false)
    {
        var colour = lightTaskbar ? Color.FromArgb(dimmed ? 110 : 255, 0, 0, 0) : Color.FromArgb(dimmed ? 120 : 255, 255, 255, 255);
        using var ico = new MemoryStream();
        var pngs = Sizes.Select(s => Png(Glyph(s, colour))).ToList();
        using (var w = new BinaryWriter(ico, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write((short)0); w.Write((short)1); w.Write((short)Sizes.Length);   // ICONDIR
            var offset = 6 + 16 * Sizes.Length;
            for (var i = 0; i < Sizes.Length; i++)                                // ICONDIRENTRY (PNG images)
            {
                var s = Sizes[i];
                w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                w.Write((byte)0); w.Write((byte)0); w.Write((short)1); w.Write((short)32);
                w.Write(pngs[i].Length); w.Write(offset);
                offset += pngs[i].Length;
            }
            foreach (var p in pngs) w.Write(p);
        }
        ico.Position = 0;
        return new Icon(ico, SystemInformation.SmallIconSize);
    }

    /// <summary>Rounded outline (the bar) with a filled progress segment, stroke scaled with the size.</summary>
    private static Bitmap Glyph(int s, Color colour)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.Half;

        var stroke = Math.Max(1, (int)Math.Round(s / 16.0));
        var marginX = Math.Max(1, (int)Math.Round(s / 16.0));
        var height = (int)Math.Round(s * 0.44);
        if ((s - height) % 2 != 0) height++;                     // centre on whole pixels
        var outer = new Rectangle(marginX, (s - height) / 2, s - 2 * marginX, height);
        var radius = Math.Max(2, (int)Math.Round(s * 0.14));

        using (var path = Rounded(RectangleF.Inflate(outer, -stroke / 2f, -stroke / 2f), radius - stroke / 2f))
        using (var pen = new Pen(colour, stroke))
            g.DrawPath(pen, path);

        // Inside: one wide segment and two small "keys", so it reads as a bar with buttons (not a battery).
        var gap = stroke + Math.Max(1, (int)Math.Round(s / 16.0));
        var inner = Rectangle.Inflate(outer, -gap, -gap);
        var spacing = Math.Max(1, (int)Math.Round(s / 16.0));
        var small = Math.Max(1, (int)Math.Round((inner.Width - 2 * spacing) * 0.2));
        var wide = inner.Width - 2 * spacing - 2 * small;
        var segRadius = Math.Max(0.5f, (radius - gap) * 0.6f);
        using (var brush = new SolidBrush(colour))
        {
            var x = inner.X;
            foreach (var w in new[] { wide, small, small })
            {
                using var path = Rounded(new RectangleF(x, inner.Y, w, inner.Height), segRadius);
                g.FillPath(brush, path);
                x += w + spacing;
            }
        }
        return bmp;
    }

    private static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    private static byte[] Png(Bitmap b)
    {
        using (b)
        using (var m = new MemoryStream())
        {
            b.Save(m, ImageFormat.Png);
            return m.ToArray();
        }
    }

    /// <summary>Writes previews of every size (for checking the design).</summary>
    public static void SavePreview(string path, bool lightTaskbar)
    {
        var colour = lightTaskbar ? Color.Black : Color.White;
        var bg = lightTaskbar ? Color.FromArgb(240, 240, 240) : Color.FromArgb(32, 32, 32);
        var width = Sizes.Sum() + 10 * (Sizes.Length + 1);
        using var sheet = new Bitmap(width * 4, 74 * 4);
        using var g = Graphics.FromImage(sheet);
        g.Clear(bg);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        var x = 10;
        foreach (var s in Sizes)
        {
            using var glyph = Glyph(s, colour);
            g.DrawImage(glyph, x * 4, 5 * 4, s * 4, s * 4);
            x += s + 10;
        }
        sheet.Save(path, ImageFormat.Png);
    }
}
