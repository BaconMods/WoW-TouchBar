using System.Text.Json;
using WoWTouchBar.Core.Protocol;

namespace WoWTouchBar.Core.Notes;

/// <summary>A note on the NOTE tab.</summary>
public sealed record PadNote(string Text);

/// <summary>A timer on the TIMER tab.</summary>
public sealed record PadTimer(string Label, DateTimeOffset Ends, int TotalSeconds)
{
    /// <summary>Identifies the timer for "already alerted" (same name and end time = same timer).</summary>
    public string Key => $"{Ends.ToUnixTimeSeconds()}|{Label}";
    public TimeSpan Left(DateTimeOffset now) => Ends > now ? Ends - now : TimeSpan.Zero;
    public bool Done(DateTimeOffset now) => Ends <= now;
}

/// <summary>
/// Notes and timers (Phase 7): the ONE list, owned by the app. The tray adds to it directly. In game, /wtb note and
/// /wtb timer give each item a unique id and the addon keeps re-sending its recent ones; the app takes each id in
/// once and ignores it afterwards. So removing a note or timer here (the X on the bar, the tray) deletes it for real
/// and it never comes back - with no time limit, and nothing is lost if the app wasn't running when it was added.
/// Saved in %LOCALAPPDATA%\WoWTouchBar\notes.json. Thread-safe: the tray window edits while the bar loop reads.
/// </summary>
public sealed class Scratchpad
{
    public const int MaxNotes = 8, MaxNoteLength = 80, MaxTimers = 8, MaxLabelLength = 32;
    public static readonly TimeSpan MaxTimer = TimeSpan.FromHours(24);
    /// <summary>A timer that ended longer ago than this (e.g. while the PC was off) is not alerted any more.</summary>
    public static readonly TimeSpan AlertWindow = TimeSpan.FromHours(12);
    private const int MaxAcknowledged = 64;
    /// <summary>Ids from the game remembered as "already taken in" (the addon re-sends at most 16 at a time).</summary>
    private const int MaxImported = 512;

    private readonly object _lock = new();
    private Data _d = new();
    private string? _path;

    /// <summary>Goes up on every change (redraw / save).</summary>
    public int Version { get; private set; }

    /// <summary>The last thing added (from anywhere), for a short "NOTE ADDED" / "TIMER STARTED" on the bar.</summary>
    public (int Seq, string Title, string Detail)? LastAdded { get; private set; }
    private void Added(string title, string detail) => LastAdded = ((LastAdded?.Seq ?? 0) + 1, title, detail);
    private static string Clock(int seconds) => seconds >= 3600 ? $"{seconds / 3600}:{seconds / 60 % 60:00}:{seconds % 60:00}" : $"{seconds / 60}:{seconds % 60:00}";
    private int _savedVersion;

    private sealed class Data
    {
        public List<string> Notes { get; set; } = [];
        public List<TimerData> Timers { get; set; } = [];
        public List<string> Acknowledged { get; set; } = [];
        /// <summary>Game ids already taken in ("n123" / "t456"): never added again, even after they were removed.</summary>
        public List<string> Imported { get; set; } = [];
    }

    private sealed class TimerData
    {
        public string Label { get; set; } = "";
        public long Ends { get; set; }
        public int Total { get; set; }
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWTouchBar", "notes.json");

    private void Changed() => Version++;

    // ------------------------------------------------------------------ reading

    public IReadOnlyList<PadNote> Notes
    {
        get { lock (_lock) return [.. _d.Notes.Select(n => new PadNote(n))]; }
    }

    /// <summary>All timers, soonest first.</summary>
    public IReadOnlyList<PadTimer> Timers
    {
        get { lock (_lock) return [.. _d.Timers.Select(ToTimer).OrderBy(t => t.Ends)]; }
    }

    private static PadTimer ToTimer(TimerData t) => new(t.Label, DateTimeOffset.FromUnixTimeSeconds(t.Ends), t.Total);

    // ------------------------------------------------------------------ adding and removing

    /// <summary>Adds a note from the tray. Returns null, or why not.</summary>
    public string? AddNote(string text)
    {
        var t = Clean(text, MaxNoteLength);
        if (t.Length == 0) return "The note is empty.";
        lock (_lock)
        {
            if (_d.Notes.Count >= MaxNotes) return $"{MaxNotes} notes is the most - remove one first.";
            _d.Notes.Add(t);
            Added("NOTE ADDED", t);
            Changed();
        }
        return null;
    }

    public bool RemoveNote(PadNote note)
    {
        lock (_lock)
        {
            var i = _d.Notes.IndexOf(note.Text);
            if (i < 0) return false;
            _d.Notes.RemoveAt(i);
            Changed();
            return true;
        }
    }

    /// <summary>Starts a timer from the tray. Returns null, or why not.</summary>
    public string? StartTimer(string label, TimeSpan length, DateTimeOffset now)
    {
        if (length < TimeSpan.FromSeconds(1) || length > MaxTimer) return "Pick a time between 1 second and 24 hours.";
        var name = Clean(label, MaxLabelLength);
        lock (_lock)
        {
            if (_d.Timers.Count >= MaxTimers) return $"{MaxTimers} timers is the most - stop one first.";
            _d.Timers.Add(new TimerData { Label = name.Length > 0 ? name : "Timer", Ends = (now + length).ToUnixTimeSeconds(), Total = (int)length.TotalSeconds });
            Added("TIMER STARTED", $"{(name.Length > 0 ? name : "Timer")} - {Clock((int)length.TotalSeconds)}");
            Changed();
        }
        return null;
    }

    public bool CancelTimer(PadTimer timer)
    {
        lock (_lock)
        {
            var i = _d.Timers.FindIndex(t => t.Label == timer.Label && t.Ends == timer.Ends.ToUnixTimeSeconds());
            if (i < 0) return false;
            _d.Timers.RemoveAt(i);
            Changed();
            return true;
        }
    }

    /// <summary>The X on the bar: deleted for real (wherever it was added).</summary>
    public void Remove(PadNote note) => RemoveNote(note);
    public void Remove(PadTimer timer) => CancelTimer(timer);

    /// <summary>Undo the X: the note comes back (at the end of the list).</summary>
    public void Restore(PadNote note)
    {
        lock (_lock)
        {
            if (_d.Notes.Count < MaxNotes) { _d.Notes.Add(note.Text); Changed(); }
        }
    }

    /// <summary>Undo the X: the timer comes back with its original end time.</summary>
    public void Restore(PadTimer timer)
    {
        lock (_lock)
        {
            if (_d.Timers.Count >= MaxTimers) return;
            _d.Timers.Add(new TimerData { Label = timer.Label, Ends = timer.Ends.ToUnixTimeSeconds(), Total = timer.TotalSeconds });
            Changed();
        }
    }

    // ------------------------------------------------------------------ from the game: each id taken in once

    private bool Take(string key)
    {
        if (_d.Imported.Contains(key)) return false;
        _d.Imported.Add(key);
        while (_d.Imported.Count > MaxImported) _d.Imported.RemoveAt(0);
        return true;
    }

    /// <summary>Notes added in game (NOTES message): new ids join the list (the oldest note goes if it is full).</summary>
    public void ImportGameNotes(IReadOnlyList<GameNote> notes)
    {
        lock (_lock)
        {
            foreach (var n in notes)
            {
                var text = Clean(n.Text, MaxNoteLength);
                if (text.Length == 0 || !Take("n" + n.Id)) continue;
                if (_d.Notes.Count >= MaxNotes) _d.Notes.RemoveAt(0);
                _d.Notes.Add(text);
                Added("NOTE ADDED", text);
                Changed();
            }
        }
    }

    /// <summary>Timers started in game (TIMERS message): new ids join the list (the soonest-ending goes if it is full).</summary>
    public void ImportGameTimers(IReadOnlyList<GameTimer> timers)
    {
        lock (_lock)
        {
            foreach (var t in timers)
            {
                if (t.Total == 0 || !Take("t" + t.Id)) continue;
                if (_d.Timers.Count >= MaxTimers) _d.Timers.Remove(_d.Timers.MinBy(x => x.Ends)!);
                var label = Clean(t.Label, MaxLabelLength);
                _d.Timers.Add(new TimerData { Label = label.Length > 0 ? label : "Timer", Ends = t.Ends, Total = (int)Math.Min(t.Total, (uint)MaxTimer.TotalSeconds) });
                Added("TIMER STARTED", $"{(label.Length > 0 ? label : "Timer")} - {Clock((int)Math.Min(t.Total, (uint)MaxTimer.TotalSeconds))}");
                Changed();
            }
        }
    }

    // ------------------------------------------------------------------ "timer done" alerts

    /// <summary>The first timer that has ended and not been alerted yet (only within <see cref="AlertWindow"/>), or null.</summary>
    public PadTimer? DueAlert(DateTimeOffset now)
    {
        lock (_lock)
            return Timers.FirstOrDefault(t => t.Done(now) && now - t.Ends <= AlertWindow && !_d.Acknowledged.Contains(t.Key));
    }

    /// <summary>The alert was tapped away: never alert this timer again, and it leaves the list.</summary>
    public void Acknowledge(PadTimer timer)
    {
        lock (_lock)
        {
            if (!_d.Acknowledged.Contains(timer.Key)) _d.Acknowledged.Add(timer.Key);
            while (_d.Acknowledged.Count > MaxAcknowledged) _d.Acknowledged.RemoveAt(0);
            _d.Timers.RemoveAll(t => t.Label == timer.Label && t.Ends == timer.Ends.ToUnixTimeSeconds());
            Changed();
        }
    }

    // ------------------------------------------------------------------ saving

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Loads notes.json (a missing or damaged file gives an empty pad, never an error).</summary>
    public static Scratchpad Load(string? path = null)
    {
        var pad = new Scratchpad { _path = path ?? DefaultPath };
        try
        {
            if (File.Exists(pad._path) && JsonSerializer.Deserialize<Data>(File.ReadAllText(pad._path), Json) is { } d)
            {
                d.Notes ??= []; d.Timers ??= []; d.Acknowledged ??= []; d.Imported ??= [];
                pad._d = d;
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException) { }
        return pad;
    }

    /// <summary>Writes notes.json if anything changed since the last save (temp file + replace). Returns false on failure.</summary>
    public bool SaveIfChanged()
    {
        if (_path is null || Version == _savedVersion) return true;
        try
        {
            string text;
            int version;
            lock (_lock) { text = JsonSerializer.Serialize(_d, Json); version = Version; }
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path + ".tmp", text);
            File.Move(_path + ".tmp", _path, overwrite: true);
            _savedVersion = version;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
    /// <summary>
    /// The same time formats as "/wtb timer" in game: "90s", "10m", "1h30m", "1h 30m", "1:30" (min:sec), "1:02:03",
    /// and a plain number = minutes. Null if unreadable or outside 1 s .. 24 h (pure; tested).
    /// </summary>
    public static TimeSpan? ParseDuration(string? text)
    {
        var s = (text ?? "").Trim().ToLowerInvariant();
        if (s.Length == 0) return null;
        long seconds;
        var parts = s.Split(':');
        if (parts.Length is 2 or 3 && parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)) && parts.Skip(1).All(p => p.Length == 2))
            seconds = parts.Aggregate(0L, (acc, p) => acc * 60 + long.Parse(p));
        else if (s.All(char.IsAsciiDigit))
            seconds = s.Length > 6 ? long.MaxValue : long.Parse(s) * 60;
        else
        {
            var m = System.Text.RegularExpressions.Regex.Matches(s, @"(\d{1,6})\s*([hms])");
            if (m.Count == 0 || System.Text.RegularExpressions.Regex.Replace(s, @"(\d{1,6})\s*([hms])", "").Trim().Length > 0) return null;
            seconds = m.Sum(x => long.Parse(x.Groups[1].Value) * (x.Groups[2].Value switch { "h" => 3600L, "m" => 60L, _ => 1L }));
        }
        return seconds < 1 || seconds > MaxTimer.TotalSeconds ? null : TimeSpan.FromSeconds(seconds);
    }

    /// <summary>Trims, removes control characters and cuts to <paramref name="max"/> characters.</summary>
    public static string Clean(string? s, int max)
    {
        var t = new string((s ?? "").Where(c => !char.IsControl(c)).ToArray()).Trim();
        return t.Length > max ? t[..max].TrimEnd() : t;
    }
}
