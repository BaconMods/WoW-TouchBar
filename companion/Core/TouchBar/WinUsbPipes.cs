using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// The Touch Bar display interface (MI_01) through Microsoft's WinUSB driver: one bulk IN and one bulk OUT pipe,
/// as DFRDisplayKm finds them (Device.c, PrepareHardware). Only works once our WinUSB setup file is installed;
/// otherwise <see cref="FindDevicePath"/> returns null. Never installs or changes anything itself.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WinUsbPipes : IUsbBulkPipes
{
    /// <summary>Per-transfer timeout. The driver used 300 ms for writes; reads had none (would block forever).</summary>
    public const uint TimeoutMs = 1000;

    private readonly SafeFileHandle _file;
    private readonly IntPtr _usb;
    private readonly byte _in, _out;
    private bool _disposed;

    private WinUsbPipes(SafeFileHandle file, IntPtr usb, byte pipeIn, byte pipeOut)
    {
        _file = file; _usb = usb; _in = pipeIn; _out = pipeOut;
    }

    public static string? FindDevicePath() => DeviceInterfaces.FirstPresent(DfrUsbProtocol.WinUsbInterfaceGuid);

    public static WinUsbPipes Open(string devicePath)
    {
        var file = Native.CreateFileW(devicePath, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_ATTRIBUTE_NORMAL | Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (file.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not open Touch Bar device {devicePath}");

        var usb = IntPtr.Zero;
        try
        {
            if (!Native.WinUsb_Initialize(file, out usb)) throw Error("WinUsb_Initialize");
            if (!Native.WinUsb_QueryInterfaceSettings(usb, 0, out var iface)) throw Error("WinUsb_QueryInterfaceSettings");
            byte? pipeIn = null, pipeOut = null;
            for (byte i = 0; i < iface.bNumEndpoints; i++)
            {
                if (!Native.WinUsb_QueryPipe(usb, 0, i, out var pipe)) throw Error("WinUsb_QueryPipe");
                if (pipe.PipeType != Native.UsbdPipeTypeBulk) continue;
                if ((pipe.PipeId & 0x80) != 0) pipeIn ??= pipe.PipeId; else pipeOut ??= pipe.PipeId;
            }
            if (pipeIn is not { } pin || pipeOut is not { } pout)
                throw new IOException("Touch Bar display interface has no bulk IN + OUT pipe pair.");
            foreach (var p in new[] { pin, pout })
            {
                var timeout = TimeoutMs;
                if (!Native.WinUsb_SetPipePolicy(usb, p, Native.PIPE_TRANSFER_TIMEOUT, sizeof(uint), ref timeout)) throw Error("WinUsb_SetPipePolicy");
            }
            return new WinUsbPipes(file, usb, pin, pout);
        }
        catch
        {
            if (usb != IntPtr.Zero) Native.WinUsb_Free(usb);
            file.Dispose();
            throw;
        }
    }

    public void Reset()
    {
        if (!Native.WinUsb_ResetPipe(_usb, _out)) throw Error("WinUsb_ResetPipe (out)");
        if (!Native.WinUsb_ResetPipe(_usb, _in)) throw Error("WinUsb_ResetPipe (in)");
    }

    public void Write(byte[] data)
    {
        var written = Transfer(_out, data, data.Length, write: true);
        if (written < 0) throw Error("WinUsb_WritePipe", -written);
        if (written != data.Length) throw new IOException($"Touch Bar took {written} of {data.Length} bytes.");
    }

    public int Read(byte[] buffer)
    {
        var read = Transfer(_in, buffer, buffer.Length, write: false);
        if (read >= 0) return read;
        if (-read == Native.ERROR_SEM_TIMEOUT) return 0;
        throw Error("WinUsb_ReadPipe", -read);
    }

    // Transfers use unmanaged memory and an OVERLAPPED + event, the way WinUSB is meant to be used on a handle
    // opened with FILE_FLAG_OVERLAPPED. (Synchronous calls on a managed array failed on the device with
    // error 998, "invalid access to memory location", 2026-11-09.)
    private IntPtr _buffer;
    private int _bufferSize;
    private readonly IntPtr _overlapped = Marshal.AllocHGlobal(Marshal.SizeOf<NativeOverlapped>());
    private readonly ManualResetEvent _done = new(false);

    /// <summary>Bytes transferred, or minus the Win32 error code.</summary>
    private int Transfer(byte pipe, byte[] data, int length, bool write)
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
            ? Native.WinUsb_WritePipe(_usb, pipe, _buffer, (uint)length, IntPtr.Zero, _overlapped)
            : Native.WinUsb_ReadPipe(_usb, pipe, _buffer, (uint)length, IntPtr.Zero, _overlapped);
        if (!ok)
        {
            var err = Marshal.GetLastWin32Error();
            if (err != Native.ERROR_IO_PENDING) return -err;
        }
        // The pipe's transfer timeout (TimeoutMs) ends a stuck transfer with ERROR_SEM_TIMEOUT.
        if (!Native.WinUsb_GetOverlappedResult(_usb, _overlapped, out var transferred, true)) return -Marshal.GetLastWin32Error();
        if (!write) Marshal.Copy(_buffer, data, 0, (int)transferred);
        return (int)transferred;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Native.WinUsb_Free(_usb);
        _file.Dispose();
        _done.Dispose();
        if (_buffer != IntPtr.Zero) Marshal.FreeHGlobal(_buffer);
        Marshal.FreeHGlobal(_overlapped);
    }

    private static Win32Exception Error(string call) => Error(call, Marshal.GetLastWin32Error());

    private static Win32Exception Error(string call, int code) =>
        new(code, $"{call} failed (Win32 error {code}: {new Win32Exception(code).Message})");

    /// <summary>Diagnostics for TouchBarTest usbprobe: interface + every endpoint, then one read and one GINF write.</summary>
    public static IEnumerable<string> Probe(string devicePath)
    {
        var file = Native.CreateFileW(devicePath, Native.GENERIC_READ | Native.GENERIC_WRITE,
            Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING,
            Native.FILE_ATTRIBUTE_NORMAL | Native.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        if (file.IsInvalid) { yield return $"open failed {Marshal.GetLastWin32Error()}"; yield break; }
        if (!Native.WinUsb_Initialize(file, out var usb)) { yield return $"init failed {Marshal.GetLastWin32Error()}"; file.Dispose(); yield break; }
        for (byte alt = 0; alt < 4; alt++)
        {
            if (!Native.WinUsb_QueryInterfaceSettings(usb, alt, out var d)) { yield return $"alt {alt}: none ({Marshal.GetLastWin32Error()})"; break; }
            yield return $"alt {alt}: interface {d.bInterfaceNumber} altSetting {d.bAlternateSetting} endpoints {d.bNumEndpoints} class {d.bInterfaceClass:X2}/{d.bInterfaceSubClass:X2}/{d.bInterfaceProtocol:X2}";
            for (byte i = 0; i < d.bNumEndpoints; i++)
                if (Native.WinUsb_QueryPipe(usb, alt, i, out var p))
                    yield return $"  pipe {i}: id 0x{p.PipeId:X2} type {p.PipeType} maxPacket {p.MaximumPacketSize} interval {p.Interval}";
        }
        new WinUsbPipes(file, usb, 0, 0).Dispose(); // WinUSB allows one user at a time: close before Open
        {
            using var real = Open(devicePath);
            yield return $"open: {real.Describe()}";
            var buf = new byte[512];
            var r = real.Transfer(real._in, buf, buf.Length, write: false);
            yield return $"read before any write: {(r >= 0 ? $"{r} bytes {Convert.ToHexString(buf, 0, Math.Min(r, 80))}" : $"error {-r}")}";
            var g = DfrUsbProtocol.BuildGenericRequest(DfrUsbProtocol.KeyGetInfo);
            var w = real.Transfer(real._out, g, g.Length, write: true);
            yield return $"write GINF: {(w >= 0 ? $"{w} bytes" : $"error {-w}")}";
            r = real.Transfer(real._in, buf, buf.Length, write: false);
            yield return $"read after GINF: {(r >= 0 ? $"{r} bytes {Convert.ToHexString(buf, 0, Math.Min(r, 80))}" : $"error {-r}")}";
        }
    }

    /// <summary>For the log: the pipes found at open ("in 0x81 / out 0x02").</summary>
    public string Describe() => $"in 0x{_in:X2} / out 0x{_out:X2}";

    private static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint FILE_ATTRIBUTE_NORMAL = 0x80, FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint PIPE_TRANSFER_TIMEOUT = 0x03;
        public const int UsbdPipeTypeBulk = 2;
        public const int ERROR_SEM_TIMEOUT = 121;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct UsbInterfaceDescriptor
        {
            public byte bLength, bDescriptorType, bInterfaceNumber, bAlternateSetting, bNumEndpoints,
                bInterfaceClass, bInterfaceSubClass, bInterfaceProtocol, iInterface;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WinUsbPipeInformation
        {
            public int PipeType; // USBD_PIPE_TYPE
            public byte PipeId;
            public ushort MaximumPacketSize;
            public byte Interval;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_Initialize(SafeFileHandle device, out IntPtr usb);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_Free(IntPtr usb);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_QueryInterfaceSettings(IntPtr usb, byte alternateIndex, out UsbInterfaceDescriptor descriptor);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_QueryPipe(IntPtr usb, byte alternateIndex, byte pipeIndex, out WinUsbPipeInformation pipe);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_SetPipePolicy(IntPtr usb, byte pipeId, uint policyType, uint valueLength, ref uint value);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_ResetPipe(IntPtr usb, byte pipeId);

        public const int ERROR_IO_PENDING = 997;

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_WritePipe(IntPtr usb, byte pipeId, IntPtr buffer, uint length, IntPtr transferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_ReadPipe(IntPtr usb, byte pipeId, IntPtr buffer, uint length, IntPtr transferred, IntPtr overlapped);

        [DllImport("winusb.dll", SetLastError = true)]
        public static extern bool WinUsb_GetOverlappedResult(IntPtr usb, IntPtr overlapped, out uint transferred, bool wait);
    }
}

/// <summary>Finds a present device by its device interface GUID (Configuration Manager).</summary>
[SupportedOSPlatform("windows")]
internal static class DeviceInterfaces
{
    public static string? FirstPresent(Guid interfaceGuid) => AllPresent(interfaceGuid).FirstOrDefault();

    /// <summary>Every present device interface path for this GUID (empty if none).</summary>
    public static IReadOnlyList<string> AllPresent(Guid interfaceGuid)
    {
        if (CM_Get_Device_Interface_List_SizeW(out var size, ref interfaceGuid, null, 0) != 0 || size <= 1) return [];
        var chars = new char[size];
        if (CM_Get_Device_Interface_ListW(ref interfaceGuid, null, chars, size, 0) != 0) return [];
        return new string(chars).Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_List_SizeW(out int length, ref Guid interfaceClass, string? deviceId, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_Interface_ListW(ref Guid interfaceClass, string? deviceId, char[] buffer, int length, uint flags);
}
