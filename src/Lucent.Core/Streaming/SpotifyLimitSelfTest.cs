using System.Net;
using System.Net.Http.Headers;

namespace Lucent.Streaming;

/// <summary>
/// For the self-test: how long Lucent waits when Spotify says to (Retry-After in seconds or as a date), how seldom it asks
/// Spotify's Web API while nothing plays or while it keeps failing, and that a song chosen while Spotify has asked Lucent
/// to wait goes to the Spotify app here. Nothing is sent to Spotify: a stand-in answers.
/// </summary>
public static class SpotifyLimitSelfTest
{
    private const string Song = "spotify:track:4uLU6hMCjMI75M1A2tKUQC";
    private const string Playlist = "spotify:playlist:37i9dQZF1DXcBWIGoYBM5M";

    /// <summary>Returns the number of problems.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        void Check(string what, TimeSpan got, TimeSpan expected)
        {
            if (got != expected)
            {
                log($"::error::Spotify limits: {what}: got {got}, expected {expected}.");
                failures++;
            }
        }

        var now = new DateTimeOffset(2026, 10, 31, 14, 0, 0, TimeSpan.Zero);
        Check("Retry-After in seconds", SpotifyRemote.RetryAfter(new RetryConditionHeaderValue(TimeSpan.FromSeconds(28_800)), now), TimeSpan.FromHours(8));
        Check("Retry-After as a date", SpotifyRemote.RetryAfter(new RetryConditionHeaderValue(now.AddMinutes(51)), now), TimeSpan.FromMinutes(51));
        Check("Retry-After in the past", SpotifyRemote.RetryAfter(new RetryConditionHeaderValue(now.AddMinutes(-5)), now), TimeSpan.FromSeconds(5));
        Check("no Retry-After", SpotifyRemote.RetryAfter(null, now), TimeSpan.FromSeconds(5));

        Check("just paused", SpotifyRemote.IdleReadEvery(TimeSpan.FromSeconds(10)), TimeSpan.FromSeconds(5));
        Check("paused a few minutes", SpotifyRemote.IdleReadEvery(TimeSpan.FromMinutes(3)), TimeSpan.FromSeconds(15));
        Check("paused half an hour", SpotifyRemote.IdleReadEvery(TimeSpan.FromMinutes(30)), TimeSpan.FromMinutes(1));
        Check("paused overnight", SpotifyRemote.IdleReadEvery(TimeSpan.FromHours(8)), TimeSpan.FromMinutes(5));

        var clientId = Environment.GetEnvironmentVariable("LUCENT_SPOTIFY_CLIENT_ID");
        if (SpotifySignIn.ClientId is null)
        {
            Environment.SetEnvironmentVariable("LUCENT_SPOTIFY_CLIENT_ID", "self-test");
        }

        try
        {
            failures += PacingAsync(log).GetAwaiter().GetResult();
            // Last, as it has Lucent wait for half a minute (only in this run: a wait that short isn't saved).
            failures += ChosenWhileWaitingAsync(log).GetAwaiter().GetResult();
        }
        finally
        {
            Environment.SetEnvironmentVariable("LUCENT_SPOTIFY_CLIENT_ID", clientId);
        }

        log(failures == 0 ? "Spotify limits: Retry-After, pacing and songs chosen during a wait OK." : $"Spotify limits: {failures} problem(s).");
        return failures;
    }

    // Twenty reads in a row, as fast as the app here saying it changed could ask for them, while the Web API keeps failing
    // or saying nothing plays: it is asked once, not twenty times.
    private static async Task<int> PacingAsync(Action<string> log)
    {
        var failures = 0;
        foreach (var (what, playing, answer) in new[]
        {
            ("paused here while Spotify fails", false, HttpStatusCode.BadGateway),
            ("paused here while Spotify says no", false, HttpStatusCode.Forbidden),
            ("playing here while Spotify says nothing plays", true, HttpStatusCode.NoContent),
            ("playing here while Spotify fails", true, HttpStatusCode.BadGateway),
        })
        {
            var asked = 0;
            using var http = new HttpClient(new Answering(request =>
            {
                asked++;
                return new HttpResponseMessage(answer);
            }));
            using var spotify = new SpotifyRemote(http, "self-test", _ => { }, new StandIn(playing, opensOnly: false));
            for (var read = 0; read < 20; read++)
            {
                try
                {
                    await spotify.ReadAsync(CancellationToken.None);
                }
                catch (RemoteProblem)
                {
                    // Shown in the player; the next read is what's checked.
                }
            }

            if (asked > 1)
            {
                log($"::error::Spotify limits: {what}: the Web API was asked {asked} times in 20 quick reads, expected once.");
                failures++;
            }
        }

        return failures;
    }

    // While Spotify has asked Lucent to wait, a song chosen from a playlist plays through the app here, from the playlist,
    // without asking the Web API; where the app can only show it (Windows), the player says to press play there.
    private static async Task<int> ChosenWhileWaitingAsync(Action<string> log)
    {
        var failures = 0;
        SpotifyWait.WaitFor(TimeSpan.FromSeconds(30));
        var track = new RemoteTrack("4uLU6hMCjMI75M1A2tKUQC", "A song", "An artist", "An album", TimeSpan.FromMinutes(3), Uri: Song);
        var list = new RemoteList(Playlist, "A playlist", new[] { track });
        foreach (var opensOnly in new[] { false, true })
        {
            var asked = 0;
            using var http = new HttpClient(new Answering(_ =>
            {
                asked++;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }));
            var app = new StandIn(playing: false, opensOnly);
            using var spotify = new SpotifyRemote(http, "self-test", _ => { }, app);
            string? problem = null;
            try
            {
                await spotify.PlayFromListAsync(list, track, CancellationToken.None);
            }
            catch (RemoteProblem ex)
            {
                problem = ex.Message;
            }

            var how = opensOnly ? "an app that only shows songs" : "an app that plays songs";
            if (asked > 0 || app.Played != (Song, Playlist))
            {
                log($"::error::Spotify limits: a song chosen during a wait, with {how}: asked the Web API {asked} times, gave the app {app.Played}.");
                failures++;
            }

            if (opensOnly != (problem?.Contains("Press play there", StringComparison.Ordinal) == true))
            {
                log($"::error::Spotify limits: a song chosen during a wait, with {how}: the player said \"{problem}\".");
                failures++;
            }
        }

        return failures;
    }

    // Spotify's servers, answering every request the same way (and handing out a sign-in token when asked).
    private sealed class Answering(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel) =>
            Task.FromResult(request.RequestUri?.Host == "accounts.spotify.com"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("""{"access_token":"self-test","expires_in":3600}""") }
                : answer(request));
    }

    // The Spotify app here, with a song loaded, playing or paused. It plays a song it's given, or only shows it, as Windows' does.
    private sealed class StandIn(bool playing, bool opensOnly) : IRemotePlayer, IRemoteLinkPlayer
    {
        public (string Link, string? Context)? Played { get; private set; }

        public string Name => "Spotify";

        public Task<RemoteState?> ReadAsync(CancellationToken cancel) => Task.FromResult<RemoteState?>(new RemoteState(
            new RemoteTrack("loaded", "Another song", "An artist", "An album", TimeSpan.FromMinutes(4)), playing, TimeSpan.FromSeconds(30), DateTime.UtcNow));

        public Task<bool> PlayLinkAsync(string link, string? context, CancellationToken cancel)
        {
            Played = (link, context);
            return Task.FromResult(!opensOnly);
        }

        public Task PlayAsync(CancellationToken cancel) => Task.CompletedTask;

        public Task PauseAsync(CancellationToken cancel) => Task.CompletedTask;

        public Task NextAsync(CancellationToken cancel) => Task.CompletedTask;

        public Task PreviousAsync(CancellationToken cancel) => Task.CompletedTask;

        public Task SeekAsync(TimeSpan position, CancellationToken cancel) => Task.CompletedTask;

        public Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) => Task.FromResult<byte[]?>(null);

        public void Dispose()
        {
        }
    }
}
