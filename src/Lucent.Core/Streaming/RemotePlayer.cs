namespace WinampInspiredVisualizer.Streaming;

/// <summary>Which other app's player Lucent shows and controls.</summary>
public enum RemoteApp
{
    /// <summary>None: the player plays Lucent's own music.</summary>
    None,
    Spotify,
    AppleMusic,
}

/// <summary>A song playing in another app, as that app describes it.</summary>
/// <param name="Id">Tells one song from the next, so its artwork is only fetched once.</param>
/// <param name="ArtworkUrl">Where its cover can be downloaded, when the app gives one as a web address.</param>
public sealed record RemoteTrack(string Id, string Title, string Artist, string Album, TimeSpan Duration, string? ArtworkUrl = null);

/// <summary>What another app is playing and where it's up to, as read at <paramref name="ReadAt"/>.</summary>
public sealed record RemoteState(RemoteTrack? Track, bool IsPlaying, TimeSpan Position, DateTime ReadAt)
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

/// <summary>A problem the user can do something about, worded for them (sign in again, open the app, and so on).</summary>
public sealed class RemoteProblem : Exception
{
    public RemoteProblem(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Another app's player (Spotify, Apple Music) that Lucent shows and controls. The sound stays in that
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
