namespace WoWTouchBar.Core.Protocol;

/// <summary>
/// Reassembles multi-frame messages and drops repeats. The same frame is usually captured several
/// times while the addon holds it (~200 ms at ~7 captures/s), so consecutive duplicates are expected.
/// </summary>
public sealed class Reassembler
{
    public static readonly TimeSpan PartialTimeout = TimeSpan.FromSeconds(3);

    private (ushort Seq, byte Index, MessageType Type)? _lastKey;
    private readonly Dictionary<(ushort Seq, MessageType Type, byte Count), (byte[]?[] Parts, TimeSpan Started)> _partial = [];

    public int Duplicates { get; private set; }
    public int Expired { get; private set; }

    /// <summary>Returns the full payload when a message is complete; null for duplicates or incomplete sets.</summary>
    public byte[]? Accept(Frame f, TimeSpan now)
    {
        var key = (f.Sequence, f.ChunkIndex, f.Type);
        if (_lastKey == key) { Duplicates++; return null; }
        _lastKey = key;
        if (f.ChunkCount == 1) return f.Payload;

        foreach (var stale in _partial.Where(p => now - p.Value.Started > PartialTimeout).Select(p => p.Key).ToList())
        {
            _partial.Remove(stale);
            Expired++;
        }
        var setKey = (f.Sequence, f.Type, f.ChunkCount);
        if (!_partial.TryGetValue(setKey, out var set))
        {
            set = (new byte[]?[f.ChunkCount], now);
            _partial[setKey] = set;
        }
        set.Parts[f.ChunkIndex] = f.Payload;
        if (set.Parts.Any(p => p is null)) return null;
        _partial.Remove(setKey);
        return set.Parts.SelectMany(p => p!).ToArray();
    }
}

/// <summary>Latest known game state, built from decoded messages.</summary>
public sealed class GameState
{
    private readonly Dictionary<int, ObjectiveMessage> _objectives = [];

    public HelloMessage? Hello { get; private set; }
    public PlayerMessage? Player { get; private set; }
    public QuestMessage? Quest { get; private set; }
    public UtilityMessage? Utility { get; private set; }
    public StatusMessage? Status { get; private set; }
    /// <summary>When the current Status content first arrived (repeats from keyframes don't reset it).</summary>
    public TimeSpan StatusAt { get; private set; }
    public ReputationMessage? Reputation { get; private set; }
    /// <summary>The player's in-game options (pages / alerts); null until the addon sends them = everything on.</summary>
    public DisplayMessage? Display { get; private set; }
    /// <summary>Notes / timers typed in game (whole lists, latest); null until the addon sends them (older addons never do).</summary>
    public NotesMessage? Notes { get; private set; }
    public TimersMessage? Timers { get; private set; }

    /// <summary>Recent chat lines for the bar, numbered 1, 2, 3... Memory only: never logged or saved.</summary>
    public IReadOnlyList<(int Seq, ChatMessage Chat)> Chats => _chats;
    public int ChatCount { get; private set; }
    public const int MaxChats = 10;
    private readonly List<(int Seq, ChatMessage Chat)> _chats = [];
    public TimeSpan? LastMessageAt { get; private set; }
    public int Messages { get; private set; }

    /// <summary>Objectives of the current quest, in order; null entries are still to arrive.</summary>
    public IReadOnlyList<ObjectiveMessage?> Objectives =>
        Quest is null || Quest.None
            ? []
            : Enumerable.Range(1, Quest.ObjectiveCount).Select(i => _objectives.GetValueOrDefault(i)).ToList();

    /// <summary>The objective to show: the addon's "current" one, else the first not done, else the first.</summary>
    public ObjectiveMessage? CurrentObjective
    {
        get
        {
            var list = Objectives;
            if (Quest?.CurrentObjective is { } c && c >= 1 && c <= list.Count && list[c - 1] is { } cur) return cur;
            return list.FirstOrDefault(o => o is { Done: false }) ?? list.FirstOrDefault(o => o is not null);
        }
    }

    /// <summary>Seconds in the dungeon queue right now (counts on from the last status), or null.</summary>
    public int? QueueSeconds(TimeSpan now) =>
        Status is { Queued: true, QueuedSeconds: { } q } ? (int)q + (int)Math.Max(0, (now - StatusAt).TotalSeconds) : null;

    /// <summary>Ready-check seconds left right now (counts down from the last status), or null.</summary>
    public int? ReadyCheckSecondsLeft(TimeSpan now) =>
        Status is { ReadyCheck: true, ReadyCheckSeconds: { } s } ? Math.Max(0, s - (int)(now - StatusAt).TotalSeconds) : null;

    public void Apply(Message m, TimeSpan now)
    {
        Messages++;
        LastMessageAt = now;
        switch (m)
        {
            case HelloMessage h: Hello = h; break;
            case PlayerMessage p: Player = p; break;
            case UtilityMessage u: Utility = u; break;
            case StatusMessage s:
                if (s != Status) { Status = s; StatusAt = now; }
                break;
            case ReputationMessage r: Reputation = r; break;
            case DisplayMessage d: Display = d; break;
            case NotesMessage n: Notes = n; break;
            case TimersMessage t: Timers = t; break;
            case ChatMessage c:
                ChatCount++;
                _chats.Add((ChatCount, c));
                if (_chats.Count > MaxChats) _chats.RemoveAt(0);
                break;
            case QuestMessage q:
                if (Quest?.QuestId != q.QuestId) _objectives.Clear();
                foreach (var extra in _objectives.Keys.Where(k => k > q.ObjectiveCount).ToList()) _objectives.Remove(extra);
                Quest = q;
                break;
            case ObjectiveMessage o:
                // Objectives for another quest (e.g. sent just before the quest changed) are ignored.
                if (Quest is null || o.QuestId == Quest.QuestId) _objectives[o.Index] = o;
                break;
        }
    }
}
