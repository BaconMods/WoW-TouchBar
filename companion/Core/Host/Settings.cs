using System.Text.Json;
using System.Text.Json.Serialization;
using WoWTouchBar.Core.TouchBar.Ui;

namespace WoWTouchBar.Core.Host;

/// <summary>
/// Companion settings, saved locally as %LOCALAPPDATA%\WoWTouchBar\settings.json. A missing or damaged file
/// gives the defaults (never an error). The quest policy is an addon setting (`/wtb policy` in game), not here:
/// the companion can't send anything to the addon.
/// </summary>
public sealed record Settings
{
    public PageId Page { get; init; } = PageId.Quest;
    /// <summary>Older settings files only had this; kept so they still load (true = F1-F12 mode).</summary>
    public bool FunctionKeys { get; init; }
    /// <summary>Outside-WoW page mode; null in older files (then <see cref="FunctionKeys"/> decides).</summary>
    public SystemMode? Mode { get; init; }
    public bool Paused { get; init; }
    /// <summary>The player's own buttons for the outside-WoW page (tray > Settings), at most 12.</summary>
    public IReadOnlyList<Input.CustomButton> CustomButtons { get; init; } = [];
    /// <summary>Highlight colour of the bar (tray > Colour); purple when missing (older files).</summary>
    public AccentColour Accent { get; init; } = AccentColour.Purple;
    /// <summary>The quick-add shortcut ("Ctrl+Alt+N"); null in older files = the default; "" = off.</summary>
    public string? QuickAddKey { get; init; }
    /// <summary>How a finished timer shows (tray > Timer alert); fireworks when missing (older files).</summary>
    public AlertStyle AlertStyle { get; init; } = AlertStyle.Fireworks;

    [JsonIgnore]
    public SystemMode EffectiveMode => Mode ?? (FunctionKeys ? SystemMode.FunctionKeys : SystemMode.Media);

    public bool Equals(Settings? other) =>
        other is not null && Page == other.Page && FunctionKeys == other.FunctionKeys && Mode == other.Mode && Paused == other.Paused
        && Accent == other.Accent && QuickAddKey == other.QuickAddKey && AlertStyle == other.AlertStyle && CustomButtons.SequenceEqual(other.CustomButtons);

    public override int GetHashCode() => HashCode.Combine(Page, Mode, Paused, Accent, CustomButtons.Count);

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WoWTouchBar", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Settings Load(string? path = null) => Load(path, out _);

    /// <summary>Loads settings; <paramref name="warning"/> says why defaults were used for a damaged file (null if fine or absent).</summary>
    public static Settings Load(string? path, out string? warning)
    {
        warning = null;
        try
        {
            var p = path ?? DefaultPath;
            if (!File.Exists(p)) return new Settings();
            var s = JsonSerializer.Deserialize<Settings>(File.ReadAllText(p), Json) ?? throw new JsonException("empty");
            if (!Enum.IsDefined(s.Accent)) s = s with { Accent = AccentColour.Purple };
            if (!Enum.IsDefined(s.AlertStyle)) s = s with { AlertStyle = AlertStyle.Fireworks };
            if (s.AlertStyle is AlertStyle.Rainbow or AlertStyle.Warp) s = s with { AlertStyle = AlertStyle.Flash }; // retired styles
            if (Enum.IsDefined(s.Page)) return s;
            warning = $"unknown page {(int)s.Page} in settings - using Quest";
            return s with { Page = PageId.Quest };
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            warning = $"settings file unreadable ({ex.GetType().Name}) - using defaults";
            return new Settings();
        }
    }

    /// <summary>Writes atomically (temp file + replace). Returns false if it could not be saved.</summary>
    public bool Save(string? path = null)
    {
        try
        {
            var p = path ?? DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            var tmp = p + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, Json));
            File.Move(tmp, p, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return false; }
    }
}
