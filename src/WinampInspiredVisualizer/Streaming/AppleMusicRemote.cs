using System.IO;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// The Apple Music app for Windows (or iTunes), through Windows' media controls: every player that shows
/// up in the media panel by the volume flyout tells Windows what's playing and takes its play, pause,
/// skip and seek buttons, and Windows lets other apps read and press those. No Apple sign-in is needed;
/// whoever is signed in to Apple Music is who's playing.
/// </summary>
/// <remarks>
/// Apple's MusicKit only runs on Apple's own platforms, and MusicKit on the web plays in a browser, not
/// the app, so this is how Windows does it. The Mac controls the Music app with AppleScript instead
/// (see the Mac's Streaming/AppleMusicRemote.cs).
/// </remarks>
public sealed class AppleMusicRemote : IRemotePlayer
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private (string Id, IRandomAccessStreamReference Thumbnail)? _cover;

    public string Name => "Apple Music";

    /// <summary>True when Windows can let Lucent see other apps' media controls (Windows 10 1809 and later).</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var session = await SessionAsync()
            ?? throw new RemoteProblem("Open the Apple Music app and play something, then Lucent can show and control it.");

        var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancel);
        var timeline = session.GetTimelineProperties();
        var playing = session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        if (properties is null || string.IsNullOrEmpty(properties.Title))
        {
            return new RemoteState(null, playing, TimeSpan.Zero, DateTime.UtcNow);
        }

        var duration = timeline.EndTime - timeline.StartTime;
        var id = $"{properties.Artist}|{properties.AlbumTitle}|{properties.Title}";
        if (properties.Thumbnail is { } thumbnail)
        {
            _cover = (id, thumbnail);
        }

        // Windows gives the position as of when the app last reported it, which is when it's read from.
        var readAt = timeline.LastUpdatedTime.UtcDateTime;
        if (readAt == default || readAt > DateTime.UtcNow)
        {
            readAt = DateTime.UtcNow;
        }

        var track = new RemoteTrack(id, properties.Title, properties.Artist ?? string.Empty, properties.AlbumTitle ?? string.Empty,
            duration > TimeSpan.Zero ? duration : TimeSpan.Zero);
        return new RemoteState(track, playing, timeline.Position - timeline.StartTime, readAt);
    }

    public Task PlayAsync(CancellationToken cancel) => PressAsync(session => session.TryPlayAsync().AsTask(cancel));

    public Task PauseAsync(CancellationToken cancel) => PressAsync(session => session.TryPauseAsync().AsTask(cancel));

    public Task NextAsync(CancellationToken cancel) => PressAsync(session => session.TrySkipNextAsync().AsTask(cancel));

    public Task PreviousAsync(CancellationToken cancel) => PressAsync(session => session.TrySkipPreviousAsync().AsTask(cancel));

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        PressAsync(session => session.TryChangePlaybackPositionAsync(position.Ticks).AsTask(cancel));

    public async Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel)
    {
        if (_cover is not { } cover || cover.Id != track.Id)
        {
            return null;
        }

        using var stream = await cover.Thumbnail.OpenReadAsync().AsTask(cancel);
        using var reader = stream.AsStreamForRead();
        using var bytes = new MemoryStream();
        await reader.CopyToAsync(bytes, cancel);
        return bytes.Length > 0 ? bytes.ToArray() : null;
    }

    private async Task PressAsync(Func<GlobalSystemMediaTransportControlsSession, Task<bool>> press)
    {
        var session = await SessionAsync()
            ?? throw new RemoteProblem("Open the Apple Music app and play something, then Lucent can control it.");
        if (!await press(session))
        {
            throw new RemoteProblem("Apple Music didn't take that button just now.");
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> SessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return _manager.GetSessions().FirstOrDefault(session => IsAppleMusic(session.SourceAppUserModelId));
    }

    // The Apple Music app is "AppleInc.AppleMusicWin_…!App"; iTunes from Apple's site or the Store has iTunes in its name.
    private static bool IsAppleMusic(string? appId) =>
        appId is not null && (appId.Contains("AppleMusic", StringComparison.OrdinalIgnoreCase)
            || appId.Contains("iTunes", StringComparison.OrdinalIgnoreCase));

    public void Dispose()
    {
        _manager = null;
        _cover = null;
    }
}
