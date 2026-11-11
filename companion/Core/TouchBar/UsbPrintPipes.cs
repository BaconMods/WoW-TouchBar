using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// The Touch Bar display interface (MI_01) through Microsoft's USB printer class driver, usbprint.sys.
/// usbprint exposes the device as a printer port: the app opens its device interface
/// (<c>GUID_DEVINTERFACE_USBPRINT</c>) with CreateFile and sends bytes with WriteFile (the bulk OUT pipe) /
/// ReadFile (the bulk IN pipe). Unlike WinUSB and libusb0 (feasibility W13/W15), a buffered-I/O class driver
/// copies the data through a kernel buffer before the USB request, which may be accepted behind the T2's USB
/// controller where a program's own buffer is refused (W17/W18). Only works once our usbprint setup is
/// installed; otherwise <see cref="FindDevicePath"/> returns null. Never installs or changes anything itself.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class UsbPrintPipes : IUsbBulkPipes
{
    /// <summary>GUID_DEVINTERFACE_USBPRINT - the device interface usbprint.sys exposes for each USB printer.</summary>
    public static readonly Guid UsbPrintInterfaceGuid = new("28d78fad-5a12-11d1-ae5b-0000f803a8c2");
    public const uint TimeoutMs = 1000;

    private readonly SafeFileHandle _file;
    private readonly IntPtr _overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
    private readonly ManualResetEvent _done = new(false);
    private IntPtr _buffer;
    private int _bufferSize;
    private bool _disposed;

    private UsbPrintPipes(SafeFileHandle file) => _file = file;

    /// <summary>The USBPRINT interface that belongs to the Touch Bar (VID_05AC&amp;PID_8302), or null.</summary>
    public static string? FindDevicePath() =>
        DeviceInterfaces.AllPresent(UsbPrintInterfaceGuid)
            .FirstOrDefault(p => p.Contains("vid_05ac", StringComparison.OrdinalIgnoreCase)
                              && p.Contains("pid_8302", StringComparison.OrdinalIgnoreCase));

    public static UsbPrintPipes Open(string devicePath)
    {
        var file = Native.CreateFileW(devicePath, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_ATTRIBUTE_NORMAL | Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open Touch Bar printer device {devicePath}");
        return new UsbPrintPipes(file);
    }

    /// <summary>usbprint has no pipe-reset for applications; the stack manages the data toggle itself.</summary>
    public void Reset() { }

    public void Write(byte[] data)
    {
        var written = Transfer(data, data.Length, write: true);
        if (written < 0) throw Error("WriteFile", -written);
        if (written != data.Length) throw new IOException($"Touch Bar took {written} of {data.Length} bytes.");
    }

    public int Read(byte[] buffer)
    {
        var read = Transfer(buffer, buffer.Length, write: false);
        if (read >= 0) return read;
        // usbprint often returns no data on reads (it has no descriptor/pipe-level reply channel); treat a
        // timeout or a broken read as "no reply", which the handshake and frame logic tolerate (W10).
        if (-read is Native.ERROR_SEM_TIMEOUT or Native.ERROR_OPERATION_ABORTED) return 0;
        throw Error("ReadFile", -read);
    }

    // Overlapped I/O with a per-transfer timeout, so a read that never completes (common on usbprint) cannot hang
    // the app. Mirrors WinUsbPipes: unmanaged buffer + OVERLAPPED + event.
    private int Transfer(byte[] data, int length, bool write)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (length > _bufferSize)
        {
            if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
            _buffer = Marshal.AllocHGlobal(length);
            _bufferSize = length;
        }
        if (write) Marshal.Copy(data, 0, _buffer, length);
        _done.Reset();
        Marshal.StructureToPtr(new NativeOverlapped { EventHandle = _done.SafeWaitHandle.DangerousGetHandle() }, _overlapped, false);

        var ok = write
            ? Native.WriteFile(_file, _buffer, (uint)length, IntPtr.Zero, _overlapped)
            : Native.ReadFile(_file, _buffer, (uint)length, IntPtr.Zero, _overlapped);
        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != Native.ERROR_IO_PENDING) return -err;
        }
        if (!_done.WaitOne(TimeSpan.FromMilliseconds(TimeoutMs)))
        {
            Native.CancelIoEx(_file, _overlapped);
            return -Native.ERROR_SEM_TIMEOUT;
        }
        if (!Native.GetOverlappedResult(_file, _overlapped, out var transferred, false)) return -Marshal.GetLastWin32Error();
        if (!write) Marshal.Copy(_buffer, data, 0, (int)transferred);
        return (int)transferred;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _file.Dispose();
        _done.Dispose();
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        Marshal.FreeHGlobal(_overlapped);
    }

    /// <summary>Diagnostics for TouchBarTest usbprobe: open, read before any write, send GINF, read again.</summary>
    public static IEnumerable<string> Probe(string devicePath)
    {
        UsbPrintPipes? p = null;
        var lines = new List<string>();
        try { p = Open(devicePath); } catch (Exception ex) { lines.Add($"open: {ex.Message}"); }
        if (p is not null)
            using (p)
            {
                lines.Add($"open: {devicePath}");
                var buf = new byte[512];
                var r = p.Transfer(buf, buf.Length, write: false);
                lines.Add($"read before any write: {Result(r, buf)}");
                var g = DfrUsbProtocol.BuildGenericRequest(DfrUsbProtocol.KeyGetInfo);
                var w = p.Transfer(g, g.Length, write: true);
                lines.Add($"write GINF: {(w >= 0 ? $"{w} bytes" : $"error {-w}")}");
                r = p.Transfer(buf, buf.Length, write: false);
                lines.Add($"read after GINF: {Result(r, buf)}");
            }
        foreach (var l in lines) yield return l;
    }

    private static string Result(int n, byte[] buf) => n >= 0 ? $"{n} bytes {Convert.ToHexString(buf, 0, Math.Min(n, 80))}" : $"error {-n}";

    private static Win32Exception Error(string call, int code) =>
        new(code, $"{call} failed (Win32 error {code}: {new Win32Exception(code).Message})");

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const int ERROR_IO_PENDING = 997, ERROR_SEM_TIMEOUT = 121, ERROR_OPERATION_ABORTED = 995;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle file, IntPtr buffer, uint length, IntPtr written, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool ReadFile(SafeFileHandle file, IntPtr buffer, uint length, IntPtr read, IntPtr overlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out uint transferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    }
}
