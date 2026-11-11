namespace WoWTouchBar.Core.TouchBar;

/// <summary>Something that can show a picture on the Touch Bar (the old DFRDisplayKm driver, or WinUSB).</summary>
public interface ITouchBarDisplay : IDisposable
{
    /// <summary>Short name for the log: "winusb" or "dfrdisplaykm".</summary>
    string Kind { get; }
    void Draw(PixelBuffer buffer, int beginX = 0, int beginY = 0);
    void Clear();
}

/// <summary>Raw bulk pipes of the Touch Bar display interface (MI_01). Implemented by <see cref="WinUsbPipes"/>; faked in tests.</summary>
public interface IUsbBulkPipes : IDisposable
{
    void Reset();
    void Write(byte[] data);
    /// <summary>Reads one reply into <paramref name="buffer"/>; returns the byte count, 0 on timeout.</summary>
    int Read(byte[] buffer);
}
