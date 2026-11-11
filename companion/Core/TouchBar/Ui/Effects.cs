namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>How a finished timer shows on the bar (tray > Timer alert), saved in settings.json under these names. Rainbow
/// and Warp are retired: settings with them get Flash.</summary>
public enum AlertStyle { Fireworks, Laser, Rainbow, Flash, Warp, Shockwave }

/// <summary>
/// The "timer done" animations: drawn over the whole bar for a few seconds, the timer's name in the middle. Pure
/// functions of the time since the alert started (deterministic, so they can be tested and look the same each time).
/// </summary>
public static class Effects
{
    private const int W = BarController.Width, H = BarController.Height;

    public static void Draw(Canvas c, AlertStyle style, TimeSpan t, string text, Palette pal)
    {
        var s = Math.Max(0, t.TotalSeconds);
        switch (style)
        {
            case AlertStyle.Shockwave:
                Shockwave(c.Image, s, pal.Accent);
                ShockLabel(c, text, s);
                return;
            case AlertStyle.Flash or AlertStyle.Warp or AlertStyle.Rainbow:
                if (BarController.FlashOn(t)) Ui.Draw.Flash(c, text, pal.Accent, Rgb.White); // plain, in the bar colour
                return;
            case AlertStyle.Fireworks: Fireworks(c.Image, s); break;
            case AlertStyle.Laser: Laser(c.Image, s, pal); break;
        }
        Label(c, text, false);
    }

    /// <summary>The message on a dark pill so it stays readable over the effect.</summary>
    private static void Label(Canvas c, string text, bool darkText)
    {
        var t = PixelFont.Normalise(text);
        var maxW = W - 120;
        var w = Math.Min(PixelFont.MeasureWidth(t, Ui.Draw.Large), maxW);
        var x = (W - w) / 2;
        if (!darkText) Ui.Draw.RoundedRect(c.Image, x - 24, 6, w + 48, H - 12, 12, Rgb.Black);
        Ui.Draw.Text(c, t, x, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, darkText ? new Rgb(10, 10, 12) : Rgb.White, maxW);
    }

    private static void Add(PixelBuffer img, int x, int y, Rgb col, double a)
    {
        if (x < 0 || y < 0 || x >= W || y >= H || a <= 0) return;
        a = Math.Min(1, a);
        var p = img.GetPixel(x, y);
        byte Mix(byte under, byte over) => (byte)Math.Min(255, under + over * a);
        img.SetPixel(x, y, new Rgb(Mix(p.R, col.R), Mix(p.G, col.G), Mix(p.B, col.B)));
    }

    private static readonly Rgb[] Colours =
        [new(255, 80, 80), new(255, 200, 60), new(90, 220, 120), new(80, 170, 255), new(200, 110, 255), new(255, 255, 255)];

    /// <summary>
    /// Fireworks: rockets shoot up from the bottom edge, then burst into round showers of sparks (round on the real
    /// bar: same reach across and down) that drift down and fade. Several go off at once along the whole bar.
    /// </summary>
    private static void Fireworks(PixelBuffer img, double s)
    {
        img.FillRect(0, 0, W, H, Rgb.Black);
        const double every = 0.07, rise = 0.25, life = 1.3;
        for (var k = (int)Math.Max(0, Math.Floor((s - rise - life) / every)); k * every <= s; k++)
        {
            var t = s - k * every;
            var cx = 30 + (k * 619 + 157) % (W - 60);
            var cy = 14 + (k * 41) % 22;
            var col = Colours[k % Colours.Length];
            if (t < rise)
            {
                // the rocket: a short bright streak climbing to where it bursts
                var p = t / rise;
                var y = H + 4 - (H + 4 - cy) * (1 - (1 - p) * (1 - p));
                for (var d = 0; d < 8; d++) { Add(img, cx, (int)(y + d), Rgb.White, 1 - d * 0.11); Add(img, cx + 1, (int)(y + d), new Rgb(255, 210, 120), 0.8 - d * 0.1); }
                continue;
            }
            var age = (t - rise) / life;
            if (age > 1) continue;
            var reach = (1 - Math.Pow(1 - age, 3)) * (24 + (k * 13) % 8);   // radius in pixels: round, fits the bar
            var bright = Math.Pow(1 - age, 0.9) * 2.5;
            if (age < 0.1)
                for (var dx = -6; dx <= 6; dx++)
                    for (var dy = -6; dy <= 6; dy++)
                        Add(img, cx + dx, cy + dy, Rgb.White, (1 - age / 0.1) * (1 - Math.Sqrt(dx * dx + dy * dy) / 7.0));
            for (var j = 0; j < 24; j++)
            {
                var angle = j * Math.PI * 2 / 24 + k * 0.5;
                var fall = 9 * age * age;                                    // a little gravity
                for (var tr = 0; tr < 4; tr++)
                {
                    var r = reach * (1 - tr * 0.12);
                    var x = cx + Math.Cos(angle) * r;
                    var y = cy + Math.Sin(angle) * r + fall;
                    var a = bright * (tr == 0 ? 1 : 0.6 - tr * 0.12);
                    Add(img, (int)Math.Round(x), (int)Math.Round(y), col, a);
                    if (tr == 0)
                    {
                        for (var dx = 0; dx <= 1; dx++)
                            for (var dy = 0; dy <= 1; dy++) Add(img, (int)Math.Round(x) + dx, (int)Math.Round(y) + dy, col, a);
                    }
                }
            }
        }
    }
    /// <summary>A scanner beam sweeping end to end with a smooth fading streak behind it, and a thin glowing line.</summary>
    private static void Laser(PixelBuffer img, double s, Palette pal)
    {
        img.FillRect(0, 0, W, H, Rgb.Black);
        var beam = new Rgb(255, 40, 40);
        double Pos(double time) => W / 2.0 + (W / 2.0 - 30) * Math.Sin(time * Math.PI * 2 / 2.2);
        var glow = new double[W];
        const int samples = 90;
        for (var k = 0; k < samples; k++)
        {
            var p = Pos(s - k * 0.004);                       // 0.36 s of trail, sampled finely so it's one streak
            var weight = k == 0 ? 1 : 0.8 * (1 - k / (double)samples);
            var width = k == 0 ? 16.0 : 9.0;
            for (var x = Math.Max(0, (int)p - 50); x < Math.Min(W, (int)p + 50); x++)
                glow[x] = Math.Max(glow[x], weight * Math.Exp(-Math.Pow((x - p) / width, 2)));
        }
        for (var x = 0; x < W; x++)
        {
            if (glow[x] < 0.02) continue;
            for (var y = 0; y < H; y++)
                Add(img, x, y, beam, glow[x] * (0.35 + 0.65 * Math.Exp(-Math.Pow((y - H / 2.0) / 9.0, 2))) * 1.3);
        }
        for (var x = 0; x < W; x++) { Add(img, x, H / 2 - 1, beam, 0.3); Add(img, x, H / 2, beam, 0.45); }
    }
    // ------------------------------------------------------------------ Shockwave

    /// <summary>
    /// Shockwave in the player's colour: every half second a pulse bursts from the middle - the whole bar kicks bright,
    /// two glowing waves race to both ends with sparks flying off them.
    /// </summary>
    private static void Shockwave(PixelBuffer img, double s, Rgb accent)
    {
        const double every = 0.5, speed = 1700;
        var since = s % every;
        var kick = Math.Exp(-since / 0.12);
        accent = new Rgb((byte)Math.Min(255, accent.R * 1.5 + 30), (byte)Math.Min(255, accent.G * 1.5 + 30), (byte)Math.Min(255, accent.B * 1.5 + 30)); // brighter than the tab colour
        var bgLevel = 0.06 + 0.55 * kick;
        var bg = new Rgb((byte)(accent.R * bgLevel), (byte)(accent.G * bgLevel), (byte)(accent.B * bgLevel));
        img.FillRect(0, 0, W, H, bg);
        var hot = new Rgb((byte)Math.Min(255, accent.R + 150), (byte)Math.Min(255, accent.G + 150), (byte)Math.Min(255, accent.B + 150));
        for (var p = 0; p < 4; p++)                                   // this pulse and the ones before (still travelling)
        {
            var age = since + p * every;
            var dist = age * speed;
            if (dist > W / 2.0 + 200) continue;
            var fade = Math.Max(0, 1 - 0.6 * dist / (W / 2.0 + 200));
            foreach (var dir in new[] { -1, 1 })
            {
                var front = W / 2.0 + dir * dist;
                for (var x = (int)Math.Max(0, front - 700); x < Math.Min(W, front + 700); x++)
                {
                    var behind = (front - x) * dir;                    // >0: behind the front (the trail)
                    var a = behind < 0 ? Math.Exp(-Math.Pow(behind / 8.0, 2)) : Math.Exp(-behind / 90.0);
                    a *= fade * 1.8;
                    if (a < 0.02) continue;
                    var core = behind is > -6 and < 18 ? 1 : 0;
                    for (var y = 0; y < H; y++)
                    {
                        var v = a * (0.6 + 0.4 * Math.Exp(-Math.Pow((y - H / 2.0) / 14.0, 2)));
                        Add(img, x, y, core == 1 ? hot : accent, v);
                    }
                }
                for (var j = 0; j < 14; j++)                          // sparks thrown off the wave
                {
                    var sx = front - dir * (8 + (j * 37 + p * 11) % 90) * (0.4 + age);
                    var sy = H / 2.0 + Math.Sin(j * 2.1 + p) * (10 + age * 40);
                    Add(img, (int)sx, (int)sy, Rgb.White, fade * 0.9);
                    Add(img, (int)sx + 1, (int)sy, hot, fade * 0.7);
                }
            }
        }
    }

    /// <summary>The message pops bigger on each pulse, then settles.</summary>
    private static void ShockLabel(Canvas c, string text, double s)
    {
        var t = PixelFont.Normalise(text);
        var pop = s % 0.5 < 0.12;
        var scale = pop && PixelFont.MeasureWidth(t, 5) < W - 160 ? 5 : Ui.Draw.Large;
        var w = Math.Min(PixelFont.MeasureWidth(t, scale), W - 120);
        var x = (W - w) / 2;
        Ui.Draw.RoundedRect(c.Image, x - 24, 4, w + 48, H - 8, 12, Rgb.Black);
        Ui.Draw.Text(c, t, x, Ui.Draw.CenterY(scale), scale, Rgb.White, W - 120);
    }

}
