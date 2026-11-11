using WoWTouchBar.Core.Input;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.App;

/// <summary>
/// Tray > "My keys...": the player's own buttons for the outside-WoW page. Each has a label and opens a program,
/// a website or a folder, or presses a key. Nothing here ever acts on WoW.
/// </summary>
internal sealed class MyKeysForm : Form
{
    private static readonly (ShortcutKind Kind, string Text)[] Kinds =
        [(ShortcutKind.OpenProgram, "Open a program"), (ShortcutKind.OpenWebsite, "Open a website"),
         (ShortcutKind.OpenFolder, "Open a folder"), (ShortcutKind.PressKey, "Press a key")];

    private static readonly (SystemKey Key, string Text)[] KeyChoices =
        [(SystemKey.PlayPause, "Play / pause"), (SystemKey.NextTrack, "Next track"), (SystemKey.PreviousTrack, "Previous track"),
         (SystemKey.VolumeUp, "Volume up"), (SystemKey.VolumeDown, "Volume down"), (SystemKey.Mute, "Mute"), (SystemKey.Escape, "Esc"),
         .. Enumerable.Range(0, 12).Select(i => (SystemKey.F1 + i, $"F{i + 1}"))];

    private readonly List<CustomButton> _buttons;
    private readonly ListBox _list = new() { Left = 12, Top = 40, Width = 180, Height = 250, IntegralHeight = false };
    private readonly TextBox _label = new() { Left = 300, Top = 40, Width = 220, MaxLength = Shortcuts.MaxLabel }; // typed normally; the bar shows it in capitals
    private readonly ComboBox _kind = new() { Left = 300, Top = 72, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _target = new() { Left = 300, Top = 104, Width = 220 };
    private readonly Button _browse = new() { Left = 526, Top = 103, Width = 80, Text = "Browse..." };
    private readonly ComboBox _key = new() { Left = 300, Top = 104, Width = 220, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _targetLabel = new() { Left = 210, Top = 107, Width = 88 };
    private readonly Label _problem = new() { Left = 210, Top = 140, Width = 400, Height = 40, ForeColor = Color.Firebrick };
    private bool _loading;

    public IReadOnlyList<CustomButton> Result => _buttons;

    public MyKeysForm(IReadOnlyList<CustomButton> current)
    {
        _buttons = [.. current];
        SuspendLayout(); // laid out at 96 DPI (100 %); scaled to the screen below (a 13" MacBook Pro runs at 200 %)
        Text = "WoW Touch Bar - My keys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 340);

        Controls.Add(new Label
        {
            Left = 12, Top = 10, Width = 600, Height = 24,
            Text = $"Your own buttons for the Touch Bar outside WoW (up to {Shortcuts.MaxButtons}). Tap the toggle on the right until it says MY KEYS.",
        });
        Controls.Add(new Label { Left = 210, Top = 43, Width = 88, Text = "Label" });
        Controls.Add(new Label { Left = 210, Top = 75, Width = 88, Text = "What it does" });
        Controls.AddRange([_list, _label, _kind, _target, _browse, _key, _targetLabel, _problem]);

        var add = new Button { Left = 12, Top = 296, Width = 42, Text = "+" };
        var remove = new Button { Left = 58, Top = 296, Width = 42, Text = "-" };
        var up = new Button { Left = 104, Top = 296, Width = 42, Text = "↑" };
        var down = new Button { Left = 150, Top = 296, Width = 42, Text = "↓" };
        var save = new Button { Left = 444, Top = 296, Width = 80, Text = "Save" };
        var cancel = new Button { Left = 530, Top = 296, Width = 80, Text = "Cancel", DialogResult = DialogResult.Cancel };
        Controls.AddRange([add, remove, up, down, save, cancel]);
        AcceptButton = save;
        CancelButton = cancel;

        _kind.Items.AddRange(Kinds.Select(k => (object)k.Text).ToArray());
        _key.Items.AddRange(KeyChoices.Select(k => (object)k.Text).ToArray());

        add.Click += (_, _) =>
        {
            if (_buttons.Count >= Shortcuts.MaxButtons) return;
            _buttons.Add(new CustomButton("NEW", ShortcutKind.OpenWebsite, "https://"));
            RefreshList(_buttons.Count - 1);
            _label.Focus();
        };
        remove.Click += (_, _) => { if (_list.SelectedIndex >= 0) { _buttons.RemoveAt(_list.SelectedIndex); RefreshList(Math.Min(_list.SelectedIndex, _buttons.Count - 1)); } };
        up.Click += (_, _) => MoveSelected(-1);
        down.Click += (_, _) => MoveSelected(+1);
        _list.SelectedIndexChanged += (_, _) => { if (!_loading) ShowSelected(); }; // not while we update the list ourselves
        _label.TextChanged += (_, _) => Edit(b => b with { Label = _label.Text }); // kept as typed; tidied on Save
        _kind.SelectedIndexChanged += (_, _) => Edit(b => b with { Kind = Kinds[Math.Max(0, _kind.SelectedIndex)].Kind });
        _target.TextChanged += (_, _) => Edit(b => b with { Target = _target.Text });
        _key.SelectedIndexChanged += (_, _) => Edit(b => b with { Key = KeyChoices[Math.Max(0, _key.SelectedIndex)].Key });
        _browse.Click += (_, _) => Browse();
        save.Click += (_, _) => Save();

        // Scale every control from the 96 DPI layout above to the real display scaling, like a designer-made form.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        ResumeLayout(false);
        PerformLayout();

        RefreshList(_buttons.Count > 0 ? 0 : -1);
    }

    /// <summary>
    /// Developer self-test (`--selftest-mykeys`): types into the label and address boxes the way a keyboard does
    /// (one character at a time, through the text box) and checks the selection, enabled state and text all survive.
    /// </summary>
    internal static string SelfTest()
    {
        using var f = new MyKeysForm([new("BNET", ShortcutKind.OpenWebsite, "https://battle.net")])
            { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
        f.Show();
        Application.DoEvents();
        f._label.Text = "";
        foreach (var ch in "My keys") { f._label.Text += ch; f._label.SelectionStart = f._label.Text.Length; Application.DoEvents(); }
        if (f._list.SelectedIndex != 0) return "FAIL: selection lost while typing the label";
        if (!f._label.Enabled) return "FAIL: label box disabled while typing";
        if (f._label.Text != "My keys") return $"FAIL: label box shows '{f._label.Text}'";
        f._target.Text = "https://";
        foreach (var ch in "www.wowhead.com") { f._target.Text += ch; Application.DoEvents(); }
        if (f._list.SelectedIndex != 0 || !f._target.Enabled || f._target.Text != "https://www.wowhead.com") return "FAIL: address box";
        if ((string)f._list.Items[0] != "My keys") return "FAIL: list not renamed";
        return "PASS";
    }

    private void MoveSelected(int by)
    {
        var i = _list.SelectedIndex;
        var j = i + by;
        if (i < 0 || j < 0 || j >= _buttons.Count) return;
        (_buttons[i], _buttons[j]) = (_buttons[j], _buttons[i]);
        RefreshList(j);
    }

    private void RefreshList(int select)
    {
        _loading = true;
        _list.Items.Clear();
        foreach (var b in _buttons) _list.Items.Add(b.Label.Length > 0 ? b.Label : "(no label)");
        _loading = false;
        _list.SelectedIndex = select;
        ShowSelected();
    }

    private void ShowSelected()
    {
        var i = _list.SelectedIndex;
        var has = i >= 0 && i < _buttons.Count;
        foreach (var c in new Control[] { _label, _kind, _target, _browse, _key }) c.Enabled = has;
        if (!has) { _problem.Text = ""; return; }
        _loading = true;
        var b = _buttons[i];
        _label.Text = b.Label;
        _kind.SelectedIndex = Array.FindIndex(Kinds, k => k.Kind == b.Kind);
        _target.Text = b.Target;
        _key.SelectedIndex = Math.Max(0, Array.FindIndex(KeyChoices, k => k.Key == b.Key));
        _loading = false;
        UpdateFields();
    }

    private void Edit(Func<CustomButton, CustomButton> change)
    {
        var i = _list.SelectedIndex;
        if (_loading || i < 0 || i >= _buttons.Count) return;
        _buttons[i] = change(_buttons[i]);
        // Renaming a list entry can clear the selection; put it back so the field being typed in stays enabled.
        _loading = true;
        var name = _buttons[i].Label.Trim().Length > 0 ? _buttons[i].Label.Trim() : "(no label)";
        if (!Equals(_list.Items[i], name)) _list.Items[i] = name;
        if (_list.SelectedIndex != i) _list.SelectedIndex = i;
        _loading = false;
        UpdateFields();
    }

    private void UpdateFields()
    {
        var i = _list.SelectedIndex;
        if (i < 0) return;
        var b = _buttons[i];
        var isKey = b.Kind == ShortcutKind.PressKey;
        _key.Visible = isKey;
        _target.Visible = !isKey;
        _browse.Visible = b.Kind is ShortcutKind.OpenProgram or ShortcutKind.OpenFolder;
        _targetLabel.Text = b.Kind switch { ShortcutKind.OpenWebsite => "Address", ShortcutKind.OpenFolder => "Folder", ShortcutKind.PressKey => "Key", _ => "Program" };
        _problem.Text = Shortcuts.Problem(b) ?? "";
    }

    private void Browse()
    {
        var i = _list.SelectedIndex;
        if (i < 0) return;
        if (_buttons[i].Kind == ShortcutKind.OpenFolder)
        {
            using var dlg = new FolderBrowserDialog { Description = "Folder to open" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _target.Text = dlg.SelectedPath;
        }
        else
        {
            using var dlg = new OpenFileDialog { Title = "Program to open", Filter = "Programs and shortcuts|*.exe;*.lnk;*.url;*.appref-ms|All files|*.*" };
            if (dlg.ShowDialog(this) == DialogResult.OK) _target.Text = dlg.FileName;
        }
    }

    private void Save()
    {
        for (var i = 0; i < _buttons.Count; i++)
        {
            _buttons[i] = _buttons[i] with { Label = _buttons[i].Label.Trim(), Target = _buttons[i].Target.Trim() };
            if (Shortcuts.Problem(_buttons[i]) is { } problem)
            {
                RefreshList(i);
                MessageBox.Show(this, $"Button {i + 1} ({_buttons[i].Label}): {problem}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
        }
        DialogResult = DialogResult.OK;
        Close();
    }
}
