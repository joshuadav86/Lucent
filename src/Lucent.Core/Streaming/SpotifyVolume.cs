namespace Lucent.Streaming;

/// <summary>
/// Converts between Lucent's volume bar and Spotify's own volume slider, so the bar sounds the same with Spotify as
/// it does with Lucent's own music and with other apps.
/// </summary>
/// <remarks>
/// Lucent's bar is a straight multiplier on the sound (45% is about 7 dB down), as are Windows' volume mixer and a
/// browser tab's volume. Spotify's slider is much steeper: measured on Windows on 2026-10-31, Spotify at 44% was about
/// 27 dB down, close to nothing over speakers. That fits Spotify's level being its slider to the fourth power (44% to
/// the fourth is 28 dB down), so Lucent sets Spotify to the fourth root of the bar: the bar at 45% asks Spotify for
/// 82%, which Spotify plays about 7 dB down, as Lucent would. Used for Spotify's Web API volume (every device) and the
/// Mac's AppleScript volume, which are both Spotify's slider. Not for Windows' mixer level, which is already straight.
/// </remarks>
public static class SpotifyVolume
{
    /// <summary>Spotify's slider (0 to 1) for a place on Lucent's volume bar (0 to 1).</summary>
    public static double ToSpotify(double bar) => Math.Pow(Math.Clamp(bar, 0, 1), 0.25);

    /// <summary>The place on Lucent's volume bar (0 to 1) for Spotify's slider (0 to 1).</summary>
    public static double FromSpotify(double spotify) => Math.Pow(Math.Clamp(spotify, 0, 1), 4);

    /// <summary>Spotify's slider as the whole percent its API and AppleScript take.</summary>
    public static int ToSpotifyPercent(double bar) => (int)Math.Round(ToSpotify(bar) * 100);

    /// <summary>
    /// For the self-test: the bar's mid places come out as loud with Spotify as with Lucent, and the ends stay the
    /// ends. Returns the number of problems.
    /// </summary>
    public static int SelfTest(Action<string> log)
    {
        var failures = 0;
        void Check(string what, bool ok)
        {
            if (!ok)
            {
                log($"::error::Spotify volume: {what}.");
                failures++;
            }
        }

        Check("0% stays silent", ToSpotifyPercent(0) == 0);
        Check("100% stays full", ToSpotifyPercent(1) == 100);
        Check("45% asks Spotify for 82%", ToSpotifyPercent(0.45) == 82);
        foreach (var bar in new[] { 0.05, 0.2, 0.45, 0.67, 0.9 })
        {
            // What Spotify plays (its slider to the fourth) against Lucent's own straight bar, in dB.
            var spotifyDb = 80 * Math.Log10(ToSpotify(bar));
            var lucentDb = 20 * Math.Log10(bar);
            Check($"{bar:P0} plays {spotifyDb:0.0} dB with Spotify but {lucentDb:0.0} dB in Lucent", Math.Abs(spotifyDb - lucentDb) < 0.01);
            Check($"{bar:P0} doesn't read back the same", Math.Abs(FromSpotify(ToSpotify(bar)) - bar) < 1e-9);
        }

        log(failures == 0 ? "Spotify volume: bar matches Spotify's slider curve." : $"Spotify volume: {failures} problem(s).");
        return failures;
    }
}
