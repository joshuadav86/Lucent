using System.Diagnostics;

namespace WinampInspiredVisualizer.Editor.Stems;

/// <summary>
/// The apps' --self-test check of the stem splitter: it loads the model on the graphics card if it can and
/// splits a few seconds of made-up music, and if the graphics card did it, checks it gets the same stems as
/// the processor (a graphics driver that gets the sums wrong would otherwise go unnoticed).
/// </summary>
/// <remarks>Runs only when the model is there: CI makes it and points LUCENT_STEM_MODEL at it.</remarks>
public static class StemSelfTest
{
    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log)
    {
        if (!StemModel.IsDownloaded)
        {
            log("Stem splitter: no model here (CI sets LUCENT_STEM_MODEL), so it wasn't tested.");
            return 0;
        }

        try
        {
            var music = MadeUpMusic(4, 44100, 2);
            var stopwatch = Stopwatch.StartNew();
            using var splitter = StemSplitter.Open(StemModel.FilePath);
            log($"Stem splitter: loaded on the {splitter.Device} in {stopwatch.Elapsed.TotalSeconds:0.0} s.");
            if (splitter.GraphicsCardDifference is { } difference)
            {
                log($"Stem splitter: the graphics card's test segment came out {difference:0.0} dB off the processor's (of the music's level).");
            }
            stopwatch.Restart();
            var stems = splitter.Split(music);
            // Checked after splitting too, since the graphics card can load the model and then fail to run it.
            if (splitter.GraphicsCardProblem is { } problem)
            {
                log($"Stem splitter: the graphics card couldn't run it, so the processor did ({problem}).");
            }
            log($"Stem splitter: split {music.Duration.TotalSeconds:0} s of music into {string.Join(", ", stems.Sources)} on the {splitter.Device} in {stopwatch.Elapsed.TotalSeconds:0.0} s.");

            var problems = Check(stems, music, log);

            // How close the stems add up to the music shows the model is doing its job (Demucs' stems nearly do).
            var sum = stems.Mix(stems.Sources);
            log($"Stem splitter: the stems added together differ from the music by {Decibels(Difference(sum, music), Rms(music)):0.0} dB (of its level).");

            if (splitter.OnGraphicsCard)
            {
                using var processor = StemSplitter.Open(StemModel.FilePath, useGraphicsCard: false);
                var expected = processor.Split(music);
                for (var s = 0; s < stems.Stems.Count; s++)
                {
                    // Against the music's level: a stem with next to nothing in it would make any difference look big.
                    var off = Decibels(Difference(stems.Stems[s], expected.Stems[s]), Rms(music));
                    log($"Stem splitter: {stems.Sources[s]} on the graphics card differs from the processor's by {off:0.0} dB (of the music's level).");
                    if (off > -40)
                    {
                        log($"::error::The graphics card's {stems.Sources[s]} came out different from the processor's ({off:0.0} dB).");
                        problems++;
                    }
                }
            }

            // Mono at another sample rate goes through the conversions on the way in and out.
            var mono = MadeUpMusic(1, 48000, 1);
            problems += Check(splitter.Split(mono), mono, log);
            return problems;
        }
        catch (Exception ex)
        {
            log($"::error::The stem splitter didn't work: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Check(StemSet stems, AudioBuffer music, Action<string> log)
    {
        foreach (var stem in stems.Stems)
        {
            if (stem.Frames != music.Frames || stem.ChannelCount != music.ChannelCount || stem.SampleRate != music.SampleRate)
            {
                log($"::error::A stem came out {stem.Frames} frames of {stem.ChannelCount} channel(s) at {stem.SampleRate} Hz, " +
                    $"for {music.Frames} frames of {music.ChannelCount} at {music.SampleRate} Hz.");
                return 1;
            }

            if (stem.Channels.Any(channel => channel.Any(value => !float.IsFinite(value))))
            {
                log("::error::A stem came out with samples that aren't numbers.");
                return 1;
            }
        }

        return 0;
    }

    /// <summary>A bass line, a wobbling lead, a kick drum and a little noise.</summary>
    private static AudioBuffer MadeUpMusic(double seconds, int rate, int channels)
    {
        var frames = (int)(seconds * rate);
        var random = new Random(1);
        var result = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            result[c] = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                var t = i / (double)rate;
                var value = 0.3 * Math.Sin(2 * Math.PI * 110 * t) * (Math.Sin(2 * Math.PI * 0.5 * t) > 0 ? 1 : 0)
                            + 0.2 * Math.Sin(2 * Math.PI * 440 * t + 3 * Math.Sin(2 * Math.PI * 5 * t))
                            + 0.5 * Math.Exp(-30 * (t % 0.5)) * Math.Sin(2 * Math.PI * 60 * t);
                result[c][i] = (float)((c == 0 ? value : 0.8 * value) + 0.05 * (random.NextDouble() * 2 - 1));
            }
        }

        return new AudioBuffer(result, rate);
    }

    private static double Difference(AudioBuffer a, AudioBuffer b)
    {
        double sum = 0;
        long count = 0;
        for (var c = 0; c < a.ChannelCount; c++)
        {
            for (var i = 0; i < a.Frames; i++)
            {
                var d = a.Channels[c][i] - b.Channels[c][i];
                sum += d * d;
                count++;
            }
        }

        return Math.Sqrt(sum / Math.Max(1, count));
    }

    private static double Rms(AudioBuffer audio) => Difference(audio, AudioBuffer.Silent(audio.ChannelCount, audio.Frames, audio.SampleRate));

    private static double Decibels(double value, double reference) => 20 * Math.Log10(Math.Max(value, 1e-12) / Math.Max(reference, 1e-12));
}
