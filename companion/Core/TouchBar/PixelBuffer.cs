namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// A simple RGB image the size of (part of) the Touch Bar. Origin (0,0) is top-left.
/// Hardware-independent so every page can be snapshot-tested without the device.
/// </summary>
public sealed class PixelBuffer
{
    private readonly byte[] _rgb;

    public int Width { get; }
    public int Height { get; }

    public PixelBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Size must be positive.");
        Width = width;
        Height = height;
        _rgb = new byte[width * height * 3];
    }

    public Rgb GetPixel(int x, int y)
    {
        var i = Index(x, y);
        return new Rgb(_rgb[i], _rgb[i + 1], _rgb[i + 2]);
    }

    public void SetPixel(int x, int y, Rgb color)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return; // clip silently
        var i = Index(x, y);
        _rgb[i] = color.R;
        _rgb[i + 1] = color.G;
        _rgb[i + 2] = color.B;
    }

    public void Fill(Rgb color) => FillRect(0, 0, Width, Height, color);

    public void FillRect(int x, int y, int width, int height, Rgb color)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(Width, x + width);
        var y1 = Math.Min(Height, y + height);
        for (var yy = y0; yy < y1; yy++)
            for (var xx = x0; xx < x1; xx++)
                SetPixel(xx, yy, color);
    }

    /// <summary>Writes an uncompressed 24-bit BMP (diagnostics only; no image library needed).</summary>
    public void SaveBmp(string path)
    {
        var rowBytes = (Width * 3 + 3) & ~3;
        var size = 54 + rowBytes * Height;
        using var w = new BinaryWriter(File.Create(path));
        w.Write((byte)'B'); w.Write((byte)'M'); w.Write(size); w.Write(0); w.Write(54);
        w.Write(40); w.Write(Width); w.Write(-Height); w.Write((short)1); w.Write((short)24);
        w.Write(0); w.Write(rowBytes * Height); w.Write(2835); w.Write(2835); w.Write(0); w.Write(0);
        var row = new byte[rowBytes];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var i = (y * Width + x) * 3;
                row[x * 3] = _rgb[i + 2]; row[x * 3 + 1] = _rgb[i + 1]; row[x * 3 + 2] = _rgb[i];
            }
            w.Write(row);
        }
    }

    /// <summary>
    /// A fingerprint of every pixel (64-bit FNV-1a). The host redraws the Touch Bar only when this changes, so any
    /// visible change (e.g. the volume slider's knob, which has no text) is sent, and nothing is sent twice.
    /// </summary>
    public ulong ContentHash()
    {
        var h = 14695981039346656037UL;
        foreach (var b in _rgb) { h ^= b; h *= 1099511628211UL; }
        return h;
    }

    private int Index(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
            throw new ArgumentOutOfRangeException(nameof(x), $"({x},{y}) outside {Width}x{Height}");
        return (y * Width + x) * 3;
    }
}

public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb Black = new(0, 0, 0);
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Red = new(255, 0, 0);
    public static readonly Rgb Green = new(0, 255, 0);
    public static readonly Rgb Blue = new(0, 0, 255);
    public static readonly Rgb Yellow = new(255, 255, 0);
    public static readonly Rgb Cyan = new(0, 255, 255);
    public static readonly Rgb Magenta = new(255, 0, 255);
}
