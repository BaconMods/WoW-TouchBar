using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.Core.Input;

/// <summary>
/// THE ONLY PLACE the app may press a key (see docs/SAFETY_AND_PRIVACY.md).
/// One tap on the system page = one key press to Windows (Esc, F1-F12, volume, media). Refuses whenever a
/// WoW window is in the foreground, so nothing is ever sent to the game. A code-search test fails the
/// build if key / mouse sending APIs appear in any other file.
/// </summary>
[SupportedOSPlatform("windows")]
public static partial class SystemKeys
{
    public static ushort VirtualKey(SystemKey key) => key switch
    {
        SystemKey.Escape => 0x1B,
        >= SystemKey.F1 and <= SystemKey.F12 => (ushort)(0x70 + (key - SystemKey.F1)),
        SystemKey.VolumeDown => 0xAE,
        SystemKey.VolumeUp => 0xAF,
        SystemKey.Mute => 0xAD,
        SystemKey.NextTrack => 0xB0,
        SystemKey.PreviousTrack => 0xB1,
        SystemKey.PlayPause => 0xB3,
        _ => throw new ArgumentOutOfRangeException(nameof(key)),
    };

    /// <summary>WoW client process names (Wow, WowClassic, WowT, WowB, WowClassicT, ...).</summary>
    public static bool IsWowProcess(string? processName) => processName is not null && WowName().IsMatch(processName);

    [GeneratedRegex("^Wow(Classic)?[TB]?$", RegexOptions.IgnoreCase)]
    private static partial Regex WowName();

    /// <summary>Process name of the foreground window, or null if there is none / it can't be read.</summary>
    public static string? ForegroundProcessName()
    {
        var hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out var pid);
        try { using var p = Process.GetProcessById((int)pid); return p.ProcessName; }
        catch (ArgumentException) { return null; }
    }

    /// <summary>Presses and releases one key. Returns false (with a reason) if refused or if Windows rejected it.</summary>
    public static bool Press(SystemKey key, out string result)
    {
        var foreground = ForegroundProcessName();
        if (IsWowProcess(foreground)) { result = "refused: WoW is focused"; return false; }
        var vk = VirtualKey(key);
        var flags = key >= SystemKey.VolumeDown ? Native.KEYEVENTF_EXTENDEDKEY : 0u;
        var inputs = new[]
        {
            new Native.INPUT { type = Native.INPUT_KEYBOARD, ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = flags } },
            new Native.INPUT { type = Native.INPUT_KEYBOARD, ki = new Native.KEYBDINPUT { wVk = vk, dwFlags = flags | Native.KEYEVENTF_KEYUP } },
        };
        var sent = Native.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Native.INPUT>());
        result = sent == inputs.Length ? $"sent to {foreground ?? "desktop"}" : $"failed ({Marshal.GetLastWin32Error()})";
        return sent == inputs.Length;
    }

    private static class Native
    {
        public const uint INPUT_KEYBOARD = 1;
        public const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

        // INPUT is a tagged union; MOUSEINPUT is its largest member (32 bytes on x64), so pad the size to match.
        [StructLayout(LayoutKind.Explicit, Size = 40)]
        public struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public KEYBDINPUT ki; }

        [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    }
}
