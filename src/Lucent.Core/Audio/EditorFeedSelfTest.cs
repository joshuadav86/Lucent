using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// The apps' --self-test check that the visuals and analyzer follow the editor: while an <see cref="EditorFeed"/> is
/// open the capture shows the editor's sound (not the player's or other apps'), at the editor's rate, and goes back
/// to what it showed before once it's closed.
/// </summary>
public static class EditorFeedSelfTest
{
    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log)
    {
        var problems = 0;
        var capture = new AudioCapture();
        capture.Reset(44100);
        capture.BeginExternal(48000);
        var feed = new EditorFeed(new Steady(0.25f, 22050), capture, 120);
        var block = new float[4096];
        feed.Read(block, 0, block.Length);
        capture.Write(new float[] { 1f, 1f, 1f, 1f });
        capture.WriteExternal(new float[] { 0.5f, 0.5f }, new float[] { 0.5f, 0.5f }, new float[] { 0.5f, 0.5f });
        // Reads glide through the newest block at the music's speed, so wait for it to have "played".
        Thread.Sleep(50);
        var heard = new float[16];
        capture.ReadLatest(heard, 0);
        if (!capture.IsEditor || capture.SampleRate != 22050 || capture.DelaySamples != 22050 * 120 / 1000 * 2 / 3
            || heard.Any(sample => sample != 0.25f))
        {
            problems++;
            log($"::error::While the editor played, the visuals saw {string.Join(", ", heard)} at {capture.SampleRate} Hz instead of the editor's sound.");
        }

        feed.Dispose();
        feed.Read(block, 0, block.Length);
        if (capture.IsEditor || !capture.IsExternal || capture.SampleRate != 48000)
        {
            problems++;
            log($"::error::After the editor stopped, the visuals went to {capture.SampleRate} Hz instead of back to other apps' 48000 Hz.");
        }

        capture.EndExternal();
        if (capture.SampleRate != 44100)
        {
            problems++;
            log($"::error::After the editor and other apps, the visuals went to {capture.SampleRate} Hz instead of the player's 44100 Hz.");
        }

        if (problems == 0)
        {
            log("The visuals and analyzer follow the editor while it plays, then go back.");
        }

        return problems;
    }

    /// <summary>A stereo sound at one level, forever.</summary>
    private sealed class Steady : ISampleProvider
    {
        private readonly float _level;

        public Steady(float level, int sampleRate)
        {
            _level = level;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            Array.Fill(buffer, _level, offset, count);
            return count;
        }
    }
}
