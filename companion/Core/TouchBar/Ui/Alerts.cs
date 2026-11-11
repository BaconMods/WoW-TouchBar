using WoWTouchBar.Core.Protocol;

namespace WoWTouchBar.Core.TouchBar.Ui;

public enum AlertKind { QuestComplete, BagsFull, DurabilityLow, LevelUp, NewMail, ReadyCheck, DungeonReady, TimerDone, Removed, Added }

/// <summary>An alert to show over the page. Persistent ones last as long as the game state does.</summary>
public sealed record Alert(AlertKind Kind, string Title, string Detail, bool Persistent);

/// <summary>
/// Turns game-state changes into short alerts (pure; tested). Only real changes count: values seen for the first
/// time (login, reload, app start) never alert. Ready check and dungeon ready stay while they last; the others show
/// for a few seconds. A tap dismisses the current alert (a dismissed ready check / dungeon proposal stays hidden
/// until the next one).
/// </summary>
public sealed class AlertTracker
{
    public static readonly TimeSpan ShowFor = TimeSpan.FromSeconds(7);
    public const int DurabilityWarnPercent = 10;

    private (uint Id, bool Ready)? _quest;
    private int? _bagsFree, _durability, _level;
    private bool? _mail;
    private (Alert Alert, TimeSpan Until)? _timed;
    private bool _readyDismissed, _proposalDismissed;

    public void Update(GameState s, TimeSpan now)
    {
        if (s.Quest is { None: false } q)
        {
            if (_quest is { } prev && prev.Id == q.QuestId && !prev.Ready && q.ReadyForTurnIn)
                Show(new Alert(AlertKind.QuestComplete, "QUEST COMPLETE", $"TURN IN: {q.Title}", false), now);
            _quest = (q.QuestId, q.ReadyForTurnIn);
        }
        if (s.Utility is { } u)
        {
            if (u.BagsFree == 0 && _bagsFree is > 0) Show(new Alert(AlertKind.BagsFull, "BAGS FULL", "NO FREE BAG SLOTS LEFT", false), now);
            if (u.DurabilityLowest is { } d && d <= DurabilityWarnPercent && _durability is > DurabilityWarnPercent)
                Show(new Alert(AlertKind.DurabilityLow, "REPAIR SOON", $"LOWEST ITEM AT {d}% DURABILITY", false), now);
            if (u.BagsFree is not null) _bagsFree = u.BagsFree;
            if (u.DurabilityLowest is not null) _durability = u.DurabilityLowest;
        }
        if (s.Player?.Level is { } level)
        {
            if (_level is { } before && level > before) Show(new Alert(AlertKind.LevelUp, "LEVEL UP!", $"YOU ARE NOW LEVEL {level}", false), now);
            _level = level;
        }
        if (s.Status is { } st)
        {
            if (st.NewMail && _mail == false) Show(new Alert(AlertKind.NewMail, "NEW MAIL", "WAITING AT A MAILBOX", false), now);
            _mail = st.NewMail;
            if (!st.ReadyCheck) _readyDismissed = false;
            if (!st.Proposal) _proposalDismissed = false;
        }
    }

    private void Show(Alert a, TimeSpan now) => _timed = (a, now + ShowFor);

    /// <summary>The alert to show now: dungeon ready, then ready check, then the latest short alert.</summary>
    public Alert? Current(GameState s, TimeSpan now)
    {
        if (s.Status is { Proposal: true } && !_proposalDismissed)
            return new Alert(AlertKind.DungeonReady, "DUNGEON READY", "ACCEPT IN GAME", true);
        if (s.Status is { ReadyCheck: true } && !_readyDismissed)
        {
            var left = s.ReadyCheckSecondsLeft(now);
            return new Alert(AlertKind.ReadyCheck, "READY CHECK", left is { } l ? $"{l} SECONDS LEFT - ANSWER IN GAME" : "ANSWER IN GAME", true);
        }
        if (_timed is { } t && now < t.Until) return t.Alert;
        return null;
    }

    public void Dismiss(GameState s, TimeSpan now)
    {
        switch (Current(s, now)?.Kind)
        {
            case AlertKind.DungeonReady: _proposalDismissed = true; break;
            case AlertKind.ReadyCheck: _readyDismissed = true; break;
            case not null: _timed = null; break;
        }
    }
}
