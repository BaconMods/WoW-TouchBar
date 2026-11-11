using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// The Touch Bar display interface (MI_01) through libusb-win32 (libusb0.sys + libusb0.dll, official 1.4.0.2 release).
/// libusb0.sys sends plain USB requests the way DFRDisplayKm does, so it may work behind the T2's USB controller where
/// WinUSB doesn't (feasibility W13/W14). Only works once our libusb0 setup is installed and libusb0.dll is next to
/// the app; otherwise <see cref="FindDevicePath"/> returns null. Never installs or changes anything itself.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class LibUsb0Pipes : IUsbBulkPipes
{
    public const int TimeoutMs = 1000;
    /// <summary>Endpoints of the display interface, as the device reports them (WinUSB probe 2026-11-09).</summary>
    public const byte EndpointIn = 0x82, EndpointOut = 0x01;
    public const int DisplayInterface = 1, DisplayConfiguration = 2;
    private const int ETIMEDOUT = 116; // libusb-win32 returns -ETIMEDOUT

    /// <summary>
    /// Field offsets in libusb-win32's byte-packed structs (lusb0_usb.h, pshpack1, 64-bit pointers, LIBUSB_PATH_MAX 512).
    /// usb_bus: next, prev, dirname[512], devices, location (u32), root_dev.
    /// usb_device: next, prev, filename[512], bus, descriptor (18 B: idVendor @+8, idProduct @+10), config, dev, devnum.
    /// </summary>
    public static class Layout
    {
        public const int BusNext = 0, BusDevices = 16 + 512;
        public const int DeviceNext = 0, DeviceFilename = 16, DeviceDescriptor = 16 + 512 + 8;
        public const int DescriptorIdVendor = 8, DescriptorIdProduct = 10;
        public const int PathMax = 512;
    }

    private readonly IntPtr _handle;
    private bool _disposed;

    /// <summary>How the interface was claimed (for the log).</summary>
    public string Describe { get; }

    private LibUsb0Pipes(IntPtr handle, string describe) { _handle = handle; Describe = describe; }

    /// <summary>libusb0's name for the Touch Bar display, or null if libusb0.dll or the setup isn't there.</summary>
    public static string? FindDevicePath()
    {
        if (!NativeLibrary.TryLoad("libusb0.dll", typeof(LibUsb0Pipes).Assembly, null, out _)) return null;
        var dev = FindDevice(null);
        return dev == IntPtr.Zero ? null : ReadFilename(dev);
    }

    public static LibUsb0Pipes Open(string filename)
    {
        var dev = FindDevice(filename);
        if (dev == IntPtr.Zero) throw new IOException($"libusb0 device {filename} is gone.");
        var h = Native.usb_open(dev);
        if (h == IntPtr.Zero) throw new IOException($"usb_open failed: {LastError()}");
        // Select the display configuration right now, then the caller starts the handshake straight away: the Linux
        // driver notes the device answers "the first request sent in a particular timeframe after the USB device
        // configuration is set" (appletbdrm.c). DFRDisplayKm likewise selects it and talks at once in D0Entry.
        var c = Native.usb_set_configuration(h, DisplayConfiguration);
        var describe = c < 0 ? $"set_configuration({DisplayConfiguration}) failed ({LastError().Trim()})" : $"set_configuration({DisplayConfiguration})";
        if (Native.usb_claim_interface(h, DisplayInterface) < 0)
        {
            var err = LastError();
            Native.usb_close(h);
            throw new IOException($"usb_claim_interface({DisplayInterface}) failed: {err} ({describe})");
        }
        return new LibUsb0Pipes(h, $"{describe}, claimed, in 0x{EndpointIn:X2} / out 0x{EndpointOut:X2}");
    }

    public void Reset()
    {
        // DFRDisplayKm resets both pipes; for libusb0 that is clear-halt (also resets the data toggle).
        if (Native.usb_clear_halt(_handle, EndpointOut) < 0) throw new IOException($"usb_clear_halt(out) failed: {LastError()}");
        if (Native.usb_clear_halt(_handle, EndpointIn) < 0) throw new IOException($"usb_clear_halt(in) failed: {LastError()}");
    }

    public void Write(byte[] data)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var n = Native.usb_bulk_write(_handle, EndpointOut, data, data.Length, TimeoutMs);
        if (n < 0) throw new IOException($"usb_bulk_write failed ({n}): {LastError()}");
        if (n != data.Length) throw new IOException($"Touch Bar took {n} of {data.Length} bytes.");
    }

    public int Read(byte[] buffer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var n = Native.usb_bulk_read(_handle, EndpointIn, buffer, buffer.Length, TimeoutMs);
        if (n >= 0) return n;
        if (n == -ETIMEDOUT) return 0;
        throw new IOException($"usb_bulk_read failed ({n}): {LastError()}");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Native.usb_release_interface(_handle, DisplayInterface);
        Native.usb_close(_handle);
    }

    /// <summary>Diagnostics for TouchBarTest usbprobe: every device libusb0 drives.</summary>
    public static IEnumerable<string> Probe()
    {
        if (!NativeLibrary.TryLoad("libusb0.dll", typeof(LibUsb0Pipes).Assembly, null, out _)) { yield return "libusb0.dll not found next to the app"; yield break; }
        foreach (var dev in Devices())
        {
            var d = dev + Layout.DeviceDescriptor;
            yield return $"libusb0 device {ReadFilename(dev)} vid=0x{(ushort)Marshal.ReadInt16(d, Layout.DescriptorIdVendor):X4} pid=0x{(ushort)Marshal.ReadInt16(d, Layout.DescriptorIdProduct):X4}";
        }
    }

    /// <summary>
    /// Diagnostics for TouchBarTest usbprobe. Open (selects configuration 2, claims), then at once - nothing in
    /// between, because the device may only answer soon after its configuration is set (Linux appletbdrm.c) - send
    /// GINF and read up to 3 replies (a REDY "ready" signal may come first). Only then the slower checks. Results are
    /// collected first and reported afterwards, so logging adds no delay. <paramref name="reset"/>: clear-halt both
    /// pipes before GINF (as DFRDisplayKm does).
    /// </summary>
    public static IEnumerable<string> ProbeHandshake(string filename, bool reset)
    {
        var lines = new List<string>();
        LibUsb0Pipes? p = null;
        try { p = Open(filename); } catch (Exception ex) { lines.Add($"open: {ex.Message}"); }
        if (p is not null)
        {
            using (p)
            {
                var t = System.Diagnostics.Stopwatch.StartNew();
                lines.Add($"open: {p.Describe}");
                if (reset) lines.Add($"clear_halt out: {Native.usb_clear_halt(p._handle, EndpointOut)} in: {Native.usb_clear_halt(p._handle, EndpointIn)} ({t.ElapsedMilliseconds} ms)");
                var buf = new byte[512];
                var g = DfrUsbProtocol.BuildGenericRequest(DfrUsbProtocol.KeyGetInfo);
                var n = Native.usb_bulk_write(p._handle, EndpointOut, g, g.Length, 5000);
                lines.Add($"write GINF at {t.ElapsedMilliseconds} ms: {(n >= 0 ? $"{n} bytes" : $"{n} {LastError().Trim()}")}");
                for (var i = 0; i < 3; i++)
                {
                    n = Native.usb_bulk_read(p._handle, EndpointIn, buf, buf.Length, 2000);
                    lines.Add($"read reply {i + 1} at {t.ElapsedMilliseconds} ms: {Result(n, buf)}");
                    if (n < 0 || !DfrUsbProtocol.IsReadySignal(buf.AsSpan(0, n))) break;
                }
                var dn = Native.usb_get_descriptor(p._handle, 1, 0, buf, 18);
                lines.Add($"control GET_DESCRIPTOR(device): {Result(dn, buf)}");
            }
        }
        foreach (var l in lines) yield return l;
    }

    /// <summary>For diagnostics: libusb0's own debug messages (DLL to stderr; driver via DbgPrint, seen in DebugView).</summary>
    public static void SetDebug(int level) => Native.usb_set_debug(level);

    private static string Result(int n, byte[] buf) => n >= 0 ? $"{n} bytes {Convert.ToHexString(buf, 0, Math.Min(n, 80))}" : $"{n} {LastError().Trim()}";

    private static IEnumerable<IntPtr> Devices()
    {
        Native.usb_init();
        Native.usb_find_busses();
        Native.usb_find_devices();
        for (var bus = Native.usb_get_busses(); bus != IntPtr.Zero; bus = Marshal.ReadIntPtr(bus, Layout.BusNext))
            for (var dev = Marshal.ReadIntPtr(bus, Layout.BusDevices); dev != IntPtr.Zero; dev = Marshal.ReadIntPtr(dev, Layout.DeviceNext))
                yield return dev;
    }

    private static IntPtr FindDevice(string? filename)
    {
        foreach (var dev in Devices())
        {
            var d = dev + Layout.DeviceDescriptor;
            if ((ushort)Marshal.ReadInt16(d, Layout.DescriptorIdVendor) != 0x05AC || (ushort)Marshal.ReadInt16(d, Layout.DescriptorIdProduct) != 0x8302) continue;
            if (filename is null || ReadFilename(dev) == filename) return dev;
        }
        return IntPtr.Zero;
    }

    private static string ReadFilename(IntPtr dev) => Marshal.PtrToStringAnsi(dev + Layout.DeviceFilename) ?? "";

    private static string LastError() => Marshal.PtrToStringAnsi(Native.usb_strerror()) ?? "unknown error";

    private static class Native
    {
        private const string Dll = "libusb0.dll";
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void usb_init();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_find_busses();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_find_devices();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr usb_get_busses();
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr usb_open(IntPtr device);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_close(IntPtr handle);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_set_configuration(IntPtr handle, int configuration);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_claim_interface(IntPtr handle, int iface);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_release_interface(IntPtr handle, int iface);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_clear_halt(IntPtr handle, uint endpoint);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_resetep(IntPtr handle, uint endpoint);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern void usb_set_debug(int level);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_get_descriptor(IntPtr handle, byte type, byte index, byte[] buf, int size);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_bulk_write(IntPtr handle, int endpoint, byte[] bytes, int size, int timeout);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern int usb_bulk_read(IntPtr handle, int endpoint, byte[] bytes, int size, int timeout);
        [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)] public static extern IntPtr usb_strerror();
    }
}
