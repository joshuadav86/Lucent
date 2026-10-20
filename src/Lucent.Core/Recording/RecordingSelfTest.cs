using System.IO;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Recording;

/// <summary>
/// The apps' --self-test check of the Record window's insides, with made-up sound instead of an audio
/// interface (CI machines have none): two "songs" from a four-channel input at 44.1 kHz, recorded from its
/// third and fourth channels at 48 kHz, with a mark made by hand. The file has to read back as a WAV at the
/// right rate and length, with the marks as cue points, a .cue sheet and a dry copy beside it, and the
/// visuals have to have followed the input and gone back afterwards. Lucent hearing the song change is a
/// guess, so missing it is only a warning.
/// </summary>
public static class RecordingSelfTest
{
    /// <summary>
    /// Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).
    /// <paramref name="decode"/> is the app's own check that it can play a file, run on the recording.
    /// </summary>
    public static int Run(Action<string> log, Func<string, bool> decode)
    {
        var folder = Path.Combine(Path.GetTempPath(), "lucent-recording-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        var problems = 0;
        try
        {
            const int inputRate = 44100;
            const int songSeconds = 100;
            var capture = new AudioCapture();
            capture.Reset(44100);
            using (var recorder = new LiveRecorder(capture, 48000, 2) { BitDepth = 24 })
            {
                if (!capture.IsLive || capture.SampleRate != 48000)
                {
                    problems++;
                    log("::error::Recording: the visuals didn't switch to the live input.");
                }

                var path = RecordingNames.NextPath(folder, "{name} {take}", new RecordingNameParts("Self-test: set", DateTime.Now, 48000, 24, "Test"));
                recorder.Start(new RecordingTarget(path, "Self-test set", RecordingNames.DryPath(path), CueSheet: true));
                var random = new Random(7);
                var block = new float[441 * 4];
                long frame = 0;
                for (var b = 0; b < inputRate * songSeconds * 2 / 441; b++)
                {
                    for (var f = 0; f < 441; f++, frame++)
                    {
                        var t = (double)frame / inputRate;
                        var sample = t < songSeconds
                            ? 0.3 * Math.Sin(2 * Math.PI * 220 * t) + 0.2 * Math.Sin(2 * Math.PI * 277 * t) + 0.05 * (random.NextDouble() - 0.5)
                            : 0.3 * Math.Sin(2 * Math.PI * 311 * t) + 0.2 * Math.Sin(2 * Math.PI * 466 * t) + 0.2 * (random.NextDouble() - 0.5);
                        block[f * 4] = 0.9f;
                        block[f * 4 + 1] = -0.9f;
                        block[f * 4 + 2] = (float)sample;
                        block[f * 4 + 3] = (float)(sample * 0.5);
                    }

                    recorder.Write(block, 441, 4, 2, inputRate);
                    if (b == inputRate * 150 / 441)
                    {
                        // Marks go through the same queue as the sound, so this lands 150 s in.
                        recorder.MarkTrack();
                    }

                    while (recorder.Queued > TimeSpan.FromSeconds(5))
                    {
                        Thread.Sleep(1);
                    }
                }

                if (!recorder.StopAsync().Wait(TimeSpan.FromSeconds(30)))
                {
                    problems++;
                    log("::error::Recording: the file took more than 30 seconds to finish.");
                }

                if (recorder.Dropped > TimeSpan.Zero)
                {
                    problems++;
                    log($"::error::Recording: {recorder.Dropped.TotalSeconds:0.0} s of sound were let go.");
                }
            }

            if (capture.IsLive || capture.SampleRate != 44100)
            {
                problems++;
                log("::error::Recording: the visuals didn't go back to the player after the Record window closed.");
            }

            var file = Directory.GetFiles(folder, "*.wav").FirstOrDefault(f => !f.Contains("(dry)", StringComparison.Ordinal));
            if (file is null)
            {
                log("::error::Recording: no WAV file was saved.");
                return problems + 1;
            }

            using (var reader = new WaveFileReader(file))
            {
                var seconds = reader.TotalTime.TotalSeconds;
                var samples = reader.ToSampleProvider();
                var buffer = new float[48000 * 2];
                samples.Read(buffer, 0, buffer.Length);
                var left = buffer.Skip(48000).Where((_, i) => i % 2 == 0).Max(Math.Abs);
                var right = buffer.Skip(48000).Where((_, i) => i % 2 == 1).Max(Math.Abs);
                if (reader.WaveFormat.SampleRate != 48000 || reader.WaveFormat.BitsPerSample != 24 || Math.Abs(seconds - songSeconds * 2) > 1
                    || left > 0.6 || Math.Abs(right * 2 - left) > 0.05)
                {
                    problems++;
                    log($"::error::Recording: the file came out as {reader.WaveFormat}, {seconds:0.0} s, peaks {left:0.00}/{right:0.00} (wanted 48 kHz 24-bit, {songSeconds * 2} s, channels 3 and 4).");
                }
            }

            var marks = TrackMarkers.Read(file);
            if (marks.Count < 2 || marks[0].Start != TimeSpan.Zero || !marks.Any(m => Math.Abs(m.Start.TotalSeconds - 150) < 1))
            {
                problems++;
                log($"::error::Recording: the track marks read back as {string.Join(", ", marks.Select(m => $"{m.Title} at {m.Start.TotalSeconds:0.0} s"))}.");
            }

            if (!marks.Any(m => Math.Abs(m.Start.TotalSeconds - songSeconds) < 5))
            {
                log($"::warning::Recording: Lucent didn't hear the song change at {songSeconds} s (marks: {string.Join(", ", marks.Select(m => $"{m.Start.TotalSeconds:0.0} s"))}).");
            }

            if (!File.Exists(Path.ChangeExtension(file, ".cue")) || !File.Exists(RecordingNames.DryPath(file)))
            {
                problems++;
                log("::error::Recording: the .cue sheet or the copy without plugins is missing.");
            }

            if (!decode(file))
            {
                problems++;
            }

            if (problems == 0)
            {
                log($"Recording: saved {Path.GetFileName(file)} from a made-up input with {marks.Count} tracks marked: {string.Join(", ", marks.Select(m => $"{m.Start.TotalSeconds:0.0} s"))}.");
            }
        }
        catch (Exception ex)
        {
            problems++;
            log($"::error::Recording failed: {ex}");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (Exception)
            {
                // Only a temporary folder.
            }
        }

        return problems;
    }
}
