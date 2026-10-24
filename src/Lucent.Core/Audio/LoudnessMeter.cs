namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Measures loudness the way broadcasters and streaming services do (ITU-R BS.1770-4, EBU R 128 and
/// EBU Tech 3341/3342): momentary loudness over the last 0.4 s, short-term over the last 3 s, integrated
/// over everything since it last started again, the loudness range, and the true peak. Fed every
/// stereo sample the analyzer sees, on the audio thread, through <see cref="AudioCapture"/>; read by the
/// analyzer's loudness panel on the UI thread.
/// </summary>
/// <remarks>
/// The sound is K-weighted (a shelf lifting the treble about 4 dB, as the head does, then a high-pass
/// taking out rumble below about 40 Hz) and its power gathered in 100 ms pieces. Each 100 ms the newest
/// 400 ms is one gating block for the integrated loudness, and the newest 3 s one for the range. Those are
/// kept in histograms of 0.1 LU steps, so hours of a radio station take no more memory than a song.
/// </remarks>
public sealed class LoudnessMeter
{
    // Gating, as BS.1770 and Tech 3342 set it out.
    private const double AbsoluteGate = -70;
    private const double IntegratedRelativeGate = -10;
    private const double RangeRelativeGate = -20;

    // The histograms run from the absolute gate up to well past full scale, in tenths of an LU.
    private const double HistogramTop = 10;
    private const int HistogramSteps = (int)((HistogramTop - AbsoluteGate) * 10);

    private const int MomentaryPieces = 4;
    private const int ShortTermPieces = 30;

    private readonly object _lock = new();

    // Each piece is the K-weighted power of 100 ms, both channels added.
    private readonly double[] _pieces = new double[ShortTermPieces];
    private int _piecesFilled;
    private int _newestPiece = -1;
    private double _pieceSum;
    private int _pieceLength;
    private int _pieceSamples;

    private readonly long[] _blockCounts = new long[HistogramSteps];
    private readonly double[] _blockPower = new double[HistogramSteps];
    private readonly long[] _shortCounts = new long[HistogramSteps];
    private readonly double[] _shortPower = new double[HistogramSteps];

    // The K-weighting's two stages, the same for each channel, and each channel's filter memory.
    private Biquad _shelf;
    private Biquad _highPass;
    private readonly double[] _state = new double[8];

    // The true peak: the sound upsampled (four times below 96 kHz, twice below 192 kHz) so peaks between samples are seen.
    private float[][] _phases = Array.Empty<float[]>();
    private readonly float[] _history = new float[2 * 2 * HistoryLength];
    private int _historyAt;
    private float _truePeak;
    private const int TapsPerPhase = 12;
    private const int HistoryLength = TapsPerPhase;

    private int _listeners;

    public LoudnessMeter(int sampleRate = 44100)
    {
        Restart(sampleRate);
    }

    public int SampleRate { get; private set; }

    /// <summary>True while something shows the readings; until then it isn't fed, so it costs nothing.</summary>
    public bool IsListening => Volatile.Read(ref _listeners) > 0;

    /// <summary>Starts feeding the meter, from nothing, for something that shows it (the analyzer). Pair with <see cref="StopListening"/>.</summary>
    public void Listen()
    {
        if (Interlocked.Increment(ref _listeners) == 1)
        {
            Restart();
        }
    }

    public void StopListening()
    {
        if (Interlocked.Decrement(ref _listeners) < 0)
        {
            Interlocked.Exchange(ref _listeners, 0);
        }
    }

    /// <summary>Starts measuring again from nothing, at the same sample rate.</summary>
    public void Restart() => Restart(SampleRate);

    /// <summary>Starts measuring again from nothing, for sound at <paramref name="sampleRate"/>.</summary>
    public void Restart(int sampleRate)
    {
        lock (_lock)
        {
            if (sampleRate != SampleRate || _phases.Length == 0)
            {
                SampleRate = Math.Max(8000, sampleRate);
                Design(SampleRate);
            }

            Array.Clear(_pieces);
            _piecesFilled = 0;
            _newestPiece = -1;
            _pieceSum = 0;
            _pieceSamples = 0;
            Array.Clear(_blockCounts);
            Array.Clear(_blockPower);
            Array.Clear(_shortCounts);
            Array.Clear(_shortPower);
            Array.Clear(_state);
            Array.Clear(_history);
            _historyAt = 0;
            _truePeak = 0;
        }
    }

    /// <summary>Measures more of the sound, the left and right channels the same length.</summary>
    public void Write(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        lock (_lock)
        {
            for (var i = 0; i < left.Length; i++)
            {
                var l = Weigh(left[i], 0);
                var r = Weigh(right[i], 4);
                _pieceSum += l * l + r * r;
                if (++_pieceSamples == _pieceLength)
                {
                    EndPiece();
                }

                PeakOf(left[i], right[i]);
            }
        }
    }

    /// <summary>What it has measured so far.</summary>
    public LoudnessReading Read()
    {
        lock (_lock)
        {
            return new LoudnessReading(
                _piecesFilled >= MomentaryPieces ? Lufs(Mean(MomentaryPieces)) : double.NegativeInfinity,
                _piecesFilled >= MomentaryPieces ? Lufs(Mean(Math.Min(_piecesFilled, ShortTermPieces))) : double.NegativeInfinity,
                Integrated(),
                Range(),
                _truePeak > 0 ? 20 * Math.Log10(_truePeak) : double.NegativeInfinity);
        }
    }

    // ---- Loudness ----

    private static double Lufs(double power) => power > 0 ? -0.691 + 10 * Math.Log10(power) : double.NegativeInfinity;

    private static double PowerOf(double lufs) => Math.Pow(10, (lufs + 0.691) / 10);

    // The mean power of the newest pieces.
    private double Mean(int count)
    {
        var sum = 0.0;
        for (var i = 0; i < count; i++)
        {
            sum += _pieces[(_newestPiece - i + ShortTermPieces) % ShortTermPieces];
        }

        return sum / count;
    }

    private void EndPiece()
    {
        _newestPiece = (_newestPiece + 1) % ShortTermPieces;
        _pieces[_newestPiece] = _pieceSum / _pieceLength;
        _piecesFilled = Math.Min(_piecesFilled + 1, ShortTermPieces);
        _pieceSum = 0;
        _pieceSamples = 0;

        // Every 100 ms the newest 400 ms is a gating block, and once there's 3 s, the newest 3 s a range block.
        if (_piecesFilled >= MomentaryPieces)
        {
            Gather(_blockCounts, _blockPower, Mean(MomentaryPieces));
        }

        if (_piecesFilled >= ShortTermPieces)
        {
            Gather(_shortCounts, _shortPower, Mean(ShortTermPieces));
        }
    }

    private static void Gather(long[] counts, double[] power, double blockPower)
    {
        var lufs = Lufs(blockPower);
        if (lufs <= AbsoluteGate)
        {
            return;
        }

        var step = Math.Min(HistogramSteps - 1, (int)((lufs - AbsoluteGate) * 10));
        counts[step]++;
        power[step] += blockPower;
    }

    // The steps at or above a loudness, so a gate can leave out the ones below it.
    private static int StepOf(double lufs) => Math.Clamp((int)Math.Floor((lufs - AbsoluteGate) * 10), 0, HistogramSteps);

    private double Integrated()
    {
        // The mean of every block above the absolute gate, then again of those within 10 LU of that.
        var (count, power) = Sum(_blockCounts, _blockPower, 0);
        if (count == 0)
        {
            return double.NegativeInfinity;
        }

        var gate = Lufs(power / count) + IntegratedRelativeGate;
        (count, power) = Sum(_blockCounts, _blockPower, StepOf(gate));
        return count == 0 ? double.NegativeInfinity : Lufs(power / count);
    }

    private double Range()
    {
        // The spread between the quietest and loudest stretches (10th to 95th percentile of the 3 s
        // loudness), leaving out anything more than 20 LU below the mean, like fade-outs and pauses.
        var (count, power) = Sum(_shortCounts, _shortPower, 0);
        if (count == 0)
        {
            return 0;
        }

        var first = StepOf(Lufs(power / count) + RangeRelativeGate);
        (count, _) = Sum(_shortCounts, _shortPower, first);
        if (count == 0)
        {
            return 0;
        }

        double Percentile(double share)
        {
            var wanted = (long)Math.Round((count - 1) * share);
            var seen = 0L;
            for (var step = first; step < HistogramSteps; step++)
            {
                seen += _shortCounts[step];
                if (seen > wanted)
                {
                    return AbsoluteGate + (step + 0.5) / 10;
                }
            }

            return HistogramTop;
        }

        return Math.Max(0, Percentile(0.95) - Percentile(0.10));
    }

    private static (long Count, double Power) Sum(long[] counts, double[] power, int from)
    {
        var count = 0L;
        var sum = 0.0;
        for (var step = from; step < HistogramSteps; step++)
        {
            count += counts[step];
            sum += power[step];
        }

        return (count, sum);
    }

    // ---- Filters ----

    private struct Biquad
    {
        public double B0, B1, B2, A1, A2;
    }

    // One sample through both K-weighting stages, with the channel's memory at state[at..at+3].
    private double Weigh(float sample, int at)
    {
        var s = _state;
        var x = (double)sample;
        var y = _shelf.B0 * x + s[at];
        s[at] = _shelf.B1 * x - _shelf.A1 * y + s[at + 1];
        s[at + 1] = _shelf.B2 * x - _shelf.A2 * y;
        var z = _highPass.B0 * y + s[at + 2];
        s[at + 2] = _highPass.B1 * y - _highPass.A1 * z + s[at + 3];
        s[at + 3] = _highPass.B2 * y - _highPass.A2 * z;
        return z;
    }

    private void Design(int sampleRate)
    {
        // BS.1770's two stages, worked out for any sample rate (its tables give them at 48 kHz only).
        var k = Math.Tan(Math.PI * 1681.974450955533 / sampleRate);
        var q = 0.7071752369554196;
        var vh = Math.Pow(10, 3.999843853973347 / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + k / q + k * k;
        _shelf = new Biquad
        {
            B0 = (vh + vb * k / q + k * k) / a0,
            B1 = 2 * (k * k - vh) / a0,
            B2 = (vh - vb * k / q + k * k) / a0,
            A1 = 2 * (k * k - 1) / a0,
            A2 = (1 - k / q + k * k) / a0,
        };

        k = Math.Tan(Math.PI * 38.13547087602444 / sampleRate);
        q = 0.5003270373238773;
        a0 = 1 + k / q + k * k;
        _highPass = new Biquad
        {
            B0 = 1,
            B1 = -2,
            B2 = 1,
            A1 = 2 * (k * k - 1) / a0,
            A2 = (1 - k / q + k * k) / a0,
        };

        _pieceLength = Math.Max(1, sampleRate / 10);

        // The upsampler: a windowed sinc, split into one short filter per in-between point.
        var factor = sampleRate < 96000 ? 4 : sampleRate < 192000 ? 2 : 1;
        var taps = TapsPerPhase * factor;
        var centre = (taps - 1) / 2.0;
        _phases = new float[factor][];
        for (var phase = 0; phase < factor; phase++)
        {
            var filter = new float[TapsPerPhase];
            var sum = 0.0;
            for (var tap = 0; tap < TapsPerPhase; tap++)
            {
                var n = tap * factor + phase;
                var t = (n - centre) / factor;
                var sinc = Math.Abs(t) < 1e-9 ? 1 : Math.Sin(Math.PI * t) / (Math.PI * t);
                // Blackman window.
                var w = 0.42 - 0.5 * Math.Cos(2 * Math.PI * (n + 0.5) / taps) + 0.08 * Math.Cos(4 * Math.PI * (n + 0.5) / taps);
                filter[tap] = (float)(sinc * w);
                sum += sinc * w;
            }

            // Each in-between point passes a steady level unchanged.
            for (var tap = 0; tap < TapsPerPhase; tap++)
            {
                filter[tap] = (float)(filter[tap] / sum);
            }

            _phases[phase] = filter;
        }
    }

    // The highest point the sound reaches between samples, as the newest sample comes in.
    private void PeakOf(float left, float right)
    {
        // Each channel's last few samples, twice over so they can be read as one run without wrapping.
        var at = _historyAt;
        _history[at] = _history[at + HistoryLength] = left;
        _history[2 * HistoryLength + at] = _history[3 * HistoryLength + at] = right;
        _historyAt = (at + 1) % HistoryLength;

        var peak = _truePeak;
        peak = MathF.Max(peak, MathF.Max(MathF.Abs(left), MathF.Abs(right)));
        var start = _historyAt;
        foreach (var filter in _phases)
        {
            float l = 0, r = 0;
            for (var tap = 0; tap < TapsPerPhase; tap++)
            {
                // The filter's first tap meets the newest sample.
                var back = start + TapsPerPhase - 1 - tap;
                l += filter[tap] * _history[back];
                r += filter[tap] * _history[2 * HistoryLength + back];
            }

            peak = MathF.Max(peak, MathF.Max(MathF.Abs(l), MathF.Abs(r)));
        }

        _truePeak = peak;
    }
}

/// <summary>
/// One look at a <see cref="LoudnessMeter"/>: loudness in LUFS (negative infinity before there's any sound),
/// the range in LU, and the true peak in dBTP.
/// </summary>
public readonly record struct LoudnessReading(double Momentary, double ShortTerm, double Integrated, double Range, double TruePeak);
