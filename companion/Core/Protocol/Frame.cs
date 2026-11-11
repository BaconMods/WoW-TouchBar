namespace WoWTouchBar.Core.Protocol;

/// <summary>Optical protocol v1 constants and frame layout (docs/09_PROTOCOL.md). Mirrors addon/WoWTouchBar/Protocol.lua.</summary>
public static class ProtocolV1
{
    public const byte Version = 1;
    public const byte Magic1 = 0x57, Magic2 = 0x54; // "WT"
    public const int FrameBytes = 88;
    public const int HeaderBytes = 9;
    public const int MaxPayload = FrameBytes - HeaderBytes - 2; // 77
    public const int Cols = 89, Rows = 9;
    public const int BytesPerRow = (Cols - 1) / 8; // 11
    public const int MaxText = 48;
    public const int MaxChatText = 255; // chat text only (WoW's own chat limit); mirrors P.MAX_CHAT_TEXT
    public const int MaxNoteText = 80;   // mirrors P.MAX_NOTE_TEXT
    public const int MaxTimerLabel = 32; // mirrors P.MAX_TIMER_LABEL

    /// <summary>Grid shapes (columns → rows) that all carry 704 data bits; mirrors P.LAYOUTS in Protocol.lua.</summary>
    public static readonly IReadOnlyDictionary<int, int> Layouts = new Dictionary<int, int> { [89] = 9, [177] = 5, [353] = 3, [705] = 2 };

    /// <summary>Grid cell value (true = white) for a frame. Row 0 / column 0 are timing patterns.</summary>
    public static bool Cell(ReadOnlySpan<byte> frame, int row, int col, int cols = Cols)
    {
        if (row == 0) return col % 2 == 0;
        if (col == 0) return row % 2 == 0;
        var (i, mask) = DataBit(row, col, cols);
        return (frame[i] & mask) != 0;
    }

    /// <summary>
    /// Line layout: one row of colour cells. Green alternates (on at even cells) as the timing pattern;
    /// red = data bit 2i, blue = data bit 2i+1. Mirrors P.LINE_COLS / P.toLine in Protocol.lua.
    /// </summary>
    public const int LineCols = 352;

    public static (bool Red, bool Green, bool Blue) LineCell(ReadOnlySpan<byte> frame, int index)
    {
        var (ri, rm) = LineBit(index, blue: false);
        var (bi, bm) = LineBit(index, blue: true);
        return ((frame[ri] & rm) != 0, index % 2 == 0, (frame[bi] & bm) != 0);
    }

    public static (int ByteIndex, byte Mask) LineBit(int index, bool blue)
    {
        var bit = 2 * index + (blue ? 1 : 0);
        return (bit / 8, (byte)(0x80 >> (bit % 8)));
    }

    /// <summary>Maps a data cell (row ≥ 1, col ≥ 1) to its byte index and bit mask.</summary>
    public static (int ByteIndex, byte Mask) DataBit(int row, int col, int cols = Cols)
    {
        var bit = (row - 1) * (cols - 1) + (col - 1);
        return (bit / 8, (byte)(0x80 >> (bit % 8)));
    }
}

public static class Crc16
{
    /// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF). Check value for "123456789" is 0x29B1.</summary>
    public static ushort Compute(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (crc & 0x8000) != 0 ? (ushort)((crc << 1) ^ 0x1021) : (ushort)(crc << 1);
        }
        return crc;
    }
}

public enum MessageType : byte
{
    Hello = 1, Player = 2, Quest = 3, Objective = 4, Utility = 5, Navigation = 6, Heartbeat = 7, Status = 8, Reputation = 9, Chat = 10, Display = 11, Notes = 12, Timers = 13,
}

public sealed record Frame(MessageType Type, ushort Sequence, byte ChunkIndex, byte ChunkCount, byte[] Payload)
{
    /// <summary>Validates and parses one 88-byte frame. Returns null (with a reason) on any problem.</summary>
    public static Frame? TryParse(ReadOnlySpan<byte> data, out string? error)
    {
        error = null;
        if (data.Length != ProtocolV1.FrameBytes) { error = "length"; return null; }
        if (data[0] != ProtocolV1.Magic1 || data[1] != ProtocolV1.Magic2) { error = "magic"; return null; }
        if (data[2] != ProtocolV1.Version) { error = $"version {data[2]}"; return null; }
        int len = data[8];
        if (len > ProtocolV1.MaxPayload) { error = "payload-length"; return null; }
        var crc = (ushort)(data[ProtocolV1.HeaderBytes + len] << 8 | data[ProtocolV1.HeaderBytes + len + 1]);
        if (Crc16.Compute(data[..(ProtocolV1.HeaderBytes + len)]) != crc) { error = "crc"; return null; }
        // Padding is always zero: anything else means part of the strip was covered or misread.
        if (data[(ProtocolV1.HeaderBytes + len + 2)..].ContainsAnyExcept((byte)0)) { error = "padding"; return null; }
        byte index = data[6], count = data[7];
        if (count == 0 || index >= count) { error = "chunk"; return null; }
        return new Frame((MessageType)data[3], (ushort)(data[4] << 8 | data[5]), index, count,
            data.Slice(ProtocolV1.HeaderBytes, len).ToArray());
    }
}
