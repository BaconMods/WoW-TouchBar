using WoWTouchBar.Core.Protocol;
using WoWTouchBar.Core.TouchBar;

namespace WoWTouchBar.Core.Bridge;

/// <summary>
/// Finds the addon's cell grid in a captured image and reads its 88 data bytes.
/// Row 0 and column 0 alternate white/black, so every cell boundary is visible: the reader measures
/// each column and row from those timing patterns instead of assuming a cell size, and counts the
/// timing row to tell which grid shape (89x9 … 705x2) is shown. Copes with UI scale, Windows DPI
/// stretching (seen: 1.52x), uneven cells (1/2 px or 9/10 px) and dim colours. CRC checks are the caller's job.
/// </summary>
public sealed class StripReader
{
    public const double MinPitch = 1;
    public const int MaxPitch = 14;
    private const int MinContrast = 90; // R+G+B difference between the strip's light and dark cells
    private const int ThresholdWindow = 160;

    /// <summary>How far from the capture origin the strip may start, in pixels.</summary>
    public int SearchRadius { get; init; } = 24;

    /// <summary>Wide enough for 89 columns at 14 px or 705 columns at ~1.9 px.</summary>
    public int CaptureWidth => 1400;
    public int CaptureHeight => ProtocolV1.Rows * MaxPitch + SearchRadius;

    /// <summary>Measured cell pitch (x) and grid columns of the last successful read, for diagnostics.</summary>
    public double LastPitch { get; private set; }
    public int LastColumns { get; private set; }
    public int LastContrast { get; private set; }

    /// <summary>Longest regular light/dark run sequence seen by the last Read (0 = nothing strip-like on screen).</summary>
    public int LastTimingRuns { get; private set; }

    private const int MinChannelContrast = 30; // one channel's light/dark difference for the colour line (dim tone ~51)

    private static int Sum(Rgb p) => p.R + p.G + p.B;
    private static int Green(Rgb p) => p.G;

    public byte[]? Read(PixelBuffer img)
    {
        LastTimingRuns = 0;
        var maxY = Math.Min(SearchRadius + MaxPitch, img.Height - 1);
        for (var y = 0; y <= maxY; y++)
        {
            var bytes = ReadGrid(img, y) ?? ReadLine(img, y);
            if (bytes is not null) return bytes;
        }
        return null;
    }

    /// <summary>Grey grids: row 0 alternates white/black; the number of timing cells gives the shape.</summary>
    private byte[]? ReadGrid(PixelBuffer img, int y)
    {
        if (!RowThreshold(img, y, Sum, MinContrast, out var threshold)) return null;
        var starts = TimingRuns(img, y, threshold, Sum);
        if (starts is null) return null;
        foreach (var (cols, rows) in ProtocolV1.Layouts.OrderByDescending(l => l.Key))
        {
            if (starts.Count < cols) continue;
            if (!Columns(starts, cols, out var colCentres, out var pitch)) continue;
            if (!Rows(img, (int)colCentres[0], y, rows, threshold, pitch, out var rowCentres)) continue;
            var bytes = Sample(img, colCentres, rowCentres, cols, out var contrast);
            if (!HasMagic(bytes)) continue;
            Found(pitch, cols, contrast);
            return bytes;
        }
        return null;
    }

    /// <summary>Colour line: green alternates per cell (timing); red and blue are two data bits per cell.</summary>
    private byte[]? ReadLine(PixelBuffer img, int y)
    {
        if (!RowThreshold(img, y, Green, MinChannelContrast, out var threshold)) return null;
        var starts = TimingRuns(img, y, threshold, Green);
        if (starts is null || starts.Count < ProtocolV1.LineCols) return null;
        if (!Columns(starts, ProtocolV1.LineCols, out var centres, out var pitch)) return null;

        // Middle of the line's height: the rows where the first cells still alternate green on/off
        // (background, however green, is the same in neighbouring cells).
        bool InLine(int yy)
        {
            for (var c = 0; c < 4; c++)
                if (img.GetPixel((int)centres[c], yy).G > threshold != (c % 2 == 0)) return false;
            return true;
        }
        if (!InLine(y)) return null;
        int top = y, bottom = y;
        while (top > 0 && InLine(top - 1)) top--;
        while (bottom + 1 < img.Height && InLine(bottom + 1)) bottom++;
        if (bottom - top + 1 > pitch * 1.5 + 2) return null; // taller than a cell: not the line
        var yc = (top + bottom) / 2;

        long light = 0, dark = 0;
        int nl = 0, nd = 0;
        for (var c = 0; c < ProtocolV1.LineCols; c++)
        {
            var g = img.GetPixel((int)centres[c], yc).G;
            if (c % 2 == 0) { light += g; nl++; } else { dark += g; nd++; }
        }
        var lightAvg = light / (double)nl;
        var darkAvg = dark / (double)nd;
        var contrast = (int)(lightAvg - darkAvg);
        if (contrast < MinChannelContrast) return null;
        var level = (lightAvg + darkAvg) / 2; // same "on" level is used for red and blue

        var bytes = new byte[ProtocolV1.FrameBytes];
        for (var c = 0; c < ProtocolV1.LineCols; c++)
        {
            var x = (int)centres[c];
            if (x >= img.Width) return null;
            var p = img.GetPixel(x, yc);
            if (p.R > level) { var (i, m) = ProtocolV1.LineBit(c, blue: false); bytes[i] |= m; }
            if (p.B > level) { var (i, m) = ProtocolV1.LineBit(c, blue: true); bytes[i] |= m; }
        }
        if (!HasMagic(bytes)) return null;
        Found(pitch, ProtocolV1.LineCols, contrast);
        return bytes;
    }

    private static bool HasMagic(byte[]? b) => b is not null && b[0] == ProtocolV1.Magic1 && b[1] == ProtocolV1.Magic2;

    private void Found(double pitch, int cols, int contrast)
    {
        LastPitch = pitch;
        LastColumns = cols;
        LastContrast = contrast;
    }

    /// <summary>Light/dark threshold for this row from the area where the strip would start.</summary>
    private static bool RowThreshold(PixelBuffer img, int y, Func<Rgb, int> value, int minContrast, out double threshold)
    {
        int min = int.MaxValue, max = int.MinValue;
        var end = Math.Min(img.Width, ThresholdWindow);
        for (var x = 0; x < end; x++)
        {
            var s = value(img.GetPixel(x, y));
            if (s < min) min = s;
            if (s > max) max = s;
        }
        threshold = (min + max) / 2.0;
        return max - min >= minContrast;
    }

    /// <summary>Start x of each alternating run along row y (light first), until the pattern stops being regular.</summary>
    private List<int>? TimingRuns(PixelBuffer img, int y, double threshold, Func<Rgb, int> value)
    {
        bool Light(int x) => value(img.GetPixel(x, y)) > threshold;
        var x0 = -1;
        var limit = Math.Min(SearchRadius, img.Width - 1);
        for (var x = 0; x <= limit; x++)
            if (Light(x)) { x0 = x; break; }
        if (x0 < 0 || (x0 > 0 && Light(x0 - 1))) return null;

        var starts = new List<int>(706);
        var pos = x0;
        var light = true;
        while (pos < img.Width && starts.Count <= 706)
        {
            var start = pos;
            starts.Add(start);
            while (pos < img.Width && Light(pos) == light) pos++;
            var len = pos - start;
            if (pos >= img.Width || len > MaxPitch + 1) break; // ran off the image or into the background
            if (starts.Count > 8)
            {
                var avg = (start - starts[0]) / (double)(starts.Count - 1);
                if (len > avg * 1.5 + 1) break; // last cell merged with the background
            }
            light = !light;
        }
        LastTimingRuns = Math.Max(LastTimingRuns, starts.Count);
        return starts.Count >= 89 ? starts : null;
    }

    private static bool Columns(List<int> starts, int cols, out double[] centres, out double pitch)
    {
        centres = [];
        pitch = (starts[cols - 1] - starts[0]) / (double)(cols - 1);
        if (pitch < MinPitch || pitch > MaxPitch) return false;
        for (var c = 0; c < cols - 1; c++)
            if (Math.Abs(starts[c + 1] - starts[c] - pitch) > Math.Max(pitch * 0.5, 0.75)) return false;
        centres = new double[cols];
        for (var c = 0; c < cols - 1; c++) centres[c] = (starts[c] + starts[c + 1]) / 2.0;
        centres[cols - 1] = starts[cols - 1] + pitch / 2;
        return true;
    }

    /// <summary>
    /// Column 0 alternates light/dark down the rows; find each row's centre. Cells are square, so the
    /// column pitch (measured over many cells) also places the last row and bounds each row's height.
    /// </summary>
    private static bool Rows(PixelBuffer img, int x, int yInside, int rows, double threshold, double pitch, out double[] centres)
    {
        bool Light(int y) => Sum(img.GetPixel(x, y)) > threshold;
        centres = [];
        var top = yInside;
        while (top > 0 && Light(top - 1)) top--;

        var starts = new int[rows];
        var pos = top;
        for (var r = 0; r < rows; r++)
        {
            starts[r] = pos;
            if (r == rows - 1) break; // the last row may merge with the background
            var light = r % 2 == 0;
            while (pos < img.Height && Light(pos) == light) pos++;
            var len = pos - starts[r];
            if (len < 1 || Math.Abs(len - pitch) > Math.Max(pitch * 0.5, 0.75) || pos >= img.Height) return false;
        }
        if (starts[rows - 1] + pitch / 2 >= img.Height) return false;
        centres = new double[rows];
        for (var r = 0; r < rows - 1; r++) centres[r] = (starts[r] + starts[r + 1]) / 2.0;
        centres[rows - 1] = starts[rows - 1] + pitch / 2;
        return true;
    }

    private static byte[]? Sample(PixelBuffer img, double[] cx, double[] cy, int cols, out int contrast)
    {
        // Calibrate the light/dark threshold from the timing row's cell centres (never fixed RGB values).
        long light = 0, dark = 0;
        int nl = 0, nd = 0;
        for (var c = 0; c < cols; c++)
        {
            var s = Sum(img.GetPixel((int)cx[c], (int)cy[0]));
            if (c % 2 == 0) { light += s; nl++; } else { dark += s; nd++; }
        }
        var lightAvg = light / (double)nl;
        var darkAvg = dark / (double)nd;
        contrast = (int)(lightAvg - darkAvg);
        if (contrast < MinContrast) return null;
        var threshold = (lightAvg + darkAvg) / 2;

        var rows = cy.Length;
        var bytes = new byte[ProtocolV1.FrameBytes];
        for (var r = 1; r < rows; r++)
        {
            var y = (int)cy[r];
            if (y >= img.Height) return null;
            for (var c = 1; c < cols; c++)
            {
                var x = (int)cx[c];
                if (x >= img.Width) return null;
                if (Sum(img.GetPixel(x, y)) > threshold)
                {
                    var (i, mask) = ProtocolV1.DataBit(r, c, cols);
                    bytes[i] |= mask;
                }
            }
        }
        return bytes;
    }
}
