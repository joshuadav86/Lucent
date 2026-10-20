using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// The apps' --self-test check of video playback, with the clips in tools/test-media: four seconds of a dimmed test
/// pattern that flashes white for a tenth of a second at 1, 2 and 3 seconds, with a beep at the same moments. Each is
/// played through libVLC with the sound read as an output reads it (CI machines may have no speakers), paused
/// partway, then played again from 2.5 seconds after it ends. Every flash has to be seen and every beep heard, each
/// beep within <see cref="InStepMs"/> of its flash, and the sound has to last as long as the video.
/// </summary>
public static class VideoSelfTest
{
    /// <summary>How far apart a beep and its flash may be. People notice from about 45 ms with the sound early.</summary>
    private const double InStepMs = 45;

    /// <summary>Further apart than this fails; between the two, it's a warning (a slow CI machine, say).</summary>
    private const double OutOfStepMs = 90;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log, IReadOnlyList<string> videos)
    {
        if (videos.Count == 0)
        {
            return 0;
        }

        try
        {
            VideoEngine.Get();
            log($"Video: libVLC {VideoEngine.Version} loaded.");
        }
        catch (Exception ex)
        {
            log($"::error::Video: libVLC didn't load, so no video can play: {ex}");
            return 1;
        }

        var problems = 0;
        foreach (var path in videos)
        {
            try
            {
                problems += Test(log, path);
            }
            catch (Exception ex)
            {
                problems++;
                log($"::error::Video: {Path.GetFileName(path)} didn't play: {ex}");
            }
        }

        return problems;
    }

    private static int Test(Action<string> log, string path)
    {
        var name = Path.GetFileName(path);
        var problems = 0;
        if (!VideoFiles.IsVideo(path))
        {
            problems++;
            log($"::error::Video: {name} wasn't recognised as a video.");
        }

        using var video = new VideoStream(path);
        if (!video.HasSound || !video.HasPicture || Math.Abs(video.TotalTime.TotalSeconds - 4) > 0.5)
        {
            problems++;
            log($"::error::Video: {name} opened as {(video.HasPicture ? "a picture" : "no picture")}, {(video.HasSound ? "sound" : "no sound")} and {video.TotalTime.TotalSeconds:0.00} s, not a 4 s video with sound.");
        }

        // Played from the start, with a pause partway: three flashes and beeps.
        var first = Play(video, pauseAt: TimeSpan.FromSeconds(1.4));
        problems += Check(log, $"{name} played through", first, 3);
        if (video.Frames.Width != 320 || video.Frames.Height != 180)
        {
            problems++;
            log($"::error::Video: {name}'s pictures came as {video.Frames.Width} × {video.Frames.Height}, not 320 × 180.");
        }

        // Played again from 2.5 seconds, after it ended: only the last flash and beep.
        video.CurrentTime = TimeSpan.FromSeconds(2.5);
        var again = Play(video, pauseAt: null);
        problems += Check(log, $"{name} played again from 2.5 s", again, 1);
        // Half a second in, less the moment libVLC takes to start.
        if (again.HalfSecondIn is not { } position || position.TotalSeconds < 2.4 || position.TotalSeconds > 3.2)
        {
            problems++;
            log($"::error::Video: half a second after playing {name} from 2.5 s, it said it was at {again.HalfSecondIn?.TotalSeconds:0.00} s.");
        }

        if (video.Relined > 0)
        {
            log($"::warning::Video: {name}'s sound had to be lined up with its picture again {video.Relined} time(s) while playing.");
        }

        return problems;
    }

    private static int Check(Action<string> log, string what, Playthrough run, int expected)
    {
        var problems = 0;
        if (!run.Ended)
        {
            problems++;
            log($"::error::Video: {what} didn't reach its end within {Limit.TotalSeconds:0} s.");
        }

        if (run.Flashes.Count != expected || run.Beeps.Count != expected)
        {
            problems++;
            log($"::error::Video: {what}: saw {run.Flashes.Count} flash(es) and heard {run.Beeps.Count} beep(s), not {expected} of each.");
            return problems;
        }

        var gaps = run.Flashes.Zip(run.Beeps, (flash, beep) => (beep - flash) / 1000.0).ToList();
        var worst = gaps.MaxBy(Math.Abs);
        var described = string.Join(", ", gaps.Select(gap => $"{gap:+0;-0} ms"));
        if (Math.Abs(worst) > OutOfStepMs)
        {
            problems++;
            log($"::error::Video: {what}: the sound was out of step with the picture (beep minus flash: {described}).");
        }
        else if (Math.Abs(worst) > InStepMs)
        {
            log($"::warning::Video: {what}: the sound was a little out of step with the picture (beep minus flash: {described}).");
        }
        else
        {
            log($"Video: {what}, sound in step with the picture (beep minus flash: {described}).");
        }

        return problems;
    }

    // Plays the video to its end the way the player does: 10 ms of sound read every 10 ms, while each picture is
    // checked for a flash as it's shown. Times are libVLC's clock, in microseconds: when a flash was shown, and when each beep
    // (the first loud sample after a quiet stretch) would reach the speakers.
    private static Playthrough Play(VideoStream video, TimeSpan? pauseAt)
    {
        var run = new Playthrough();
        // Each picture is looked at as libVLC shows it, timed by libVLC's clock.
        var bright = false;
        video.Frames.Shown = (pixels, width, height, stride) =>
        {
            var shownAt = video.Clock;
            var middle = new byte[4];
            Marshal.Copy(pixels + height / 2 * stride + width / 2 * 4, middle, 0, 4);
            var now = middle[0] > 200 && middle[1] > 200 && middle[2] > 200;
            if (now && !bright)
            {
                lock (run)
                {
                    run.Flashes.Add(shownAt);
                }
            }

            bright = now;
        };

        var rate = video.WaveFormat.SampleRate;
        var chunk = new byte[rate / 100 * video.WaveFormat.BlockAlign];
        var quiet = int.MaxValue;
        var clock = Stopwatch.StartNew();
        long due = 0;
        var paused = false;
        video.Playing();
        while (clock.Elapsed < Limit)
        {
            while (clock.ElapsedMilliseconds < due)
            {
                Thread.Sleep(1);
            }

            due += 10;
            if (pauseAt is { } at && !paused && clock.Elapsed >= at)
            {
                paused = true;
                video.Paused();
                Thread.Sleep(400);
                video.Playing();
                due = clock.ElapsedMilliseconds;
            }

            var readAt = video.Clock;
            run.HalfSecondIn ??= clock.Elapsed >= TimeSpan.FromMilliseconds(500) ? video.CurrentTime : null;
            var read = video.Read(chunk, 0, chunk.Length);
            if (read == 0)
            {
                run.Ended = true;
                break;
            }

            var samples = MemoryMarshal.Cast<byte, float>(chunk.AsSpan(0, read));
            for (var i = 0; i < samples.Length / 2; i++)
            {
                if (Math.Abs(samples[i * 2]) > 0.1f)
                {
                    if (quiet > rate / 20)
                    {
                        run.Beeps.Add(readAt + VideoStream.HeardDelayUs + i * 1_000_000L / rate);
                    }

                    quiet = 0;
                }
                else if (quiet < int.MaxValue)
                {
                    quiet++;
                }
            }
        }

        // The last picture may come a moment after the sound runs out.
        Thread.Sleep(100);
        video.Frames.Shown = null;
        video.Paused();
        return run;
    }

    private sealed class Playthrough
    {
        public List<long> Flashes { get; } = new();

        public List<long> Beeps { get; } = new();

        public bool Ended { get; set; }

        public TimeSpan? HalfSecondIn { get; set; }
    }
}
