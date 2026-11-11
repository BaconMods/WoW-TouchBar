using System.Runtime.InteropServices;
using WoWTouchBar.Core.Notes;

namespace WoWTouchBar.App;

/// <summary>
/// The quick-add box (the shortcut, Ctrl+Option+N by default): one line near the top of the screen. "10m tea" starts a
/// timer, anything else is a note. Enter adds and closes; Esc or clicking away closes. Focus goes back to whatever you
/// were in (WoW included) when it closes.
/// </summary>
internal sealed class QuickAddForm : Form
{
    private readonly Scratchpad _pad;
    private readonly TextBox _text = new() { Left = 12, Top = 12, Width = 456, Font = new Font("Segoe UI", 13f) };
    private readonly Label _hint = new() { Left = 12, Top = 50, Width = 456, Height = 20, ForeColor = SystemColors.GrayText };

    public QuickAddForm(Scratchpad pad)
    {
        _pad = pad;
        SuspendLayout(); // laid out at 96 DPI and scaled to the screen below
        Text = "WoW Touch Bar - quick add";
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(480, 78);
        Controls.AddRange([_text, _hint]);
        _text.TextChanged += (_, _) => UpdateHint();
        _text.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Add(); }
            else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; Close(); }
        };
        Deactivate += (_, _) => Close(); // clicking away closes it
        Load += (_, _) =>
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + area.Height / 6);
        };
        Shown += (_, _) => { ForceForeground(); _text.Focus(); };
        UpdateHint();
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
    }

    private void UpdateHint() => _hint.Text = QuickAdd.Describe(QuickAdd.Parse(_text.Text));

    private void Add()
    {
        var intent = QuickAdd.Parse(_text.Text);
        var problem = intent.IsTimer
            ? _pad.StartTimer(intent.Text, intent.Length!.Value, DateTimeOffset.UtcNow)
            : intent.Text.Length == 0 ? null : _pad.AddNote(intent.Text);
        if (problem is not null) { _hint.Text = problem; _hint.ForeColor = Color.Firebrick; return; }
        Close();
    }

    /// <summary>The shortcut gives this program the right to come to the front; make sure it really does (over a game too).</summary>
    private void ForceForeground()
    {
        SetForegroundWindow(Handle);
        Activate();
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);
}
