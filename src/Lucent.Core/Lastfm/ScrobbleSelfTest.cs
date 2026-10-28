using System.Security.Cryptography;
using System.Text;

namespace Lucent.Lastfm;

/// <summary>
/// Checks Last.fm's rules for when a song counts, on a made-up clock with a stand-in for Last.fm: half the song or
/// four minutes, not songs under 30 seconds, not while paused, again when played again, a minute for radio songs,
/// scrobbles kept and sent later when Last.fm can't be reached, and hearts as loves. Also checks calls are signed as Last.fm checks them.
/// Both apps run it in their self-tests.
/// </summary>
public static class ScrobbleSelfTest
{
    private static readonly DateTime Start = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(200);

    /// <summary>Runs the checks, reporting each through <paramref name="log"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        void Check(bool ok, string what)
        {
            if (!ok)
            {
                log($"Scrobbling: {what}");
                failures++;
            }
        }

        // A 3-minute song counts after 90 seconds, not before, with the time it started.
        {
            var (scrobbler, lastfm, clock) = Make();
            var song = new ScrobbleSong("Boards of Canada", "Roygbiv", "Music Has the Right to Children", TimeSpan.FromMinutes(3));
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(89));
            Check(lastfm.Sent.Count == 0, "a 3-minute song counted before half of it was heard.");
            Check(lastfm.NowPlaying.Count == 1, $"now playing was sent {lastfm.NowPlaying.Count} times, not once.");
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(2), from: TimeSpan.FromSeconds(89));
            Check(lastfm.Sent.Count == 1, $"a 3-minute song heard for 91 s was scrobbled {lastfm.Sent.Count} times, not once.");
            Check(lastfm.Sent.FirstOrDefault()?.StartedAt == new DateTimeOffset(Start).ToUnixTimeSeconds(),
                "the scrobble doesn't carry the time the song started.");
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(80), from: TimeSpan.FromSeconds(91));
            Check(lastfm.Sent.Count == 1, "a song counted twice in one play.");
        }

        // A 10-minute song counts at four minutes; a 20-second one never.
        {
            var (scrobbler, lastfm, clock) = Make();
            clock.Play(scrobbler, new ScrobbleSong("Orbital", "Halcyon", Duration: TimeSpan.FromMinutes(10)), TimeSpan.FromSeconds(241));
            Check(lastfm.Sent.Count == 1, "a 10-minute song didn't count after four minutes.");
            clock.Play(scrobbler, new ScrobbleSong("Aphex Twin", "Btoum-Roumada", Duration: TimeSpan.FromSeconds(20)), TimeSpan.FromSeconds(20));
            Check(lastfm.Sent.Count == 1, "a 20-second song counted.");
        }

        // Paused time doesn't count, and now playing is sent again when it carries on.
        {
            var (scrobbler, lastfm, clock) = Make();
            var song = new ScrobbleSong("Bonobo", "Kerala", Duration: TimeSpan.FromMinutes(4));
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(60));
            clock.Play(scrobbler, song, TimeSpan.FromMinutes(5), from: TimeSpan.FromSeconds(60), playing: false);
            Check(lastfm.Sent.Count == 0, "time spent paused counted.");
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(61), from: TimeSpan.FromSeconds(60));
            Check(lastfm.Sent.Count == 1, "a song paused partway didn't count once the rest of its half was heard.");
            Check(lastfm.NowPlaying.Count == 2, $"now playing was sent {lastfm.NowPlaying.Count} times, not again after the pause.");
        }

        // Played again from the start (repeat one) counts again.
        {
            var (scrobbler, lastfm, clock) = Make();
            var song = new ScrobbleSong("Tycho", "Awake", Duration: TimeSpan.FromMinutes(2));
            clock.Play(scrobbler, song, TimeSpan.FromMinutes(2));
            clock.Play(scrobbler, song, TimeSpan.FromMinutes(2));
            Check(lastfm.Sent.Count == 2, $"a song played twice in a row was scrobbled {lastfm.Sent.Count} times, not twice.");
        }

        // A radio song (length unknown, not chosen by the listener) counts after a minute.
        {
            var (scrobbler, lastfm, clock) = Make();
            var song = new ScrobbleSong("Carbon Based Lifeforms", "Photosynthesis", ChosenByUser: false);
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(59), position: false);
            Check(lastfm.Sent.Count == 0, "a radio song counted before a minute.");
            clock.Play(scrobbler, song, TimeSpan.FromSeconds(2), position: false);
            Check(lastfm.Sent.Count == 1 && !lastfm.Sent[0].ChosenByUser, "a radio song didn't count after a minute, as not chosen by the listener.");
        }

        // A song with no artist isn't scrobbled.
        {
            var (scrobbler, lastfm, clock) = Make();
            clock.Play(scrobbler, new ScrobbleSong("", "Track 01", Duration: TimeSpan.FromMinutes(3)), TimeSpan.FromMinutes(3));
            Check(lastfm.Sent.Count == 0 && lastfm.NowPlaying.Count == 0, "a song with no artist went to Last.fm.");
        }

        // Offline: scrobbles wait, then go together in one batch once Last.fm answers again.
        {
            var (scrobbler, lastfm, clock) = Make();
            lastfm.Offline = true;
            clock.Play(scrobbler, new ScrobbleSong("Moby", "Porcelain", Duration: TimeSpan.FromMinutes(2)), TimeSpan.FromMinutes(2));
            clock.Play(scrobbler, new ScrobbleSong("Air", "La Femme d'Argent", Duration: TimeSpan.FromMinutes(2)), TimeSpan.FromMinutes(2));
            Check(scrobbler.Waiting == 2 && scrobbler.Problem is not null, $"offline, {scrobbler.Waiting} scrobbles were waiting, not 2, or no problem was shown.");
            lastfm.Offline = false;
            clock.Play(scrobbler, new ScrobbleSong("Massive Attack", "Teardrop", Duration: TimeSpan.FromMinutes(5)), TimeSpan.FromMinutes(6));
            Check(scrobbler.Waiting == 0 && lastfm.Sent.Count == 3 && lastfm.Batches == 1,
                $"back online, {lastfm.Sent.Count} of 3 scrobbles went in {lastfm.Batches} batches, with {scrobbler.Waiting} left waiting.");
        }

        // A sign-in taken back on Last.fm's site signs Lucent out.
        {
            var (scrobbler, lastfm, clock) = Make();
            var signedOut = false;
            scrobbler.SignedOut += (_, _) => signedOut = true;
            lastfm.SignInGone = true;
            clock.Play(scrobbler, new ScrobbleSong("Röyksopp", "Eple", Duration: TimeSpan.FromMinutes(2)), TimeSpan.FromMinutes(2));
            Check(signedOut, "a sign-in Last.fm no longer takes didn't sign Lucent out.");
        }

        // A heart in Lucent loves the song on Last.fm, and taking it off takes the love back.
        {
            var (scrobbler, lastfm, _) = Make();
            scrobbler.Love("Bicep", "Glue", love: true);
            scrobbler.Love("Bicep", "Glue", love: false);
            Check(lastfm.Loves.SequenceEqual(new[] { ("Bicep", "Glue", true), ("Bicep", "Glue", false) }), "hearting a song didn't love it on Last.fm and back.");
        }

        // Signed as Last.fm checks it: fields by name, format left out, the secret on the end, MD5 in lowercase hex.
        var fields = new Dictionary<string, string> { ["token"] = "T0k", ["method"] = "auth.getSession", ["api_key"] = "K3y", ["format"] = "json" };
        var expected = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes("api_keyK3ymethodauth.getSessiontokenT0ks3cret")));
        Check(LastfmClient.Sign(fields, "s3cret") == expected, "calls aren't signed the way Last.fm checks them.");

        log(failures == 0 ? "Scrobbling: Last.fm's rules for when a song counts are kept." : $"Scrobbling: {failures} problem(s).");
        return failures;
    }

    private static (Scrobbler, FakeLastfm, Clock) Make()
    {
        var lastfm = new FakeLastfm();
        var clock = new Clock();
        return (new Scrobbler(lastfm, clock: () => clock.Now), lastfm, clock);
    }

    private sealed class Clock
    {
        public DateTime Now { get; private set; } = Start;

        /// <summary>Plays <paramref name="song"/> for <paramref name="time"/>, looking five times a second as the player does.</summary>
        public void Play(Scrobbler scrobbler, ScrobbleSong song, TimeSpan time, TimeSpan from = default, bool playing = true, bool position = true)
        {
            for (var t = TimeSpan.Zero; t <= time; t += Tick)
            {
                scrobbler.Observe(new Listening(song, playing, position ? from + (playing ? t : TimeSpan.Zero) : null));
                Now += Tick;
            }
        }
    }

    private sealed class FakeLastfm : IScrobbleService
    {
        public List<ScrobbleSong> NowPlaying { get; } = new();

        public List<Scrobble> Sent { get; } = new();

        public int Batches { get; private set; }

        public bool Offline { get; set; }

        public bool SignInGone { get; set; }

        public Task NowPlayingAsync(ScrobbleSong song, CancellationToken cancel)
        {
            NowPlaying.Add(song);
            return Task.CompletedTask;
        }

        public List<(string Artist, string Title, bool Love)> Loves { get; } = new();

        public Task LoveAsync(string artist, string title, bool love, CancellationToken cancel)
        {
            Loves.Add((artist, title, love));
            return Task.CompletedTask;
        }

        public Task ScrobbleAsync(IReadOnlyList<Scrobble> scrobbles, CancellationToken cancel)
        {
            if (SignInGone)
            {
                throw new LastfmProblem("Signed out.", 9);
            }

            if (Offline)
            {
                throw new HttpRequestException("Offline.");
            }

            Batches++;
            Sent.AddRange(scrobbles);
            return Task.CompletedTask;
        }
    }
}
