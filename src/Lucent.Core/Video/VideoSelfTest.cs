using System.Diagnostics;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using WinampInspiredVisualizer.Editor;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// The apps' --self-test check of video playback, with the clips in tools/test-media: four seconds of a dimmed test
/// pattern that flashes for a tenth of a second (white, yellow, then cyan) at 1, 2 and 3 seconds, with a beep at the
/// same moments. Each is played through libVLC with the sound read as an output reads it (CI machines may have no
/// speakers), paused partway, then played again from 1.5 seconds after it ends. Every flash has to be seen and every
/// beep heard, each beep within <see cref="InStepMs"/> of its flash, and the sound has to last as long as the video.
/// MP4 and MOV clips are then cut without re-encoding (<see cref="Mp4Cutter"/>) and the cut played the same way.
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
    /// virtual machine (<paramref name="virtualMachine"/>), sound out of step with the picture, or one beep too many, only warns: GitHub's
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
            // GitHub's virtual Macs stall now and then for longer than a flash lasts, and libVLC drops pictures that
            // come too late, so a whole flash could go unseen. There, every picture is shown, late or not.
            VideoEngine.KeepLatePictures = virtualMachine;
            VideoEngine.Get();
            log($"Video: libVLC {VideoEngine.Version} loaded{(virtualMachine ? ", showing every picture however late (a virtual machine)" : string.Empty)}.");
        }
        catch (Exception ex)
        {
            log($"::error::Video: libVLC didn't load, so no video can play: {ex}");
            return 1;
        }

        // The first play after libVLC loads can start cold: on GitHub's Windows machines its first beep has come 0.4 s
        // ahead of its flash, while every play after was in step. So one untimed play warms it up before any is timed.
        try
        {
            using var warmUp = new VideoStream(videos[0]);
            Play(warmUp, pauseAt: null);
        }
        catch (Exception)
        {
            // The timed plays below say what's wrong.
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

        if (Mp4Cutter.CanCut(path))
        {
            problems += TestCut(log, path, virtualMachine);
        }

        return problems;
    }

    // Cuts the second around the middle flash out without re-encoding (the clips have a keyframe every half
    // second), then plays what's left: the first and last flashes, now a second apart, each still with its beep.
    private static int TestCut(Action<string> log, string path, bool virtualMachine)
    {
        var name = Path.GetFileName(path);
        var problems = 0;
        var cut = Path.Combine(Path.GetTempPath(), $"lucent-cut-{Guid.NewGuid():N}{Path.GetExtension(path)}");
        try
        {
            var movie = Mp4Cutter.Read(path);
            // A start just after a keyframe moves back to it, and an end lands on a picture.
            var keep = new[] { new VideoRange(0, 1.5), new VideoRange(2.52, 4) };
            var snapped = movie.Snap(keep);
            if (snapped.Count != 2 || Math.Abs(snapped[1].Start - 2.5) > 0.001 || Math.Abs(snapped[0].End - 1.5) > 0.001)
            {
                problems++;
                log($"::error::Video: cutting {name}, the kept parts snapped to {string.Join(", ", snapped.Select(range => $"{range.Start:0.000}-{range.End:0.000} s"))}, not 0-1.5 s and 2.5-4 s.");
            }

            Mp4Cutter.Write(movie, keep, cut);
            using var video = new VideoStream(cut);
            if (!video.HasSound || !video.HasPicture || Math.Abs(video.TotalTime.TotalSeconds - 3) > 0.1)
            {
                problems++;
                log($"::error::Video: {name} cut down to 3 s opened as {(video.HasPicture ? "a picture" : "no picture")}, {(video.HasSound ? "sound" : "no sound")} and {video.TotalTime.TotalSeconds:0.00} s.");
            }

            problems += Check(log, $"{name} cut without re-encoding", Play(video, pauseAt: null), 2, virtualMachine);
            problems += TestEditorSound(log, path);
            problems += TestPreview(log, path);
            problems += TestPictures(log, path);
        }
        catch (Exception ex)
        {
            problems++;
            log($"::error::Video: {name} couldn't be cut without re-encoding: {ex}");
        }
        finally
        {
            try
            {
                File.Delete(cut);
            }
            catch (IOException)
            {
            }
        }

        return problems;
    }

    // The pictures from just before the flash at 1 s to just after it, as read for saving.
    private const string FlashExpected = "dark dark dark flash flash flash dark dark dark";

    // Whether each picture between two moments (seconds of the edit) is a flash or dark, as read for saving.
    private static string FlashColours(LibVLC libVlc, VideoEdit video, double from, double to)
    {
        var pictures = VideoExport.Pictures(video, from, to);
        var seen = new List<string>();
        VideoExport.Read(libVlc, video, pictures, 64, 36, (_, pixels, stride) =>
        {
            var middle = new byte[4];
            Marshal.Copy(pixels + 18 * stride + 32 * 4, middle, 0, 4);
            seen.Add(middle[0] > 200 || middle[1] > 200 || middle[2] > 200 ? "flash" : "dark");
        }, CancellationToken.None);
        return string.Join(" ", seen);
    }

    // A copy re-encoded smaller (where the app has an encoder, as on the Mac): it's whole, as long as the video, and
    // its flash is where it was, the same three pictures, so its pictures went in in order and on time.
    private static int TestCopy(Action<string> log, string name, VideoEdit video, AudioBuffer audio)
    {
        if (VideoCopy.CreateWriter is null)
        {
            return 0;
        }

        var copy = Path.Combine(Path.GetTempPath(), $"lucent-copy-{Guid.NewGuid():N}.mp4");
        try
        {
            var choice = new VideoCopyChoice { Format = VideoCopyFormat.H264, Size = 0, FrameRate = 0, Quality = VideoCopyQuality.Medium, Path = copy };
            var note = VideoCopy.Save(video, audio, 0, video.Length, choice, VideoExport.PictureSize(video), null, CancellationToken.None);
            var (copied, _, _, _) = VideoEdit.Load(copy, null, CancellationToken.None);
            if (Math.Abs(copied.Length - video.Length) > 0.1)
            {
                log($"::error::Video: the copy of {name} is {copied.Length:0.00} s long, not {video.Length:0.00} s.");
                return 1;
            }

            using var libVlc = VideoEngine.CreateForFrames();
            var flash = FlashColours(libVlc, copied, 0.9, 1.2);
            if (flash != FlashExpected)
            {
                log($"::error::Video: in the copy of {name}, the frames from 0.9 s to 1.2 s were {flash}, not {FlashExpected}.");
                return 1;
            }

            log($"Video: a copy of {name} re-encoded is {new FileInfo(copy).Length} bytes, with its flash in place. {note}");
            return 0;
        }
        catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException or AudioFileException)
        {
            log($"::error::Video: {name} couldn't be copied: {ex.Message}");
            return 1;
        }
        finally
        {
            try
            {
                File.Delete(copy);
            }
            catch (IOException)
            {
            }
        }
    }

    // Saving frames: every picture from just before a flash to just after it comes out in order, the flash's three
    // and no more, and the same across a cut (with 1.5-2.5 s deleted, the last flash is 2 s into the edit). Then a
    // short GIF is made.
    private static int TestPictures(Action<string> log, string path)
    {
        var name = Path.GetFileName(path);
        var (video, audio, _, _) = VideoEdit.Load(path, null, CancellationToken.None);
        using var libVlc = VideoEngine.CreateForFrames();
        string Colours(double from, double to) => FlashColours(libVlc, video, from, to);

        var problems = 0;
        var flash = Colours(0.9, 1.2);
        if (flash != FlashExpected)
        {
            problems++;
            log($"::error::Video: saving the frames from 0.9 s to 1.2 s of {name} gave {flash}, not {FlashExpected}.");
        }

        problems += TestCopy(log, name, video, audio);

        var rate = audio.SampleRate;
        video.Delete(new FrameRange((int)(1.5 * rate), rate), 1);
        var cut = Colours(1.9, 2.2);
        if (cut != FlashExpected)
        {
            problems++;
            log($"::error::Video: with 1.5-2.5 s of {name} cut, saving the frames from 1.9 s to 2.2 s gave {cut}, not {FlashExpected}.");
        }

        var gif = Path.Combine(Path.GetTempPath(), $"lucent-gif-{Guid.NewGuid():N}.gif");
        try
        {
            VideoExport.SaveGif(video, VideoExport.Pictures(video, 0.5, 1.5, 10), 10, 320, loop: true, dither: true, gif, null, CancellationToken.None);
            var bytes = File.ReadAllBytes(gif);
            if (bytes.Length < 100 || !bytes.AsSpan(0, 6).SequenceEqual("GIF89a"u8) || bytes[^1] != 0x3B)
            {
                problems++;
                log($"::error::Video: the GIF made of {name} isn't a whole GIF ({bytes.Length} bytes).");
            }
            else if (problems == 0)
            {
                log($"Video: {name}'s frames save exactly, across a cut too, and a GIF of it is {bytes.Length} bytes.");
            }
        }
        finally
        {
            try
            {
                File.Delete(gif);
            }
            catch (IOException)
            {
            }
        }

        return problems;
    }

    // The editor opens a video by its sound, decoded by Lucent's own AAC decoder (or the system's, for other sounds):
    // it has to decode, the whole 4 seconds, with the beeps where they are in the video.
    private static int TestEditorSound(Action<string> log, string path)
    {
        var name = Path.GetFileName(path);
        var (_, audio, _, _) = VideoEdit.Load(path, null, CancellationToken.None);
        var seconds = audio.Frames / (double)audio.SampleRate;
        var beeps = new List<double>();
        var quiet = 0;
        var samples = audio.Channels[0];
        for (var i = 0; i < samples.Length; i++)
        {
            if (Math.Abs(samples[i]) > 0.2f)
            {
                if (quiet > audio.SampleRate / 4)
                {
                    beeps.Add(i / (double)audio.SampleRate);
                }

                quiet = 0;
            }
            else
            {
                quiet++;
            }
        }

        var heard = string.Join(", ", beeps.Select(beep => $"{beep:0.000} s"));
        if (Math.Abs(seconds - 4) > 0.1 || beeps.Count != 3 || beeps.Select((beep, i) => Math.Abs(beep - (i + 1))).Max() > 0.03)
        {
            log($"::error::Video: the editor read {name}'s sound as {seconds:0.000} s with beeps at {heard}, not 4 s with beeps at 1, 2 and 3 s.");
            return 1;
        }

        log($"Video: the editor reads {name}'s sound ({seconds:0.000} s, beeps at {heard}).");
        return 0;
    }

    // The editor's picture: asked for the middle of a flash (three pictures long), it shows the flash, to a picture
    // either side, and asked for a moment between flashes, it doesn't.
    private static int TestPreview(Action<string> log, string path)
    {
        var name = Path.GetFileName(path);
        using var preview = new VideoPreview(path);
        string? Shows(double seconds)
        {
            var before = preview.Frames.Count;
            preview.Show(seconds);
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < TimeSpan.FromSeconds(5) && (preview.Frames.Count == before || preview.Frames.Count == 0 || preview.IsFinding))
            {
                Thread.Sleep(10);
            }

            // A moment more, in case pictures on the way to pausing were shown.
            Thread.Sleep(200);
            string? colour = null;
            preview.Frames.CopyLatest((pixels, width, height, stride) =>
            {
                var middle = new byte[4];
                Marshal.Copy(pixels + height / 2 * stride + width / 2 * 4, middle, 0, 4);
                colour = (middle[2] > 200, middle[1] > 200, middle[0] > 200) switch
                {
                    (true, true, true) => "white",
                    (true, true, false) => "yellow",
                    (false, true, true) => "cyan",
                    _ => "dark",
                };
            });
            return colour;
        }

        var flash = Shows(1.034);
        var between = Shows(1.5);
        var again = Shows(2.034);
        if (flash is null or "dark" || between != "dark" || again is null or "dark")
        {
            log($"::error::Video: the editor's picture of {name} at 1 s, 1.5 s and 2 s was {flash ?? "nothing"}, {between ?? "nothing"} and {again ?? "nothing"}, not the flash, the dimmed pattern, then the flash.");
            return 1;
        }

        log($"Video: the editor shows {name}'s pictures where asked ({flash} at 1 s, {between} at 1.5 s, {again} at 2 s).");
        return 0;
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

        if (run.Flashes.Count < expected && run.Beeps.Count == expected && run.LostPictures > 0 && virtualMachine)
        {
            // Every picture of a flash came too late to show, so libVLC dropped them, as it should.
            log($"::warning::Video: {what}: saw {run.Flashes.Count} flash(es) of {expected}, as this virtual machine was too slow to show {run.LostPictures} picture(s) in time, so they were dropped. It wasn't counted as a failure.");
            return problems;
        }

        if (run.Flashes.Count == expected && run.Beeps.Count == expected + 1 && virtualMachine)
        {
            // When a stalled virtual machine has the sound lined up with the picture again mid-beep, part of the beep
            // can be skipped or heard twice, so it counts as two. The one furthest from any flash is the extra one.
            var extra = run.Beeps.MaxBy(beep => run.Flashes.Min(flash => Math.Abs(beep - flash)));
            run.Beeps.Remove(extra);
            log($"::warning::Video: {what}: heard {expected + 1} beep(s) for {expected} flash(es), as this virtual machine stalled mid-beep. The extra one was left out, and it wasn't counted as a failure.");
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
        var lostBefore = video.LostPictures;
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
        // Counted since libVLC last started, which may have been before this play or during it.
        var lost = video.LostPictures;
        run.LostPictures = lost >= lostBefore ? lost - lostBefore : lost;
        video.Paused();
        return run;
    }

    private sealed class Playthrough
    {
        public List<long> Flashes { get; } = new();

        public List<long> Beeps { get; } = new();

        public bool Ended { get; set; }

        /// <summary>Pictures libVLC dropped for coming too late.</summary>
        public int LostPictures { get; set; }

        /// <summary>The loudest sample heard (the beeps are at half volume).</summary>
        public float Loudest { get; set; }

        /// <summary>Samples that weren't sound at all: not a number, or beyond full volume.</summary>
        public int Garbled { get; set; }

        public TimeSpan? HalfSecondIn { get; set; }
    }
}
