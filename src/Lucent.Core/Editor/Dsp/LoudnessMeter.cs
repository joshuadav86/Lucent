namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>
/// Measures integrated loudness in LUFS as defined by ITU-R BS.1770-4 (the measure streaming
/// services use): K-weighting, 400 ms blocks overlapping by 75%, an absolute gate at -70 LUFS
/// and a relative gate 10 LU below the ungated level.
/// </summary>
public static class LoudnessMeter
{
    /// <summary>
    /// Integrated loudness of the audio, or null when it's shorter than one 400 ms block
    /// or too quiet to measure (everything below the absolute gate).
    /// </summary>
    public static double? IntegratedLoudness(AudioBuffer audio, Action<double>? progress = null)
    {
        var rate = audio.SampleRate;
        var blockLength = (int)Math.Round(0.4 * rate);
        var step = (int)Math.Round(0.1 * rate);
        if (audio.Frames < blockLength)
        {
            return null;
        }

        // Mean square of the K-weighted signal in every 100 ms step, per channel. A 400 ms
        // block is then the average of four neighbouring steps.
        var steps = audio.Frames / step;
        var stepPower = new double[steps];
        for (var c = 0; c < audio.ChannelCount; c++)
        {
            // L and R count fully; BS.1770 weights only surround channels differently,
            // and the editor holds at most two channels.
            var filter = new KWeighting(rate);
            var samples = audio.Channels[c];
            for (var s = 0; s < steps; s++)
            {
                double sum = 0;
                var end = (s + 1) * step;
                for (var i = s * step; i < end; i++)
                {
                    var y = filter.Process(samples[i]);
                    sum += y * y;
                }

                stepPower[s] += sum / step;
            }

            progress?.Invoke((c + 1.0) / audio.ChannelCount);
        }

        var stepsPerBlock = blockLength / step;
        var blockCount = steps - stepsPerBlock + 1;
        if (blockCount <= 0)
        {
            return null;
        }

        var blockPower = new double[blockCount];
        for (var b = 0; b < blockCount; b++)
        {
            double sum = 0;
            for (var k = 0; k < stepsPerBlock; k++)
            {
                sum += stepPower[b + k];
            }

            blockPower[b] = sum / stepsPerBlock;
        }

        const double absoluteGate = -70.0;
        var aboveAbsolute = blockPower.Where(p => ToLufs(p) > absoluteGate).ToList();
        if (aboveAbsolute.Count == 0)
        {
            return null;
        }

        var relativeGate = ToLufs(aboveAbsolute.Average()) - 10.0;
        var gated = aboveAbsolute.Where(p => ToLufs(p) > relativeGate).ToList();
        return gated.Count == 0 ? null : ToLufs(gated.Average());
    }

    private static double ToLufs(double power) => power <= 0 ? double.NegativeInfinity : -0.691 + 10 * Math.Log10(power);

    /// <summary>
    /// The two-stage K-weighting filter (a high shelf for the head's effect, then a high pass),
    /// with coefficients derived for any sample rate the same way libebur128 does.
    /// </summary>
    internal sealed class KWeighting
    {
        private readonly Biquad _shelf;
        private readonly Biquad _highPass;

        public KWeighting(int sampleRate)
        {
            {
                const double f0 = 1681.974450955533;
                const double gain = 3.999843853973347;
                const double q = 0.7071752369554196;
                var k = Math.Tan(Math.PI * f0 / sampleRate);
                var vh = Math.Pow(10, gain / 20);
                var vb = Math.Pow(vh, 0.4996667741545416);
                var a0 = 1 + k / q + k * k;
                _shelf = new Biquad(
                    (vh + vb * k / q + k * k) / a0,
                    2 * (k * k - vh) / a0,
                    (vh - vb * k / q + k * k) / a0,
                    2 * (k * k - 1) / a0,
                    (1 - k / q + k * k) / a0);
            }

            {
                const double f0 = 38.13547087602444;
                const double q = 0.5003270373238773;
                var k = Math.Tan(Math.PI * f0 / sampleRate);
                var a0 = 1 + k / q + k * k;
                _highPass = new Biquad(1, -2, 1, 2 * (k * k - 1) / a0, (1 - k / q + k * k) / a0);
            }
        }

        public double Process(double x) => _highPass.Process(_shelf.Process(x));
    }

    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _z1, _z2;

        public Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _a1 = a1;
            _a2 = a2;
        }

        public double Process(double x)
        {
            // Transposed direct form II, in double precision so very low frequencies stay accurate.
            var y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }
    }
}
