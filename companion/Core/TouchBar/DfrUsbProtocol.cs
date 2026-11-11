using System.Buffers.Binary;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// The USB bulk messages the Touch Bar display understands, as sent by the DFRDisplayKm driver
/// (github.com/imbushuo/DFRDisplayKm @ 360a53fcf33e, MIT: Dfr.h, DfrTransport.c, DfrDisplay.c, Device.c).
/// Here the app sends them itself through Windows' WinUSB driver instead of through our kernel driver.
/// All integers little-endian, structs packed (pack 1).
///
///   generic request (32 B): header 0x15120002, 0, 0, length 16 | key (GINF / REDY / CLDR), 8 x 0, end 0x10
///   frame update: header 0x00120002, 9, 0, length (= total - 16) | content (44 B): u16 1, u8 frame id, u8 0,
///                 u32 0, 24 x 0, u16 x, u16 y, u16 w, u16 h, u32 pixel bytes | pixels | 88-byte trailer
///   info reply (65 B):  header 0x01140000 @0, key GINF @16, width @32, height @36, pixel format @53
///   frame reply (40 B): header 0x01140000 @0, key UDCL @16, frame id @32
/// </summary>
public static class DfrUsbProtocol
{
    public const uint RequestHeader = 0x15120002;
    public const uint ResponseHeader = 0x01140000;
    public const uint FrameRequestHeader = 0x00120002;
    public const uint FrameRequestReserved1 = 0x00000009;
    public const ushort FrameContentReserved0 = 0x0001;
    public const uint RequestEnd = 0x00000010;

    public const uint KeyGetInfo = 0x47494e46;     // "GINF"
    public const uint KeyHostReady = 0x52454459;   // "REDY"
    /// <summary>
    /// "CLRD" as the Linux driver sends it (drivers/gpu/drm/tiny/appletbdrm.c). DFRDisplayKm sends 0x434c4452
    /// ("CLDR") and notes that clearing "doesn't quite work" - most likely because of that typo.
    /// </summary>
    public const uint KeyClearScreen = 0x434c5244; // "CLRD"
    public const uint KeyClearScreenDfr = 0x434c4452; // "CLDR" (DFRDisplayKm)
    public const uint KeyFrameUpdated = 0x5544434c; // "UDCL"

    public const int GenericRequestSize = 32;
    public const int FrameHeaderSize = 16 + 44;
    public const int InfoResponseSize = 65;
    public const int FrameResponseSize = 40;

    /// <summary>Retries of the info read while the display is still starting (driver: DFR_INIT_GINF_RETRY_MAX).</summary>
    public const int InfoReadRetries = 100;
    /// <summary>Extra sends of a frame whose reply doesn't match (driver: DFR_FB_UPDATE_RETRANSMIT_MAX).</summary>
    public const int FrameResends = 2;

    /// <summary>Fixed trailer the driver appends after the pixels (DfrUpdatePadding in DfrDisplay.c).</summary>
    public static ReadOnlySpan<byte> FrameTrailer =>
    [
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFE, 0xFF, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x08, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x02, 0x00, 0x08, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00,
        0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
    ];

    /// <summary>
    /// Device interface GUID our WinUSB setup file registers on the display interface (MI_01).
    /// Ours, not the DFRDisplayKm one, so the app can tell which driver is active.
    /// </summary>
    public static readonly Guid WinUsbInterfaceGuid = new("5617297c-08d1-403d-9e5d-667703855b3d");

    public static byte[] BuildGenericRequest(uint key)
    {
        var r = new byte[GenericRequestSize];
        var s = r.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[0..], RequestHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..], 16); // sizeof(content)
        BinaryPrimitives.WriteUInt32LittleEndian(s[16..], key);
        BinaryPrimitives.WriteUInt32LittleEndian(s[28..], RequestEnd);
        return r;
    }

    /// <summary>Frame update for <paramref name="buffer"/> at (x, y), pixels as for the old driver (column-major, bottom-to-top, RGB).</summary>
    public static byte[] BuildFrameRequest(PixelBuffer buffer, byte frameId, int beginX = 0, int beginY = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        DfrProtocol.CheckBounds(buffer, beginX, beginY);
        var pixelBytes = buffer.Width * buffer.Height * 3;
        var r = new byte[FrameHeaderSize + pixelBytes + FrameTrailer.Length];
        var s = r.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(s[0..], FrameRequestHeader);
        BinaryPrimitives.WriteUInt32LittleEndian(s[4..], FrameRequestReserved1);
        BinaryPrimitives.WriteUInt32LittleEndian(s[12..], (uint)(r.Length - 16));
        BinaryPrimitives.WriteUInt16LittleEndian(s[16..], FrameContentReserved0);
        s[18] = frameId;
        BinaryPrimitives.WriteUInt16LittleEndian(s[48..], (ushort)beginX);
        BinaryPrimitives.WriteUInt16LittleEndian(s[50..], (ushort)beginY);
        BinaryPrimitives.WriteUInt16LittleEndian(s[52..], (ushort)buffer.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(s[54..], (ushort)buffer.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(s[56..], (uint)pixelBytes);
        DfrProtocol.WritePixels(buffer, s.Slice(FrameHeaderSize, pixelBytes));
        FrameTrailer.CopyTo(s[(FrameHeaderSize + pixelBytes)..]);
        return r;
    }

    /// <summary>Next frame id as the driver counts: 1..255, then wraps to 1.</summary>
    public static byte NextFrameId(byte current) => current >= 0xFF ? (byte)1 : (byte)(current + 1);

    public readonly record struct DisplayInfo(int Width, int Height, uint PixelFormat);

    /// <summary>
    /// True for the device's "ready" message (key REDY). Linux: the device "responds to the first request sent in a
    /// particular timeframe after the USB device configuration is set with a readiness signal" - read again after it.
    /// </summary>
    public static bool IsReadySignal(ReadOnlySpan<byte> reply) =>
        reply.Length >= 20 && BinaryPrimitives.ReadUInt32LittleEndian(reply[16..]) == KeyHostReady;

    /// <summary>Reads the reply to GINF. Null if it is short or not an info reply.</summary>
    public static DisplayInfo? ParseInfo(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < InfoResponseSize) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(reply) != ResponseHeader) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(reply[16..]) != KeyGetInfo) return null;
        return new DisplayInfo(
            (int)BinaryPrimitives.ReadUInt32LittleEndian(reply[32..]),
            (int)BinaryPrimitives.ReadUInt32LittleEndian(reply[36..]),
            BinaryPrimitives.ReadUInt32LittleEndian(reply[53..]));
    }

    /// <summary>
    /// True if <paramref name="reply"/> confirms frame <paramref name="frameId"/>. The driver means to check this
    /// but compares the key with the frame id (always unequal), so it always resends; we check what it meant.
    /// </summary>
    public static bool IsFrameAck(ReadOnlySpan<byte> reply, byte frameId) =>
        reply.Length >= FrameResponseSize
        && BinaryPrimitives.ReadUInt32LittleEndian(reply) == ResponseHeader
        && BinaryPrimitives.ReadUInt32LittleEndian(reply[16..]) == KeyFrameUpdated
        && reply[32] == frameId;
}
