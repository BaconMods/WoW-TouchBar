using System.Management;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace WoWTouchBar.Core.Input;

/// <summary>Facts about the Mac we run on, from the model identifier Windows reports (e.g. "MacBookPro16,2").</summary>
public static partial class MacHardware
{
    /// <summary>
    /// True for Touch Bar Macs with a physical Esc key: MacBookPro16,x (16" 2019, 13" 2020; confirmed on a 16,2).
    /// The 2016-2019 Touch Bar models (MacBookPro13,x / 14,x / 15,x) have Esc on the Touch Bar itself.
    /// Unknown models return false, so the bar keeps its Esc button rather than leaving the user without one.
    /// </summary>
    public static bool HasPhysicalEscape(string? model) => model is not null && PhysicalEsc().IsMatch(model.Trim());

    [GeneratedRegex(@"^MacBookPro16,\d+$", RegexOptions.IgnoreCase)]
    private static partial Regex PhysicalEsc();

    /// <summary>Model identifier from Windows (Win32_ComputerSystem.Model), or null if it can't be read.</summary>
    [SupportedOSPlatform("windows")]
    public static string? ReadModel()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT Model FROM Win32_ComputerSystem");
            foreach (ManagementObject o in q.Get()) using (o) return o["Model"] as string;
        }
        catch (ManagementException) { }
        return null;
    }
}
