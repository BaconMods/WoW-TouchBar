namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>Colours for one rendering; the stale palette greys everything out.</summary>
public sealed record Palette(Rgb Text, Rgb Dim, Rgb Xp, Rgb Rested, Rgb Done, Rgb Warn, Rgb Bad, Rgb Track, Rgb Button, Rgb Accent)
{
    public static readonly Palette Normal = new(
        Text: Rgb.White, Dim: new Rgb(150, 150, 150), Xp: new Rgb(140, 60, 220), Rested: new Rgb(60, 90, 200),
        Done: new Rgb(90, 200, 90), Warn: new Rgb(230, 190, 60), Bad: new Rgb(230, 70, 60), Track: new Rgb(40, 40, 40),
        Button: new Rgb(48, 48, 52), Accent: new Rgb(110, 50, 180));

    public static readonly Palette Stale = new(
        Text: new Rgb(150, 150, 150), Dim: new Rgb(100, 100, 100), Xp: new Rgb(80, 60, 100), Rested: new Rgb(55, 60, 80),
        Done: new Rgb(90, 120, 90), Warn: new Rgb(130, 120, 80), Bad: new Rgb(130, 80, 80), Track: new Rgb(35, 35, 35),
        Button: new Rgb(40, 40, 42), Accent: new Rgb(70, 50, 95));

    /// <summary>(selected tab / button colour, progress bar colour) for each choice in the tray's Colour menu.</summary>
    public static (Rgb Accent, Rgb Bar) Tones(AccentColour a) => a switch
    {
        AccentColour.Blue => (new Rgb(40, 90, 190), new Rgb(60, 130, 230)),
        AccentColour.Teal => (new Rgb(20, 125, 130), new Rgb(30, 170, 170)),
        AccentColour.Green => (new Rgb(40, 130, 60), new Rgb(60, 170, 80)),
        AccentColour.Orange => (new Rgb(190, 90, 20), new Rgb(230, 120, 40)),
        AccentColour.Red => (new Rgb(170, 40, 50), new Rgb(220, 60, 70)),
        AccentColour.Pink => (new Rgb(180, 50, 120), new Rgb(220, 80, 160)),
        AccentColour.Grey => (new Rgb(85, 85, 95), new Rgb(150, 150, 160)),
        _ => (Normal.Accent, Normal.Xp),
    };

    /// <summary>
    /// The palette for the player's chosen colour. Purple gives exactly
    /// <see cref="Normal"/> / <see cref="Stale"/>. With blue, rested XP turns purple so it still stands out from the bar.
    /// </summary>
    public static Palette For(AccentColour a, bool stale = false)
    {
        if (a == AccentColour.Purple || !Enum.IsDefined(a)) return stale ? Stale : Normal;
        var (accent, bar) = Tones(a);
        var rested = a == AccentColour.Blue ? Normal.Xp : Normal.Rested;
        if (!stale) return Normal with { Accent = accent, Xp = bar, Rested = rested };
        static Rgb Grey(Rgb c) => new((byte)((c.R * 2 + 45 * 3) / 5), (byte)((c.G * 2 + 45 * 3) / 5), (byte)((c.B * 2 + 45 * 3) / 5));
        return Stale with { Accent = Grey(accent), Xp = Grey(bar), Rested = Grey(rested) };
    }
}

/// <summary>The bar's highlight colour (tray > Colour). Purple is the default.</summary>
public enum AccentColour { Purple, Blue, Teal, Green, Orange, Red, Pink, Grey }

/// <summary>What a page draws into: the image, its tap regions, and every text drawn (for tests and logs).</summary>
public sealed class Canvas
{
    public PixelBuffer Image { get; } = new(BarController.Width, BarController.Height);
    public List<HitRegion> Regions { get; } = [];
    public List<string> Texts { get; } = [];

    /// <summary>The render's clock (scrolling text).</summary>
    public TimeSpan Now { get; init; }
    /// <summary>When each scrolling text first appeared, kept by the controller between renders (key: place + text).</summary>
    public Dictionary<string, TimeSpan> ScrollStarts { get; init; } = [];
    /// <summary>Scrolling texts drawn this time (the controller forgets the others, so a text that comes back starts over).</summary>
    public HashSet<string> ScrollKeys { get; } = [];
    /// <summary>Something on the bar is moving: the app redraws it often until this is false.</summary>
    public bool Animating { get; set; }

    /// <summary>How long the scrolling text with this key has been showing.</summary>
    public TimeSpan ScrollAge(string key)
    {
        ScrollKeys.Add(key);
        if (!ScrollStarts.TryGetValue(key, out var start)) ScrollStarts[key] = start = Now;
        return Now - start;
    }
}

/// <summary>Drawing helpers shared by the pages. Text uses the 5x7 pixel font.</summary>
public static class Draw
{
    public const int Small = 3;                 // 18 px per character, 21 px tall
    public const int Large = 4;                 // 24 px per character, 28 px tall
    public const int Line1Y = 6, Line2Y = 33;   // two lines of small text
    public static int CenterY(int scale) => (BarController.Height - PixelFont.GlyphHeight * scale) / 2;

    /// <summary>Draws text cut to maxWidth; returns the x after it.</summary>
    public static int Text(Canvas c, string text, int x, int y, int scale, Rgb colour, int maxWidth = int.MaxValue)
    {
        var fitted = PixelFont.Fit(text, maxWidth, scale);
        if (fitted.Length > 0) c.Texts.Add(fitted);
        return PixelFont.DrawText(c.Image, fitted, x, y, scale, colour);
    }

    /// <summary>
    /// Like <see cref="Text"/>, but text too long for maxWidth scrolls gently (<see cref="Marquee"/>) instead of being cut
    /// with "...", with soft edges where it is cut off. Only for text on the black bar. Returns the x after it.
    /// </summary>
    public static int ScrollText(Canvas c, string text, int x, int y, int scale, Rgb colour, int maxWidth)
    {
        var t = PixelFont.Normalise(text);
        var width = PixelFont.MeasureWidth(t, scale);
        if (width <= maxWidth || maxWidth < PixelFont.Advance * scale * 4) return Text(c, t, x, y, scale, colour, maxWidth);
        var offset = Marquee.Offset(width - maxWidth, c.ScrollAge($"{x},{y}|{t}"));
        c.Animating = true;
        PixelFont.DrawTextClipped(c.Image, t, x, y, scale, colour, offset, x, x + maxWidth, Rgb.Black, Marquee.Fade);
        // For tests and logs: the characters mostly in view.
        var advance = PixelFont.Advance * scale;
        var first = Math.Min(t.Length - 1, (offset + advance / 2) / advance);
        c.Texts.Add(t.Substring(first, Math.Min(t.Length - first, maxWidth / advance)));
        return x + maxWidth;
    }

    /// <summary>Corner radius for buttons, tabs, boxes and banners (slightly rounded).</summary>
    public const int Radius = 6;
    public const int BoxTop = 4;
    public static int BoxHeight => BarController.Height - 2 * BoxTop;

    /// <summary>
    /// Filled rectangle with rounded corners. Corner edges are smoothed: each corner pixel is blended with what is
    /// already there by how much of it lies inside the curve (4 x 4 samples).
    /// </summary>
    public static void RoundedRect(PixelBuffer img, int x, int y, int width, int height, int radius, Rgb fill, Rgb? under = null)
    {
        if (width <= 0 || height <= 0) return;
        radius = Math.Min(radius, Math.Min(width, height) / 2);
        img.FillRect(x, y, width, height, fill);
        if (radius <= 0) return;
        for (var cy = 0; cy < radius; cy++)
            for (var cx = 0; cx < radius; cx++)
            {
                var inside = 0;
                for (var sy = 0; sy < 4; sy++)
                    for (var sx = 0; sx < 4; sx++)
                    {
                        double dx = radius - (cx + (sx + 0.5) / 4), dy = radius - (cy + (sy + 0.5) / 4);
                        if (dx * dx + dy * dy <= radius * radius) inside++;
                    }
                if (inside == 16) continue;
                var a = inside / 16.0;
                foreach (var (px, py) in new[] { (x + cx, y + cy), (x + width - 1 - cx, y + cy), (x + cx, y + height - 1 - cy), (x + width - 1 - cx, y + height - 1 - cy) })
                    Blend(img, px, py, fill, a, under ?? Rgb.Black);
            }
    }

    /// <summary>
    /// Copies a picture (e.g. album art) onto the bar with rounded corners that blend into <paramref name="background"/>.
    /// Anything outside the bar is clipped.
    /// </summary>
    public static void Picture(PixelBuffer img, PixelBuffer pic, int x, int y, int radius, Rgb background)
    {
        for (var py = 0; py < pic.Height; py++)
            for (var px = 0; px < pic.Width; px++)
                img.SetPixel(x + px, y + py, pic.GetPixel(px, py));
        radius = Math.Min(radius, Math.Min(pic.Width, pic.Height) / 2);
        for (var cy = 0; cy < radius; cy++)
            for (var cx = 0; cx < radius; cx++)
            {
                var inside = 0;
                for (var sy = 0; sy < 4; sy++)
                    for (var sx = 0; sx < 4; sx++)
                    {
                        double dx = radius - (cx + (sx + 0.5) / 4), dy = radius - (cy + (sy + 0.5) / 4);
                        if (dx * dx + dy * dy <= radius * radius) inside++;
                    }
                if (inside == 16) continue;
                var a = inside / 16.0;
                foreach (var (qx, qy) in new[] { (cx, cy), (pic.Width - 1 - cx, cy), (cx, pic.Height - 1 - cy), (pic.Width - 1 - cx, pic.Height - 1 - cy) })
                    Blend(img, x + qx, y + qy, pic.GetPixel(qx, qy), a, background);
            }
    }

    // Corner pixels were just overwritten by the fill; blend fill with what lies under the box (black bar background).
    private static void Blend(PixelBuffer img, int x, int y, Rgb fill, double a, Rgb under)
    {
        if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) return;
        byte Mix(byte f, byte u) => (byte)Math.Round(f * a + u * (1 - a));
        img.SetPixel(x, y, new Rgb(Mix(fill.R, under.R), Mix(fill.G, under.G), Mix(fill.B, under.B)));
    }

    /// <summary>A full-height button with a centred label, registered as a tap region.</summary>
    public static void Button(Canvas c, int x, int width, string label, BarAction action, Rgb fill, Rgb text)
    {
        RoundedRect(c.Image, x, BoxTop, width, BoxHeight, Radius, fill);
        var fitted = PixelFont.Fit(label, width - 8, Small);
        c.Texts.Add(fitted);
        var tx = x + (width - PixelFont.MeasureWidth(fitted, Small)) / 2;
        PixelFont.DrawText(c.Image, fitted, tx, CenterY(Small), Small, text);
        c.Regions.Add(new HitRegion(x, width, action));
    }

    /// <summary>Program icons on "My keys" buttons are drawn at this size (pixels), left of the label.</summary>
    public const int IconSize = 36;

    /// <summary>A button showing only a symbol (media / volume / brightness); the label is still recorded for tests and logs.</summary>
    public static void SymbolButton(Canvas c, int x, int width, string label, BarAction action, Rgb fill, PixelBuffer symbol)
    {
        RoundedRect(c.Image, x, BoxTop, width, BoxHeight, Radius, fill);
        Picture(c.Image, symbol, x + (width - symbol.Width) / 2, (BarController.Height - symbol.Height) / 2, 0, fill);
        c.Texts.Add(label);
        c.Regions.Add(new HitRegion(x, width, action));
    }

    /// <summary>A button with a picture (e.g. a program's icon) and its label side by side, centred together.</summary>
    public static void IconButton(Canvas c, int x, int width, string label, BarAction action, Rgb fill, Rgb text, PixelBuffer? icon)
    {
        if (icon is null) { Button(c, x, width, label, action, fill, text); return; }
        RoundedRect(c.Image, x, BoxTop, width, BoxHeight, Radius, fill);
        const int gap = 8;
        var fitted = PixelFont.Fit(label, Math.Max(0, width - 16 - icon.Width - gap), Small);
        c.Texts.Add(fitted);
        var textW = fitted.Length > 0 ? PixelFont.MeasureWidth(fitted, Small) + gap : 0;
        var left = x + Math.Max(8, (width - icon.Width - textW) / 2);
        Picture(c.Image, icon, left, (BarController.Height - icon.Height) / 2, 0, fill);
        if (fitted.Length > 0) PixelFont.DrawText(c.Image, fitted, left + icon.Width + gap, CenterY(Small), Small, text);
        c.Regions.Add(new HitRegion(x, width, action));
    }

    /// <summary>Width of the volume slider on the Windows pages.</summary>
    public const int SliderWidth = 380;

    /// <summary>
    /// Volume slider: a speaker symbol and a track with a knob at the current level (grey and a crossed speaker when muted).
    /// The whole box is one tap / drag region; the % comes from the finger's place along the track.
    /// </summary>
    public static void VolumeSlider(Canvas c, int x, int width, int? percent, bool muted, Palette pal) =>
        Slider(c, x, width, percent, muted, pal, brightness: false);

    /// <summary>The brightness slider (a sun instead of the speaker), shown when nothing is playing.</summary>
    public static void BrightnessSlider(Canvas c, int x, int width, int? percent, Palette pal) =>
        Slider(c, x, width, percent, false, pal, brightness: true);

    private static void Slider(Canvas c, int x, int width, int? percent, bool muted, Palette pal, bool brightness)
    {
        RoundedRect(c.Image, x, BoxTop, width, BoxHeight, Radius, pal.Button);
        var symbol = brightness ? IconKind.BrightnessUp : muted ? IconKind.Mute : IconKind.Speaker;
        Picture(c.Image, IconArt.Render(symbol, pal.Text, pal.Button), x + 16, (BarController.Height - IconArt.Size) / 2, 0, pal.Button);
        c.Texts.Add(brightness ? "BRT" : "VOL");
        const int labelW = 70, valueW = 20, trackH = 8, knob = 22; // no % text - the fill shows the level
        var trackLeft = x + labelW + knob / 2;
        var trackWidth = width - labelW - valueW - knob;
        var trackY = (BarController.Height - trackH) / 2;
        RoundedRect(c.Image, trackLeft, trackY, trackWidth, trackH, trackH / 2, pal.Track, pal.Button);
        if (percent is { } p)
        {
            var fillTone = muted ? pal.Dim : pal.Accent;
            var filled = (int)Math.Round(trackWidth * p / 100.0);
            RoundedRect(c.Image, trackLeft, trackY, Math.Max(trackH, filled), trackH, trackH / 2, fillTone, pal.Button);
            RoundedRect(c.Image, trackLeft + filled - knob / 2, (BarController.Height - knob) / 2, knob, knob, knob / 2, muted ? pal.Dim : pal.Text, pal.Button);

        }
        c.Regions.Add(new HitRegion(x, width, new VolumeSlider(trackLeft, trackWidth, brightness)));
    }

    /// <summary>The whole bar in one bright colour with a message in large dark text (a finished timer).</summary>
    public static void Flash(Canvas c, string text, Rgb fill, Rgb? ink = null)
    {
        RoundedRect(c.Image, 2, 2, BarController.Width - 4, BarController.Height - 4, Radius + 2, fill);
        var t = PixelFont.Normalise(text);
        var maxW = BarController.Width - 80;
        var w = Math.Min(PixelFont.MeasureWidth(t, Large), maxW);
        Text(c, t, (BarController.Width - w) / 2, CenterY(Large), Large, ink ?? new Rgb(10, 10, 12), maxW);
    }

    public static readonly Rgb BannerBackground = new(30, 30, 34);
    public static readonly Rgb WhisperColour = new(230, 130, 230), GuildColour = new(100, 220, 100);

    /// <summary>
    /// The one banner style used for alerts and chat: dark rounded box, a thin coloured stripe on the left, a short
    /// coloured title on top and the detail in white below. Tapping it runs <paramref name="onTap"/> (dismiss).
    /// Chat lines have no box (<paramref name="boxed"/> false): the Touch Bar shows this dark grey as black anyway, and
    /// the website pictures should match the bar.
    /// </summary>
    public static void Banner(Canvas c, int x, int width, Rgb tone, string title, string detail, Palette pal, BarAction onTap,
        bool boxed = true, string? scrollText = null, int scrollPx = 0)
    {
        if (boxed) RoundedRect(c.Image, x, BoxTop, width, BoxHeight, Radius, BannerBackground);
        c.Image.FillRect(x + 8, BoxTop + 8, 4, BoxHeight - 16, tone);
        Text(c, title, x + 22, Line1Y, Small, tone, width - 36);
        if (scrollText is null) Text(c, detail, x + 22, Line2Y, Small, pal.Text, width - 36);
        else
        {
            // Long chat text glides (see ChatTracker); the canvas keeps the whole characters in view for tests and logs.
            c.Texts.Add(detail);
            c.Animating = true;
            PixelFont.DrawTextClipped(c.Image, scrollText, x + 22, Line2Y, Small, pal.Text, scrollPx, x + 22, x + width - 14,
                boxed ? BannerBackground : Rgb.Black, Marquee.Fade);
        }
        c.Regions.Add(new HitRegion(x, width, onTap));
    }

    /// <summary>Characters of small text that fit in a banner <paramref name="width"/> pixels wide.</summary>
    public static int ChatChars(int width) => Math.Max(1, (width - 36) / (PixelFont.Advance * Small));

    /// <summary>A chat line: who (coloured like WoW's chat) on top, the text below; tap to skip.</summary>
    public static void ChatBanner(Canvas c, int x, int width, ChatBanner b, Palette pal) =>
        Banner(c, x, width, b.Kind is Protocol.ChatKind.Guild or Protocol.ChatKind.GuildMention ? GuildColour : WhisperColour,
            b.Header, b.Line, pal, new DismissChat(), boxed: false, scrollText: b.Full, scrollPx: b.ScrollPx);

    /// <summary>Colour of an alert's stripe and title.</summary>
    public static Rgb AlertTone(AlertKind kind, Palette pal) => kind switch
    {
        AlertKind.QuestComplete or AlertKind.DungeonReady or AlertKind.LevelUp or AlertKind.Added => pal.Done,
        AlertKind.BagsFull => pal.Bad,
        AlertKind.DurabilityLow or AlertKind.ReadyCheck => pal.Warn,
        _ => new Rgb(150, 150, 230),
    };

    public static void AlertBanner(Canvas c, int x, int width, Alert a, Palette pal) =>
        Banner(c, x, width, AlertTone(a.Kind, pal), a.Title, a.Detail, pal, new DismissAlert());

    /// <summary>Horizontal progress bar; fraction is clamped to 0..1, NaN draws empty.</summary>
    public static void Bar(Canvas c, int x, int y, int width, int height, double fraction, Rgb fill, Rgb track)
    {
        c.Image.FillRect(x, y, width, height, track);
        var f = double.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0, 1);
        c.Image.FillRect(x, y, (int)Math.Round(width * f), height, fill);
    }

    /// <summary>Copper as "12G 3S 4C" (leading zero units left out; "0C" for nothing).</summary>
    public static string Money(ulong copper)
    {
        ulong g = copper / 10000, s = copper / 100 % 100, c = copper % 100;
        if (g > 0) return $"{g}G {s}S {c}C";
        if (s > 0) return $"{s}S {c}C";
        return $"{c}C";
    }
}
