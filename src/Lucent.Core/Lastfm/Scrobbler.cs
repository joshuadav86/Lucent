using System.IO;
using System.Text.Json;
using Lucent.Settings;
using Lucent.Streaming;

namespace Lucent.Lastfm;

/// <summary>A song as Last.fm is told about it.</summary>
/// <param name="Duration">How long it is, or zero when nobody knows (a song on the radio).</param>
/// <param name="ChosenByUser">False for a song a radio station picked.</param>
public sealed record ScrobbleSong(string Artist, string Title, string Album = "", TimeSpan Duration = default, string AlbumArtist = "",
    bool ChosenByUser = true)
{
    /// <summary>
    /// The song another app is playing, or null when it may not be a song. On YouTube, and on Windows any site in a
    /// browser (Windows doesn't say which site), only songs with an album count: YouTube Music's. A plain YouTube
    /// video's "artist" is its channel, and its title is often "Artist - Song (Official Video)".
    /// </summary>
    public static ScrobbleSong? FromApp(RemoteApp app, RemoteTrack track) =>
        app == RemoteApp.YouTube && track.Album.Trim().Length == 0 ? null : new ScrobbleSong(track.Artist, track.Title, track.Album, track.Duration);

    /// <summary>
    /// Whether <paramref name="other"/> is the same song, by title and artist. One may name every artist ("Ulrich Schnauss,
    /// Mark Peters") where the other names only the first, and the album can turn up a moment after the rest, as
    /// a music app reads its song from two places.
    /// </summary>
    public bool IsSameSong(ScrobbleSong other) =>
        string.Equals(Title.Trim(), other.Title.Trim(), StringComparison.OrdinalIgnoreCase)
        && (Artist.Contains(other.Artist.Trim(), StringComparison.OrdinalIgnoreCase)
            || other.Artist.Contains(Artist.Trim(), StringComparison.OrdinalIgnoreCase));
}

/// <summary>What's playing, as the player sees it each time it looks.</summary>
/// <param name="Position">Where the song is up to, or null when that isn't known (the radio).</param>
public readonly record struct Listening(ScrobbleSong Song, bool IsPlaying, TimeSpan? Position);

/// <summary>A song that was listened to enough to count, waiting to go to Last.fm.</summary>
/// <param name="StartedAt">When it started playing, as Unix seconds (UTC), which is how Last.fm wants it.</param>
public sealed record Scrobble(string Artist, string Title, string Album, string AlbumArtist, int DurationSeconds, long StartedAt,
    bool ChosenByUser);

/// <summary>Where scrobbles go: Last.fm itself (<see cref="LastfmClient"/>), or a stand-in for the self-test.</summary>
public interface IScrobbleService
{
    Task NowPlayingAsync(ScrobbleSong song, CancellationToken cancel);

    /// <summary>Sends up to <see cref="Scrobbler.MaxBatch"/> scrobbles. Throws <see cref="LastfmProblem"/> when Last.fm says no.</summary>
    Task ScrobbleAsync(IReadOnlyList<Scrobble> scrobbles, CancellationToken cancel);
}

/// <summary>
/// Follows what the player plays and tells Last.fm: "now playing" when a song starts or carries on after a pause,
/// and a scrobble once it has been heard for half its length or four minutes, whichever comes first, as Last.fm
/// asks. Songs shorter than 30 seconds don't count. Only time spent playing counts, so pausing, or skipping
/// ahead, doesn't bring the scrobble closer. A song played again from the start (on repeat, say) counts again.
/// </summary>
/// <remarks>
/// The player calls <see cref="Observe"/> a few times a second, from its own thread. Scrobbles that can't be sent
/// (offline, or Last.fm is down) wait in lastfm-scrobbles.json and are sent later, for up to two weeks, which is as
/// far back as Last.fm takes them.
/// </remarks>
public sealed class Scrobbler
{
    /// <summary>The most scrobbles Last.fm takes at once.</summary>
    public const int MaxBatch = 50;

    private const int MostWaiting = 5000;

    private static readonly TimeSpan ShortestSong = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(4);

    // A radio song's length isn't known, and Lucent usually tunes in partway through, so it counts after a minute.
    private static readonly TimeSpan UnknownLengthWait = TimeSpan.FromMinutes(1);

    // The most time one look can add: more than this between looks means the computer was asleep or Lucent was stuck.
    private static readonly TimeSpan LongestStep = TimeSpan.FromSeconds(5);

    // Going back this far to near the start of the same song is playing it again.
    private static readonly TimeSpan RestartJump = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RestartWithin = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan RetryAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan KeepFor = TimeSpan.FromDays(14);

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly IScrobbleService _service;
    private readonly string? _waitingPath;
    private readonly Func<DateTime> _clock;
    private readonly List<Scrobble> _waiting;

    private ScrobbleSong? _song;
    private DateTime _startedAt;
    private TimeSpan _heard;
    private TimeSpan? _lastPosition;
    private bool _wasPlaying;
    private bool _scrobbled;
    private bool _toldNowPlaying;
    private DateTime _lastLook;
    private bool _sending;
    private DateTime _sendAfter;

    /// <param name="waitingPath">Where unsent scrobbles are kept, or null to keep them only in memory.</param>
    /// <param name="clock">The time now in UTC, so the self-test can move it along; the real clock when null.</param>
    public Scrobbler(IScrobbleService service, string? waitingPath = null, Func<DateTime>? clock = null)
    {
        _service = service;
        _waitingPath = waitingPath;
        _clock = clock ?? (() => DateTime.UtcNow);
        _waiting = Load(waitingPath);
    }

    /// <summary>The file in Lucent's settings folder for scrobbles waiting to be sent.</summary>
    public static string WaitingFile => Path.Combine(AppDataFolder.Path, "lastfm-scrobbles.json");

    /// <summary>Raised when Last.fm no longer accepts the sign-in (taken back on Last.fm's site), so the account is forgotten.</summary>
    public event EventHandler? SignedOut;

    /// <summary>Raised when <see cref="Status"/> may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>How many scrobbles were sent since Lucent started.</summary>
    public int Sent { get; private set; }

    /// <summary>The last song sent, "Artist – Title", or null before the first.</summary>
    public string? LastSent { get; private set; }

    /// <summary>How many scrobbles are waiting to be sent.</summary>
    public int Waiting => _waiting.Count;

    /// <summary>What went wrong the last time, worded for the user, or null when the last send went through.</summary>
    public string? Problem { get; private set; }

    /// <summary>One line for the Last.fm tab saying how scrobbling is going.</summary>
    public string Status =>
        Problem is not null ? Problem
        : _waiting.Count > 0 ? $"{Count(_waiting.Count)} waiting to be sent."
        : LastSent is not null ? $"Last scrobbled: {LastSent}. {Count(Sent)} since Lucent started."
        : "Songs are scrobbled once you've heard half of them, or four minutes.";

    private static string Count(int scrobbles) => scrobbles == 1 ? "1 scrobble" : $"{scrobbles} scrobbles";

    /// <summary>Looks at what's playing now, or null when nothing is.</summary>
    public void Observe(Listening? now)
    {
        var at = _clock();
        var step = _lastLook == default ? TimeSpan.Zero : at - _lastLook;
        step = step < TimeSpan.Zero ? TimeSpan.Zero : step > LongestStep ? LongestStep : step;
        _lastLook = at;

        if (now is { Song: var song } listening && song.Artist.Trim().Length > 0 && song.Title.Trim().Length > 0)
        {
            if (_song is null || !song.IsSameSong(_song) || PlayedAgain(listening.Position))
            {
                Start(listening, at);
            }
            else
            {
                if (_wasPlaying && listening.IsPlaying)
                {
                    _heard += step;
                }

                // The same song, perhaps with its album now known.
                if (song.Album.Length > _song.Album.Length || (song.Duration > TimeSpan.Zero && _song.Duration <= TimeSpan.Zero))
                {
                    _song = song;
                }
            }

            _lastPosition = listening.Position;
            if (listening.IsPlaying && !_toldNowPlaying)
            {
                _toldNowPlaying = true;
                _ = TellNowPlayingAsync(_song!);
            }
            else if (!listening.IsPlaying)
            {
                // Told again when it carries on, as Last.fm stops showing it after a while.
                _toldNowPlaying = false;
            }

            _wasPlaying = listening.IsPlaying;
            if (!_scrobbled && Counts(_song!, _heard))
            {
                _scrobbled = true;
                Add(_song!);
            }
        }
        else
        {
            // Nothing playing (or a song with no artist to scrobble under). The song is kept, so if the same one
            // comes back where it left off, as a music app's song can blink out for a moment, it's the same play.
            _wasPlaying = false;
            _toldNowPlaying = false;
        }

        if (_waiting.Count > 0 && !_sending && at >= _sendAfter)
        {
            _ = SendAsync();
        }
    }

    /// <summary>Forgets the scrobbles waiting to be sent, when the user signs out: they're for that account.</summary>
    public void Forget()
    {
        _waiting.Clear();
        Save();
    }

    /// <summary>Whether the same song went back to near its start: played again, on repeat or by choice.</summary>
    private bool PlayedAgain(TimeSpan? position) =>
        position is { } now && _lastPosition is { } before && now < RestartWithin && before - now > RestartJump;

    private void Start(Listening listening, DateTime at)
    {
        _song = listening.Song;
        var position = listening.Position ?? TimeSpan.Zero;
        _startedAt = position > TimeSpan.Zero ? at - position : at;
        _heard = TimeSpan.Zero;
        _scrobbled = false;
        _toldNowPlaying = false;
        _wasPlaying = listening.IsPlaying;
    }

    private static bool Counts(ScrobbleSong song, TimeSpan heard)
    {
        if (song.Duration <= TimeSpan.Zero)
        {
            return heard >= UnknownLengthWait;
        }

        if (song.Duration < ShortestSong)
        {
            return false;
        }

        var half = TimeSpan.FromTicks(song.Duration.Ticks / 2);
        return heard >= (half < LongestWait ? half : LongestWait);
    }

    private async Task TellNowPlayingAsync(ScrobbleSong song)
    {
        try
        {
            await _service.NowPlayingAsync(song, CancellationToken.None);
        }
        catch (LastfmProblem problem) when (problem.SignInGone)
        {
            ForgetSignIn(problem);
        }
        catch (Exception ex) when (ex is LastfmProblem or HttpRequestException or TaskCanceledException or IOException)
        {
            // Now playing is only for show; the scrobble is what counts, and it waits and tries again.
        }
    }

    private void Add(ScrobbleSong song)
    {
        _waiting.Add(new Scrobble(song.Artist.Trim(), song.Title.Trim(), song.Album.Trim(), song.AlbumArtist.Trim(),
            (int)Math.Round(song.Duration.TotalSeconds), new DateTimeOffset(DateTime.SpecifyKind(_startedAt, DateTimeKind.Utc)).ToUnixTimeSeconds(),
            song.ChosenByUser));
        if (_waiting.Count > MostWaiting)
        {
            _waiting.RemoveRange(0, _waiting.Count - MostWaiting);
        }

        // A new scrobble is a good moment to try the waiting ones again.
        _sendAfter = default;
        Save();
    }

    private async Task SendAsync()
    {
        _sending = true;
        try
        {
            var tooOld = new DateTimeOffset(DateTime.SpecifyKind(_clock() - KeepFor, DateTimeKind.Utc)).ToUnixTimeSeconds();
            if (_waiting.RemoveAll(scrobble => scrobble.StartedAt < tooOld) > 0)
            {
                Save();
            }

            while (_waiting.Count > 0)
            {
                var batch = _waiting.Take(MaxBatch).ToList();
                try
                {
                    await _service.ScrobbleAsync(batch, CancellationToken.None);
                }
                catch (LastfmProblem problem) when (problem.Unsendable)
                {
                    // Last.fm will never take these (it found something wrong with a song's details), so they're dropped
                    // rather than holding up the rest.
                }

                _waiting.RemoveRange(0, batch.Count);
                Save();
                Sent += batch.Count;
                LastSent = $"{batch[^1].Artist} – {batch[^1].Title}";
                Problem = null;
            }
        }
        catch (LastfmProblem problem) when (problem.SignInGone)
        {
            ForgetSignIn(problem);
        }
        catch (Exception ex) when (ex is LastfmProblem or HttpRequestException or TaskCanceledException or IOException)
        {
            _sendAfter = _clock() + RetryAfter;
            Problem = ex is LastfmProblem { Message: var message }
                ? $"{message} {Count(_waiting.Count)} waiting to be sent."
                : $"Couldn't reach Last.fm. {Count(_waiting.Count)} waiting, sent when it's back.";
        }
        finally
        {
            _sending = false;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ForgetSignIn(LastfmProblem problem)
    {
        Problem = problem.Message;
        _sendAfter = DateTime.MaxValue;
        SignedOut?.Invoke(this, EventArgs.Empty);
    }

    private static List<Scrobble> Load(string? path)
    {
        try
        {
            return path is not null && File.Exists(path)
                ? JsonSerializer.Deserialize<List<Scrobble>>(File.ReadAllText(path), JsonOptions) ?? new()
                : new();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            return new();
        }
    }

    private void Save()
    {
        if (_waitingPath is null)
        {
            return;
        }

        try
        {
            if (_waiting.Count == 0)
            {
                File.Delete(_waitingPath);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_waitingPath)!);
            var temp = _waitingPath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_waiting, JsonOptions));
            File.Move(temp, _waitingPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Kept in memory: they're still sent while Lucent runs.
        }
    }
}
