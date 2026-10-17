namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>
/// Changes pitch without changing length: a phase vocoder stretches the audio in time,
/// then a band-limited resample squeezes it back to its original length, which moves the pitch.
/// </summary>
/// <remarks>
/// Uses identity phase locking (Laroche and Dolson, 1999): each spectral peak's phase advances
/// at its measured frequency and the bins around it keep their phase relationship to it, which
/// avoids most of the "phasey", washed-out sound of a plain phase vocoder. Left and right get the
/// same phase changes, worked out from their sum, so the stereo image stays where it was.
/// </remarks>
public static class PitchShifter
{
    public static AudioBuffer Shift(AudioBuffer input, double semitones, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        if (Math.Abs(semitones) < 1e-6 || input.Frames == 0)
        {
            return input;
        }

        var ratio = Math.Pow(2, semitones / 12);
        var stretched = Stretch(input, ratio, fraction => progress?.Invoke(fraction * 0.75), cancellation, out var padding);

        // Read the stretched audio back `ratio` times faster: the pitch moves and the length returns to the original.
        var kernel = new SincKernel(ratio);
        var output = new float[input.ChannelCount][];
        for (var c = 0; c < input.ChannelCount; c++)
        {
            cancellation.ThrowIfCancellationRequested();
            var source = stretched[c];
            var result = new float[input.Frames];
            Parallel.For(0, (input.Frames + 4095) / 4096, block =>
            {
                var end = Math.Min(input.Frames, block * 4096 + 4096);
                for (var i = block * 4096; i < end; i++)
                {
                    result[i] = kernel.Sample(source, 0, source.Length, (i + padding) * ratio);
                }
            });
            output[c] = result;
            progress?.Invoke(0.75 + 0.25 * (c + 1) / input.ChannelCount);
        }

        return new AudioBuffer(output, input.SampleRate);
    }

    /// <summary>
    /// Changes the length by <paramref name="ratio"/> (2 = twice as long, half the tempo) without changing the pitch.
    /// </summary>
    public static AudioBuffer TimeStretch(AudioBuffer input, double ratio, Action<double>? progress = null,
        CancellationToken cancellation = default)
    {
        if (Math.Abs(ratio - 1) < 1e-6 || input.Frames == 0)
        {
            return input;
        }

        var stretched = Stretch(input, ratio, progress ?? (_ => { }), cancellation, out var padding);
        // Input frame i landed at (i + padding) * ratio.
        var start = (int)Math.Round(padding * ratio);
        var frames = (int)Math.Round(input.Frames * ratio);
        var output = stretched.Select(channel =>
        {
            var result = new float[frames];
            Array.Copy(channel, start, result, 0, Math.Min(frames, channel.Length - start));
            return result;
        }).ToArray();
        return new AudioBuffer(output, input.SampleRate);
    }

    /// <summary>
    /// Time-stretches by <paramref name="ratio"/> (2 = twice as long) without changing pitch.
    /// The input is treated as having <paramref name="padding"/> frames of silence in front, so
    /// input frame i lands at output position (i + padding) * ratio.
    /// </summary>
    /// <remarks>
    /// Frames are handled in batches: the FFTs of a batch run on all cores, and only the cheap
    /// phase bookkeeping, which depends on the previous frame, runs one frame after another.
    /// </remarks>
    private static float[][] Stretch(AudioBuffer input, double ratio, Action<double> progress,
        CancellationToken cancellation, out int padding)
    {
        // About 90 ms windows: fine enough frequency detail for music at any common sample rate.
        var size = input.SampleRate > 64000 ? 8192 : 4096;
        var bins = size / 2 + 1;
        padding = size;

        // Both hops stay at or below a quarter window, so frames always overlap by at least 75%.
        var quarter = size / 4.0;
        var analysisHop = ratio >= 1 ? quarter / ratio : quarter;
        var synthesisHop = analysisHop * ratio;

        var fft = new Fft(size);
        var window = new double[size];
        for (var n = 0; n < size; n++)
        {
            window[n] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / size);
        }

        var stereo = input.ChannelCount > 1;
        var left = input.Channels[0];
        var right = stereo ? input.Channels[1] : left;
        var padded = padding;
        var paddedLength = (long)input.Frames + 2L * padding;
        var frameCount = (int)Math.Ceiling((paddedLength - size) / analysisHop) + 1;
        var outputLength = (int)Math.Round((frameCount - 1) * synthesisHop) + size;

        var outLeft = new float[outputLength];
        var outRight = stereo ? new float[outputLength] : outLeft;
        var norm = new float[outputLength];

        const int batchSize = 32;
        var frames = Enumerable.Range(0, batchSize).Select(_ => new FrameData(size, bins)).ToArray();
        var previousPhase = new double[bins];
        var synthPhase = new double[bins];
        var peaks = new List<int>();
        long previousAnalysis = 0, previousSynthesis = 0;

        for (var batchStart = 0; batchStart < frameCount; batchStart += batchSize)
        {
            cancellation.ThrowIfCancellationRequested();
            progress((double)batchStart / frameCount);
            var count = Math.Min(batchSize, frameCount - batchStart);

            // 1. Analysis, in parallel: window, one FFT for both channels, unpack, measure the sum's spectrum.
            Parallel.For(0, count, slot =>
            {
                var f = frames[slot];
                var k = batchStart + slot;
                f.AnalysisAt = (long)Math.Round(k * analysisHop);
                f.SynthesisAt = (long)Math.Round(k * synthesisHop);
                for (var n = 0; n < size; n++)
                {
                    var index = f.AnalysisAt + n - padded;
                    var inside = index >= 0 && index < left.Length;
                    f.Re[n] = inside ? left[index] * window[n] : 0;
                    f.Im[n] = inside && stereo ? right[index] * window[n] : 0;
                }

                fft.Transform(f.Re, f.Im, inverse: false);
                for (var b = 0; b < bins; b++)
                {
                    var mirror = b == 0 ? 0 : size - b;
                    f.LeftRe[b] = 0.5 * (f.Re[b] + f.Re[mirror]);
                    f.LeftIm[b] = 0.5 * (f.Im[b] - f.Im[mirror]);
                    f.RightRe[b] = 0.5 * (f.Im[b] + f.Im[mirror]);
                    f.RightIm[b] = -0.5 * (f.Re[b] - f.Re[mirror]);
                    var sumRe = f.LeftRe[b] + (stereo ? f.RightRe[b] : 0);
                    var sumIm = f.LeftIm[b] + (stereo ? f.RightIm[b] : 0);
                    f.Magnitude[b] = Math.Sqrt(sumRe * sumRe + sumIm * sumIm);
                    f.Phase[b] = Math.Atan2(sumIm, sumRe);
                }
            });

            // 2. Phase bookkeeping, frame by frame: each depends on the one before.
            for (var slot = 0; slot < count; slot++)
            {
                var f = frames[slot];
                var phase = f.Phase;
                if (batchStart + slot == 0)
                {
                    Array.Copy(phase, synthPhase, bins);
                }
                else
                {
                    var hopIn = f.AnalysisAt - previousAnalysis;
                    var hopOut = f.SynthesisAt - previousSynthesis;
                    FindPeaks(f.Magnitude, peaks);
                    if (peaks.Count == 0)
                    {
                        for (var b = 0; b < bins; b++)
                        {
                            synthPhase[b] = AdvancePhase(b, size, phase[b], previousPhase[b], synthPhase[b], hopIn, hopOut);
                        }
                    }
                    else
                    {
                        // Advance each peak at its own frequency, then carry its neighbours along with it.
                        var regionStart = 0;
                        for (var p = 0; p < peaks.Count; p++)
                        {
                            var peak = peaks[p];
                            var regionEnd = p + 1 < peaks.Count ? LowestBetween(f.Magnitude, peak, peaks[p + 1]) : bins;
                            var peakPhase = AdvancePhase(peak, size, phase[peak], previousPhase[peak], synthPhase[peak], hopIn, hopOut);
                            for (var b = regionStart; b < regionEnd; b++)
                            {
                                synthPhase[b] = b == peak ? peakPhase : peakPhase + phase[b] - phase[peak];
                            }

                            regionStart = regionEnd;
                        }
                    }
                }

                for (var b = 0; b < bins; b++)
                {
                    synthPhase[b] = Wrap(synthPhase[b]);
                    f.Rotation[b] = synthPhase[b] - phase[b];
                    previousPhase[b] = phase[b];
                }

                previousAnalysis = f.AnalysisAt;
                previousSynthesis = f.SynthesisAt;
            }

            // 3. Synthesis, in parallel: rotate both channels by the same amount, one inverse FFT for both.
            Parallel.For(0, count, slot =>
            {
                var f = frames[slot];
                for (var b = 0; b < bins; b++)
                {
                    var edge = b == 0 || b == bins - 1;
                    var cos = edge ? 1 : Math.Cos(f.Rotation[b]);
                    var sin = edge ? 0 : Math.Sin(f.Rotation[b]);
                    var lr = f.LeftRe[b] * cos - f.LeftIm[b] * sin;
                    var li = f.LeftRe[b] * sin + f.LeftIm[b] * cos;
                    var rr = f.RightRe[b] * cos - f.RightIm[b] * sin;
                    var ri = f.RightRe[b] * sin + f.RightIm[b] * cos;
                    f.Re[b] = lr - ri;
                    f.Im[b] = li + rr;
                    if (!edge)
                    {
                        f.Re[size - b] = lr + ri;
                        f.Im[size - b] = -li + rr;
                    }
                }

                fft.Transform(f.Re, f.Im, inverse: true);
            });

            // 4. Overlap-add, in order.
            for (var slot = 0; slot < count; slot++)
            {
                var f = frames[slot];
                var length = (int)Math.Min(size, outputLength - f.SynthesisAt);
                for (var n = 0; n < length; n++)
                {
                    var at = f.SynthesisAt + n;
                    var w = window[n];
                    outLeft[at] += (float)(f.Re[n] / size * w);
                    if (stereo)
                    {
                        outRight[at] += (float)(f.Im[n] / size * w);
                    }

                    norm[at] += (float)(w * w);
                }
            }
        }

        // Undo the overlapping windows' gain. The very ends (inside the padding) have almost no
        // window weight, so the divisor is kept from getting tiny there.
        for (var n = 0; n < outputLength; n++)
        {
            var divisor = Math.Max(norm[n], 0.5f);
            outLeft[n] /= divisor;
            if (stereo)
            {
                outRight[n] /= divisor;
            }
        }

        progress(1);
        return stereo ? new[] { outLeft, outRight } : new[] { outLeft };
    }

    /// <summary>Working space for one frame of a batch.</summary>
    private sealed class FrameData
    {
        public FrameData(int size, int bins)
        {
            Re = new double[size];
            Im = new double[size];
            LeftRe = new double[bins];
            LeftIm = new double[bins];
            RightRe = new double[bins];
            RightIm = new double[bins];
            Magnitude = new double[bins];
            Phase = new double[bins];
            Rotation = new double[bins];
        }

        public long AnalysisAt { get; set; }

        public long SynthesisAt { get; set; }

        public double[] Re { get; }

        public double[] Im { get; }

        public double[] LeftRe { get; }

        public double[] LeftIm { get; }

        public double[] RightRe { get; }

        public double[] RightIm { get; }

        public double[] Magnitude { get; }

        public double[] Phase { get; }

        public double[] Rotation { get; }
    }

    /// <summary>The standard phase vocoder step for one bin: measure its true frequency, then advance by the output hop.</summary>
    private static double AdvancePhase(int bin, int size, double phase, double previousPhase, double previousSynth,
        long hopIn, long hopOut)
    {
        var expected = 2 * Math.PI * bin / size;
        var deviation = Wrap(phase - previousPhase - expected * hopIn);
        var frequency = expected + deviation / hopIn;
        return previousSynth + frequency * hopOut;
    }

    /// <summary>Bins louder than their two neighbours on each side.</summary>
    private static void FindPeaks(double[] magnitude, List<int> peaks)
    {
        peaks.Clear();
        var max = magnitude.Max();
        if (max <= 0)
        {
            return;
        }

        // Ignore the noise floor far below the loudest partial.
        var floor = max * 1e-5;
        for (var b = 2; b < magnitude.Length - 2; b++)
        {
            var m = magnitude[b];
            if (m > floor && m > magnitude[b - 1] && m >= magnitude[b + 1] && m > magnitude[b - 2] && m >= magnitude[b + 2])
            {
                peaks.Add(b);
            }
        }
    }

    /// <summary>The quietest bin between two peaks: where one peak's region ends and the next begins.</summary>
    private static int LowestBetween(double[] magnitude, int from, int to)
    {
        var lowest = from + 1;
        for (var b = from + 1; b < to; b++)
        {
            if (magnitude[b] < magnitude[lowest])
            {
                lowest = b;
            }
        }

        return lowest;
    }

    private static double Wrap(double angle) => angle - 2 * Math.PI * Math.Round(angle / (2 * Math.PI));
}
