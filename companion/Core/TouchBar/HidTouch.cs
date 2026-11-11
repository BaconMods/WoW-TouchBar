using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// Read-only access to the Touch Bar's touch sensor (USB VID_05AC PID_8302, interface MI_00, which Windows
/// lists as a "HID-compliant touch pad"). Only reads HID input reports from that one device; never sends
/// input anywhere and never writes to the device.
/// </summary>
[SupportedOSPlatform("windows")]
public static class HidTouch
{
    public const string TouchBarHardwareId = "vid_05ac&pid_8302";

    public sealed record Caps(ushort UsagePage, ushort Usage, int InputReportLength, int FeatureReportLength, IReadOnlyList<ValueCap> Values, IReadOnlyList<ButtonCap> Buttons);
    public sealed record ValueCap(byte ReportId, ushort UsagePage, ushort Usage, ushort LinkCollection, int LogicalMin, int LogicalMax, int BitSize, int ReportCount);
    public sealed record ButtonCap(byte ReportId, ushort UsagePage, ushort UsageMin, ushort UsageMax, ushort LinkCollection);

    /// <summary>All HID interface paths that belong to the Touch Bar (usually one: the touch sensor).</summary>
    public static IReadOnlyList<string> FindPaths()
    {
        Native.HidD_GetHidGuid(out var guid);
        if (Native.CM_Get_Device_Interface_List_SizeW(out var size, ref guid, null, 0) != 0 || size <= 1) return [];
        var chars = new char[size];
        if (Native.CM_Get_Device_Interface_ListW(ref guid, null, chars, size, 0) != 0) return [];
        return new string(chars).Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(p => p.Contains(TouchBarHardwareId, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Opens a HID path for reading (shared, so Windows keeps its own access).</summary>
    public static SafeFileHandle OpenRead(string path)
    {
        var h = Native.CreateFileW(path, Native.GENERIC_READ, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open {path} for reading");
        return h;
    }

    /// <summary>Report layout, read with an access-0 handle (works even when reading is refused).</summary>
    public static Caps? GetCaps(string path)
    {
        using var h = Native.CreateFileW(path, 0, Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (h.IsInvalid || !Native.HidD_GetPreparsedData(h, out var pp)) return null;
        try
        {
            if (Native.HidP_GetCaps(pp, out var caps) != Native.HIDP_STATUS_SUCCESS) return null;
            var values = new List<ValueCap>();
            if (caps.NumberInputValueCaps > 0)
            {
                var vc = new Native.HIDP_VALUE_CAPS[caps.NumberInputValueCaps];
                var n = caps.NumberInputValueCaps;
                if (Native.HidP_GetValueCaps(0, vc, ref n, pp) == Native.HIDP_STATUS_SUCCESS)
                    for (var i = 0; i < n; i++)
                        values.Add(new ValueCap(vc[i].ReportID, vc[i].UsagePage, vc[i].IsRange != 0 ? vc[i].UsageMin : vc[i].UsageMin, vc[i].LinkCollection,
                            vc[i].LogicalMin, vc[i].LogicalMax, vc[i].BitSize, vc[i].ReportCount));
            }
            var buttons = new List<ButtonCap>();
            if (caps.NumberInputButtonCaps > 0)
            {
                var bc = new Native.HIDP_BUTTON_CAPS[caps.NumberInputButtonCaps];
                var n = caps.NumberInputButtonCaps;
                if (Native.HidP_GetButtonCaps(0, bc, ref n, pp) == Native.HIDP_STATUS_SUCCESS)
                    for (var i = 0; i < n; i++)
                        buttons.Add(new ButtonCap(bc[i].ReportID, bc[i].UsagePage, bc[i].UsageMin, bc[i].IsRange != 0 ? bc[i].UsageMax : bc[i].UsageMin, bc[i].LinkCollection));
            }
            return new Caps(caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.FeatureReportByteLength, values, buttons);
        }
        finally { Native.HidD_FreePreparsedData(pp); }
    }

    /// <summary>Blocking read of one input report; returns the bytes read, or 0 on failure / when the handle is closed.</summary>
    public static int Read(SafeFileHandle handle, byte[] buffer)
        => Native.ReadFile(handle, buffer, buffer.Length, out var read, IntPtr.Zero) ? read : 0;

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2;
        public const uint OPEN_EXISTING = 3;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices;
            public ushort NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        // HIDP_VALUE_CAPS / HIDP_BUTTON_CAPS: 72 bytes each; the Range/NotRange union is read as its first fields.
        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_VALUE_CAPS
        {
            public ushort UsagePage; public byte ReportID; public byte IsAlias;
            public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
            public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute, HasNull, Reserved;
            public ushort BitSize, ReportCount;
            public ushort Reserved2a, Reserved2b, Reserved2c, Reserved2d, Reserved2e;
            public uint UnitsExp, Units;
            public int LogicalMin, LogicalMax, PhysicalMin, PhysicalMax;
            public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_BUTTON_CAPS
        {
            public ushort UsagePage; public byte ReportID; public byte IsAlias;
            public ushort BitField, LinkCollection, LinkUsage, LinkUsagePage;
            public byte IsRange, IsStringRange, IsDesignatorRange, IsAbsolute;
            public ushort ReportCount, Reserved2;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 9)] public uint[] Reserved;
            public ushort UsageMin, UsageMax, StringMin, StringMax, DesignatorMin, DesignatorMax, DataIndexMin, DataIndexMax;
        }

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll", SetLastError = true)] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr preparsed);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr preparsed);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr preparsed, out HIDP_CAPS caps);
        [DllImport("hid.dll")] public static extern int HidP_GetValueCaps(int reportType, [Out] HIDP_VALUE_CAPS[] caps, ref ushort length, IntPtr preparsed);
        [DllImport("hid.dll")] public static extern int HidP_GetButtonCaps(int reportType, [Out] HIDP_BUTTON_CAPS[] caps, ref ushort length, IntPtr preparsed);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle h, byte[] buffer, int toRead, out int read, IntPtr overlapped);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_Interface_List_SizeW(out int length, ref Guid interfaceClass, string? deviceId, uint flags);
        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
        public static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, char[] buffer, int length, uint flags);
    }
}
