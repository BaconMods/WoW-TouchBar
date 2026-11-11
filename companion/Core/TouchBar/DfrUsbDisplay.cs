namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// Drives the Touch Bar display over raw bulk pipes, doing in the app what DFRDisplayKm does in the kernel:
/// start-up handshake (Device.c, D0Entry) and frame updates with up to 2 resends (DfrDisplay.c).
/// Pure logic over <see cref="IUsbBulkPipes"/>, so it is tested without hardware.
/// </summary>
public sealed class DfrUsbDisplay : ITouchBarDisplay
{
    private readonly IUsbBulkPipes _pipes;
    private readonly Action<string> _log;
    private readonly byte[] _reply = new byte[64 + DfrUsbProtocol.InfoResponseSize];
    private byte _frameId;
    private bool _warnedUnconfirmed;

    public string Kind { get; }
    public DfrUsbProtocol.DisplayInfo Info { get; }
    /// <summary>Frames sent (each Draw), extra sends after a non-matching reply, and frames never confirmed.</summary>
    public int Frames { get; private set; }
    public int Resends { get; private set; }
    public int Unconfirmed { get; private set; }

    private DfrUsbDisplay(IUsbBulkPipes pipes, DfrUsbProtocol.DisplayInfo info, byte firstFrameId, Action<string> log, string kind)
    {
        Kind = kind;
        _pipes = pipes;
        Info = info;
        _frameId = firstFrameId;
        _log = log;
    }

    /// <summary>
    /// Handshake: reset pipes, ask for display info (GINF; the display may need a few reads while it starts),
    /// check the pixel format, say the host is ready (REDY), clear (CLDR), then draw one black frame as the driver does.
    /// Throws <see cref="IOException"/> if the display doesn't answer as expected. Takes ownership of <paramref name="pipes"/>.
    /// </summary>
    /// <param name="assumeInfo">
    /// Display size to fall back to if the device never answers GINF (some paths, e.g. usbprint, give no reply
    /// channel). When null, a missing GINF reply is an error, as before.
    /// </param>
    public static DfrUsbDisplay Start(IUsbBulkPipes pipes, Action<string>? log = null, byte firstFrameId = 0, string kind = "winusb", DfrUsbProtocol.DisplayInfo? assumeInfo = null)
    {
        ArgumentNullException.ThrowIfNull(pipes);
        log ??= _ => { };
        try
        {
            pipes.Reset();
            pipes.Write(DfrUsbProtocol.BuildGenericRequest(DfrUsbProtocol.KeyGetInfo));
            var buffer = new byte[64 + DfrUsbProtocol.InfoResponseSize];
            DfrUsbProtocol.DisplayInfo? info = null;
            for (var attempt = 0; attempt <= DfrUsbProtocol.InfoReadRetries; attempt++)
            {
                var n = pipes.Read(buffer);
                if (DfrUsbProtocol.IsReadySignal(buffer.AsSpan(0, n))) { log("touchbar=ready signal before GINF reply"); continue; } // as Linux does
                if (n < DfrUsbProtocol.InfoResponseSize) continue; // still starting: read again (driver does the same)
                info = DfrUsbProtocol.ParseInfo(buffer.AsSpan(0, n))
                    ?? throw new IOException($"Touch Bar sent an unexpected reply to GINF ({Hex(buffer, n)}).");
                break;
            }
            if (info is null && assumeInfo is { } assumed)
            {
                log($"touchbar={kind} no GINF reply; assuming {assumed.Width}x{assumed.Height}");
                info = assumed;
            }
            if (info is not { } i) throw new IOException("Touch Bar did not answer GINF.");
            if (i.PixelFormat != DfrProtocol.FramebufferFormat)
                throw new IOException($"Touch Bar reports pixel format 0x{i.PixelFormat:X8}, expected 0x{DfrProtocol.FramebufferFormat:X8}.");
            if (i.Width <= 0 || i.Height <= 0 || i.Width > DfrProtocol.MaxWidth || i.Height > DfrProtocol.MaxHeight)
                throw new IOException($"Touch Bar reports an unlikely size {i.Width}x{i.Height}.");

            pipes.Write(DfrUsbProtocol.BuildGenericRequest(DfrUsbProtocol.KeyHostReady));
            pipes.Write(DfrUsbProtocol.BuildGenericRequest(DfrUsbProtocol.KeyClearScreen));
            var display = new DfrUsbDisplay(pipes, i, firstFrameId, log, kind);
            log($"touchbar={kind} info {i.Width}x{i.Height}");
            display.Clear();
            return display;
        }
        catch
        {
            pipes.Dispose();
            throw;
        }
    }

    public void Draw(PixelBuffer buffer, int beginX = 0, int beginY = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        if (beginX < 0 || beginY < 0 || beginX + buffer.Width > Info.Width || beginY + buffer.Height > Info.Height)
            throw new ArgumentOutOfRangeException(nameof(buffer),
                $"Image {buffer.Width}x{buffer.Height} at ({beginX},{beginY}) exceeds the display ({Info.Width}x{Info.Height}).");

        _frameId = DfrUsbProtocol.NextFrameId(_frameId);
        var request = DfrUsbProtocol.BuildFrameRequest(buffer, _frameId, beginX, beginY);
        Frames++;
        var n = 0;
        for (var send = 0; send <= DfrUsbProtocol.FrameResends; send++)
        {
            if (send > 0) Resends++;
            _pipes.Write(request);
            n = _pipes.Read(_reply);
            if (DfrUsbProtocol.IsFrameAck(_reply.AsSpan(0, n), _frameId)) return;
            if (n == 0) break; // no reply at all (timeout): sending again won't help, and each wait costs time
        }
        // Same outcome as the old driver (which never matched a reply): the frame was sent, just not confirmed.
        Unconfirmed++;
        if (!_warnedUnconfirmed)
        {
            _warnedUnconfirmed = true;
            _log($"touchbar={Kind} frame {_frameId} not confirmed (reply {Hex(_reply, n)}); continuing");
        }
    }

    public void Clear() => Draw(new PixelBuffer(Info.Width, Info.Height));

    public void Dispose() => _pipes.Dispose();

    private static string Hex(byte[] b, int n) => n <= 0 ? "none" : Convert.ToHexString(b, 0, Math.Min(n, 48));
}
