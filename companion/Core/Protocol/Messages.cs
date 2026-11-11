using System.Text;

namespace WoWTouchBar.Core.Protocol;

public enum Flavour : byte { Unknown = 0, Forever = 1, Retail = 2 }
public enum QuestSource : byte { None = 0, Pinned = 1, SuperTracked = 2, Selected = 3, Watched = 4 }

public abstract record Message;
public sealed record HelloMessage(Flavour Flavour, uint? Interface, string AddonVersion) : Message;
public sealed record PlayerMessage(int? Level, uint? Xp, uint? XpMax, uint? Rested, bool MaxLevel, bool XpUnknown) : Message
{
    /// <summary>XP progress 0–100, or null when unknown / max level.</summary>
    public double? XpPercent => MaxLevel || Xp is null || XpMax is null or 0 ? null : Math.Min(100.0, 100.0 * Xp.Value / XpMax.Value);
}
public sealed record QuestMessage(uint QuestId, QuestSource Source, int ObjectiveCount, bool Complete, bool ReadyForTurnIn,
    int? CurrentObjective, string Title) : Message
{
    public bool None => QuestId == 0;
}
public sealed record ObjectiveMessage(uint QuestId, int Index, int? Have, int? Need, bool Done, bool Numeric, string Label) : Message;
public sealed record UtilityMessage(ulong? Money, int? BagsFree, int? BagsTotal, int? DurabilityLowest) : Message;
public sealed record HeartbeatMessage(int UptimeSeconds) : Message;

/// <summary>Mail / ready check / dungeon finder. Seconds are as of sending; the app counts on from when it arrived.</summary>
public sealed record StatusMessage(bool NewMail, bool ReadyCheck, int? ReadyCheckSeconds, bool Proposal, bool Queued,
    bool Tank, bool Healer, bool Dps, uint? QueuedSeconds) : Message
{
    public static readonly StatusMessage Idle = new(false, false, null, false, false, false, false, false, null);
}

/// <summary>The watched reputation. Value / Max are progress within the current standing (null = unknown).</summary>
/// <summary>The player's options from the in-game panel: which pages show and which alerts are on (bit masks).</summary>
public sealed record DisplayMessage(byte Pages, byte Alerts) : Message
{
    public static readonly DisplayMessage All = new(63, 127);
}

public enum ChatKind : byte { Whisper = 1, BattleNetWhisper = 2, Guild = 3, GuildMention = 4 }

/// <summary>A whisper / guild line for the bar. NEVER logged or stored by the app (privacy; see 07_SECURITY_AND_POLICY).</summary>
public sealed record ChatMessage(ChatKind Kind, int Id, string Sender, string Text) : Message;

/// <summary>A note added in game, with its unique id (the app takes each id in once).</summary>
public sealed record GameNote(uint Id, string Text);

/// <summary>The notes added in game recently (the addon's "sent" list, Phase 7).</summary>
public sealed record NotesMessage(IReadOnlyList<GameNote> Notes) : Message;

/// <summary>A timer started in game: unique id, ends at <paramref name="Ends"/> (seconds since 1970, from WoW's time()).</summary>
public sealed record GameTimer(uint Id, long Ends, uint Total, string Label);

/// <summary>The timers started in game recently (the addon's "sent" list, Phase 7).</summary>
public sealed record TimersMessage(IReadOnlyList<GameTimer> Timers) : Message;

public sealed record ReputationMessage(bool None, uint? FactionId, int? Reaction, uint? Value, uint? Max, string Name, string Standing) : Message;

/// <summary>Decodes payloads. Anything malformed (wrong length, invalid UTF-8) → null; unknown types → null.</summary>
public static class MessageParser
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static Message? Parse(MessageType type, ReadOnlySpan<byte> p)
    {
        try
        {
            var r = new Reader(p);
            Message? m = type switch
            {
                MessageType.Hello => new HelloMessage((Flavour)r.U8(), r.U32(), $"{r.U8()}.{r.U8()}.{r.U8()}"),
                MessageType.Player => Player(ref r),
                MessageType.Quest => Quest(ref r),
                MessageType.Objective => Objective(ref r),
                MessageType.Utility => new UtilityMessage(r.U64(), r.U8OrNull(), r.U8OrNull(), r.U8OrNull()),
                MessageType.Heartbeat => new HeartbeatMessage(r.U16()),
                MessageType.Status => Status(ref r),
                MessageType.Reputation => Reputation(ref r),
                MessageType.Chat => Chat(ref r),
                MessageType.Display => new DisplayMessage((byte)r.U8(), (byte)r.U8()),
                MessageType.Notes => Notes(ref r),
                MessageType.Timers => Timers(ref r),
                _ => null,
            };
            return m is not null && r.AtEnd ? m : null;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException or DecoderFallbackException or FormatException)
        {
            return null;
        }
    }

    private static StatusMessage Status(ref Reader r)
    {
        var flags = r.U8();
        var ready = r.U8OrNull();
        var queued = r.U32();
        bool F(int bit) => (flags & bit) != 0;
        return new StatusMessage(F(1), F(2), ready, F(4), F(8), F(16), F(32), F(64), queued);
    }

    private static ChatMessage? Chat(ref Reader r)
    {
        var kind = (ChatKind)r.U8();
        var id = r.U16();
        var sender = r.Str();
        var text = r.Str(ProtocolV1.MaxChatText);
        return Enum.IsDefined(kind) && text.Length > 0 ? new ChatMessage(kind, id, sender, text) : null;
    }

    private static NotesMessage Notes(ref Reader r)
    {
        var count = r.U8();
        var list = new List<GameNote>(count);
        for (var i = 0; i < count; i++)
        {
            var id = r.U32();
            var text = r.Str(ProtocolV1.MaxNoteText);
            if (id is { } n) list.Add(new GameNote(n, text));
        }
        return new NotesMessage(list);
    }

    private static TimersMessage Timers(ref Reader r)
    {
        var count = r.U8();
        var list = new List<GameTimer>(count);
        for (var i = 0; i < count; i++)
        {
            var id = r.U32();
            var ends = r.U32();
            var total = r.U32();
            var label = r.Str(ProtocolV1.MaxTimerLabel);
            if (id is { } n && ends is { } e && total is { } t) list.Add(new GameTimer(n, e, t, label));
        }
        return new TimersMessage(list);
    }

    private static ReputationMessage Reputation(ref Reader r)
    {
        var flags = r.U8();
        var id = r.U32();
        var reaction = r.U8OrNull();
        var value = r.U32();
        var max = r.U32();
        return new ReputationMessage((flags & 1) != 0, id, reaction, value, max, r.Str(), r.Str());
    }

    private static PlayerMessage Player(ref Reader r)
    {
        var level = r.U8OrNull();
        var xp = r.U32();
        var xpMax = r.U32();
        var rested = r.U32();
        var flags = r.U8();
        return new PlayerMessage(level, xp, xpMax, rested, (flags & 1) != 0, (flags & 2) != 0);
    }

    private static QuestMessage Quest(ref Reader r)
    {
        var id = r.U32() ?? 0;
        var source = (QuestSource)r.U8();
        var count = r.U8();
        var flags = r.U8();
        var current = r.U8OrNull();
        return new QuestMessage(id, source, count, (flags & 1) != 0, (flags & 2) != 0, current, r.Str());
    }

    private static ObjectiveMessage Objective(ref Reader r)
    {
        var id = r.U32() ?? 0;
        var index = r.U8();
        var have = r.U16OrNull();
        var need = r.U16OrNull();
        var flags = r.U8();
        return new ObjectiveMessage(id, index, have, need, (flags & 1) != 0, (flags & 2) != 0, r.Str());
    }

    /// <summary>Removes control characters (the addon strips them too; never trust the wire).</summary>
    public static string Sanitise(string s) => new(s.Where(c => !char.IsControl(c)).ToArray());

    private ref struct Reader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _d = data;
        private int _i;
        public readonly bool AtEnd => _i == _d.Length;

        public int U8() => _i < _d.Length ? _d[_i++] : throw new FormatException("short");
        public int? U8OrNull() { var v = U8(); return v == 0xFF ? null : v; }
        public int U16() => U8() << 8 | U8();
        public int? U16OrNull() { var v = U16(); return v == 0xFFFF ? null : v; }
        public uint? U32()
        {
            var v = (uint)U8() << 24 | (uint)U8() << 16 | (uint)U8() << 8 | (uint)U8();
            return v == 0xFFFFFFFF ? null : v;
        }
        public ulong? U64()
        {
            ulong v = 0;
            for (var k = 0; k < 8; k++) v = v << 8 | (uint)U8();
            return v == ulong.MaxValue ? null : v;
        }
        public string Str(int max = ProtocolV1.MaxText)
        {
            var len = U8();
            if (len > max || _i + len > _d.Length) throw new FormatException("string");
            var s = StrictUtf8.GetString(_d.Slice(_i, len));
            _i += len;
            return Sanitise(s);
        }
    }
}
