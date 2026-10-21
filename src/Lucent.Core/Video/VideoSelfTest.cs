using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// The apps' --self-test check of video playback, with the clips in tools/test-media: four seconds of a dimmed test
/// pattern that flashes for a tenth of a second (white, yellow, then cyan) at 1, 2 and 3 seconds, with a beep at the
/// same moments. Each is played through libVLC with the sound read as an output reads it (CI machines may have no
/// speakers), paused partway, then played again from 1.5 seconds after it ends. Every flash has to be seen and every
/// beep heard, each beep within <see cref="InStepMs"/> of its flash, and the sound has to last as long as the video.
/// </summary>
public static class VideoSelfTest
{
    /// <summary>How far apart a beep and its flash may be. People notice from about 45 ms with the sound early.</summary>
    private const double InStepMs = 45;

    /// <summary>Further apart than this fails; between the two, it's a warning (a slow CI machine, say).</summary>
    private const double OutOfStepMs = 90;

    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(20);

    /// <summary>How long each of a flash's pictures lasts: the clips are 30 frames a second.</summary>
    private const long FlashPictureUs = 1_000_000 / 30;

    /// <summary>
    /// Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems). On a
    /// virtual machine (<paramref name="virtualMachine"/>), sound out of step with the picture only warns: GitHub's
    /// virtual Macs show pictures up to a tenth of a second late, which a real Mac doesn't.
    /// </summary>
    public static int Run(Action<string> log, IReadOnlyList<string> videos, bool virtualMachine = false)
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
                problems += Test(log, path, virtualMachine);
            }
            catch (Exception ex)
            {
                problems++;
                log($"::error::Video: {Path.GetFileName(path)} didn't play: {ex}");
            }
        }

        return problems;
    }

    private static int Test(Action<string> log, string path, bool virtualMachine)
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
        problems += Check(log, $"{name} played through", first, 3, virtualMachine);
        if (video.Frames.Width != 320 || video.Frames.Height != 180)
        {
            problems++;
            log($"::error::Video: {name}'s pictures came as {video.Frames.Width} × {video.Frames.Height}, not 320 × 180.");
        }

        // Played again from 1.5 seconds, after it ended: the last two flashes and beeps. The first is half a second
        // in, which gives libVLC (a moment to start again on a slow machine) time to show it.
        video.CurrentTime = TimeSpan.FromSeconds(1.5);
        var again = Play(video, pauseAt: null);
        problems += Check(log, $"{name} played again from 1.5 s", again, 2, virtualMachine);
        // Half a second in, less the moment libVLC takes to start.
        if (again.HalfSecondIn is not { } position || position.TotalSeconds < 1.4 || position.TotalSeconds > 2.2)
        {
            problems++;
            log($"::error::Video: half a second after playing {name} from 1.5 s, it said it was at {again.HalfSecondIn?.TotalSeconds:0.00} s.");
        }

        if (video.Relined > 0)
        {
            log($"::warning::Video: {name}'s sound had to be lined up with its picture again {video.Relined} time(s) while playing.");
        }

        return problems;
    }

    private static int Check(Action<string> log, string what, Playthrough run, int expected, bool virtualMachine)
    {
        var problems = 0;
        if (!run.Ended)
        {
            problems++;
            log($"::error::Video: {what} didn't reach its end within {Limit.TotalSeconds:0} s.");
        }

        // The beeps are a clean tone at half volume. Sound in the wrong format (16-bit read as float, say) comes out
        // as noise far beyond full volume, or not a number at all, while still seeming to beep in time.
        if (run.Garbled > 0 || run.Loudest is < 0.3f or > 0.8f)
        {
            problems++;
            log($"::error::Video: {what}: the sound came out garbled ({run.Garbled} samples weren't sound, and the loudest was {run.Loudest:0.00} where the beeps are 0.5).");
        }

        if (run.Flashes.Count != expected || run.Beeps.Count != expected)
        {
            problems++;
            log($"::error::Video: {what}: saw {run.Flashes.Count} flash(es) and heard {run.Beeps.Count} beep(s), not {expected} of each.");
            return problems;
        }

        var gaps = run.Flashes.Zip(run.Beeps, (flash, beep) => (beep - flash) / 1000.0).ToList();
        var worst = gaps.MaxBy(Math.Abs);
        var described = string.Join(", ", gaps.Select(gap => $"{Math.Round(gap):+0;-0;0} ms"));
        if (Math.Abs(worst) > OutOfStepMs && virtualMachine)
        {
            log($"::warning::Video: {what}: the sound was out of step with the picture (beep minus flash: {described}). This is a virtual machine, so it wasn't counted as a failure.");
        }
        else if (Math.Abs(worst) > OutOfStepMs)
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
        // Each picture is looked at as libVLC shows it, timed by libVLC's clock. A flash is three pictures (white,
        // yellow, then cyan), so one a slow machine drops still leaves the flash's start known from the others.
        var flashing = false;
        video.Frames.Shown = (pixels, width, height, stride) =>
        {
            var shownAt = video.Clock;
            var middle = new byte[4];
            Marshal.Copy(pixels + height / 2 * stride + width / 2 * 4, middle, 0, 4);
            var (blue, green, red) = (middle[0] > 200, middle[1] > 200, middle[2] > 200);
            var picture = (red, green, blue) switch
            {
                (true, true, true) => 0,
                (true, true, false) => 1,
                (false, true, true) => 2,
                _ => -1,
            };
            if (picture >= 0 && !flashing)
            {
                lock (run)
                {
                    run.Flashes.Add(shownAt - picture * FlashPictureUs);
                }
            }

            flashing = picture >= 0;
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
            foreach (var sample in samples)
            {
                if (float.IsFinite(sample) && Math.Abs(sample) <= 1)
                {
                    run.Loudest = Math.Max(run.Loudest, Math.Abs(sample));
                }
                else
                {
                    run.Garbled++;
                }
            }

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

        /// <summary>The loudest sample heard (the beeps are at half volume).</summary>
        public float Loudest { get; set; }

        /// <summary>Samples that weren't sound at all: not a number, or beyond full volume.</summary>
        public int Garbled { get; set; }

        public TimeSpan? HalfSecondIn { get; set; }
    }
}
