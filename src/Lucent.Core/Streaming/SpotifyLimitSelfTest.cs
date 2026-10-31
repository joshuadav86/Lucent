using System.Net.Http.Headers;

namespace Lucent.Streaming;

/// <summary>
/// For the self-test: how long Lucent waits when Spotify says to (Retry-After in seconds or as a date), and how seldom
/// it asks Spotify's Web API while nothing plays. Nothing is sent to Spotify.
/// </summary>
public static class SpotifyLimitSelfTest
{
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
        Check("paused overnight", SpotifyRemote.IdleReadEvery(TimeSpan.FromHours(8)), TimeSpan.FromMinutes(1));

        log(failures == 0 ? "Spotify limits: Retry-After and idle pacing OK." : $"Spotify limits: {failures} problem(s).");
        return failures;
    }
}
