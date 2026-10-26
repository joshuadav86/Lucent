using Lucent.Mac.Audio;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;

namespace Lucent.Mac;

/// <summary>
/// The self-test's sound checks, none of which need speakers (CI's Macs have none, or only a silent
/// stand-in): the outputs can be listed, and two tracks chained gaplessly come out back to back with
/// not a sample missing or added between them.
/// </summary>
public static partial class SelfTest
{
    private const int GaplessRate = 44100;
    private const int GaplessChannels = 2;

    // Odd lengths, so the switch from one track to the next falls part way through a buffer.
    private const int FirstTrackFrames = GaplessRate / 2 + 37;
    private const int SecondTrackFrames = GaplessRate / 3 + 11;

    /// <summary>Lists the outputs as the Sound options do. A Mac with none passes: there's just nothing to list.</summary>
    private static int TestOutputDevices()
    {
        try
        {
            var devices = OutputDevices.Enumerate();
            Console.WriteLine($"Sound outputs: {devices.Count}.");
            foreach (var device in devices)
            {
                var rates = ExclusiveDevice.AvailableRates(device.ObjectId)
                    .Select(range => range.Minimum == range.Maximum ? $"{range.Minimum:0}" : $"{range.Minimum:0}-{range.Maximum:0}");
                var owner = ExclusiveDevice.ReadOwner(device.ObjectId);
                Console.WriteLine($"  {device.Name} ({device.Id}): {ExclusiveDevice.ReadRate(device.ObjectId):0} Hz now, takes {string.Join(", ", rates)} Hz"
                    + (owner == -1 ? "" : $", held by process {owner}"));
            }

            var fallback = OutputDevices.Open(null);
            Console.WriteLine(fallback is null ? "No default output." : $"Default output: {fallback.Name}.");
            if (OutputDevices.Open("no-such-output") is { } missing && missing != fallback)
            {
                Console.WriteLine("::error::An output that isn't there didn't fall back to the default one.");
                return 1;
            }

            return 0;
        }
        catch (IOException ex)
        {
            // Core Audio answered with an error: a runner without sound, not a fault in Lucent.
            Console.WriteLine($"::warning::Couldn't list the sound outputs: {ex.Message}");
            return 0;
        }
        catch (Exception ex)
        {
            // A missing function or a wrongly declared one, which would break the Sound options for everyone.
            Console.WriteLine($"::error::Listing the sound outputs failed: {ex}");
            return 1;
        }
    }

    /// <summary>
    /// Chains two made-up tracks as the player does for gapless playback, first straight from memory and
    /// then decoded from WAV files by Core Audio, and checks every sample comes out in order; then has the
    /// player line one file up behind another (without playing them).
    /// </summary>
    private static int TestGapless()
    {
        var problems = 0;
        var folder = Directory.CreateTempSubdirectory("lucent-gapless");
        try
        {
            var first = Ramp(0, FirstTrackFrames);
            var second = Ramp(FirstTrackFrames, SecondTrackFrames);
            var expected = first.Concat(second).ToArray();

            problems += CheckChain("from memory", expected,
                ("first", MemoryTrack(first)), ("second", MemoryTrack(second)));

            var firstPath = Path.Combine(folder.FullName, "first.wav");
            var secondPath = Path.Combine(folder.FullName, "second.wav");
            var monoPath = Path.Combine(folder.FullName, "mono.wav");
            WriteWav(firstPath, first, GaplessChannels);
            WriteWav(secondPath, second, GaplessChannels);
            WriteWav(monoPath, new float[GaplessRate / 10], 1);
            problems += CheckChain("decoded by Core Audio", expected,
                (firstPath, AudioReaderFactory.Open(firstPath)), (secondPath, AudioReaderFactory.Open(secondPath)));

            problems += CheckPlayerQueue(firstPath, secondPath, monoPath);
            problems += CheckCrossfade();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"::error::The gapless playback check failed: {ex}");
            problems++;
        }
        finally
        {
            try
            {
                folder.Delete(recursive: true);
            }
            catch (IOException)
            {
                // Left for the system to tidy away.
            }
        }

        return problems;
    }

    // Reads two tracks through a TrackChain in odd-sized pieces and compares what comes out with what went in.
    private static int CheckChain(string how, float[] expected, (string Path, WaveStream Reader) first, (string Path, WaveStream Reader) second)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(GaplessRate, GaplessChannels);
        var firstTrack = ChainedTrack.Create(first.Path, first.Reader, format, allowResampling: false);
        var secondTrack = ChainedTrack.Create(second.Path, second.Reader, format, allowResampling: false);
        if (firstTrack is null || secondTrack is null)
        {
            Console.WriteLine($"::error::Gapless ({how}): two tracks in the same format couldn't be chained.");
            first.Reader.Dispose();
            second.Reader.Dispose();
            return 1;
        }

        var chain = new TrackChain(firstTrack, format);
        var advances = 0;
        chain.Advanced += () => advances++;
        chain.Finished += finished => finished.Dispose();
        chain.SetNext(secondTrack);

        var output = new List<float>(expected.Length);
        var buffer = new float[1002];
        int read;
        while ((read = chain.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.AddRange(buffer.Take(read));
        }

        chain.Current.Dispose();

        var mismatch = Enumerable.Range(0, Math.Min(output.Count, expected.Length))
            .FirstOrDefault(i => Math.Abs(output[i] - expected[i]) > 1e-6f, -1);
        Console.WriteLine($"Gapless ({how}): {output.Count / GaplessChannels} frames from {expected.Length / GaplessChannels}, moved on {advances} time(s).");
        if (output.Count != expected.Length || mismatch >= 0 || advances != 1)
        {
            var at = mismatch >= 0 ? mismatch : Math.Min(output.Count, expected.Length);
            Console.WriteLine($"::error::Gapless ({how}): the second track didn't follow the first sample for sample (first difference at frame {at / GaplessChannels}).");
            return 1;
        }

        return 0;
    }

    // Two steady tones crossfaded by a TrackChain: they overlap by the fade, the old one is let go once,
    // and equal-power curves never push the level past what the two summed would reach.
    private static int CheckCrossfade()
    {
        const double fade = 0.1;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(GaplessRate, GaplessChannels);
        var first = ChainedTrack.Create("first", MemoryTrack(Enumerable.Repeat(0.5f, FirstTrackFrames * GaplessChannels).ToArray()), format, false)!;
        var second = ChainedTrack.Create("second", MemoryTrack(Enumerable.Repeat(-0.25f, SecondTrackFrames * GaplessChannels).ToArray()), format, false)!;
        var chain = new TrackChain(first, format) { CrossfadeSeconds = fade };
        var advances = 0;
        var finished = new List<ChainedTrack>();
        chain.Advanced += () => advances++;
        chain.Finished += track =>
        {
            finished.Add(track);
            track.Dispose();
        };
        chain.SetNext(second);

        var output = new List<float>();
        var buffer = new float[1002];
        int read;
        while ((read = chain.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.AddRange(buffer.Take(read));
        }

        chain.Current.Dispose();
        var frames = output.Count / GaplessChannels;
        var overlap = FirstTrackFrames + SecondTrackFrames - frames;
        var loudest = output.Max(Math.Abs);
        Console.WriteLine($"Crossfade: {frames} frames, overlapping by {overlap}, loudest {loudest:0.000}, moved on {advances} time(s).");
        var problems = 0;
        if (overlap < (fade - 0.03) * GaplessRate || overlap > fade * GaplessRate + 1)
        {
            Console.WriteLine($"::error::Crossfade: the tracks overlapped by {overlap} frames, not about {fade * GaplessRate:0}.");
            problems++;
        }

        if (advances != 1 || finished.Count != 1 || !ReferenceEquals(finished[0], first) || chain.Fading is not null)
        {
            Console.WriteLine($"::error::Crossfade: moved on {advances} time(s) and let go of {finished.Count} track(s).");
            problems++;
        }

        if (Math.Abs(output[0] - 0.5f) > 1e-6f || Math.Abs(output[^1] + 0.25f) > 1e-6f || loudest > 0.5f + 1e-4f)
        {
            Console.WriteLine("::error::Crossfade: the fade didn't run from the old track at full level to the new one at full level.");
            problems++;
        }

        return problems;
    }

    // The player lines a file with the same format up behind the one loaded, and turns down one it can't follow seamlessly.
    private static int CheckPlayerQueue(string firstPath, string secondPath, string monoPath)
    {
        using var player = new AudioPlayer { Gapless = true };
        player.Load(firstPath);
        var problems = 0;
        if (!player.QueueNext(secondPath) || player.QueuedPath != secondPath)
        {
            Console.WriteLine("::error::Gapless: the player wouldn't line up a track in the same format.");
            problems++;
        }

        if (player.QueueNext(monoPath) || player.QueuedPath is not null)
        {
            Console.WriteLine("::error::Gapless: the player lined up a mono track behind a stereo one.");
            problems++;
        }

        player.Gapless = false;
        if (player.QueueNext(secondPath))
        {
            Console.WriteLine("::error::Gapless: the player lined up a track with gapless playback off.");
            problems++;
        }

        if (player.Output is not { Exclusive: false, SampleRate: GaplessRate })
        {
            Console.WriteLine($"::error::Gapless: the player reported the wrong output ({player.Output}).");
            problems++;
        }

        if (problems == 0)
        {
            Console.WriteLine("Gapless: the player lines up the next track, and leaves one it can't follow seamlessly.");
        }

        return problems;
    }

    // Interleaved stereo that carries on counting from one track into the next, so any gap or overlap shows.
    private static float[] Ramp(int startFrame, int frames)
    {
        var samples = new float[frames * GaplessChannels];
        for (var i = 0; i < frames; i++)
        {
            var value = (startFrame + i) % 2000 / 2000f - 0.5f;
            samples[i * GaplessChannels] = value;
            samples[i * GaplessChannels + 1] = -value;
        }

        return samples;
    }

    private static WaveStream MemoryTrack(float[] samples)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        return new RawSourceWaveStream(new MemoryStream(bytes), WaveFormat.CreateIeeeFloatWaveFormat(GaplessRate, GaplessChannels));
    }

    private static void WriteWav(string path, float[] samples, int channels)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(GaplessRate, channels));
        writer.WriteSamples(samples, 0, samples.Length);
    }
}
