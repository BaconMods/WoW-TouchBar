using System.Diagnostics;
using System.Runtime.Versioning;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.Core.Input;

public enum ShortcutKind { OpenProgram, OpenWebsite, OpenFolder, PressKey }

/// <summary>
/// One of the player's own buttons for the outside-WoW page ("MY KEYS"), set up in the tray app's Settings window.
/// <paramref name="Target"/> is the program / folder path or the website address; <paramref name="Key"/> is used by PressKey.
/// </summary>
public sealed record CustomButton(string Label, ShortcutKind Kind, string Target = "", SystemKey Key = SystemKey.PlayPause);

/// <summary>Checks and runs custom buttons. Like every key the app presses: never while WoW is focused.</summary>
public static class Shortcuts
{
    public const int MaxButtons = 12;
    public const int MaxLabel = 12;
    private static readonly string[] ProgramExtensions = [".exe", ".lnk", ".url", ".appref-ms"];

    /// <summary>Why a button can't be saved, or null if it's fine (pure; tested).</summary>
    public static string? Problem(CustomButton b, Func<string, bool>? fileExists = null, Func<string, bool>? folderExists = null)
    {
        fileExists ??= File.Exists;
        folderExists ??= Directory.Exists;
        if (string.IsNullOrWhiteSpace(b.Label)) return "Give the button a label.";
        if (b.Label.Trim().Length > MaxLabel) return $"Keep the label to {MaxLabel} characters or fewer.";
        switch (b.Kind)
        {
            case ShortcutKind.OpenWebsite:
                return Uri.TryCreate(b.Target.Trim(), UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp)
                    ? null : "Enter a website address starting with https://";
            case ShortcutKind.OpenProgram:
                var path = b.Target.Trim().Trim('"');
                if (path.Length == 0 || !fileExists(path)) return "Pick a program that exists (Browse...).";
                return ProgramExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)
                    ? null : "Pick a program (.exe) or a shortcut (.lnk).";
            case ShortcutKind.OpenFolder:
                return b.Target.Trim().Length > 0 && folderExists(b.Target.Trim().Trim('"')) ? null : "Pick a folder that exists (Browse...).";
            case ShortcutKind.PressKey:
                return Enum.IsDefined(b.Key) ? null : "Pick a key.";
            default:
                return "Unknown button type.";
        }
    }

    /// <summary>
    /// Runs a button from a tap. Returns a short outcome for the log (never the website or path). Buttons that press a
    /// key are refused while WoW is focused (the key would go into the game); buttons that open a program / website /
    /// folder may run from the in-game panel - that is like alt-tabbing, not input to WoW.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static string Run(CustomButton b)
    {
        if (b.Kind == ShortcutKind.PressKey && SystemKeys.IsWowProcess(SystemKeys.ForegroundProcessName())) return "refused: WoW is focused";
        if (Problem(b) is { } problem) return $"not run: {problem}";
        if (b.Kind == ShortcutKind.PressKey) return SystemKeys.Press(b.Key, out var r) ? $"{b.Key} {r}" : $"{b.Key} {r}";
        try
        {
            Process.Start(new ProcessStartInfo(b.Target.Trim().Trim('"')) { UseShellExecute = true })?.Dispose();
            return $"opened ({b.Kind})";
        }
        catch (Exception ex) { return $"failed ({ex.GetType().Name})"; }
    }
}
