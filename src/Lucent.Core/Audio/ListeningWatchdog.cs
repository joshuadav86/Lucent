using System.Diagnostics;

namespace Lucent.Audio;

/// <summary>
/// Notices when listening to another app has quietly stopped working, so it can be started again without the user
/// switching it off and on. Windows and macOS can leave a loopback or tap running but hearing nothing (the app's sound
/// moved to a new audio session, the audio service restarted, the output changed) without saying so. The sign is the
/// controlled app saying it's playing here while nothing has been heard for a few seconds.
/// </summary>
/// <remarks>
/// The app could also really be silent (a quiet intro, its own volume at zero, or playing out of an output that isn't
/// the one listened to), so each restart waits twice as long as the one before, up to a minute, until sound is heard.
/// A restart only costs a moment of the visuals, which are hearing nothing anyway.
/// </remarks>
public sealed class ListeningWatchdog
{
    /// <summary>How long the app can play with nothing heard before listening starts again.</summary>
    public static readonly TimeSpan FirstWait = TimeSpan.FromSeconds(5);

    /// <summary>The longest gap between restarts while the app stays silent.</summary>
    public static readonly TimeSpan LongestWait = TimeSpan.FromMinutes(1);

    private readonly Func<TimeSpan> _clock;
    private TimeSpan _startedAt;
    private TimeSpan _wait = FirstWait;

    public ListeningWatchdog()
        : this(Clock())
    {
    }

    /// <summary>For tests: <paramref name="clock"/> says how much time has passed.</summary>
    public ListeningWatchdog(Func<TimeSpan> clock)
    {
        _clock = clock;
        _startedAt = clock();
    }

    /// <summary>How many times <see cref="ShouldRestart"/> has said to restart since sound was last heard.</summary>
    public int RestartsSinceSound { get; private set; }

    /// <summary>Call whenever listening starts, for any reason, so it has a fair chance to hear something first.</summary>
    public void Started() => _startedAt = _clock();

    /// <summary>
    /// Called about once a second. <paramref name="expectSound"/>: the app says it's playing on this computer.
    /// <paramref name="heardNothing"/>: nothing has been heard for <see cref="FirstWait"/>. True means start listening again
    /// now (and call <see cref="Started"/> once it has).
    /// </summary>
    public bool ShouldRestart(bool expectSound, bool heardNothing)
    {
        if (!heardNothing)
        {
            // Working: the next stall gets the short wait again.
            _wait = FirstWait;
            RestartsSinceSound = 0;
            return false;
        }

        if (!expectSound)
        {
            return false;
        }

        var now = _clock();
        if (now - _startedAt < _wait)
        {
            return false;
        }

        _startedAt = now;
        RestartsSinceSound++;
        _wait = _wait * 2 > LongestWait ? LongestWait : _wait * 2;
        return true;
    }

    private static Func<TimeSpan> Clock()
    {
        var watch = Stopwatch.StartNew();
        return () => watch.Elapsed;
    }
}

/// <summary>The apps' --self-test check of <see cref="ListeningWatchdog"/>'s timing.</summary>
public static class ListeningWatchdogSelfTest
{
    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log)
    {
        var problems = 0;
        void Check(bool good, string problem)
        {
            if (!good)
            {
                problems++;
                log("::error::" + problem);
            }
        }

        var now = TimeSpan.Zero;
        var watchdog = new ListeningWatchdog(() => now);

        // Seconds 1 to 200 with the app playing and nothing heard (from second 5 on): when it restarts.
        var restarts = new List<int>();
        for (var second = 1; second <= 200; second++)
        {
            now = TimeSpan.FromSeconds(second);
            if (watchdog.ShouldRestart(expectSound: true, heardNothing: second >= 5))
            {
                restarts.Add(second);
                watchdog.Started();
            }
        }

        Check(restarts.Count > 0 && restarts[0] == 5, $"A stalled listening first restarted at {(restarts.Count > 0 ? restarts[0] : -1)} s, not 5 s.");
        Check(restarts.SequenceEqual(new[] { 5, 15, 35, 75, 135, 195 }),
            $"Restarts while the app stays silent came at {string.Join(", ", restarts)} s, not 5, 15, 35, 75, 135, 195 s.");

        // Paused (not expected to play): never restarts.
        watchdog = new ListeningWatchdog(() => now);
        var pausedRestarts = 0;
        for (var second = 0; second < 180; second++)
        {
            now += TimeSpan.FromSeconds(1);
            pausedRestarts += watchdog.ShouldRestart(expectSound: false, heardNothing: true) ? 1 : 0;
        }

        Check(pausedRestarts == 0, $"Listening restarted {pausedRestarts} times while the app was paused.");

        // Sound heard again resets the wait: the next stall restarts after the short wait, not the long one.
        // (Without the reset, the restart after the first would wait 10 s.)
        watchdog = new ListeningWatchdog(() => now);
        now += TimeSpan.FromSeconds(10);
        Check(watchdog.ShouldRestart(true, true), "A stall ten seconds after listening started didn't restart it.");
        watchdog.Started();
        now += TimeSpan.FromSeconds(2);
        watchdog.ShouldRestart(true, false);
        now += TimeSpan.FromSeconds(3);
        Check(watchdog.ShouldRestart(true, true), "After sound came back, a new stall didn't restart after the short wait.");

        // Just started: given the short wait before the first restart.
        watchdog = new ListeningWatchdog(() => now);
        now += TimeSpan.FromSeconds(1);
        Check(!watchdog.ShouldRestart(true, true), "Listening restarted a second after it started.");

        log(problems == 0 ? "Listening to another app restarts itself when it stalls." : $"{problems} listening watchdog problems.");
        return problems;
    }
}
