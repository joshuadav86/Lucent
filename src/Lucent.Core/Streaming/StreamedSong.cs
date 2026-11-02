using Lucent.Playlist;

namespace Lucent.Streaming;

/// <summary>
/// A Spotify song in Lucent's queue while it plays: Lucent hands the song to Spotify, follows it there, and takes the queue
/// back when it ends, so a playlist of files and Spotify songs plays straight through, in order or shuffled. The player
/// shows and controls Spotify meanwhile, as it does when Spotify is chosen under Sources, and the visuals hear it the same way.
/// </summary>
public sealed class StreamedSong
{
    // How near its end a playing song counts as finished, so the next one starts as it ends rather than after Spotify's
    // autoplay has begun another. The player checks five times a second.
    private static readonly TimeSpan EndLead = TimeSpan.FromMilliseconds(400);

    private bool _started;
    private bool _ended;
    private TimeSpan _furthest;

    public StreamedSong(PlaylistEntry entry)
    {
        Entry = entry;
        Song = SongOf(entry.Track);
    }

    /// <summary>The queue's row for the song.</summary>
    public PlaylistEntry Entry { get; }

    /// <summary>The song as Spotify knows it.</summary>
    public RemoteTrack Song { get; }

    /// <summary>What Spotify plays it from: the song on its own, so Spotify doesn't carry on through a playlist after it.</summary>
    public RemoteList List => new(null, Song.Title, new[] { Song });

    /// <summary>
    /// True, once, when the song has finished in Spotify: it has played to its end (or as good as), or Spotify has gone on
    /// to another song (its autoplay, or one picked in Spotify itself). Never before Spotify has started it, as Spotify
    /// still names the song it played before for a moment.
    /// </summary>
    public bool HasEnded(RemoteState? state)
    {
        if (_ended || state is null)
        {
            return false;
        }

        var playing = state.Track;
        var ours = playing is not null && Song.IsSameSong(playing);
        if (!_started)
        {
            _started = ours && state.IsPlaying;
            return false;
        }

        if (playing is null)
        {
            // Spotify was closed, or has let go of the song: nothing to follow until it says more.
            return false;
        }

        if (!ours)
        {
            return _ended = true;
        }

        var length = playing.Duration > TimeSpan.Zero ? playing.Duration : Song.Duration;
        if (length <= TimeSpan.Zero)
        {
            return false;
        }

        var position = state.PositionNow;
        if (state.IsPlaying)
        {
            _furthest = position > _furthest ? position : _furthest;
            return _ended = length - position <= EndLead;
        }

        // Spotify stops at the end of a song played on its own (with autoplay off), back at its start.
        return _ended = position < TimeSpan.FromSeconds(1) && length - _furthest <= TimeSpan.FromSeconds(3);
    }

    /// <summary>A Spotify song saved in a playlist or the queue, as Spotify knows it.</summary>
    public static RemoteTrack SongOf(Track track) =>
        new(track.Path[(track.Path.LastIndexOf(':') + 1)..], track.Title, track.Artist, track.Album, track.Duration,
            Uri: track.Path, ThumbnailUrl: track.CoverUrl);

    /// <summary>
    /// A Spotify song as a track for a Lucent playlist or the queue, or null when Spotify hasn't yet said which song it is
    /// (its spotify:track: address comes from Spotify's Web API, a few seconds into a song).
    /// </summary>
    public static Track? TrackOf(RemoteTrack song) =>
        song.Uri is { } uri && Track.IsStreamedPath(uri)
            ? Track.ForSpotify(uri, song.Title, song.Artist, song.Album, song.Duration, song.ListArtUrl)
            : null;
}
