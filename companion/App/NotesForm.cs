using WoWTouchBar.Core.Notes;

namespace WoWTouchBar.App;

/// <summary>
/// Tray > "Notes and timers...": add / remove notes and start / stop timers from Windows (Phase 7). Changes apply at
/// once. Notes and timers added in WoW show here too, and can be removed here like any other.
/// </summary>
internal sealed class NotesForm : Form
{
    private readonly Scratchpad _pad;
    private readonly ListBox _notes = new() { Left = 12, Top = 34, Width = 440, Height = 150, IntegralHeight = false };
    private readonly TextBox _newNote = new() { Left = 12, Top = 192, Width = 260, MaxLength = Scratchpad.MaxNoteLength };
    private readonly ListBox _timers = new() { Left = 12, Top = 262, Width = 440, Height = 110, IntegralHeight = false };
    private readonly TextBox _timerName = new() { Left = 12, Top = 380, Width = 170, MaxLength = Scratchpad.MaxLabelLength, PlaceholderText = "Name (optional)" };
    private readonly TextBox _timerLength = new() { Left = 188, Top = 380, Width = 70, Text = "10m" };
    private readonly Label _problem = new() { Left = 12, Top = 444, Width = 440, Height = 20, ForeColor = Color.Firebrick };
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 1000 };
    private List<PadNote> _shownNotes = [];
    private List<PadTimer> _shownTimers = [];
    private int _version = -1;

    private readonly TextBox _shortcut = new() { Left = 300, Top = 478, Width = 152, ReadOnly = true, BackColor = SystemColors.Window, TextAlign = HorizontalAlignment.Center };
    private readonly Func<string, string?> _setShortcut;

    /// <param name="shortcut">The quick-add shortcut now ("" = off).</param>
    /// <param name="setShortcut">Uses a new shortcut ("" = off); returns null, or why not.</param>
    public NotesForm(Scratchpad pad, string shortcut, Func<string, string?> setShortcut)
    {
        _pad = pad;
        _setShortcut = setShortcut;
        _shortcut.Text = shortcut.Length > 0 ? shortcut : "Off";
        SuspendLayout(); // laid out at 96 DPI (100 %); scaled to the screen below (a 13" MacBook Pro runs at 200 %)
        Text = "WoW Touch Bar - Notes and timers";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(464, 516);
        Controls.Add(new Label
        {
            Left = 12, Top = 481, Width = 284, Height = 32,
            Text = "Quick-add shortcut (anywhere, WoW too). Click the box and press keys; Backspace = off.",
        });
        Controls.Add(_shortcut);
        _shortcut.KeyDown += (_, e) => CaptureShortcut(e);

        Controls.Add(new Label { Left = 12, Top = 10, Width = 440, Height = 20, Text = $"Notes (NOTE tab on the Touch Bar, up to {Scratchpad.MaxNotes})" });
        var addNote = new Button { Left = 278, Top = 191, Width = 84, Text = "Add note" };
        var removeNote = new Button { Left = 368, Top = 191, Width = 84, Text = "Remove" };
        Controls.Add(new Label { Left = 12, Top = 238, Width = 440, Height = 20, Text = $"Timers (TIMER tab; an alert when one ends, up to {Scratchpad.MaxTimers})" });
        var start = new Button { Left = 264, Top = 379, Width = 90, Text = "Start timer" };
        var stop = new Button { Left = 362, Top = 379, Width = 90, Text = "Stop" };
        Controls.Add(new Label
        {
            Left = 12, Top = 410, Width = 440, Height = 32, ForeColor = SystemColors.GrayText,
            Text = "Time: 90s, 10m, 1h30m or 1:30. Notes and timers added in WoW (/wtb note, /wtb timer) show up here too.",
        });
        Controls.AddRange([_notes, _newNote, addNote, removeNote, _timers, _timerName, _timerLength, start, stop, _problem]);

        addNote.Click += (_, _) => AddNote();
        _newNote.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { AddNote(); e.SuppressKeyPress = true; } };
        removeNote.Click += (_, _) =>
        {
            if (_notes.SelectedIndex < 0 || _notes.SelectedIndex >= _shownNotes.Count) return;
            var n = _shownNotes[_notes.SelectedIndex];
            _pad.RemoveNote(n);
            RefreshLists();
        };
        start.Click += (_, _) => StartTimer();
        _timerLength.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { StartTimer(); e.SuppressKeyPress = true; } };
        _timerName.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { StartTimer(); e.SuppressKeyPress = true; } };
        stop.Click += (_, _) =>
        {
            if (_timers.SelectedIndex < 0 || _timers.SelectedIndex >= _shownTimers.Count) return;
            var t = _shownTimers[_timers.SelectedIndex];
            _pad.CancelTimer(t);
            RefreshLists();
        };
        _tick.Tick += (_, _) => RefreshLists();
        Shown += (_, _) => { RefreshLists(); _tick.Start(); _newNote.Focus(); };
        FormClosed += (_, _) => _tick.Dispose();

        // Scale every control from the 96 DPI layout above to the real display scaling, like a designer-made form.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
    }

    private void SetProblem(string? problem) => _problem.Text = problem ?? "";

    /// <summary>Click the box, press e.g. Ctrl+Option+N: that becomes the shortcut (it needs Ctrl or Option). Backspace turns it off.</summary>
    private void CaptureShortcut(KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        e.Handled = true;
        if (e.KeyCode is Keys.Back or Keys.Delete)
        {
            SetProblem(_setShortcut(""));
            _shortcut.Text = "Off";
            return;
        }
        if (Hotkey.KeyName((int)e.KeyCode) is null) return; // still holding only Ctrl / Alt / Shift
        var mods = (e.Control ? Hotkey.Ctrl : 0) | (e.Alt ? Hotkey.Alt : 0) | (e.Shift ? Hotkey.Shift : 0);
        if ((mods & (Hotkey.Ctrl | Hotkey.Alt)) == 0) { SetProblem("Use Ctrl and/or Option with a letter, number or F-key."); return; }
        var key = new Hotkey(mods, (int)e.KeyCode).ToString();
        var why = _setShortcut(key);
        SetProblem(why);
        if (why is null) _shortcut.Text = key;
    }

    private void AddNote()
    {
        var why = _pad.AddNote(_newNote.Text);
        SetProblem(why);
        if (why is null) _newNote.Clear();
        RefreshLists();
    }

    private void StartTimer()
    {
        var length = Scratchpad.ParseDuration(_timerLength.Text);
        var why = length is null ? "Use a time like 90s, 10m, 1h30m or 1:30 (up to 24 hours)." : _pad.StartTimer(_timerName.Text, length.Value, DateTimeOffset.UtcNow);
        SetProblem(why);
        if (why is null) _timerName.Clear();
        RefreshLists();
    }

    /// <summary>Notes when anything changed; timers every second (the countdowns). Keeps the selection.</summary>
    private void RefreshLists()
    {
        if (_pad.Version != _version)
        {
            _version = _pad.Version;
            var keepNote = _notes.SelectedIndex;
            _shownNotes = [.. _pad.Notes];
            _notes.BeginUpdate();
            _notes.Items.Clear();
            foreach (var n in _shownNotes) _notes.Items.Add(n.Text);
            _notes.EndUpdate();
            if (keepNote >= 0 && keepNote < _notes.Items.Count) _notes.SelectedIndex = keepNote;
        }
        var now = DateTimeOffset.UtcNow;
        var keepTimer = _timers.SelectedIndex;
        _shownTimers = [.. _pad.Timers];
        var lines = _shownTimers.Select(t =>
            $"{t.Label}  -  {(t.Done(now) ? "done" : Format(t.Left(now)))}").ToList();
        if (!lines.SequenceEqual(_timers.Items.Cast<string>()))
        {
            _timers.BeginUpdate();
            _timers.Items.Clear();
            foreach (var l in lines) _timers.Items.Add(l);
            _timers.EndUpdate();
            if (keepTimer >= 0 && keepTimer < _timers.Items.Count) _timers.SelectedIndex = keepTimer;
        }
    }

    private static string Format(TimeSpan t)
    {
        var s = (int)Math.Ceiling(t.TotalSeconds); // 4:59.5 left shows 5:00, like the bar
        return (s >= 3600 ? $"{s / 3600}:{s / 60 % 60:00}:{s % 60:00}" : $"{s / 60}:{s % 60:00}") + " left";
    }
}
