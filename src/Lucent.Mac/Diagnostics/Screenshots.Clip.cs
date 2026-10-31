using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lucent.Audio;
using NAudio.Wave;

namespace Lucent.Mac;

/// <summary>
/// FullScreen in motion, for the website's and the README's moving shots. LUCENT_PREVIEW_CLIP ("start:seconds") with
/// LUCENT_PREVIEW_AUDIO (a WAV of the song) plays that stretch of the song into the player, the equalizer and the
/// analyzer, and saves every frame of each into LUCENT_PREVIEW_LAYERS/frames (Player-00000.png on, at
/// LUCENT_PREVIEW_FPS, 60 if not set). The visuals need the graphics chip, so they're drawn apart, from the same song.
/// The song is Kepler North's "Self Actualize", which Lucent ships as its starter music, so the windows show it playing.
/// </summary>
public static partial class Screenshots
{
    private sealed record Clip(float[] Left, float[] Right, int SampleRate, double Start, double Seconds, int Fps);

    private static readonly Clip? PreviewClip = ReadClip();

    // The windows the song plays into: each one's capture, and how it moves on a frame (by seconds, at a song time).
    private static readonly List<(AudioCapture Capture, Action<double, double> Step)> ClipFeeds = new();

    private static (string Title, string Artist, string Album, int Seconds) ClipSong => ("Self Actualize", "Kepler North", "Pseudo Profound", 368);

    private static Clip? ReadClip()
    {
        if (Environment.GetEnvironmentVariable("LUCENT_PREVIEW_CLIP") is not { Length: > 0 } span
            || Environment.GetEnvironmentVariable("LUCENT_PREVIEW_AUDIO") is not { Length: > 0 } path || !File.Exists(path))
        {
            return null;
        }

        var parts = span.Split(':');
        var fps = int.TryParse(Environment.GetEnvironmentVariable("LUCENT_PREVIEW_FPS"), out var rate) && rate > 0 ? rate : 60;
        using var reader = new WaveFileReader(path);
        var samples = reader.ToSampleProvider();
        var channels = reader.WaveFormat.Channels;
        var all = new List<float>();
        var buffer = new float[16384];
        int read;
        while ((read = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            all.AddRange(buffer.AsSpan(0, read).ToArray());
        }

        var frames = all.Count / channels;
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = all[i * channels];
            right[i] = all[i * channels + (channels > 1 ? 1 : 0)];
        }

        return new Clip(left, right, reader.WaveFormat.SampleRate, double.Parse(parts[0], CultureInfo.InvariantCulture),
            double.Parse(parts[1], CultureInfo.InvariantCulture), fps);
    }

    /// <summary>Lets <paramref name="capture"/>'s window hear the clip, moving it on with <paramref name="step"/>.</summary>
    private static void FeedClip(AudioCapture capture, Action<double, double> step) => ClipFeeds.Add((capture, step));

    /// <summary>
    /// Plays the clip into the windows and saves each animated window's frames. The analyzer's picture scrolls by
    /// 11 seconds of the song before the first frame, so it starts full.
    /// </summary>
    private static void SaveClipFrames(Clip clip, string folder, IReadOnlyList<(string Kind, Window Window)> windows)
    {
        Directory.CreateDirectory(folder);
        foreach (var (capture, _) in ClipFeeds)
        {
            capture.Reset(clip.SampleRate);
        }

        var frameSeconds = 1.0 / clip.Fps;
        var from = Math.Max(0, clip.Start - 11);
        var frameCount = (int)Math.Round(clip.Seconds * clip.Fps);
        // What's been written so far, in samples. The windows read the audio a little behind the newest (as the
        // speakers lag the decoder), so the song is written that far ahead of the moment drawn.
        long written = (long)(from * clip.SampleRate);
        var steps = (int)Math.Round((clip.Start - from) * clip.Fps) + frameCount;
        for (var step = 0; step < steps; step++)
        {
            var time = from + (step + 1) * frameSeconds;
            foreach (var (capture, move) in ClipFeeds)
            {
                var until = Math.Min(clip.Left.Length, (long)(time * clip.SampleRate) + capture.DelaySamples);
                var count = (int)Math.Max(0, until - written);
                var left = clip.Left.AsSpan((int)written, count);
                var right = clip.Right.AsSpan((int)written, count);
                var mono = new float[count];
                for (var i = 0; i < count; i++)
                {
                    mono[i] = (left[i] + right[i]) / 2;
                }

                capture.Write(mono, left, right);
                move(frameSeconds, time);
            }

            written = Math.Min(clip.Left.Length, (long)(time * clip.SampleRate) + ClipFeeds[0].Capture.DelaySamples);
            var frame = step - (steps - frameCount);
            if (frame < 0)
            {
                continue;
            }

            Dispatcher.UIThread.RunJobs();
            foreach (var (kind, window) in windows)
            {
                // Drawn straight to a picture, with no render tick, so nothing moves but what the song moves. At
                // LUCENT_PREVIEW_SCALE, but the analyzer at 1, as for the still shots.
                var scale = window is AnalyzerWindow ? 1 : Scale;
                var size = window.Bounds.Size;
                using var picture = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)),
                    new Vector(96 * scale, 96 * scale));
                picture.Render(window);
                picture.Save(Path.Combine(folder, $"{kind}-{frame:00000}.png"));
            }

            if (frame % clip.Fps == 0)
            {
                Console.WriteLine($"FullScreen: frame {frame} of {frameCount}");
            }
        }
    }
}
