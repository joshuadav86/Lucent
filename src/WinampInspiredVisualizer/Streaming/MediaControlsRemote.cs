using System.IO;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Another app's player through Windows' media controls: every player that shows up in the media panel
/// by the volume flyout tells Windows what's playing and takes its play, pause, skip and seek buttons,
/// and Windows lets other apps read and press those. <see cref="AppleMusicRemote"/> and
/// <see cref="YouTubeRemote"/> say which of those players is theirs.
/// </summary>
public abstract class MediaControlsRemote : IRemotePlayer
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private (string Id, IRandomAccessStreamReference Thumbnail)? _cover;

    public abstract string Name { get; }

    /// <summary>True when Windows can let Lucent see other apps' media controls (Windows 10 1809 and later).</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <summary>What to tell the user when the app isn't playing anything Windows can see.</summary>
    protected abstract string NotOpen { get; }

    /// <summary>The app's player among everything Windows' media controls know about, or null.</summary>
    protected abstract GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions);

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var session = await SessionAsync()
            ?? throw new RemoteProblem($"{NotOpen}, then Lucent can show and control it.");

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
            ?? throw new RemoteProblem($"{NotOpen}, then Lucent can control it.");
        if (!await press(session))
        {
            throw new RemoteProblem($"{Name} didn't take that button just now.");
        }
    }

    private async Task<GlobalSystemMediaTransportControlsSession?> SessionAsync()
    {
        _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return Pick(_manager.GetSessions());
    }

    public void Dispose()
    {
        _manager = null;
        _cover = null;
    }
}
