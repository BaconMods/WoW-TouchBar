using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using WoWTouchBar.Core.TouchBar;

namespace WoWTouchBar.Core.Capture;

/// <summary>
/// Captures a small region of the screen at the top-left of the WoW window's client area using GDI.
/// Only the strip region is captured, kept in memory, and discarded — never saved or uploaded.
/// Works for windowed / borderless ("windowed fullscreen") WoW; exclusive fullscreen may not capture.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsScreenCapture
{
    /// <summary>Call once at start-up so window coordinates are physical pixels.</summary>
    public static void EnablePerMonitorDpiAwareness() => Native.SetProcessDpiAwarenessContext(new IntPtr(-4));

    /// <summary>Finds the main window of a running WoW client. Prefers Forever if several are running.</summary>
    public static WowWindow? FindWowWindow()
    {
        WowWindow? best = null;
        foreach (var p in Process.GetProcessesByName("Wow"))
        {
            if (p.MainWindowHandle == IntPtr.Zero) continue;
            string? path = null;
            try { path = p.MainModule?.FileName; } catch { /* access denied: ignore path */ }
            var flavour = path is null ? "unknown"
                : path.Contains("_forever_", StringComparison.OrdinalIgnoreCase) ? "forever"
                : path.Contains("_retail_", StringComparison.OrdinalIgnoreCase) ? "retail" : "other";
            var w = new WowWindow(p.MainWindowHandle, p.Id, flavour);
            if (best is null || flavour == "forever") best = w;
        }
        return best;
    }

    public static bool IsMinimized(WowWindow window) => Native.IsIconic(window.Handle);

    /// <summary>True when WoW is the window the user is working in (keyboard focus).</summary>
    public static bool IsForeground(WowWindow window) => Native.GetForegroundWindow() == window.Handle;

    /// <summary>Captures width×height pixels at the top-left of the window's client area.</summary>
    public static PixelBuffer? CaptureClientTopLeft(WowWindow window, int width, int height)
    {
        var origin = new Native.POINT { X = 0, Y = 0 };
        if (!Native.ClientToScreen(window.Handle, ref origin)) return null;

        var screenDc = Native.GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) return null;
        var memDc = Native.CreateCompatibleDC(screenDc);
        var bmp = Native.CreateCompatibleBitmap(screenDc, width, height);
        var old = Native.SelectObject(memDc, bmp);
        try
        {
            if (!Native.BitBlt(memDc, 0, 0, width, height, screenDc, origin.X, origin.Y, Native.SRCCOPY)) return null;

            var info = new Native.BITMAPINFO
            {
                biSize = Marshal.SizeOf<Native.BITMAPINFO>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            var raw = new byte[width * height * 4];
            Native.SelectObject(memDc, old);
            if (Native.GetDIBits(memDc, bmp, 0, (uint)height, raw, ref info, 0) == 0) return null;

            var buffer = new PixelBuffer(width, height);
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var i = (y * width + x) * 4; // BGRA
                    buffer.SetPixel(x, y, new Rgb(raw[i + 2], raw[i + 1], raw[i]));
                }
            return buffer;
        }
        finally
        {
            Native.SelectObject(memDc, old);
            Native.DeleteObject(bmp);
            Native.DeleteDC(memDc);
            Native.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private static class Native
    {
        public const uint SRCCOPY = 0x00CC0020;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct BITMAPINFO
        {
            public int biSize; public int biWidth; public int biHeight; public short biPlanes; public short biBitCount;
            public int biCompression; public int biSizeImage; public int biXPelsPerMeter; public int biYPelsPerMeter;
            public int biClrUsed; public int biClrImportant; public int bmiColors;
        }

        [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("user32.dll")] public static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr hdc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BITMAPINFO info, uint usage);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr hdc);
    }
}

public sealed record WowWindow(IntPtr Handle, int ProcessId, string Flavour);
