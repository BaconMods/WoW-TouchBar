using WoWTouchBar.Core.Protocol;

namespace WoWTouchBar.Core.TouchBar.Ui;

/// <summary>
/// What the bar shows for one chat line: a header ("WHISPER FROM THRALL") and the visible part of the text
/// (<paramref name="Line"/>, whole characters). Long text glides: <paramref name="Full"/> is drawn shifted left by
/// <paramref name="ScrollPx"/> pixels (smooth, not a character at a time).
/// </summary>
public sealed record ChatBanner(string Header, string Line, ChatKind Kind, string? Full = null, int ScrollPx = 0);

/// <summary>
/// Shows incoming whispers / guild lines one at a time (pure; tested). Each stays at least 8 s; text too long for the
/// bar waits 1.5 s, then glides at 8 characters a second, and stays 2 s at the end (30 s at most). A tap skips to
/// the next one. Nothing here is logged or stored.
/// </summary>
public sealed class ChatTracker
{
    public static readonly TimeSpan MinShow = TimeSpan.FromSeconds(8), MaxShow = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan ScrollDelay = TimeSpan.FromSeconds(1.5), EndPause = TimeSpan.FromSeconds(2);
    public const double CharsPerSecond = 8;

    private int _shownUpTo; // highest chat Seq already shown (or skipped)
    private (int Seq, ChatMessage Chat, TimeSpan Start)? _current;

    public static string Header(ChatMessage c) => c.Kind switch
    {
        ChatKind.Whisper => string.IsNullOrWhiteSpace(c.Sender) ? "WHISPER" : $"WHISPER FROM {c.Sender}",
        ChatKind.BattleNetWhisper => "BATTLE.NET FRIEND",
        ChatKind.GuildMention => string.IsNullOrWhiteSpace(c.Sender) ? "GUILD (MENTION)" : $"GUILD (MENTION) - {c.Sender}",
        _ => string.IsNullOrWhiteSpace(c.Sender) ? "GUILD" : $"GUILD - {c.Sender}",
    };

    /// <summary>How long a message with this many characters stays when the bar shows <paramref name="visible"/> at once.</summary>
    public static TimeSpan Duration(int length, int visible)
    {
        if (length <= visible) return MinShow;
        var scroll = TimeSpan.FromSeconds((length - visible) / CharsPerSecond);
        var total = ScrollDelay + scroll + EndPause;
        return total < MinShow ? MinShow : total > MaxShow ? MaxShow : total;
    }

    /// <summary>The banner to show now, or null. <paramref name="visible"/> = characters that fit on one line.</summary>
    public ChatBanner? Current(GameState s, TimeSpan now, int visible)
    {
        if (_current is { } cur)
        {
            var text = PixelFont.Normalise(cur.Chat.Text);
            if (now - cur.Start < Duration(text.Length, visible)) return Banner(cur.Chat, text, now - cur.Start, visible);
            _current = null;
        }
        var next = s.Chats.FirstOrDefault(c => c.Seq > _shownUpTo);
        if (next.Chat is null) return null;
        _shownUpTo = next.Seq;
        _current = (next.Seq, next.Chat, now);
        return Banner(next.Chat, PixelFont.Normalise(next.Chat.Text), TimeSpan.Zero, visible);
    }

    public void Dismiss() => _current = null;

    private static ChatBanner Banner(ChatMessage c, string text, TimeSpan shown, int visible)
    {
        if (text.Length <= visible || visible <= 0) return new ChatBanner(Header(c), text, c.Kind);
        var advance = PixelFont.Advance * Ui.Draw.Small;
        var px = Math.Clamp(Math.Max(0, (shown - ScrollDelay).TotalSeconds) * CharsPerSecond * advance, 0, (text.Length - visible) * advance);
        var offset = (int)Math.Floor(px / advance);
        return new ChatBanner(Header(c), text.Substring(offset, visible), c.Kind, text, (int)Math.Round(px));
    }
}
