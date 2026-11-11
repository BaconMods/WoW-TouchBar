using WoWTouchBar.Core.Protocol;

namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>The WoW pages (content on the left, tabs on the right). Shows only what the addon sent; never invents data.</summary>
public static class WowPages
{
    public const int Margin = 12;
    public const int TabWidth = 130, TabGap = 8;
    public static readonly (PageId Page, string Label)[] Tabs =
        [(PageId.Quest, "QUEST"), (PageId.Xp, "XP"), (PageId.Reputation, "REP")]; // no Bags tab: always shown in a box
    /// <summary>Page → bit in the DISPLAY message (the in-game options).</summary>
    public static int Bit(PageId p) => 1 << (int)p;

    /// <summary>The tabs the player kept on (in-game options); all of them until the addon says otherwise.
    /// NOTE / TIMER only while there are notes / timers (and the player didn't switch them off).</summary>
    public static IReadOnlyList<(PageId Page, string Label)> VisibleTabs(DisplayMessage? d, bool notes = false, bool timers = false)
    {
        var shown = d is null ? [.. Tabs] : Tabs.Where(t => (d.Pages & Bit(t.Page)) != 0).ToList();
        if (shown.Count == 0) shown = [.. Tabs]; // never an empty bar
        if (notes && (d is null || (d.Pages & Bit(PageId.Notes)) != 0)) shown.Add((PageId.Notes, "NOTE"));
        if (timers && (d is null || (d.Pages & Bit(PageId.Timers)) != 0)) shown.Add((PageId.Timers, "TIMER"));
        return shown;
    }

    /// <summary>Left edge of the tabs when <paramref name="count"/> are shown (they stay anchored on the right).</summary>
    public static int TabsXFor(int count) => DotsX - TabGap - count * TabWidth - (count - 1) * TabGap;

    /// <summary>The "•••" button at the far right: opens the in-game Windows panel (volume, media, brightness, My keys).</summary>
    public const int DotsWidth = 70;
    public static int DotsX => BarController.Width - Margin - DotsWidth;
    public static int ContentWidthFor(int count) => TabsXFor(count) - 2 * Margin;
    public static int TabsX => TabsXFor(Tabs.Length);
    public static int ContentWidth => ContentWidthFor(Tabs.Length);

    /// <summary>Characters of chat text that fit on a WoW page (less while the queue timer / bags box are shown).</summary>
    public static int ChatChars(bool queued, int tabs = 3, int utilityWidth = 0) =>
        Ui.Draw.ChatChars(ContentWidthFor(tabs) + 8 - (queued ? QueueChipWidth + TabGap : 0) - (utilityWidth > 0 ? utilityWidth + TabGap : 0));

    /// <summary>Bags / durability / gold box, shown on every WoW page (replaced the Bags tab).</summary>
    /// <summary>The box is as wide as its one line of large text, up to this.</summary>
    public const int UtilityChipMaxWidth = 500;
    private const int UPad = 14, UIcon = 30, UIconGap = 8, UItemGap = 22;

    private static (string Bags, string Durability, string Gold) UtilityTexts(UtilityMessage u)
    {
        var bags = u.BagsFree is { } free ? $"{free}" : "?";
        var dur = u.DurabilityLowest is { } d ? $"{d}%" : "?";
        var fixedW = 2 * UPad + 3 * (UIcon + UIconGap) + 2 * UItemGap
            + PixelFont.MeasureWidth(bags, Ui.Draw.Large) + PixelFont.MeasureWidth(dur, Ui.Draw.Large);
        var gold = u.Money is { } money ? MoneyToFit(money, UtilityChipMaxWidth - fixedW, Ui.Draw.Large) : "?";
        return (bags, dur, gold);
    }

    /// <summary>Width of the bags / durability / gold box for these values (tested).</summary>
    public static int UtilityChipWidthFor(UtilityMessage u)
    {
        var (bags, dur, gold) = UtilityTexts(u);
        return Math.Min(UtilityChipMaxWidth, 2 * UPad + 3 * (UIcon + UIconGap) + 2 * UItemGap
            + PixelFont.MeasureWidth(bags, Ui.Draw.Large) + PixelFont.MeasureWidth(dur, Ui.Draw.Large) + PixelFont.MeasureWidth(gold, Ui.Draw.Large));
    }

    /// <summary>The box is on unless the player turned off "Bags, durability, gold" in the in-game options.</summary>
    public static bool ShowUtility(DisplayMessage? d) => d is null || (d.Pages & Bit(PageId.Bags)) != 0;

    /// <summary>Queue timer box: hourglass, time and role on one line in large text.</summary>
    public const int QueueChipWidth = 320; // fits "[hourglass] 59:59 HEAL"; past an hour the role is dropped if it doesn't fit

    public static void Draw(Canvas c, GameState? state, LinkStatus link, PageId page, int objectiveOffset, Alert? alert = null, int? queueSeconds = null, ChatBanner? chat = null, AccentColour accent = AccentColour.Purple,
        PadView? pad = null)
    {
        var message = link switch
        {
            LinkStatus.WaitingForWow => "WAITING FOR WOW",
            LinkStatus.LookingForAddon => "WOW FOUND - WAITING FOR THE ADDON STRIP",
            LinkStatus.NoSignal => "NO SIGNAL FROM WOW",
            LinkStatus.AddonIncompatible => "ADDON AND APP VERSIONS DO NOT MATCH - UPDATE BOTH",
            _ => state?.Player is null ? "CONNECTING..." : null,
        };
        if (message is not null)
        {
            Ui.Draw.Text(c, message, Margin, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, Palette.Normal.Dim, BarController.Width - 2 * Margin);
            return;
        }

        var pal = Palette.For(accent, stale: link == LinkStatus.Stale);
        var tabs = VisibleTabs(state?.Display, pad?.Notes.Count > 0, pad?.Timers.Count > 0);
        var tabsX = TabsXFor(tabs.Count);
        var x = tabsX;
        foreach (var (p, label) in tabs)
        {
            Ui.Draw.Button(c, x, TabWidth, label, new ShowPage(p), p == page ? pal.Accent : pal.Button, p == page ? pal.Text : pal.Dim);
            x += TabWidth + TabGap;
        }
        DotsButton(c, pal);

        // Dungeon queue: a timer box left of the tabs while queued; the page gets the rest of the width.
        var w = ContentWidthFor(tabs.Count);
        if (queueSeconds is { } qs && state?.Status is { } status)
        {
            w -= QueueChipWidth + TabGap;
            QueueChip(c, tabsX - TabGap - QueueChipWidth, qs, status, pal);
        }
        if (state?.Utility is { } utility && ShowUtility(state.Display))
        {
            var uw = UtilityChipWidthFor(utility);
            UtilityChip(c, Margin + w - uw, uw, utility, pal);
            w -= uw + TabGap;
        }

        if (alert is not null)
        {
            Ui.Draw.AlertBanner(c, Margin - 4, w + 8, alert, pal);
            return;
        }
        if (chat is not null)
        {
            Ui.Draw.ChatBanner(c, Margin - 4, w + 8, chat, pal);
            return;
        }
        switch (page)
        {
            case PageId.Quest: Quest(c, state!, objectiveOffset, pal, w); break;
            case PageId.Xp: Xp(c, state!.Player!, pal, w); break;
            case PageId.Reputation: Reputation(c, state!.Reputation, pal, w); break;
            case PageId.Notes when pad is not null: Notes(c, pad, pal, w); break;
            case PageId.Timers when pad is not null: Timers(c, pad, pal, w); break;
        }
    }

    // ------------------------------------------------------------------ Notes & timers (Phase 7)

    /// <summary>One note in large text (long ones scroll), "2/3" and an X at the right; tap for the next note.</summary>
    private static void Notes(Canvas c, PadView pad, Palette pal, int w) => NoteLine(c, Margin, w, pad, pal);

    public const int TimersPerView = 3;

    /// <summary>Up to three timers side by side (name, time left, a bar that empties, an X); tap for more.</summary>
    private static void Timers(Canvas c, PadView pad, Palette pal, int w) => TimerRow(c, Margin, w, Math.Clamp(w / 340, 1, TimersPerView), pad, pal); // fewer, wider ones when space is short

    /// <summary>The small X that removes a note / timer (a few seconds to undo).</summary>
    public const int RemoveSize = 44;

    private static void RemoveButton(Canvas c, int x, BarAction action, Palette pal)
    {
        var y = (BarController.Height - RemoveSize) / 2;
        Ui.Draw.RoundedRect(c.Image, x, y, RemoveSize, RemoveSize, Ui.Draw.Radius, pal.Button);
        const int icon = 26;
        Ui.Draw.Picture(c.Image, IconArt.Render(IconKind.Close, pal.Dim, pal.Button, icon), x + (RemoveSize - icon) / 2, (BarController.Height - icon) / 2, 0, pal.Button);
        c.Regions.Add(new HitRegion(x, RemoveSize, action));
    }

    /// <summary>The current note across <paramref name="w"/> pixels from <paramref name="x"/>: text, "2/3", X.</summary>
    public static void NoteLine(Canvas c, int x, int w, PadView pad, Palette pal)
    {
        if (pad.Notes.Count == 0) return;
        var i = ((pad.NoteIndex % pad.Notes.Count) + pad.Notes.Count) % pad.Notes.Count;
        var removeX = x + w - RemoveSize;
        RemoveButton(c, removeX, new RemoveNote(pad.Notes[i]), pal);
        var counter = pad.Notes.Count > 1 ? $"{i + 1}/{pad.Notes.Count}" : "";
        var counterW = counter.Length == 0 ? 0 : PixelFont.MeasureWidth(counter, Ui.Draw.Small);
        if (counterW > 0) Ui.Draw.Text(c, counter, removeX - 16 - counterW, Ui.Draw.CenterY(Ui.Draw.Small), Ui.Draw.Small, pal.Dim);
        var textW = removeX - x - 24 - (counterW > 0 ? counterW + 16 : 0);
        Ui.Draw.ScrollText(c, pad.Notes[i].Text, x, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, pal.Text, textW);
        if (pad.Notes.Count > 1) c.Regions.Add(new HitRegion(x, removeX - 8 - x, new NextNote()));
    }

    public const int TimerGap = 28;

    /// <summary>
    /// Up to <paramref name="slots"/> timers across <paramref name="w"/> pixels from <paramref name="x"/>, plain on black:
    /// the name and the time left on top, a bar below that empties as time runs out (green and "DONE" at the end), and
    /// an X. More timers than slots: tap the row for the next ones.
    /// </summary>
    public static void TimerRow(Canvas c, int x, int w, int slots, PadView pad, Palette pal)
    {
        if (pad.Timers.Count == 0 || slots <= 0) return;
        var pages = (pad.Timers.Count + slots - 1) / slots;
        var shown = pad.Timers.Skip((pad.TimerPage % pages) * slots).Take(slots).ToList();
        var chipW = (w - (slots - 1) * TimerGap) / slots;
        for (var k = 0; k < shown.Count; k++)
        {
            var t = shown[k];
            var cx = x + k * (chipW + TimerGap);
            var removeX = cx + chipW - RemoveSize;
            RemoveButton(c, removeX, new RemoveTimer(t), pal);
            var tw = removeX - 16 - cx;
            var done = t.Done(pad.Now);
            var left = t.Left(pad.Now);
            var time = done ? "DONE" : Duration((int)Math.Ceiling(left.TotalSeconds));
            var timeW = PixelFont.MeasureWidth(time, Ui.Draw.Small);
            Ui.Draw.Text(c, time, cx + tw - timeW, Ui.Draw.Line1Y, Ui.Draw.Small, done ? pal.Done : pal.Text);
            Ui.Draw.Text(c, t.Label, cx, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Dim, tw - timeW - 16);
            var fraction = done || t.TotalSeconds <= 0 ? 1.0 : Math.Clamp(left.TotalSeconds / t.TotalSeconds, 0, 1);
            Ui.Draw.Bar(c, cx, Ui.Draw.Line2Y + 4, tw, 14, fraction, done ? pal.Done : pal.Xp, pal.Track);
        }
        if (pages > 1) c.Regions.Add(new HitRegion(x, w, new NextTimers()));
    }
    /// <summary>"•••": three dots centred on a button (full stops in the font sit too low).</summary>
    private static void DotsButton(Canvas c, Palette pal)
    {
        Ui.Draw.RoundedRect(c.Image, DotsX, Ui.Draw.BoxTop, DotsWidth, Ui.Draw.BoxHeight, Ui.Draw.Radius, pal.Button);
        const int dot = 6, gap = 6;
        var x = DotsX + (DotsWidth - 3 * dot - 2 * gap) / 2;
        var y = (BarController.Height - dot) / 2;
        for (var i = 0; i < 3; i++) Ui.Draw.RoundedRect(c.Image, x + i * (dot + gap), y, dot, dot, 3, pal.Text, pal.Button);
        c.Texts.Add("...");
        c.Regions.Add(new HitRegion(DotsX, DotsWidth, new ToggleQuickPanel()));
    }

    /// <summary>"4:32" / "1:02:09" (tested).</summary>
    public static string Duration(int seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    private static void QueueChip(Canvas c, int x, int seconds, StatusMessage s, Palette pal)
    {
        Ui.Draw.RoundedRect(c.Image, x, Ui.Draw.BoxTop, QueueChipWidth, Ui.Draw.BoxHeight, Ui.Draw.Radius, pal.Button);
        const int pad = 14, icon = 30, gap = 12, roleGap = 20;
        var time = Duration(seconds);
        var timeW = PixelFont.MeasureWidth(time, Ui.Draw.Large);
        var role = s.Tank ? "TANK" : s.Healer ? "HEAL" : s.Dps ? "DPS" : "";
        var roleW = role.Length > 0 ? PixelFont.MeasureWidth(role, Ui.Draw.Large) + roleGap : 0;
        if (icon + gap + timeW + roleW > QueueChipWidth - 2 * pad) { role = ""; roleW = 0; } // long queues: time first
        var left = x + Math.Max(pad, (QueueChipWidth - icon - gap - timeW - roleW) / 2);
        Ui.Draw.Picture(c.Image, IconArt.Render(IconKind.Hourglass, pal.Dim, pal.Button, icon), left, (BarController.Height - icon) / 2, 0, pal.Button);
        var y = Ui.Draw.CenterY(Ui.Draw.Large);
        var end = Ui.Draw.Text(c, time, left + icon + gap, y, Ui.Draw.Large, pal.Text);
        if (role.Length > 0) Ui.Draw.Text(c, role, end + roleGap, y, Ui.Draw.Large, pal.Dim);
    }

    // ------------------------------------------------------------------ Quest

    /// <summary>The objective line: "have/need label", or the label alone when not numeric (tested).</summary>
    public static string ObjectiveText(ObjectiveMessage o) =>
        o.Numeric && o.Have is { } have && o.Need is { } need
            ? (string.IsNullOrWhiteSpace(o.Label) ? $"{have}/{need}" : $"{have}/{need} {o.Label}")
            : o.Label;

    private static void Quest(Canvas c, GameState s, int offset, Palette pal, int w)
    {
        var q = s.Quest;
        if (q is null || q.None)
        {
            Ui.Draw.Text(c, q is null ? "WAITING FOR QUEST DATA" : "NO TRACKED QUEST", Margin, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, pal.Dim, w);
            return;
        }
        if (q.ReadyForTurnIn)
        {
            Ui.Draw.ScrollText(c, q.Title, Margin, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Dim, w);
            Ui.Draw.Text(c, "READY TO TURN IN", Margin, Ui.Draw.Line2Y, Ui.Draw.Small, pal.Done, w);
            return;
        }

        var o = BarController.PickObjective(s, offset);
        var counter = o is not null && q.ObjectiveCount > 1 ? $"{o.Index}/{q.ObjectiveCount}" : "";
        var counterW = counter.Length == 0 ? 0 : PixelFont.MeasureWidth(counter, Ui.Draw.Small) + 16;
        Ui.Draw.ScrollText(c, q.Title, Margin, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Dim, w - counterW);
        if (counterW > 0) Ui.Draw.Text(c, counter, Margin + w - counterW + 16, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Dim);
        if (o is null) return;

        var colour = o.Done ? pal.Done : pal.Text;
        var x = Margin;
        if (o.Numeric && o.Have is { } have && o.Need is { } need && need > 0)
        {
            x = Ui.Draw.Text(c, $"{have}/{need}", x, Ui.Draw.Line2Y, Ui.Draw.Small, colour) + 12;
            Ui.Draw.Bar(c, x, Ui.Draw.Line2Y + 4, 200, 13, (double)have / need, o.Done ? pal.Done : pal.Xp, pal.Track);
            x += 200 + 16;
            if (!string.IsNullOrWhiteSpace(o.Label)) Ui.Draw.ScrollText(c, o.Label, x, Ui.Draw.Line2Y, Ui.Draw.Small, colour, Margin + w - x);
        }
        else
        {
            Ui.Draw.ScrollText(c, ObjectiveText(o), x, Ui.Draw.Line2Y, Ui.Draw.Small, colour, w);
        }
        if (q.ObjectiveCount > 1) c.Regions.Add(new HitRegion(0, Margin + w, new NextObjective()));
    }

    // ------------------------------------------------------------------ XP

    private static void Xp(Canvas c, PlayerMessage p, Palette pal, int w)
    {
        var x = Ui.Draw.Text(c, p.Level is { } l ? $"LVL {l}" : "LVL ?", Margin, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, pal.Text) + 24;
        if (p.MaxLevel)
        {
            Ui.Draw.Text(c, "MAX LEVEL", x, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, pal.Done);
            return;
        }
        var barW = Math.Clamp(Margin + w - x - 420, 200, 760); // leave ~22 characters for "1644 TO GO  RESTED 900"
        const int barH = 24;
        var barY = (BarController.Height - barH) / 2;
        c.Image.FillRect(x, barY, barW, barH, pal.Track);
        var fill = p.XpPercent is { } pct ? (int)Math.Round(barW * pct / 100) : 0;
        if (p.Rested is { } rested && rested > 0 && p.XpMax is { } max && max > 0)
        {
            var restedEnd = Math.Min(barW, fill + (int)Math.Round(barW * (double)rested / max));
            c.Image.FillRect(x + fill, barY, restedEnd - fill, barH, pal.Rested);
        }
        c.Image.FillRect(x, barY, fill, barH, pal.Xp);
        x += barW + 20;

        var right = w + Margin - x;
        var line1 = p.XpPercent is { } v && p.Xp is { } xp && p.XpMax is { } xpMax ? $"{v:0.0}%  {xp}/{xpMax}" : "XP ?";
        Ui.Draw.Text(c, line1, x, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Text, right);
        var line2 = p.Xp is { } now && p.XpMax is { } m2 && m2 >= now ? $"{m2 - now} TO GO" : "";
        if (p.Rested is { } r && r > 0) line2 += (line2.Length > 0 ? "  " : "") + $"RESTED {r}";
        Ui.Draw.Text(c, line2, x, Ui.Draw.Line2Y, Ui.Draw.Small, p.Rested is > 0 ? pal.Rested : pal.Dim, right);
    }

    // ------------------------------------------------------------------ Bags / durability / gold (always shown)

    /// <summary>
    /// Free bag slots, lowest durability and gold with icons, on one line of large text (like the queue
    /// timer): "[chest] 8  [anvil] 76%  [coins] 7S 49C". Yellow / red when bags are nearly full or gear needs repair;
    /// "?" when unknown. Gold drops its smallest coins when space runs out (<see cref="MoneyToFit"/>).
    /// </summary>
    private static void UtilityChip(Canvas c, int x, int width, UtilityMessage u, Palette pal)
    {
        Ui.Draw.RoundedRect(c.Image, x, Ui.Draw.BoxTop, width, Ui.Draw.BoxHeight, Ui.Draw.Radius, pal.Button);
        var y = Ui.Draw.CenterY(Ui.Draw.Large);
        int Item(IconKind kind, string text, Rgb colour, int ix)
        {
            Ui.Draw.Picture(c.Image, IconArt.Render(kind, pal.Dim, pal.Button, UIcon), ix, (BarController.Height - UIcon) / 2, 0, pal.Button);
            return Ui.Draw.Text(c, text, ix + UIcon + UIconGap, y, Ui.Draw.Large, colour, x + width - UPad - (ix + UIcon + UIconGap));
        }
        var (bags, dur, gold) = UtilityTexts(u);
        var bagColour = u.BagsFree is 0 ? pal.Bad : u.BagsFree is <= 4 ? pal.Warn : pal.Text;
        var end = Item(IconKind.Bag, bags, bagColour, x + UPad);
        var durColour = u.DurabilityLowest is <= 10 ? pal.Bad : u.DurabilityLowest is <= 30 ? pal.Warn : pal.Text;
        end = Item(IconKind.Anvil, dur, durColour, end + UItemGap);
        Item(IconKind.Coin, gold, pal.Text, end + UItemGap);
    }

    /// <summary>
    /// Gold as "12G 34S 56C", dropping the smallest coins until it fits <paramref name="width"/> ("12G 34S", then "12G")
    /// (tested).
    /// </summary>
    public static string MoneyToFit(ulong copper, int width, int scale)
    {
        ulong g = copper / 10000, s = copper / 100 % 100, cu = copper % 100;
        string[] parts = g > 0 ? [$"{g}G", $"{s}S", $"{cu}C"] : s > 0 ? [$"{s}S", $"{cu}C"] : [$"{cu}C"];
        for (var n = parts.Length; n > 1; n--)
        {
            var text = string.Join(" ", parts.Take(n));
            if (PixelFont.MeasureWidth(text, scale) <= width) return text;
        }
        return parts[0];
    }
    // ------------------------------------------------------------------ Reputation (replaced Travel)

    private static void Reputation(Canvas c, ReputationMessage? r, Palette pal, int w)
    {
        if (r is null || r.None)
        {
            Ui.Draw.Text(c, r is null ? "WAITING FOR REPUTATION DATA" : "NO REPUTATION WATCHED", Margin, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Dim, w);
            if (r is not null)
                Ui.Draw.Text(c, "TICK \"SHOW AS EXPERIENCE BAR\" IN THE REPUTATION PANEL", Margin, Ui.Draw.Line2Y, Ui.Draw.Small, pal.Dim, w);
            return;
        }
        var nameW = Math.Min(PixelFont.MeasureWidth(PixelFont.Normalise(r.Name), Ui.Draw.Large), 520);
        var x = Ui.Draw.ScrollText(c, r.Name, Margin, Ui.Draw.CenterY(Ui.Draw.Large), Ui.Draw.Large, pal.Text, nameW) + 24;
        var barW = Math.Clamp(Margin + w - x - 300, 160, 700);
        const int barH = 24;
        var fraction = r.Value is { } v && r.Max is { } m && m > 0 ? (double)v / m : double.NaN;
        Ui.Draw.Bar(c, x, (BarController.Height - barH) / 2, barW, barH, fraction, pal.Done, pal.Track);
        x += barW + 20;
        var right = Margin + w - x;
        Ui.Draw.Text(c, string.IsNullOrWhiteSpace(r.Standing) ? "?" : r.Standing, x, Ui.Draw.Line1Y, Ui.Draw.Small, pal.Text, right);
        Ui.Draw.Text(c, r.Value is { } v2 && r.Max is { } m2 ? $"{v2}/{m2}" : "", x, Ui.Draw.Line2Y, Ui.Draw.Small, pal.Dim, right);
    }
}

/// <summary>What the NOTE / TIMER pages need: the lists, the clock, and which note / group of timers is showing.</summary>
public sealed record PadView(IReadOnlyList<WoWTouchBar.Core.Notes.PadNote> Notes, IReadOnlyList<WoWTouchBar.Core.Notes.PadTimer> Timers,
    DateTimeOffset Now, int NoteIndex = 0, int TimerPage = 0);
