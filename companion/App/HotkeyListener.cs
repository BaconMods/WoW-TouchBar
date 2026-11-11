using System.Runtime.InteropServices;
using WoWTouchBar.Core.Notes;

namespace WoWTouchBar.App;

/// <summary>
/// A system-wide shortcut (Windows' RegisterHotKey): Windows tells the app when the key combination is pressed,
/// whichever window has focus. The app only listens - it never sends keys anywhere. Lives on the UI thread.
/// </summary>
internal sealed class HotkeyListener : NativeWindow, IDisposable
{
    private const int WmHotkey = 0x0312, Id = 1, NoRepeat = 0x4000;
    private bool _registered;

    public event Action? Pressed;

    public HotkeyListener() => CreateHandle(new CreateParams());

    /// <summary>Uses <paramref name="key"/> (null = none). Returns null, or why it couldn't (e.g. another program has it).</summary>
    public string? Set(Hotkey? key)
    {
        if (_registered) { UnregisterHotKey(Handle, Id); _registered = false; }
        if (key is not { } k) return null;
        if (RegisterHotKey(Handle, Id, k.Modifiers | NoRepeat, k.Key)) { _registered = true; return null; }
        return $"{k} is already used by another program - pick another.";
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam == Id) Pressed?.Invoke();
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        if (_registered) UnregisterHotKey(Handle, Id);
        DestroyHandle();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
