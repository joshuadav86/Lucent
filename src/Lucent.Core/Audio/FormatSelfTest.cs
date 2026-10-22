using System.IO;
using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// For the self-test: decodes every file in tools/test-media/formats (half a second of a 440 Hz tone with a quiet
/// sweep, in each format and codec people's music comes in) the way the editor does, and checks each comes out as that
/// tone, whole. The same files are decoded on Windows and the Mac, so a format one of them can't read fails the build.
/// </summary>
public static class FormatSelfTest
{
    private const double Seconds = 0.5;
    private const double ToneHz = 440;

    /// <summary>Checks every file in <paramref name="folder"/>. Returns how many failed.</summary>
    public static int Run(Action<string> log, string? folder)
    {
        if (folder is null)
        {
            return 0;
        }

        if (!Directory.Exists(folder))
        {
            log($"::error::No test formats folder at {folder}.");
            return 1;
        }

        var failures = 0;
        var files = Directory.EnumerateFiles(folder).Where(path => !Path.GetFileName(path).StartsWith('.')).Order(StringComparer.Ordinal).ToList();
        foreach (var path in files)
        {
            var name = Path.GetFileName(path);
            // QuickTime and MP4 files are videos to the player, but their sound opens the same way for the editor.
            if (!Video.VideoFiles.IsPlayable(path))
            {
                failures++;
                log($"::error::Format {name}: its extension isn't in the list of music Lucent opens.");
                continue;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var reader = AudioReaderFactory.Open(path);
                var (seconds, rms, hz) = Measure(reader);
                var line = $"Format {name}: {reader.GetType().Name}, {reader.WaveFormat.SampleRate} Hz, {reader.WaveFormat.Channels} ch, "
                    + $"{seconds:0.000} s, level {rms:0.00}, {hz:0} Hz, in {timer.ElapsedMilliseconds} ms";
                // Lossy formats add their encoder's padding (MP3's is the most, about 75 ms). libVLC's WMA decoder (the
                // Mac's) leaves off the last packet or two, which can't be had back: about 80 ms of silence, in a song.
                var shortest = Path.GetExtension(path).Equals(".wma", StringComparison.OrdinalIgnoreCase) ? Seconds - 0.1 : Seconds - 0.04;
                // Speex is made for voices, and doesn't keep a tone's pitch: it only has to come out whole.
                var pitchKept = !Path.GetExtension(path).Equals(".spx", StringComparison.OrdinalIgnoreCase);
                if (seconds < shortest || seconds > Seconds + 0.1 || rms < 0.15 || (pitchKept && Math.Abs(hz - ToneHz) > ToneHz * 0.05))
                {
                    failures++;
                    log($"::error::{line}: not the half-second 440 Hz tone it holds.");
                }
                else
                {
                    log(line);
                }
            }
            catch (Exception ex)
            {
                failures++;
                log($"::error::Format {name} couldn't be decoded: {ex.GetType().Name}: {ex.Message}");
            }
        }

        log($"Decoded {files.Count - failures} of {files.Count} test formats.");
        return failures;
    }

    /// <summary>How long the first channel lasts, its level (RMS), and the tone's pitch, from its zero crossings.</summary>
    private static (double Seconds, double Rms, double Hz) Measure(WaveStream reader)
    {
        var samples = reader.ToSampleProvider();
        var channels = samples.WaveFormat.Channels;
        var chunk = new float[8192 * channels];
        long frames = 0, loud = 0, cycles = 0;
        var sum = 0.0;
        var high = false;
        int read;
        while ((read = samples.Read(chunk, 0, chunk.Length)) > 0)
        {
            for (var i = 0; i < read; i += channels)
            {
                var sample = chunk[i];
                frames++;
                sum += sample * sample;
                // Counted with hysteresis, so the sweep and codec noise near zero don't add crossings.
                if (!high && sample > 0.2f)
                {
                    high = true;
                    cycles++;
                }
                else if (high && sample < -0.2f)
                {
                    high = false;
                }

                if (Math.Abs(sample) > 0.2f)
                {
                    loud++;
                }
            }
        }

        var rate = samples.WaveFormat.SampleRate;
        var seconds = (double)frames / rate;
        // The pitch is measured over the part that sounds, leaving out codecs' silent priming.
        var sounding = loud > 0 ? Math.Min(seconds, Seconds) : 0;
        return (seconds, frames > 0 ? Math.Sqrt(sum / frames) : 0, sounding > 0 ? cycles / sounding : 0);
    }
}
