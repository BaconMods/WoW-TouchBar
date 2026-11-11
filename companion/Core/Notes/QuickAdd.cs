using System.Text.RegularExpressions;

namespace WoWTouchBar.Core.Notes;

/// <summary>What the quick-add box will do with its text (pure; tested).</summary>
public sealed record QuickAddIntent(bool IsTimer, string Text, TimeSpan? Length);

/// <summary>
/// The quick-add box (a shortcut, Ctrl+Option+N by default): one line that becomes a timer when it starts with a time
/// written with a unit or a colon ("10m tea", "90s", "1h30m", "1:30 pizza"), otherwise a note. A plain number stays a
/// note ("10 rare spawns").
/// </summary>
public static partial class QuickAdd
{
    [GeneratedRegex(@"^\s*((?:\d+\s*[hms]\s*)+|\d+:\d{2}(?::\d{2})?)(?:\s+(.*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex TimerPattern();

    public static QuickAddIntent Parse(string? text)
    {
        var t = (text ?? "").Trim();
        var m = TimerPattern().Match(t);
        if (m.Success && Scratchpad.ParseDuration(m.Groups[1].Value) is { } length)
            return new QuickAddIntent(true, m.Groups[2].Value.Trim(), length);
        return new QuickAddIntent(false, t, null);
    }

    /// <summary>What Enter will do, for the hint under the box.</summary>
    public static string Describe(QuickAddIntent i)
    {
        if (i.IsTimer)
        {
            var s = (int)i.Length!.Value.TotalSeconds;
            var time = s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}";
            return i.Text.Length > 0 ? $"Enter: start a {time} timer \"{i.Text}\"" : $"Enter: start a {time} timer";
        }
        return i.Text.Length > 0 ? "Enter: add this note" : "Type a note, or a timer like \"10m tea\"";
    }
}

/// <summary>
/// A keyboard shortcut like "Ctrl+Option+N" (Option = Alt in Windows; "Alt" is read too) as Windows hotkey values (pure; tested): modifier flags as RegisterHotKey
/// wants them (Alt 1, Ctrl 2, Shift 4, Win 8) and a virtual-key code. It needs Ctrl, Alt or Win, so it can't steal a
/// normal key. Keys: A-Z, 0-9, F1-F24, Space.
/// </summary>
public readonly record struct Hotkey(int Modifiers, int Key)
{
    public const int Alt = 1, Ctrl = 2, Shift = 4, Win = 8;
    public static readonly Hotkey Default = new(Ctrl | Alt, 'N');

    public static Hotkey? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var mods = 0;
        int? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var p = raw.ToUpperInvariant();
            switch (p)
            {
                case "CTRL" or "CONTROL": mods |= Ctrl; continue;
                case "ALT" or "OPTION" or "OPT": mods |= Alt; continue;   // the Mac's Option key is Alt in Windows
                case "SHIFT": mods |= Shift; continue;
                case "WIN" or "WINDOWS" or "CMD" or "COMMAND": mods |= Win; continue; // Command is the Windows key
            }
            if (key is not null) return null;
            key = p switch
            {
                { Length: 1 } when char.IsAsciiLetterUpper(p[0]) || char.IsAsciiDigit(p[0]) => p[0],
                "SPACE" => 0x20,
                _ when p.StartsWith('F') && int.TryParse(p[1..], out var f) && f is >= 1 and <= 24 => 0x70 + f - 1,
                _ => null,
            };
            if (key is null) return null;
        }
        if (key is not { } k || (mods & (Ctrl | Alt | Win)) == 0) return null;
        return new Hotkey(mods, k);
    }

    /// <summary>The key's name for a virtual-key code, or null if it isn't one we allow.</summary>
    public static string? KeyName(int vk) => vk switch
    {
        >= 'A' and <= 'Z' or >= '0' and <= '9' => ((char)vk).ToString(),
        0x20 => "Space",
        >= 0x70 and <= 0x87 => $"F{vk - 0x70 + 1}",
        _ => null,
    };

    public override string ToString()
    {
        var mods = Modifiers;
        // Named as printed on the Mac keyboard: Option (Alt in Windows) and Cmd (the Windows key).
        return string.Join("+", new[] { (Ctrl, "Ctrl"), (Alt, "Option"), (Shift, "Shift"), (Win, "Cmd") }
            .Where(m => (mods & m.Item1) != 0).Select(m => m.Item2).Append(KeyName(Key) ?? "?"));
    }
}
