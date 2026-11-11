using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// Talks to the Touch Bar through the DFRDisplayKm driver. Only works once that driver is installed;
/// otherwise <see cref="FindDevicePath"/> returns null. Never installs or changes anything itself.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class DfrDevice : ITouchBarDisplay
{
    private readonly SafeFileHandle _handle;

    private DfrDevice(SafeFileHandle handle) => _handle = handle;

    public string Kind => "dfrdisplaykm";

    /// <summary>Returns the device path of the DFR display interface, or null if the driver isn't active.</summary>
    public static string? FindDevicePath() => DeviceInterfaces.FirstPresent(DfrProtocol.DeviceInterfaceGuid);

    public static DfrDevice Open(string devicePath)
    {
        var handle = Native.CreateFileW(devicePath, Native.GENERIC_WRITE, 0, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open Touch Bar device {devicePath}");
        return new DfrDevice(handle);
    }

    public void Draw(PixelBuffer buffer, int beginX = 0, int beginY = 0)
    {
        var request = DfrProtocol.BuildUpdateRequest(buffer, beginX, beginY);
        if (!Native.DeviceIoControl(_handle, DfrProtocol.IoctlUpdateFramebuffer, request, request.Length, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Touch Bar framebuffer update failed");
    }

    public void Clear()
    {
        if (!Native.DeviceIoControl(_handle, DfrProtocol.IoctlClearFramebuffer, null, 0, IntPtr.Zero, 0, out _, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Touch Bar clear failed");
    }

    public void Dispose() => _handle.Dispose();

    private static class Native
    {
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint OPEN_EXISTING = 3;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle device, uint code, byte[]? inBuffer, int inSize, IntPtr outBuffer, int outSize, out int returned, IntPtr overlapped);
    }
}
