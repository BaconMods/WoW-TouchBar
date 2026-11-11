using System.Drawing.Drawing2D;
using WoWTouchBar.Core.Input;
using WoWTouchBar.Core.TouchBar;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.App;

/// <summary>
/// Icons for "My keys" program buttons: the program's own icon from Windows, scaled to
/// <see cref="Draw.IconSize"/> and blended onto the button colour. Read once per program and remembered; never saved.
/// </summary>
internal sealed class ButtonIcons
{
    private readonly Dictionary<string, PixelBuffer?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public PixelBuffer? For(CustomButton b)
    {
        if (b.Kind != ShortcutKind.OpenProgram) return null;
        var path = b.Target.Trim().Trim('"');
        lock (_lock)
        {
            if (!_cache.TryGetValue(path, out var icon)) _cache[path] = icon = Load(path);
            return icon;
        }
    }

    private static PixelBuffer? Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            // A large icon straight from the program when it has one (sharper), else the icon Windows shows for the file (.lnk, .url).
            Icon? icon = null;
            if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                try { icon = Icon.ExtractIcon(path, 0, 64); } catch (Exception) { icon = null; }
            icon ??= Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            using (icon)
            using (var src = icon.ToBitmap())
            using (var bmp = new Bitmap(Draw.IconSize, Draw.IconSize))
            {
                var bg = Palette.Normal.Button;
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.FromArgb(bg.R, bg.G, bg.B)); // transparent parts take the button colour
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(src, new Rectangle(0, 0, Draw.IconSize, Draw.IconSize));
                }
                var img = new PixelBuffer(Draw.IconSize, Draw.IconSize);
                for (var y = 0; y < Draw.IconSize; y++)
                    for (var x = 0; x < Draw.IconSize; x++)
                    {
                        var p = bmp.GetPixel(x, y);
                        img.SetPixel(x, y, new Rgb(p.R, p.G, p.B));
                    }
                return img;
            }
        }
        catch (Exception) { return null; } // no icon is fine: the button shows its label
    }
}
