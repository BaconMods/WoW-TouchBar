using Windows.Media.Control;
using WoWTouchBar.Core.Host;
using WoWTouchBar.Core.Input;

namespace WoWTouchBar.App;

/// <summary>
/// "Now playing" from Windows' own media controls (the same info as the volume pop-up): whatever app currently
/// reports media - Spotify, a browser playing YouTube, ... Read-only, polled every second on a background task (and right after a media tap).
/// Titles are never logged.
/// </summary>
internal sealed class WindowsNowPlaying : INowPlayingSource, IMediaControl, IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly FileLog _log;
    private volatile NowPlaying? _current;

    public NowPlaying? Current => _current;

    public WindowsNowPlaying(FileLog log)
    {
        _log = log;
        _ = Task.Run(PollAsync);
    }

    private async Task PollAsync()
    {
        GlobalSystemMediaTransportControlsSessionManager? manager = null;
        var failures = 0;
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _manager = manager;
                var session = manager.GetCurrentSession();
                if (session is null) _current = null;
                else
                {
                    var props = await session.TryGetMediaPropertiesAsync();
                    var playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                    var title = props?.Title ?? "";
                    var artist = props?.Artist ?? "";
                    // Cover art: fetched once per song (not every poll), cropped square, scaled to the bar.
                    var song = $"{session.SourceAppUserModelId}|{title}|{artist}";
                    if (song != _artFor)
                    {
                        _art = await LoadArtAsync(props?.Thumbnail, _log);
                        _artFor = song;
                    }
                    _current = new NowPlaying(title, artist, playing, _art);
                }
                failures = 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _current = null;
                manager = null;
                if (failures++ == 0) _log.Write($"now-playing unavailable: {ex.GetType().Name}");
            }
            // Every second, or sooner when a play / pause / skip was just tapped (Refresh).
            try { await _poke.WaitAsync(TimeSpan.FromSeconds(1), _cts.Token); } catch (OperationCanceledException) { break; }
        }
    }

    private volatile GlobalSystemMediaTransportControlsSessionManager? _manager;
    private readonly SemaphoreSlim _poke = new(0, 1);

    /// <summary>Look again shortly (the player needs a moment to switch), so the play / pause icon follows the tap.</summary>
    public void Refresh() => _ = Task.Delay(300).ContinueWith(_ => { try { _poke.Release(); } catch (SemaphoreFullException) { } });

    // Media control through Windows' media session (not key presses), for the in-game panel. Fire and forget.
    public void PlayPause() => Send(s => s.TryTogglePlayPauseAsync());
    public void Next() => Send(s => s.TrySkipNextAsync());
    public void Previous() => Send(s => s.TrySkipPreviousAsync());

    private void Send(Func<GlobalSystemMediaTransportControlsSession, Windows.Foundation.IAsyncOperation<bool>> command)
    {
        var session = _manager?.GetCurrentSession();
        if (session is null) return;
        _ = Task.Run(async () => { try { await command(session); } catch (Exception ex) { _log.Write($"media control failed: {ex.GetType().Name}"); } });
    }

    private string? _artFor;
    private Core.TouchBar.PixelBuffer? _art;

    /// <summary>Decodes the cover into a <see cref="CoverArt.Size"/> square (centre crop). Null if there is none or it fails.</summary>
    private static async Task<Core.TouchBar.PixelBuffer?> LoadArtAsync(Windows.Storage.Streams.IRandomAccessStreamReference? thumbnail, FileLog log)
    {
        if (thumbnail is null) return null;
        try
        {
            using var stream = await thumbnail.OpenReadAsync();
            var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
            log.Write($"now-playing art {decoder.PixelWidth}x{decoder.PixelHeight}"); // size only, never the title
            if (Environment.GetEnvironmentVariable("WTB_DUMP_ART") == "1") await DumpAsync(decoder, log);
            // Decode at up to 400 px (plenty for a 52 px cover), then let CoverArt.Fit find the cover inside the
            // picture (Spotify adds black bars and its logo underneath) and shrink it.
            var scale = Math.Min(1.0, 400.0 / Math.Max(1, Math.Max(decoder.PixelWidth, decoder.PixelHeight)));
            var w = (uint)Math.Max(1, Math.Round(decoder.PixelWidth * scale));
            var h = (uint)Math.Max(1, Math.Round(decoder.PixelHeight * scale));
            var transform = new Windows.Graphics.Imaging.BitmapTransform
            {
                ScaledWidth = w,
                ScaledHeight = h,
                InterpolationMode = Windows.Graphics.Imaging.BitmapInterpolationMode.Fant,
            };
            var data = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
                Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, transform,
                Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
            return CoverArt.Fit(CoverArt.FromBgra(data.DetachPixelData(), (int)w, (int)h));
        }
        catch (Exception) { return null; } // no art is fine; the box just shows the text
    }

    /// <summary>Developer aid (WTB_DUMP_ART=1): saves the full picture the player sends to logs\captures\art.bmp.</summary>
    private static async Task DumpAsync(Windows.Graphics.Imaging.BitmapDecoder decoder, FileLog log)
    {
        var data = await decoder.GetPixelDataAsync(Windows.Graphics.Imaging.BitmapPixelFormat.Bgra8,
            Windows.Graphics.Imaging.BitmapAlphaMode.Premultiplied, new Windows.Graphics.Imaging.BitmapTransform(),
            Windows.Graphics.Imaging.ExifOrientationMode.RespectExifOrientation, Windows.Graphics.Imaging.ColorManagementMode.DoNotColorManage);
        var dir = Path.Combine(FileLog.DefaultDirectory, "captures");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "art.bmp");
        var full = CoverArt.FromBgra(data.DetachPixelData(), (int)decoder.PixelWidth, (int)decoder.PixelHeight);
        full.SaveBmp(path);
        CoverArt.Fit(full).SaveBmp(Path.Combine(dir, "art-fit.bmp")); // what the bar shows
        log.Write($"now-playing art dumped to {path}");
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
