namespace WinampInspiredVisualizer.Streaming;

/// <summary>Which other app's player Lucent shows and controls.</summary>
public enum RemoteApp
{
    /// <summary>None: the player plays Lucent's own music.</summary>
    None,
    Spotify,
    AppleMusic,
    YouTube,
    Tidal,
    SoundCloud,
}

/// <summary>A song playing in another app, as that app describes it.</summary>
/// <param name="Id">Tells one song from the next, so its artwork is only fetched once.</param>
/// <param name="ArtworkUrl">Where its cover can be downloaded, when the app gives one as a web address.</param>
/// <param name="Uri">What the app calls the song when asked to play it (Spotify's spotify:track:…), when that isn't its ID.</param>
public sealed record RemoteTrack(string Id, string Title, string Artist, string Album, TimeSpan Duration, string? ArtworkUrl = null,
    string? Uri = null)
{
    public string DurationText => Duration > TimeSpan.Zero ? Playlist.TimeFormat.Format(Duration) : string.Empty;
}

/// <summary>What another app is playing and where it's up to, as read at <paramref name="ReadAt"/>.</summary>
/// <param name="ListId">The playlist or album the song is playing from, as the app names it, or null when it doesn't say.</param>
/// <param name="Device">The speaker, TV or computer the app is playing on (Spotify Connect's device), or null when the app doesn't say.</param>
/// <param name="Volume">The app's volume there, from 0 to 1, or null when Lucent can't read or change it.</param>
public sealed record RemoteState(RemoteTrack? Track, bool IsPlaying, TimeSpan Position, DateTime ReadAt, string? ListId = null,
    string? Device = null, double? Volume = null)
{
    /// <summary>Where the song is now: the position it was read at, moved on by the time since while it plays.</summary>
    public TimeSpan PositionNow
    {
        get
        {
            var position = IsPlaying ? Position + (DateTime.UtcNow - ReadAt) : Position;
            return Track is { Duration.Ticks: > 0 } track && position > track.Duration ? track.Duration : position;
        }
    }
}

/// <summary>The songs another app is playing through: a playlist, an album, or what's up next.</summary>
/// <param name="Id">The <see cref="RemoteState.ListId"/> it was read for.</param>
/// <param name="Note">Why it isn't the whole playlist, worded for the user, or null when it is.</param>
/// <param name="FollowsSong">True for a list that changes with every song (what's up next), so it's read again when the song changes.</param>
public sealed record RemoteList(string? Id, string Name, IReadOnlyList<RemoteTrack> Tracks, string? Note = null, bool FollowsSong = false);

/// <summary>An app whose volume Lucent can change, on the device it's playing on (<see cref="RemoteState.Volume"/>).</summary>
public interface IRemoteVolume
{
    /// <param name="volume">From 0 to 1.</param>
    Task SetVolumeAsync(double volume, CancellationToken cancel);
}

/// <summary>A problem the user can do something about, worded for them (sign in again, open the app, and so on).</summary>
public sealed class RemoteProblem : Exception
{
    public RemoteProblem(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Another app's player (Spotify, Apple Music, YouTube in a browser) that Lucent shows and controls. The sound stays in that
/// app: Lucent only says what's playing and presses its buttons. The visuals hear it through
/// <c>Audio/SystemAudio</c>, like any other app's sound.
/// </summary>
public interface IRemotePlayer : IDisposable
{
    /// <summary>The app's name, as the menus show it.</summary>
    string Name { get; }

    /// <summary>What's playing now, or null when the app has nothing to play (or isn't open).</summary>
    /// <exception cref="RemoteProblem">When the app can't be reached in a way the user can fix.</exception>
    Task<RemoteState?> ReadAsync(CancellationToken cancel);

    Task PlayAsync(CancellationToken cancel);

    Task PauseAsync(CancellationToken cancel);

    Task NextAsync(CancellationToken cancel);

    Task PreviousAsync(CancellationToken cancel);

    Task SeekAsync(TimeSpan position, CancellationToken cancel);

    /// <summary>The song's cover as an image file's bytes (JPEG or PNG), or null when it has none.</summary>
    Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel);
}

/// <summary>An <see cref="IRemotePlayer"/> that can say which songs it's playing through, and play one of them.</summary>
public interface IRemoteLists
{
    /// <summary>The list <paramref name="state"/>'s song is playing from, or null when the app can't say.</summary>
    Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel);

    /// <summary>Plays <paramref name="track"/>, carrying on through <paramref name="list"/> after it.</summary>
    Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel);
}
