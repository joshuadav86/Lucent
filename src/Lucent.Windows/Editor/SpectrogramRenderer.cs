using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lucent.Editor.Dsp;

namespace Lucent.Editor;

/// <summary>
/// Paints the editor's spectrogram: for every pixel column of the view, the frequencies at that moment,
/// low at the bottom and high at the top on a musical (logarithmic) scale, coloured by loudness. Only the
/// visible part is worked out, in the background on every core, so it keeps up with scrolling and zooming.
/// </summary>
public sealed class SpectrogramRenderer
{
    public const double LowestHz = 20;

    /// <summary>The loudness range shown, from black to white, in dB below full scale.</summary>
    public const double FloorDb = -110;

    private static readonly uint[] Palette = BuildPalette();

    /// <summary>One channel's picture of part of the audio, and the view it was drawn for.</summary>
    public sealed record Image(BitmapSource Bitmap, AudioBuffer Audio, int Channel, double ViewStart, double SamplesPerPixel, int Width, int Height);

    /// <summary>Paints one channel's lane. Runs on any thread; the bitmap comes back frozen.</summary>
    public static Image Render(AudioBuffer audio, int channel, double viewStart, double samplesPerPixel, int width, int height,
        CancellationToken cancellation)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        var rate = audio.SampleRate;
        // Finer frequency detail at high sample rates, so the bass stays as sharp in time and pitch.
        var size = rate > 64000 ? 4096 : 2048;
        var bins = size / 2;
        var highest = rate / 2.0;

        // Which FFT bins each row covers, bottom row lowest.
        var rowLow = new double[height];
        var rowHigh = new double[height];
        for (var y = 0; y < height; y++)
        {
            var top = Frequency(height - y, height, highest);
            var bottom = Frequency(height - y - 1, height, highest);
            rowLow[y] = bottom / rate * size;
            rowHigh[y] = top / rate * size;
        }

        var window = AudioAnalysis.Hann(size);
        // A full-scale sine reads 0 dB.
        var reference = window.Sum() / 2;
        var pixels = new uint[width * height];
        var samples = audio.Channels[channel];
        var options = new ParallelOptions { CancellationToken = cancellation };
        Parallel.For(0, width, options, () => (new Fft(size), new double[size], new double[size], new double[bins + 1]), (x, _, local) =>
        {
            var (fft, re, im, power) = local;
            Array.Clear(power);
            // Zoomed out, one column covers a lot of audio: average a few looks across it so nothing is skipped.
            var columnStart = viewStart + x * samplesPerPixel;
            var looks = (int)Math.Clamp(samplesPerPixel / (size / 2.0), 1, 4);
            var inside = false;
            for (var look = 0; look < looks; look++)
            {
                var centre = (long)(columnStart + samplesPerPixel * (look + 0.5) / looks);
                if (centre < 0 || centre >= samples.Length)
                {
                    continue;
                }

                inside = true;
                var start = centre - size / 2;
                for (var n = 0; n < size; n++)
                {
                    var i = start + n;
                    re[n] = i >= 0 && i < samples.Length ? samples[i] * window[n] : 0;
                    im[n] = 0;
                }

                fft.Transform(re, im, inverse: false);
                for (var b = 0; b <= bins; b++)
                {
                    power[b] += (re[b] * re[b] + im[b] * im[b]) / looks;
                }
            }

            if (!inside)
            {
                return local;
            }

            for (var y = 0; y < height; y++)
            {
                // High rows span many bins (take the loudest); low rows fall between bins (interpolate).
                var low = rowLow[y];
                var high = rowHigh[y];
                double value;
                if (high - low >= 1)
                {
                    value = 0;
                    for (var b = (int)Math.Floor(low); b <= Math.Min(bins, (int)Math.Ceiling(high)); b++)
                    {
                        value = Math.Max(value, power[b]);
                    }
                }
                else
                {
                    var at = Math.Min(bins - 1.0, (low + high) / 2);
                    var b = (int)at;
                    value = power[b] + (power[b + 1] - power[b]) * (at - b);
                }

                var db = 10 * Math.Log10(Math.Max(value, 1e-30) / (reference * reference));
                var level = Math.Clamp((db - FloorDb) / -FloorDb, 0, 1);
                pixels[y * width + x] = Palette[(int)(level * (Palette.Length - 1))];
            }

            return local;
        }, _ => { });

        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return new Image(bitmap, audio, channel, viewStart, samplesPerPixel, width, height);
    }

    /// <summary>The frequency at a height up a lane (0 is the bottom), on the log scale from <see cref="LowestHz"/> to <paramref name="highest"/>.</summary>
    public static double Frequency(double fromBottom, double height, double highest) =>
        LowestHz * Math.Pow(highest / LowestHz, fromBottom / height);

    /// <summary>How far up a lane a frequency is drawn, from 0 at the bottom to <paramref name="height"/> at the top.</summary>
    public static double Height(double frequency, double height, double highest) =>
        height * Math.Log(frequency / LowestHz) / Math.Log(highest / LowestHz);

    /// <summary>Black, through deep violet, magenta and orange, to pale yellow: dark for quiet, bright for loud.</summary>
    private static uint[] BuildPalette()
    {
        (double At, Color Color)[] stops =
        {
            (0.00, Color.FromRgb(0x00, 0x00, 0x04)),
            (0.20, Color.FromRgb(0x1B, 0x0C, 0x41)),
            (0.40, Color.FromRgb(0x5D, 0x12, 0x6E)),
            (0.58, Color.FromRgb(0x9C, 0x2E, 0x7F)),
            (0.74, Color.FromRgb(0xE1, 0x52, 0x4F)),
            (0.88, Color.FromRgb(0xFB, 0x9B, 0x06)),
            (1.00, Color.FromRgb(0xFC, 0xFF, 0xA4)),
        };

        var palette = new uint[256];
        for (var i = 0; i < palette.Length; i++)
        {
            var t = i / 255.0;
            var k = 1;
            while (k < stops.Length - 1 && stops[k].At < t)
            {
                k++;
            }

            var (a, ca) = stops[k - 1];
            var (b, cb) = stops[k];
            var f = (t - a) / (b - a);
            byte Mix(byte x, byte y) => (byte)Math.Round(x + (y - x) * f);
            palette[i] = 0xFF000000u | (uint)Mix(ca.R, cb.R) << 16 | (uint)Mix(ca.G, cb.G) << 8 | Mix(ca.B, cb.B);
        }

        return palette;
    }
}
