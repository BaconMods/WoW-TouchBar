using System.Runtime.Versioning;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// Picks how to reach the Touch Bar: Windows' own WinUSB driver if our setup file is installed (Secure Boot can stay on),
/// otherwise the old DFRDisplayKm driver, otherwise none (the app then runs without a bar). Never installs anything.
/// </summary>
public static class TouchBarDisplays
{
    /// <summary>One way to reach the display: how to find it, and how to open it from what was found.</summary>
    public sealed record Backend(string Kind, Func<string?> Find, Func<string, ITouchBarDisplay> Open);

    /// <summary>Tries each backend in order; logs one line per backend tried. Null if none works.</summary>
    public static ITouchBarDisplay? Open(IReadOnlyList<Backend> backends, Action<string> log)
    {
        foreach (var b in backends)
        {
            string? path;
            try { path = b.Find(); }
            catch (Exception ex) { log($"touchbar={b.Kind} find-error {ex.Message}"); continue; }
            if (path is null) continue;
            try
            {
                var display = b.Open(path);
                log($"touchbar=found {b.Kind} {path}");
                return display;
            }
            catch (Exception ex) { log($"touchbar={b.Kind} error {ex.Message}"); }
        }
        log("touchbar=absent (log-only mode)");
        return null;
    }

    /// <summary>The real backends: libusb0, WinUSB (Microsoft-signed drivers, Secure Boot can stay on), then the old driver.</summary>
    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Backend> Windows(Action<string> log) =>
    [
        new("libusb0", LibUsb0Pipes.FindDevicePath, path =>
        {
            var pipes = LibUsb0Pipes.Open(path);
            log($"touchbar=libusb0 {pipes.Describe}");
            return DfrUsbDisplay.Start(pipes, log, kind: "libusb0");
        }),
        new("winusb", WinUsbPipes.FindDevicePath, path =>
        {
            var pipes = WinUsbPipes.Open(path);
            log($"touchbar=winusb pipes {pipes.Describe()}");
            return DfrUsbDisplay.Start(pipes, log);
        }),
        new("usbprint", UsbPrintPipes.FindDevicePath, path =>
        {
            var pipes = UsbPrintPipes.Open(path);
            log($"touchbar=usbprint {path}");
            // usbprint gives no reply channel, so fall back to the Touch Bar's known size if GINF goes unanswered.
            return DfrUsbDisplay.Start(pipes, log, kind: "usbprint",
                assumeInfo: new DfrUsbProtocol.DisplayInfo(DfrProtocol.TouchBarWidth, DfrProtocol.TouchBarHeight, DfrProtocol.FramebufferFormat));
        }),
        new("dfrdisplaykm", DfrDevice.FindDevicePath, DfrDevice.Open),
    ];

    [SupportedOSPlatform("windows")]
    public static ITouchBarDisplay? OpenWindows(Action<string> log) => Open(Windows(log), log);
}
