using System.Buffers.Binary;

namespace WoWTouchBar.Core.TouchBar;

/// <summary>
/// Builds requests for the DFRDisplayKm driver (github.com/imbushuo/DFRDisplayKm, MIT).
/// Layout from DFRHostIo.h / DFRDisplayUm.Utility.Console:
///   header (16 bytes, little-endian, packed): BeginX u16, BeginY u16, Width u16, Height u16,
///                                             PixelFormat u32, RequireVertFlip u32
///   pixels: 3 bytes R,G,B; column by column (x = 0..W-1), each column bottom-to-top (y = H-1..0).
/// </summary>
public static class DfrProtocol
{
    public const uint IoctlUpdateFramebuffer = 0x8086A004;
    public const uint IoctlClearFramebuffer = 0x8086A008;
    public const uint FramebufferFormat = 0x52474241; // "RGBA" tag; alpha is not transmitted
    public const int HeaderSize = 16;

    /// <summary>Largest image the driver accepts (per the upstream sample). Actual device size may be smaller.</summary>
    public const int MaxWidth = 2170;
    public const int MaxHeight = 60;

    /// <summary>
    /// Measured on MacBookPro16,2 (13" 2020, physical Esc key), 2026-11-08: 2008 wide accepted, 2009 rejected.
    /// </summary>
    public const int TouchBarWidth = 2008;
    public const int TouchBarHeight = 60;

    /// <summary>Device interface GUID registered by DFRDisplayKm (Public.h).</summary>
    public static readonly Guid DeviceInterfaceGuid = new("2003cacd-9e7c-477c-ab06-a5a8bbb1a63e");

    public static byte[] BuildUpdateRequest(PixelBuffer buffer, int beginX = 0, int beginY = 0)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        CheckBounds(buffer, beginX, beginY);

        var request = new byte[HeaderSize + buffer.Width * buffer.Height * 3];
        var span = request.AsSpan();
        BinaryPrimitives.WriteUInt16LittleEndian(span[0..], (ushort)beginX);
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], (ushort)beginY);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)buffer.Width);
        BinaryPrimitives.WriteUInt16LittleEndian(span[6..], (ushort)buffer.Height);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], FramebufferFormat);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 0); // RequireVertFlip = false
        WritePixels(buffer, span[HeaderSize..]);
        return request;
    }

    /// <summary>Throws if the image at (x, y) doesn't fit the largest frame the display takes.</summary>
    public static void CheckBounds(PixelBuffer buffer, int beginX, int beginY)
    {
        if (beginX < 0 || beginY < 0) throw new ArgumentOutOfRangeException(nameof(beginX));
        if (beginX + buffer.Width > MaxWidth || beginY + buffer.Height > MaxHeight)
            throw new ArgumentOutOfRangeException(nameof(buffer),
                $"Image {buffer.Width}x{buffer.Height} at ({beginX},{beginY}) exceeds {MaxWidth}x{MaxHeight}.");
    }

    /// <summary>Pixels as the display wants them: 3 bytes R,G,B; column by column, each column bottom-to-top.</summary>
    public static void WritePixels(PixelBuffer buffer, Span<byte> destination)
    {
        var o = 0;
        for (var x = 0; x < buffer.Width; x++)
        {
            for (var y = buffer.Height - 1; y >= 0; y--)
            {
                var p = buffer.GetPixel(x, y);
                destination[o++] = p.R;
                destination[o++] = p.G;
                destination[o++] = p.B;
            }
        }
    }
}
