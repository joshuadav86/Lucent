using Lucent.Editor.Dsp;

namespace Lucent.Recording;

/// <summary>
/// Listens to a live recording for where one song gives way to the next, so a DJ set can be skipped
/// through song by song afterwards. It does its best rather than being sure: DJs blend songs that share a
/// tempo and often a key, so there's rarely a clean edge to find.
/// </summary>
/// <remarks>
/// Twice a second it measures the sound's harmony (how much of each of the 12 notes there is) and its tone
/// (how the energy spreads from the bass to the highs). It then compares the 20 seconds before each moment
/// with the 20 seconds after it: where a new song has taken over, the two sound most unlike each other. A
/// moment counts as a change when it's the most unlike point around, stands well clear of how different
/// the music usually is from moment to moment in this set, and comes at least a minute and a half after
/// the last change. A gap of silence followed by sound is always a change. Because it looks 20 seconds
/// ahead, it reports each change about 20 seconds after it happened.
/// </remarks>
public sealed class SongChangeDetector
{
    private const double HopSeconds = 0.5;
    private const int Context = 40;              // hops each side: 20 seconds
    private const int FftSize = 8192;
    private const int Features = 12 + 6;
    private static readonly double[] BandEdges = { 30, 150, 400, 1200, 3500, 8000, 16000 };

    private readonly int _sampleRate;
    private readonly int _hop;
    private readonly Fft _fft = new(FftSize);
    private readonly double[] _window = new double[FftSize];
    private readonly double[] _re = new double[FftSize];
    private readonly double[] _im = new double[FftSize];
    private readonly float[] _recent = new float[FftSize];
    private readonly List<double[]> _features = new();
    private readonly List<double> _loudness = new();
    private readonly List<double> _novelty = new();
    private readonly double _minimumGapHops;
    private int _recentAt;
    private int _sinceHop;
    private long _firstHop;   // the hop number of _features[0], as old ones are let go
    private long _lastChange = long.MinValue / 2;
    private long _silentSince = -1;
    private double _noveltySum;
    private double _noveltySquares;
    private int _noveltyCount;

    /// <param name="minimumGap">The shortest a song can be: no two changes are closer than this.</param>
    public SongChangeDetector(int sampleRate, TimeSpan? minimumGap = null)
    {
        _sampleRate = sampleRate;
        _hop = (int)(sampleRate * HopSeconds);
        _minimumGapHops = (minimumGap ?? TimeSpan.FromSeconds(90)).TotalSeconds / HopSeconds;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftSize - 1));
        }
    }

    /// <summary>
    /// Adds mono audio and returns where any songs it has now heard begin, in frames from the start of
    /// what it was given (usually nothing; a change is found about 20 seconds after it happens).
    /// </summary>
    public IReadOnlyList<long> Add(ReadOnlySpan<float> mono)
    {
        List<long>? found = null;
        foreach (var sample in mono)
        {
            _recent[_recentAt] = sample;
            _recentAt = (_recentAt + 1) % FftSize;
            if (++_sinceHop < _hop)
            {
                continue;
            }

            _sinceHop = 0;
            if (Measure() is { } change)
            {
                (found ??= new List<long>()).Add(change);
            }
        }

        return found ?? (IReadOnlyList<long>)Array.Empty<long>();
    }

    // Takes one hop's measurements, then looks for a change Context hops back. Returns its frame if there is one.
    private long? Measure()
    {
        for (var i = 0; i < FftSize; i++)
        {
            _re[i] = _recent[(_recentAt + i) % FftSize] * _window[i];
            _im[i] = 0;
        }

        _fft.Transform(_re, _im, inverse: false);
        var feature = new double[Features];
        var energy = 0.0;
        var binHz = (double)_sampleRate / FftSize;
        for (var bin = 1; bin < FftSize / 2; bin++)
        {
            var power = _re[bin] * _re[bin] + _im[bin] * _im[bin];
            var hz = bin * binHz;
            energy += power;
            if (hz is >= 55 and <= 5000)
            {
                var note = (int)Math.Round(12 * Math.Log2(hz / 440.0)) % 12;
                feature[(note + 12) % 12] += Math.Sqrt(power);
            }

            for (var band = 0; band < BandEdges.Length - 1; band++)
            {
                if (hz >= BandEdges[band] && hz < BandEdges[band + 1])
                {
                    feature[12 + band] += power;
                    break;
                }
            }
        }

        // Harmony as shares of the whole, so loudness doesn't count; tone as each band's share in decibels.
        var chromaTotal = 0.0;
        for (var n = 0; n < 12; n++)
        {
            chromaTotal += feature[n];
        }

        for (var n = 0; n < 12; n++)
        {
            feature[n] = chromaTotal > 0 ? feature[n] / chromaTotal : 0;
        }

        for (var band = 0; band < 6; band++)
        {
            feature[12 + band] = energy > 0 ? 10 * Math.Log10(feature[12 + band] / energy + 1e-9) / 30 : 0;
        }

        var hop = _firstHop + _features.Count;
        var loudness = 10 * Math.Log10(energy / FftSize * 4 / FftSize + 1e-12);
        _features.Add(feature);
        _loudness.Add(loudness);

        // Silence then sound: a gap between songs.
        long? change = null;
        var silent = loudness < -60;
        if (silent)
        {
            if (_silentSince < 0)
            {
                _silentSince = hop;
            }
        }
        else
        {
            if (_silentSince >= 0 && hop - _silentSince >= 4 && hop - _lastChange >= 20 && hop > 10)
            {
                change = Accept(hop);
            }

            _silentSince = -1;
        }

        if (change is null && _features.Count > 2 * Context)
        {
            change = LookForBlend();
        }

        // Keep only what's still needed: two windows' worth and room to find the peak.
        if (_features.Count > 4 * Context)
        {
            var drop = _features.Count - 4 * Context;
            _features.RemoveRange(0, drop);
            _loudness.RemoveRange(0, drop);
            _novelty.RemoveRange(0, Math.Min(drop, _novelty.Count));
            _firstHop += drop;
        }

        return change;
    }

    // How unlike the 20 seconds before the moment Context hops back are to the 20 seconds after it.
    private long? LookForBlend()
    {
        var center = _features.Count - Context;
        var before = Mean(center - Context, center);
        var after = Mean(center, center + Context);
        var harmony = 0.0;
        var tone = 0.0;
        for (var n = 0; n < 12; n++)
        {
            harmony += Math.Abs(before[n] - after[n]);
        }

        for (var band = 12; band < Features; band++)
        {
            tone += (before[band] - after[band]) * (before[band] - after[band]);
        }

        var novelty = harmony + Math.Sqrt(tone);
        // _novelty lines up with _features from the first hop that had a full window before it.
        while (_novelty.Count < center)
        {
            _novelty.Add(0);
        }

        _novelty.Add(novelty);
        _noveltySum += novelty;
        _noveltySquares += novelty * novelty;
        _noveltyCount++;

        // Decide about the moment half a window further back, once the novelty around it is known on both sides.
        var candidate = _novelty.Count - 1 - Context / 2;
        var candidateHop = _firstHop + candidate;
        if (candidate < Context / 2 || _noveltyCount < 2 * Context || candidateHop - _lastChange < _minimumGapHops)
        {
            return null;
        }

        var value = _novelty[candidate];
        for (var i = candidate - Context / 2; i <= candidate + Context / 2; i++)
        {
            if (i != candidate && i >= 0 && i < _novelty.Count && _novelty[i] >= value)
            {
                return null;
            }
        }

        var mean = _noveltySum / _noveltyCount;
        var spread = Math.Sqrt(Math.Max(0, _noveltySquares / _noveltyCount - mean * mean));
        return value > mean + 1.5 * spread && value > 0.12 ? Accept(candidateHop) : null;
    }

    private long Accept(long hop)
    {
        _lastChange = hop;
        // A hop is measured over the FFT window that ends there; its middle is where the sound was.
        return Math.Max(0, hop * _hop + _hop - FftSize / 2);
    }

    private double[] Mean(int from, int to)
    {
        var mean = new double[Features];
        var count = 0;
        for (var i = Math.Max(0, from); i < to && i < _features.Count; i++)
        {
            // Quiet moments (a breakdown's silence) say nothing about which song it is.
            if (_loudness[i] < -60)
            {
                continue;
            }

            for (var f = 0; f < Features; f++)
            {
                mean[f] += _features[i][f];
            }

            count++;
        }

        if (count > 0)
        {
            for (var f = 0; f < Features; f++)
            {
                mean[f] /= count;
            }
        }

        return mean;
    }
}
