using System.Globalization;
using System.Text;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>Tiny built-in 5×7 pixel font: digits, A–Z, common punctuation. Accents are dropped (ä → A).</summary>
public static class PixelFont
{
    public const int GlyphWidth = 5, GlyphHeight = 7, Advance = 6;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = ["01110", "10001", "10011", "10101", "11001", "10001", "01110"],
        ['1'] = ["00100", "01100", "00100", "00100", "00100", "00100", "01110"],
        ['2'] = ["01110", "10001", "00001", "00010", "00100", "01000", "11111"],
        ['3'] = ["11110", "00001", "00001", "01110", "00001", "00001", "11110"],
        ['4'] = ["00010", "00110", "01010", "10010", "11111", "00010", "00010"],
        ['5'] = ["11111", "10000", "11110", "00001", "00001", "10001", "01110"],
        ['6'] = ["00110", "01000", "10000", "11110", "10001", "10001", "01110"],
        ['7'] = ["11111", "00001", "00010", "00100", "01000", "01000", "01000"],
        ['8'] = ["01110", "10001", "10001", "01110", "10001", "10001", "01110"],
        ['9'] = ["01110", "10001", "10001", "01111", "00001", "00010", "01100"],
        ['A'] = ["01110", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['B'] = ["11110", "10001", "10001", "11110", "10001", "10001", "11110"],
        ['C'] = ["01110", "10001", "10000", "10000", "10000", "10001", "01110"],
        ['D'] = ["11110", "10001", "10001", "10001", "10001", "10001", "11110"],
        ['E'] = ["11111", "10000", "10000", "11110", "10000", "10000", "11111"],
        ['F'] = ["11111", "10000", "10000", "11110", "10000", "10000", "10000"],
        ['G'] = ["01110", "10001", "10000", "10111", "10001", "10001", "01111"],
        ['H'] = ["10001", "10001", "10001", "11111", "10001", "10001", "10001"],
        ['I'] = ["01110", "00100", "00100", "00100", "00100", "00100", "01110"],
        ['J'] = ["00111", "00010", "00010", "00010", "00010", "10010", "01100"],
        ['K'] = ["10001", "10010", "10100", "11000", "10100", "10010", "10001"],
        ['L'] = ["10000", "10000", "10000", "10000", "10000", "10000", "11111"],
        ['M'] = ["10001", "11011", "10101", "10101", "10001", "10001", "10001"],
        ['N'] = ["10001", "10001", "11001", "10101", "10011", "10001", "10001"],
        ['O'] = ["01110", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['P'] = ["11110", "10001", "10001", "11110", "10000", "10000", "10000"],
        ['Q'] = ["01110", "10001", "10001", "10001", "10101", "10010", "01101"],
        ['R'] = ["11110", "10001", "10001", "11110", "10100", "10010", "10001"],
        ['S'] = ["01111", "10000", "10000", "01110", "00001", "00001", "11110"],
        ['T'] = ["11111", "00100", "00100", "00100", "00100", "00100", "00100"],
        ['U'] = ["10001", "10001", "10001", "10001", "10001", "10001", "01110"],
        ['V'] = ["10001", "10001", "10001", "10001", "10001", "01010", "00100"],
        ['W'] = ["10001", "10001", "10001", "10101", "10101", "10101", "01010"],
        ['X'] = ["10001", "10001", "01010", "00100", "01010", "10001", "10001"],
        ['Y'] = ["10001", "10001", "10001", "01010", "00100", "00100", "00100"],
        ['Z'] = ["11111", "00001", "00010", "00100", "01000", "10000", "11111"],
        [' '] = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"],
        ['%'] = ["11000", "11001", "00010", "00100", "01000", "10011", "00011"],
        ['.'] = ["00000", "00000", "00000", "00000", "00000", "01100", "01100"],
        [':'] = ["00000", "01100", "01100", "00000", "01100", "01100", "00000"],
        ['/'] = ["00000", "00001", "00010", "00100", "01000", "10000", "00000"],
        ['\''] = ["00100", "00100", "01000", "00000", "00000", "00000", "00000"],
        ['"'] = ["01010", "01010", "00000", "00000", "00000", "00000", "00000"],
        ['-'] = ["00000", "00000", "00000", "11111", "00000", "00000", "00000"],
        [','] = ["00000", "00000", "00000", "00000", "01100", "00100", "01000"],
        ['!'] = ["00100", "00100", "00100", "00100", "00100", "00000", "00100"],
        ['?'] = ["01110", "10001", "00001", "00010", "00100", "00000", "00100"],
        ['('] = ["00010", "00100", "01000", "01000", "01000", "00100", "00010"],
        [')'] = ["01000", "00100", "00010", "00010", "00010", "00100", "01000"],
        ['+'] = ["00000", "00100", "00100", "11111", "00100", "00100", "00000"],
        ['&'] = ["01100", "10010", "10100", "01000", "10101", "10010", "01101"],
        ['>'] = ["10000", "01000", "00100", "00010", "00100", "01000", "10000"],
        ['<'] = ["00001", "00010", "00100", "01000", "00100", "00010", "00001"],
        ['#'] = ["01010", "01010", "11111", "01010", "11111", "01010", "01010"],
    };

    /// <summary>Upper-cases, removes accents and replaces characters the font lacks with a space.</summary>
    public static string Normalise(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var up = char.ToUpperInvariant(ch);
            sb.Append(up == '’' ? '\'' : Glyphs.ContainsKey(up) ? up : ' ');
        }
        return sb.ToString();
    }

    public static bool Supports(char c) => Glyphs.ContainsKey(c);

    /// <summary>Draws text; returns the x after the last glyph.</summary>
    public static int DrawText(PixelBuffer b, string text, int x, int y, int scale, Rgb colour)
    {
        foreach (var ch in Normalise(text))
        {
            var g = Glyphs[ch];
            for (var row = 0; row < GlyphHeight; row++)
                for (var col = 0; col < GlyphWidth; col++)
                    if (g[row][col] == '1') b.FillRect(x + col * scale, y + row * scale, scale, scale, colour);
            x += Advance * scale;
        }
        return x;
    }

    /// <summary>
    /// Draws text shifted left by <paramref name="offset"/> pixels, showing only what falls between
    /// <paramref name="clipLeft"/> and <paramref name="clipRight"/> (for scrolling text). Where the text is cut off, the
    /// last <paramref name="fade"/> pixels fade into <paramref name="background"/>.
    /// </summary>
    public static void DrawTextClipped(PixelBuffer b, string text, int x, int y, int scale, Rgb colour, int offset,
        int clipLeft, int clipRight, Rgb background, int fade = 0)
    {
        var t = Normalise(text);
        var start = x - offset;
        var end = start + t.Length * Advance * scale;
        // Each fade grows with how much text is hidden on that side, so it eases in as the text starts moving and eases
        // out as the end arrives, instead of popping on and off.
        var leftStrength = fade > 0 ? Math.Clamp((clipLeft - start) / (double)fade, 0, 1) : 0;
        var rightStrength = fade > 0 ? Math.Clamp((end - clipRight) / (double)fade, 0, 1) : 0;
        for (var i = 0; i < t.Length; i++)
        {
            var gx = start + i * Advance * scale;
            if (gx + GlyphWidth * scale <= clipLeft || gx >= clipRight) continue;
            var g = Glyphs[t[i]];
            for (var col = 0; col < GlyphWidth; col++)
                for (var sx = 0; sx < scale; sx++)
                {
                    var px = gx + col * scale + sx;
                    if (px < clipLeft || px >= clipRight) continue;
                    var a = 1.0;
                    if (leftStrength > 0 && px < clipLeft + fade) a = Math.Min(a, 1 - leftStrength * (1 - (px - clipLeft + 0.5) / fade));
                    if (rightStrength > 0 && px >= clipRight - fade) a = Math.Min(a, 1 - rightStrength * (1 - (clipRight - px - 0.5) / fade));
                    byte Mix(byte f, byte bg) => (byte)Math.Round(f * a + bg * (1 - a));
                    var shade = a >= 1 ? colour : new Rgb(Mix(colour.R, background.R), Mix(colour.G, background.G), Mix(colour.B, background.B));
                    for (var row = 0; row < GlyphHeight; row++)
                        if (g[row][col] == '1') b.FillRect(px, y + row * scale, 1, scale, shade);
                }
        }
    }

    public static int MeasureWidth(string text, int scale) => Normalise(text).Length * Advance * scale;

    /// <summary>Cuts text with "..." so it fits in maxWidth pixels at this scale.</summary>
    public static string Fit(string text, int maxWidth, int scale)
    {
        var t = Normalise(text);
        var maxChars = maxWidth / (Advance * scale);
        if (t.Length <= maxChars) return t;
        if (maxChars <= 3) return new string('.', Math.Max(0, maxChars));
        return t[..(maxChars - 3)].TrimEnd() + "...";
    }
}
