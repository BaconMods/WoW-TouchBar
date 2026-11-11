using System.Management;
using System.Runtime.Versioning;

namespace WoWTouchBar.Core.Input;

/// <summary>
/// Internal display brightness through Windows' own WMI brightness interface (not a key press).
/// Verified on the dev Mac 2026-11-09: WmiMonitorBrightness reports 81 levels (0..80), and
/// WmiSetBrightness takes a 0..100 percent, but CurrentBrightness reads back on a different scale
/// (set 100 -> reads 21, set 50 -> 11, set 20 -> 4). Stepping from the read-back value therefore
/// sank back each tap and stalled around 16 % - so steps are worked out by <see cref="BrightnessTracker"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Brightness
{
    public const int StepPercent = 10;

    private static readonly BrightnessTracker Tracker = new();

    /// <summary>Next brightness for a step change, clamped to 0..100 (pure; tested).</summary>
    public static int Next(int current, int steps) => Math.Clamp(current + steps * StepPercent, 0, 100);

    /// <summary>Changes brightness by steps x 10 %. Returns the new level, or null if the display doesn't support it.</summary>
    public static int? Change(int steps)
    {
        try
        {
            if (Read() is not { } reading) return null;
            return Set(Next(Tracker.CurrentPercent(reading), steps));
        }
        catch (ManagementException) { return null; }
    }

    /// <summary>
    /// Sets brightness to <paramref name="percent"/> (the slider). Returns it, or null if the display doesn't support it.
    /// <paramref name="readBack"/>: read the display afterwards to keep <see cref="BrightnessTracker"/> right (slow: skip
    /// it while a finger is still moving, do it once at the end).
    /// </summary>
    public static int? Set(int percent, bool readBack = true)
    {
        try
        {
            var next = Math.Clamp(percent, 0, 100);
            using (var m = new ManagementObjectSearcher(@"root\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                foreach (ManagementObject o in m.Get())
                {
                    o.InvokeMethod("WmiSetBrightness", [1u, (byte)next]);
                    o.Dispose();
                }
            if (readBack && Read() is { } after) Tracker.Record(next, after);
            return next;
        }
        catch (ManagementException) { return null; }
    }

    /// <summary>The brightness now in percent (for the slider), or null if the display doesn't report it.</summary>
    public static int? Current()
    {
        try { return Read() is { } reading ? Tracker.CurrentPercent(reading) : null; }
        catch (ManagementException) { return null; }
    }

    private static int? Read()
    {
        int? current = null;
        using var q = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
        foreach (ManagementObject o in q.Get()) { current ??= Convert.ToInt32(o["CurrentBrightness"]); o.Dispose(); }
        return current;
    }
}

/// <summary>
/// Works out the current brightness percent when the driver reads back on a different scale than it is set
/// (pure; tested). Remembers the last percent the app set and what the display read straight after; if the
/// reading hasn't changed since, the remembered percent is the truth. If something else changed brightness
/// (Windows slider, keyboard), the reading is converted with the read/set ratio learned from the last set.
/// On a normal driver the ratio is 1 and this is just the reading.
/// </summary>
public sealed class BrightnessTracker
{
    private int? _lastPercent;
    private int? _lastReading;
    private double _scale = 1.0;

    public int CurrentPercent(int reading)
    {
        if (_lastPercent is { } p && reading == _lastReading) return p;
        return Math.Clamp((int)Math.Round(reading / _scale), 0, 100);
    }

    public void Record(int percent, int reading)
    {
        _lastPercent = percent;
        _lastReading = reading;
        if (percent >= 20 && reading > 0) _scale = (double)reading / percent; // low levels are too coarse to learn from
    }
}
