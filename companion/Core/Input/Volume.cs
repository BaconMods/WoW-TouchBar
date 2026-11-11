using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace WoWTouchBar.Core.Input;

/// <summary>Media control without key presses (Windows' media session); the tray app provides it.</summary>
public interface IMediaControl
{
    void PlayPause();
    void Next();
    void Previous();
    /// <summary>Check what is playing again soon (after a play / pause / skip), instead of waiting for the next poll.</summary>
    void Refresh();
}

/// <summary>
/// Speaker volume through Windows' own audio API (Core Audio), not a key press, so it is safe while WoW is in front
/// (the in-game "•••" panel). Steps of 4 %.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Volume
{
    public const float Step = 0.04f;

    /// <summary>Next volume level for a step change, clamped to 0..1 (pure; tested).</summary>
    public static float Next(float current, int steps) => Math.Clamp(MathF.Round((current + steps * Step) * 100) / 100, 0f, 1f);

    /// <summary>Changes the default speakers' volume by steps (also unmutes). Returns the new level in %, or null.</summary>
    public static int? Change(int steps) => WithEndpoint(v =>
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out var level));
        var next = Next(level, steps);
        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(next, ref ctx));
        if (steps > 0) v.SetMute(false, ref ctx);
        return (int?)(int)Math.Round(next * 100);
    });

    /// <summary>Sets the default speakers' volume to <paramref name="percent"/> (0-100; also unmutes above 0). Returns it, or null.</summary>
    public static int? Set(int percent) => WithEndpoint(v =>
    {
        var ctx = Guid.Empty;
        var p = Math.Clamp(percent, 0, 100);
        Marshal.ThrowExceptionForHR(v.SetMasterVolumeLevelScalar(p / 100f, ref ctx));
        if (p > 0) v.SetMute(false, ref ctx);
        return (int?)p;
    });

    /// <summary>Current volume in % and whether it is muted, or null if there are no speakers.</summary>
    public static (int Percent, bool Muted)? Read() => WithEndpoint(v =>
    {
        Marshal.ThrowExceptionForHR(v.GetMasterVolumeLevelScalar(out var level));
        Marshal.ThrowExceptionForHR(v.GetMute(out var muted));
        return ((int Percent, bool Muted)?)((int)Math.Round(level * 100), muted);
    });

    /// <summary>Mutes / unmutes the default speakers. Returns true when now muted, or null.</summary>
    public static bool? ToggleMute() => WithEndpoint(v =>
    {
        var ctx = Guid.Empty;
        Marshal.ThrowExceptionForHR(v.GetMute(out var muted));
        Marshal.ThrowExceptionForHR(v.SetMute(!muted, ref ctx));
        return (bool?)!muted;
    });

    private static T? WithEndpoint<T>(Func<IAudioEndpointVolume, T?> use)
    {
        object? enumerator = null, device = null, endpoint = null;
        try
        {
            enumerator = new MMDeviceEnumeratorCom();
            Marshal.ThrowExceptionForHR(((IMMDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0 /* render */, 1 /* multimedia */, out var dev));
            device = dev;
            var iid = typeof(IAudioEndpointVolume).GUID;
            Marshal.ThrowExceptionForHR(dev.Activate(ref iid, 23 /* CLSCTX_ALL */, IntPtr.Zero, out endpoint));
            return use((IAudioEndpointVolume)endpoint);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException) { return default; }
        finally
        {
            foreach (var o in new[] { endpoint, device, enumerator })
                if (o is not null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o);
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    private class MMDeviceEnumeratorCom;

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
