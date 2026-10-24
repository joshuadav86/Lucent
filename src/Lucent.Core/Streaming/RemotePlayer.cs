using WinampInspiredVisualizer.Settings;

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
/// <param name="ThumbnailUrl">A small copy of its cover, for the little picture beside it in a list, when the app gives one.</param>
public sealed record RemoteTrack(string Id, string Title, string Artist, string Album, TimeSpan Duration, string? ArtworkUrl = null,
    string? Uri = null, string? ThumbnailUrl = null)
{
    /// <summary>The cover to show beside it in a list: the small copy, or the full one when there's no small one.</summary>
    public string? ListArtUrl => ThumbnailUrl ?? ArtworkUrl;

    public string DurationText => Duration > TimeSpan.Zero ? Playlist.TimeFormat.Format(Duration) : string.Empty;
}

/// <summary>What another app is playing and where it's up to, as read at <paramref name="ReadAt"/>.</summary>
/// <param name="ListId">The playlist or album the song is playing from, as the app names it, or null when it doesn't say.</param>
/// <param name="Device">The speaker, TV or computer the app is playing on (Spotify Connect's device), or null when the app doesn't say.</param>
/// <param name="Volume">The app's volume there, from 0 to 1, or null when Lucent can't read or change it.</param>
/// <param name="IsElsewhere">
/// True when <paramref name="Device"/> isn't this computer (a phone, speaker or TV), so the sound never passes through
/// it and the visuals can't hear it.
/// </param>
/// <param name="Shuffle">Whether the app is shuffling, or null when Lucent can't read or change it.</param>
/// <param name="Repeat">What the app repeats, or null when Lucent can't read or change it.</param>
public sealed record RemoteState(RemoteTrack? Track, bool IsPlaying, TimeSpan Position, DateTime ReadAt, string? ListId = null,
    string? Device = null, double? Volume = null, bool IsElsewhere = false, bool? Shuffle = null, RepeatMode? Repeat = null)
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
/// <param name="Id">The <see cref="RemoteState.ListId"/> it was read for (for what's up next, the playlist or album it plays from, or null).</param>
/// <param name="Note">Why it isn't the whole playlist, worded for the user, or null when it is.</param>
/// <param name="FollowsSong">True for a list that changes with every song (what's up next), so it's read again when the song changes.</param>
public sealed record RemoteList(string? Id, string Name, IReadOnlyList<RemoteTrack> Tracks, string? Note = null, bool FollowsSong = false);

/// <summary>A playlist in the account of an app Lucent controls, listed in the library under PLAYLISTS.</summary>
/// <param name="Uri">What the app calls it when asked to play it (Spotify's spotify:playlist:…).</param>
/// <param name="Songs">How many songs it has, or null when the app doesn't say.</param>
/// <param name="Owner">Who made it, when that isn't the user (a friend, or Spotify itself), or null.</param>
/// <param name="CanAdd">True when the user can add songs to it: Liked Songs, and playlists they made or collaborate on.</param>
public sealed record RemotePlaylist(string Uri, string Name, int? Songs = null, string? Owner = null, bool CanAdd = false)
{
    /// <summary>True for the account's liked songs (Spotify's Liked Songs, Music's Favorite Songs) rather than a playlist.</summary>
    public bool IsLiked => Uri.EndsWith(":collection", StringComparison.Ordinal) || Uri == "music:favorites";
}

/// <summary>An app whose volume Lucent can change, on the device it's playing on (<see cref="RemoteState.Volume"/>).</summary>
public interface IRemoteVolume
{
    /// <param name="volume">From 0 to 1.</param>
    Task SetVolumeAsync(double volume, CancellationToken cancel);
}

/// <summary>An app whose shuffle and repeat Lucent can change (<see cref="RemoteState.Shuffle"/>, <see cref="RemoteState.Repeat"/>).</summary>
public interface IRemoteModes
{
    Task SetShuffleAsync(bool on, CancellationToken cancel);

    /// <exception cref="NotSupportedException">When the app can't repeat that way (Spotify's Mac app can't repeat one song).</exception>
    Task SetRepeatAsync(RepeatMode mode, CancellationToken cancel);
}

/// <summary>An app that can play on one of several devices (Spotify Connect), and can be moved to this computer.</summary>
public interface IRemoteDevices
{
    /// <summary>Moves the app's playback to this computer, carrying on where it was.</summary>
    /// <exception cref="RemoteProblem">When the app isn't open on this computer.</exception>
    Task PlayHereAsync(CancellationToken cancel);
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

    /// <summary>
    /// How long to wait before reading the app again, after reading <paramref name="state"/>. Once a second suits an
    /// app on this computer; a web service that limits how often it's asked waits longer, as the position moves on
    /// by itself between reads. A button pressed is always read straight after.
    /// </summary>
    TimeSpan ReadAgainAfter(RemoteState? state) => TimeSpan.FromSeconds(1);
}

/// <summary>An <see cref="IRemotePlayer"/> that can say which songs it's playing through, and play one of them.</summary>
public interface IRemoteLists
{
    /// <summary>The list <paramref name="state"/>'s song is playing from, or null when the app can't say.</summary>
    Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel);

    /// <summary>Plays <paramref name="track"/>, carrying on through <paramref name="list"/> after it.</summary>
    Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel);
}

/// <summary>An <see cref="IRemotePlayer"/> that knows its songs' web addresses, to share them by (see <see cref="SongLinks"/>).</summary>
public interface IRemoteLinks
{
    /// <summary>The song's own web link (open.spotify.com, a YouTube video), or null when the app doesn't say where it is.</summary>
    string? WebLink(RemoteTrack track);
}

/// <summary>An <see cref="IRemotePlayer"/> that can list the playlists in the user's account, play one, and add songs to them.</summary>
public interface IRemotePlaylists
{
    /// <summary>The account's playlists, in the order the app lists them.</summary>
    /// <exception cref="RemoteProblem">When the app won't list them, in a way the user can fix (signing in again).</exception>
    Task<IReadOnlyList<RemotePlaylist>> PlaylistsAsync(CancellationToken cancel);

    /// <summary>Plays <paramref name="playlist"/> from its start.</summary>
    Task PlayPlaylistAsync(RemotePlaylist playlist, CancellationToken cancel);

    /// <summary>Adds <paramref name="track"/> to the end of <paramref name="playlist"/> (one with <see cref="RemotePlaylist.CanAdd"/>).</summary>
    /// <exception cref="RemoteProblem">When the app won't add it, in a way the user can fix (signing in again).</exception>
    Task AddToPlaylistAsync(RemotePlaylist playlist, RemoteTrack track, CancellationToken cancel);

    /// <summary>True when <paramref name="track"/> is already in <paramref name="playlist"/>, so adding it again would add it twice.</summary>
    /// <exception cref="RemoteProblem">When the app won't say.</exception>
    Task<bool> ContainsAsync(RemotePlaylist playlist, RemoteTrack track, CancellationToken cancel);
}
