using WoWTouchBar.Core.Protocol;
using WoWTouchBar.Core.TouchBar;

namespace WoWTouchBar.Core.Bridge;

/// <summary>Capture-independent pipeline: image → strip bytes → frame → (reassembled) message → game state.</summary>
public sealed class BridgeSession
{
    public StripReader Reader { get; } = new();
    public Reassembler Reassembler { get; } = new();
    public GameState State { get; } = new();

    public TimeSpan? LastFrameAt { get; private set; }
    public int Images { get; private set; }
    public int Frames { get; private set; }
    public int Rejected { get; private set; }
    public int Malformed { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>Feeds one captured image. Returns the message applied, if any.</summary>
    public Message? Feed(PixelBuffer image, TimeSpan now)
    {
        Images++;
        var bytes = Reader.Read(image);
        return bytes is null ? null : FeedFrameBytes(bytes, now);
    }

    /// <summary>Frames with a valid magic but another protocol version, since the last good frame.</summary>
    public int VersionMismatches { get; private set; }
    public int? OtherVersion { get; private set; }
    /// <summary>
    /// True when the addon keeps sending a protocol version this companion doesn't support (needs both updated).
    /// Requires several in a row so one damaged frame can't trigger it.
    /// </summary>
    public bool AddonIncompatible => VersionMismatches >= 5;

    public Message? FeedFrameBytes(ReadOnlySpan<byte> bytes, TimeSpan now)
    {
        var frame = Frame.TryParse(bytes, out var error);
        if (frame is null)
        {
            Rejected++;
            LastError = error;
            if (error is not null && error.StartsWith("version ") && int.TryParse(error[8..], out var v))
            {
                VersionMismatches = OtherVersion == v ? VersionMismatches + 1 : 1;
                OtherVersion = v;
            }
            return null;
        }
        Frames++;
        LastFrameAt = now;
        VersionMismatches = 0;
        var payload = Reassembler.Accept(frame, now);
        if (payload is null) return null;
        var message = MessageParser.Parse(frame.Type, payload);
        if (message is null)
        {
            if (Enum.IsDefined(frame.Type) && frame.Type != MessageType.Navigation) { Malformed++; LastError = $"malformed {frame.Type}"; }
            return null;
        }
        State.Apply(message, now);
        return message;
    }

    public void ResetCounters() => Images = Frames = Rejected = Malformed = 0;
}
