namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>The shapes a <see cref="Biquad"/> can take.</summary>
public enum FilterShape
{
    /// <summary>A bell: boosts or cuts around a frequency.</summary>
    Peak,
    /// <summary>Boosts or cuts everything below a frequency.</summary>
    LowShelf,
    /// <summary>Boosts or cuts everything above a frequency.</summary>
    HighShelf,
    /// <summary>Lets through what's above a frequency (removes rumble).</summary>
    HighPass,
    /// <summary>Lets through what's below a frequency (removes hiss).</summary>
    LowPass,
    /// <summary>Takes out a narrow band around a frequency (hum).</summary>
    Notch,
}

/// <summary>
/// A second-order IIR filter, designed with Robert Bristow-Johnson's "Audio EQ Cookbook" formulas and run
/// in transposed direct form II in double precision, so low frequencies at high sample rates stay accurate.
/// </summary>
public sealed class Biquad
{
    private double _b0, _b1, _b2, _a1, _a2;
    private double _z1, _z2;

    public Biquad(FilterShape shape, double frequency, double q, double gainDb, int sampleRate)
    {
        Design(shape, frequency, q, gainDb, sampleRate);
    }

    /// <summary>Changes the filter's settings, keeping what it's holding, so it can be retuned while audio plays.</summary>
    public void Design(FilterShape shape, double frequency, double q, double gainDb, int sampleRate)
    {
        // Keep the frequency safely below Nyquist, where the formulas break down.
        frequency = Math.Clamp(frequency, 1, sampleRate * 0.49);
        q = Math.Max(q, 0.05);
        var w0 = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * q);
        var a = Math.Pow(10, gainDb / 40);
        double b0, b1, b2, a0, a1, a2;
        switch (shape)
        {
            case FilterShape.Peak:
                b0 = 1 + alpha * a;
                b1 = -2 * cos;
                b2 = 1 - alpha * a;
                a0 = 1 + alpha / a;
                a1 = -2 * cos;
                a2 = 1 - alpha / a;
                break;
            case FilterShape.LowShelf:
            {
                var root = 2 * Math.Sqrt(a) * alpha;
                b0 = a * (a + 1 - (a - 1) * cos + root);
                b1 = 2 * a * (a - 1 - (a + 1) * cos);
                b2 = a * (a + 1 - (a - 1) * cos - root);
                a0 = a + 1 + (a - 1) * cos + root;
                a1 = -2 * (a - 1 + (a + 1) * cos);
                a2 = a + 1 + (a - 1) * cos - root;
                break;
            }

            case FilterShape.HighShelf:
            {
                var root = 2 * Math.Sqrt(a) * alpha;
                b0 = a * (a + 1 + (a - 1) * cos + root);
                b1 = -2 * a * (a - 1 + (a + 1) * cos);
                b2 = a * (a + 1 + (a - 1) * cos - root);
                a0 = a + 1 - (a - 1) * cos + root;
                a1 = 2 * (a - 1 - (a + 1) * cos);
                a2 = a + 1 - (a - 1) * cos - root;
                break;
            }

            case FilterShape.HighPass:
                b0 = (1 + cos) / 2;
                b1 = -(1 + cos);
                b2 = (1 + cos) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            case FilterShape.LowPass:
                b0 = (1 - cos) / 2;
                b1 = 1 - cos;
                b2 = (1 - cos) / 2;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
            default:
                b0 = 1;
                b1 = -2 * cos;
                b2 = 1;
                a0 = 1 + alpha;
                a1 = -2 * cos;
                a2 = 1 - alpha;
                break;
        }

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }

    public double Process(double x)
    {
        var y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    /// <summary>Filters samples in place.</summary>
    public void Process(Span<float> samples)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)Process(samples[i]);
        }
    }

    /// <summary>Forgets the audio it has seen, as if starting afresh.</summary>
    public void Reset()
    {
        _z1 = 0;
        _z2 = 0;
    }

    /// <summary>How much the filter changes the level at a frequency, in dB.</summary>
    public double ResponseDb(double frequency, int sampleRate)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var cos1 = Math.Cos(w);
        var cos2 = Math.Cos(2 * w);
        var sin1 = Math.Sin(w);
        var sin2 = Math.Sin(2 * w);
        var numRe = _b0 + _b1 * cos1 + _b2 * cos2;
        var numIm = -(_b1 * sin1 + _b2 * sin2);
        var denRe = 1 + _a1 * cos1 + _a2 * cos2;
        var denIm = -(_a1 * sin1 + _a2 * sin2);
        var magnitude = Math.Sqrt((numRe * numRe + numIm * numIm) / (denRe * denRe + denIm * denIm));
        return 20 * Math.Log10(Math.Max(magnitude, 1e-12));
    }

    /// <summary>
    /// Filters a whole channel forwards and then backwards, so the result has no phase shift (nothing is
    /// delayed or smeared in time), at the price of filtering twice. The ends are mirrored first so the
    /// filter settles before the audio starts.
    /// </summary>
    public static void ZeroPhase(float[] samples, Func<Biquad> design, int settleFrames)
    {
        var pad = Math.Min(settleFrames, Math.Max(0, samples.Length - 1));
        var work = new double[samples.Length + 2 * pad];
        for (var i = 0; i < pad; i++)
        {
            // An odd mirror (2·x0 − x) keeps the waveform's level and slope continuous at the ends.
            work[pad - 1 - i] = 2.0 * samples[0] - samples[i + 1];
            work[pad + samples.Length + i] = 2.0 * samples[^1] - samples[samples.Length - 2 - i];
        }

        for (var i = 0; i < samples.Length; i++)
        {
            work[pad + i] = samples[i];
        }

        var forward = design();
        for (var i = 0; i < work.Length; i++)
        {
            work[i] = forward.Process(work[i]);
        }

        var backward = design();
        for (var i = work.Length - 1; i >= 0; i--)
        {
            work[i] = backward.Process(work[i]);
        }

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (float)work[pad + i];
        }
    }
}
