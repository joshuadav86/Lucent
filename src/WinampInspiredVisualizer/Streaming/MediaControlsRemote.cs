using System.Diagnostics;
using System.IO;
using NAudio.CoreAudioApi;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Another app's player through Windows' media controls: every player that shows up in the media panel
/// by the volume flyout tells Windows what's playing and takes its play, pause, skip and seek buttons
/// (and shuffle and repeat, for players that offer them there), and Windows lets other apps read and press those. <see cref="AppleMusicRemote"/> and
/// <see cref="YouTubeRemote"/> say which of those players is theirs.
/// </summary>
public abstract class MediaControlsRemote : IRemotePlayer, IRemoteVolume, IRemoteModes
{
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private (string Id, IRandomAccessStreamReference Thumbnail)? _cover;

    public abstract string Name { get; }

    /// <summary>True when Windows can let Lucent see other apps' media controls (Windows 10 1809 and later).</summary>
    public static bool IsSupported => OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763);

    /// <summary>What to tell the user when the app isn't playing anything Windows can see.</summary>
    protected abstract string NotOpen { get; }

    /// <summary>
    /// The processes the app's sound comes from, as Task Manager names them (without .exe), so the visuals can
    /// listen to just that app.
    /// </summary>
    public abstract IReadOnlyList<string> ProcessNames { get; }

    /// <summary>
    /// Why the queue window can't list what's up next (see <see cref="IRemotePlayer.ListLimit"/>): Windows' media
    /// controls only say what's playing now.
    /// </summary>
    public virtual string? ListLimit => $"{Name} only tells Windows the song playing, not what's up next, so Lucent can't show it here.";

    /// <summary>Why the app's playlists can't be listed or added to (see <see cref="IRemotePlayer.PlaylistsLimit"/>).</summary>
    public virtual string? PlaylistsLimit => $"{Name} doesn't let other apps on Windows see or change your playlists.";

    /// <summary>How Windows names the app last read (its AppUserModelID), or null before then.</summary>
    protected string? SourceAppId { get; private set; }

    /// <summary>The app's player among everything Windows' media controls know about, or null.</summary>
    protected abstract GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions);

    public virtual async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var session = await SessionAsync()
            ?? throw new RemoteProblem($"{NotOpen}, then Lucent can show and control it.");

        var properties = await session.TryGetMediaPropertiesAsync().AsTask(cancel);
        var timeline = session.GetTimelineProperties();
        var info = session.GetPlaybackInfo();
        var playing = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        // Only players that offer shuffle and repeat to Windows say whether they're on.
        var shuffle = info?.Controls?.IsShuffleEnabled == true ? info.IsShuffleActive : null;
        RepeatMode? repeat = info?.Controls?.IsRepeatEnabled == true && info.AutoRepeatMode is { } mode
            ? mode switch
            {
                MediaPlaybackAutoRepeatMode.Track => RepeatMode.One,
                MediaPlaybackAutoRepeatMode.List => RepeatMode.All,
                _ => RepeatMode.Off,
            }
            : null;
        if (properties is null || string.IsNullOrEmpty(properties.Title))
        {
            return new RemoteState(null, playing, TimeSpan.Zero, DateTime.UtcNow, Shuffle: shuffle, Repeat: repeat);
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
        var volume = await Task.Run(() => AppVolume(null), cancel);
        return new RemoteState(track, playing, timeline.Position - timeline.StartTime, readAt, Volume: volume, Shuffle: shuffle, Repeat: repeat);
    }

    public virtual Task PlayAsync(CancellationToken cancel) => PressAsync(session => session.TryPlayAsync().AsTask(cancel));

    public virtual Task PauseAsync(CancellationToken cancel) => PressAsync(session => session.TryPauseAsync().AsTask(cancel));

    public virtual Task NextAsync(CancellationToken cancel) => PressAsync(session => session.TrySkipNextAsync().AsTask(cancel));

    public virtual Task PreviousAsync(CancellationToken cancel) => PressAsync(session => session.TrySkipPreviousAsync().AsTask(cancel));

    public virtual Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        PressAsync(session => session.TryChangePlaybackPositionAsync(position.Ticks).AsTask(cancel));

    public Task SetShuffleAsync(bool on, CancellationToken cancel) =>
        PressAsync(session => session.TryChangeShuffleActiveAsync(on).AsTask(cancel));

    public Task SetRepeatAsync(RepeatMode mode, CancellationToken cancel) =>
        PressAsync(session => session.TryChangeAutoRepeatModeAsync(mode switch
        {
            RepeatMode.One => MediaPlaybackAutoRepeatMode.Track,
            RepeatMode.All => MediaPlaybackAutoRepeatMode.List,
            _ => MediaPlaybackAutoRepeatMode.None,
        }).AsTask(cancel));

    public Task SetVolumeAsync(double volume, CancellationToken cancel) => Task.Run(() => AppVolume((float)Math.Clamp(volume, 0, 1)), cancel);

    /// <summary>
    /// The app's volume in Windows' volume mixer (its share of the speakers' volume), or null when it has no sound
    /// there; set to <paramref name="set"/> first, unless that's null. A browser's sound can come from a helper
    /// process, which has the browser's name too.
    /// </summary>
    protected double? AppVolume(float? set)
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var speakers = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = speakers.AudioSessionManager.Sessions;
            double? volume = null;
            for (var i = 0; i < sessions.Count; i++)
            {
                using var session = sessions[i];
                if (!IsApp((int)session.GetProcessID))
                {
                    continue;
                }

                var mixer = session.SimpleAudioVolume;
                if (set is { } level)
                {
                    mixer.Volume = level;
                    mixer.Mute = false;
                }

                volume ??= mixer.Mute ? 0 : mixer.Volume;
            }

            return volume;
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Couldn't reach {Name}'s volume: {ex.Message}");
            return null;
        }
    }

    private bool IsApp(int processId)
    {
        if (processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            return ProcessNames.Contains(process.ProcessName, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            // Gone already.
            return false;
        }
    }

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
        var session = Pick(_manager.GetSessions());
        if (session is not null)
        {
            SourceAppId = session.SourceAppUserModelId;
        }

        return session;
    }

    public void Dispose()
    {
        _manager = null;
        _cover = null;
    }
}
