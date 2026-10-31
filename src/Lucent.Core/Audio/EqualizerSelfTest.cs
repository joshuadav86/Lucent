using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>
/// The apps' --self-test check that the equalizer really changes the player's music: a tone at a band's centre comes
/// out about as much louder or quieter as its slider says, at every band count, moving a slider mid-song takes effect,
/// and switched off it leaves the music untouched.
/// </summary>
public static class EqualizerSelfTest
{
    private const int SampleRate = 48000;

    // Quiet enough that a full boost stays well clear of the headroom and the limiter.
    private const double ToneDb = -30;

    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log)
    {
        var problems = 0;
        void Check(bool good, string problem)
        {
            if (!good)
            {
                problems++;
                log("::error::" + problem);
            }
        }

        static string Show(double value) => value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);

        foreach (var count in EqualizerBands.Counts)
        {
            var bands = EqualizerBands.ForCount(count);
            // Bands in from the ends: the ends are shelves, and the top one sits close to what the sample rate can hold.
            foreach (var band in new[] { 1, count / 2, count - 2 }.Distinct())
            {
                var hertz = bands.Centres[band];
                foreach (var gainDb in new[] { -12.0, 12.0 })
                {
                    var gains = new double[count];
                    gains[band] = gainDb;
                    var change = LevelDb(hertz, bands, gains, enabled: true) - LevelDb(hertz, bands, gains, enabled: false);
                    Check(Math.Abs(change - gainDb) < 3,
                        $"With {count} bands, {Show(gainDb)} dB at {bands.Labels[band]} changed a tone there by {Show(change)} dB.");
                }
            }

            var flat = LevelDb(1000, bands, new double[count], enabled: false) - ToneDb;
            Check(Math.Abs(flat) < 0.1, $"With {count} bands switched off, the equalizer changed a tone by {Show(flat)} dB.");
        }

        // Moving a slider while a song plays: the same equalizer, already attached, takes the new setting.
        var eight = EqualizerBands.ForCount(8);
        var equalizer = new Equalizer();
        equalizer.Configure(true, eight, new double[8], 0);
        var music = equalizer.Attach(new Tone(eight.Centres[2], SampleRate));
        var before = Level(music, 0.5);
        var cut = new double[8];
        cut[2] = -12;
        equalizer.Configure(true, eight, cut, 0);
        Level(music, 0.3);
        var after = Level(music, 0.5);
        Check(after - before < -9, $"Moving a slider to -12 dB mid-song changed the music by {Show(after - before)} dB.");

        if (problems == 0)
        {
            log("The equalizer turns each band up and down by what its slider says.");
        }

        return problems;
    }

    // The level of a tone after the equalizer, in dBFS, once the filters have settled.
    private static double LevelDb(double hertz, EqualizerBands bands, double[] gainsDb, bool enabled)
    {
        var equalizer = new Equalizer();
        equalizer.Configure(enabled, bands, gainsDb, 0);
        var music = equalizer.Attach(new Tone(hertz, SampleRate));
        Level(music, 0.5);
        return Level(music, 0.5);
    }

    // Reads this many seconds and returns their level in dBFS, as a sine's peak level.
    private static double Level(ISampleProvider music, double seconds)
    {
        var channels = music.WaveFormat.Channels;
        var buffer = new float[1024 * channels];
        var left = (int)(seconds * music.WaveFormat.SampleRate) * channels;
        double sum = 0;
        var samples = 0;
        while (left > 0)
        {
            var read = music.Read(buffer, 0, Math.Min(buffer.Length, left));
            if (read <= 0)
            {
                break;
            }

            for (var i = 0; i < read; i++)
            {
                sum += buffer[i] * (double)buffer[i];
            }

            samples += read;
            left -= read;
        }

        return 10 * Math.Log10(Math.Max(1e-20, 2 * sum / Math.Max(1, samples)));
    }

    // An endless stereo sine at ToneDb.
    private sealed class Tone : ISampleProvider
    {
        private readonly double _step;
        private readonly float _amplitude = (float)Math.Pow(10, ToneDb / 20);
        private double _phase;

        public Tone(double hertz, int sampleRate)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
            _step = 2 * Math.PI * hertz / sampleRate;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            for (var i = offset; i + 1 < offset + count; i += 2)
            {
                buffer[i] = buffer[i + 1] = _amplitude * (float)Math.Sin(_phase);
                _phase += _step;
            }

            return count;
        }
    }
}
