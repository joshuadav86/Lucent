using System.IO;
using System.Net;
using System.Net.Http;
using NAudio.Wave;
using Lucent.Audio;

namespace Lucent.Recording;

/// <summary>
/// The apps' --self-test check of the Record window's insides, with made-up sound instead of an audio
/// interface (CI machines have none): two "songs" from a four-channel input at 44.1 kHz, recorded from its
/// third and fourth channels at 48 kHz, with a mark made by hand. The file has to read back as a WAV at the
/// right rate and length, with the marks as cue points, a .cue sheet and a dry copy beside it, and the
/// visuals have to have followed the input and gone back afterwards. Lucent hearing the song change is a
/// guess, so missing it is only a warning. Song identification is checked against a stand-in for
/// ACRCloud that tells the two songs apart by their notes, and checks each request is signed properly.
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
            var acrCloud = new FakeAcrCloud();
            var key = new AcrCloudKey("https://identify-test.acrcloud.com/", "test-key", "test-secret");
            using var http = new HttpClient(acrCloud);
            using (var recorder = new LiveRecorder(capture, 48000, 2) { BitDepth = 24, Identifier = new SongIdentifier(http, key) })
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

                var heldBack = RecordingsInProgress.Contains(path);
                if (!recorder.StopAsync().Wait(TimeSpan.FromSeconds(30)))
                {
                    problems++;
                    log("::error::Recording: the file took more than 30 seconds to finish.");
                }

                if (!heldBack || RecordingsInProgress.Contains(path))
                {
                    problems++;
                    log("::error::Recording: the file wasn't kept out of the library while it was being written, or was still kept out afterwards.");
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

            // The first song from 0 s, the second from the mark at 150 s (and from the heard change, if it was heard).
            var named = marks.Where(m => m.Start == TimeSpan.Zero || Math.Abs(m.Start.TotalSeconds - 150) < 1).Select(m => m.Title).ToList();
            if (acrCloud.Problem is { } badRequest)
            {
                problems++;
                log($"::error::Recording: a song lookup was sent wrongly: {badRequest}");
            }
            else if (named.Count != 2 || named[0] != "Test Artist - Song A" || named[1] != "Test Artist - Song B")
            {
                problems++;
                log($"::error::Recording: the songs weren't named from ACRCloud's answers ({acrCloud.Requests} lookups): {string.Join(", ", marks.Select(m => $"{m.Title} at {m.Start.TotalSeconds:0.0} s"))}.");
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

            problems += CheckAnswers(log);
            problems += CheckMonoMix(folder, log);

            if (problems == 0)
            {
                log($"Recording: saved {Path.GetFileName(file)} from a made-up input with {marks.Count} tracks marked: {string.Join(", ", marks.Select(m => $"{m.Title} at {m.Start.TotalSeconds:0.0} s"))} ({acrCloud.Requests} song lookups).");
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

    // A pair mixed to mono: inputs 1 and 2 at 0.4 and 0.2 make a one-channel file at 0.3.
    private static int CheckMonoMix(string folder, Action<string> log)
    {
        var capture = new AudioCapture();
        capture.Reset(48000);
        var path = Path.Combine(folder, "mono mix.wav");
        using (var recorder = new LiveRecorder(capture, 48000, 1) { BitDepth = 32, SumPair = true, DetectSongChanges = false })
        {
            recorder.Start(new RecordingTarget(path, "mono mix", null, CueSheet: false));
            var block = Enumerable.Range(0, 480 * 2).Select(i => i % 2 == 0 ? 0.4f : 0.2f).ToArray();
            for (var b = 0; b < 100; b++)
            {
                recorder.Write(block, 480, 2, 0, 48000);
            }

            recorder.StopAsync().Wait(TimeSpan.FromSeconds(30));
        }

        using var reader = new WaveFileReader(path);
        var samples = new float[4800];
        var read = reader.ToSampleProvider().Read(samples, 0, samples.Length);
        if (reader.WaveFormat.Channels != 1 || read == 0 || samples.Take(read).Any(x => Math.Abs(x - 0.3f) > 0.001f))
        {
            log($"::error::Recording: inputs 1 + 2 mixed to mono came out as {reader.WaveFormat.Channels} channel(s), first sample {samples[0]:0.000} (wanted 1 channel at 0.300).");
            return 1;
        }

        return 0;
    }

    // ACRCloud's other answers: not known, a wrong key, and nonsense.
    private static int CheckAnswers(Action<string> log)
    {
        var unknown = SongIdentifier.Read("""{"status":{"msg":"No result","code":1001,"version":"1.0"}}""");
        var badKey = SongIdentifier.Read("""{"status":{"msg":"Invalid access key","code":3001,"version":"1.0"}}""");
        var nonsense = SongIdentifier.Read("<html>Bad gateway</html>");
        if (unknown.Song is not null || unknown.Problem is not null || !badKey.KeyProblem || nonsense.Problem is null)
        {
            log("::error::Recording: ACRCloud's 'no result', 'invalid key' or an unreadable answer was taken the wrong way.");
            return 1;
        }

        var key = AcrCloudKey.FromText(new AcrCloudKey("identify-eu-west-1.acrcloud.com", "a", "b\"c").ToText());
        if (key is null || key.AccessSecret != "b\"c" || AcrCloudKey.FromText("not a key") is not null)
        {
            log("::error::Recording: an ACRCloud key didn't survive being saved and read back.");
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Stands in for ACRCloud: checks the request is put together as ACRCloud's protocol has it (signed with the
    /// secret), then names the song by its notes: 220 Hz is Song A, 311 Hz is Song B.
    /// </summary>
    private sealed class FakeAcrCloud : HttpMessageHandler
    {
        private int _requests;

        public int Requests => Volatile.Read(ref _requests);

        public string? Problem { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancel)
        {
            Interlocked.Increment(ref _requests);
            var fields = new Dictionary<string, byte[]>();
            if (request.Content is MultipartFormDataContent form)
            {
                foreach (var part in form)
                {
                    fields[part.Headers.ContentDisposition?.Name?.Trim('"') ?? string.Empty] = await part.ReadAsByteArrayAsync(cancel);
                }
            }

            string Text(string name) => fields.TryGetValue(name, out var value) ? System.Text.Encoding.UTF8.GetString(value) : string.Empty;
            var expected = SongIdentifier.Sign(new AcrCloudKey("x", "test-key", "test-secret"), Text("timestamp"));
            if (request.RequestUri?.ToString() != "https://identify-test.acrcloud.com/v1/identify" || Text("access_key") != "test-key"
                || Text("signature") != expected || Text("data_type") != "audio" || Text("signature_version") != "1"
                || !fields.TryGetValue("sample", out var wav) || Text("sample_bytes") != wav.Length.ToString(System.Globalization.CultureInfo.InvariantCulture))
            {
                Problem = $"to {request.RequestUri} with fields {string.Join(", ", fields.Keys)}";
                return Answer("""{"status":{"msg":"Invalid signature","code":3014,"version":"1.0"}}""");
            }

            using var reader = new WaveFileReader(new MemoryStream(wav));
            var samples = new float[(int)(reader.SampleCount)];
            reader.ToSampleProvider().Read(samples, 0, samples.Length);
            var song = Strength(samples, reader.WaveFormat.SampleRate, 220) > Strength(samples, reader.WaveFormat.SampleRate, 311) ? "Song A" : "Song B";
            return Answer($$$"""{"status":{"msg":"Success","code":0,"version":"1.0"},"metadata":{"music":[{"title":"{{{song}}}","artists":[{"name":"Test Artist"}],"album":{"name":"Test Album"},"score":100}]}}""");
        }

        private static HttpResponseMessage Answer(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

        // How much of one frequency there is (Goertzel).
        private static double Strength(float[] samples, int rate, double hertz)
        {
            var coefficient = 2 * Math.Cos(2 * Math.PI * hertz / rate);
            double previous = 0, before = 0;
            foreach (var sample in samples)
            {
                var now = sample + coefficient * previous - before;
                before = previous;
                previous = now;
            }

            return previous * previous + before * before - coefficient * previous * before;
        }
    }
}
