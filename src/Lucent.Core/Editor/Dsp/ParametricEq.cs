namespace WinampInspiredVisualizer.Editor.Dsp;

/// <summary>One band of the editor's equalizer.</summary>
public sealed class EqBand
{
    public FilterShape Shape { get; set; } = FilterShape.Peak;

    public double Frequency { get; set; } = 1000;

    /// <summary>The boost or cut, in dB. Ignored by high-pass, low-pass and notch bands.</summary>
    public double GainDb { get; set; }

    /// <summary>How narrow the band is: about 0.7 is broad and musical, 4 is narrow, 10 and up surgical.</summary>
    public double Q { get; set; } = 1;

    public bool Enabled { get; set; } = true;

    public EqBand Clone() => (EqBand)MemberwiseClone();

    /// <summary>True for the shapes that boost or cut, whose gain matters.</summary>
    public bool HasGain => Shape is FilterShape.Peak or FilterShape.LowShelf or FilterShape.HighShelf;

    /// <summary>Whether the band changes anything at all.</summary>
    public bool IsActive => Enabled && (!HasGain || Math.Abs(GainDb) > 0.01);

    /// <summary>The usual starting point: rumble filter off, a low shelf, three bells and a high shelf, all flat.</summary>
    public static List<EqBand> Defaults() => new()
    {
        new EqBand { Shape = FilterShape.HighPass, Frequency = 30, Q = 0.707, Enabled = false },
        new EqBand { Shape = FilterShape.LowShelf, Frequency = 100, Q = 0.707 },
        new EqBand { Shape = FilterShape.Peak, Frequency = 400, Q = 1 },
        new EqBand { Shape = FilterShape.Peak, Frequency = 1500, Q = 1 },
        new EqBand { Shape = FilterShape.Peak, Frequency = 5000, Q = 1 },
        new EqBand { Shape = FilterShape.HighShelf, Frequency = 10000, Q = 0.707 },
        new EqBand { Shape = FilterShape.LowPass, Frequency = 20000, Q = 0.707, Enabled = false },
    };

    public static List<EqBand> CloneAll(IEnumerable<EqBand> bands) => bands.Select(b => b.Clone()).ToList();
}

/// <summary>
/// Something that changes audio as it streams past, a block at a time, so the same code can render an
/// effect into the song and play it live while its settings are being changed.
/// </summary>
public interface IStreamProcessor
{
    /// <summary>Processes <paramref name="count"/> frames of each channel in place, from <paramref name="offset"/>.</summary>
    void Process(float[][] planes, int offset, int count);

    /// <summary>Forgets the audio it has seen (a new preview, or a new piece of audio).</summary>
    void Reset();

    /// <summary>How many frames late the output is (a limiter looking ahead); 0 for most processors.</summary>
    int Latency { get; }
}

/// <summary>The equalizer as a chain of biquads per channel. Its bands can be changed while it plays.</summary>
public sealed class ParametricEq : IStreamProcessor
{
    private readonly int _sampleRate;
    private readonly Biquad[][] _filters;
    private readonly bool[] _active;
    private List<EqBand> _bands;
    private List<EqBand>? _pending;

    public ParametricEq(IReadOnlyList<EqBand> bands, int channels, int sampleRate)
    {
        _sampleRate = sampleRate;
        _bands = EqBand.CloneAll(bands);
        _filters = new Biquad[channels][];
        _active = new bool[_bands.Count];
        for (var c = 0; c < channels; c++)
        {
            _filters[c] = _bands.Select(b => new Biquad(b.Shape, b.Frequency, b.Q, b.GainDb, sampleRate)).ToArray();
        }

        for (var i = 0; i < _bands.Count; i++)
        {
            _active[i] = _bands[i].IsActive;
        }
    }

    public int Latency => 0;

    /// <summary>
    /// New settings, picked up at the next block (safe to call from the UI thread while the audio thread
    /// plays). The bands must be the same number as before.
    /// </summary>
    public void Update(IReadOnlyList<EqBand> bands) => Volatile.Write(ref _pending, EqBand.CloneAll(bands));

    public void Process(float[][] planes, int offset, int count)
    {
        if (Interlocked.Exchange(ref _pending, null) is { } bands && bands.Count == _bands.Count)
        {
            _bands = bands;
            for (var i = 0; i < bands.Count; i++)
            {
                var band = bands[i];
                var wasActive = _active[i];
                _active[i] = band.IsActive;
                foreach (var chain in _filters)
                {
                    chain[i].Design(band.Shape, band.Frequency, band.Q, band.GainDb, _sampleRate);
                    if (!wasActive)
                    {
                        // A band switched on starts clean rather than with whatever it held from long ago.
                        chain[i].Reset();
                    }
                }
            }
        }

        for (var c = 0; c < Math.Min(planes.Length, _filters.Length); c++)
        {
            var span = planes[c].AsSpan(offset, count);
            var chain = _filters[c];
            for (var b = 0; b < chain.Length; b++)
            {
                if (_active[b])
                {
                    chain[b].Process(span);
                }
            }
        }
    }

    public void Reset()
    {
        foreach (var filter in _filters.SelectMany(chain => chain))
        {
            filter.Reset();
        }
    }

    /// <summary>The whole equalizer's effect on the level at a frequency, in dB, for drawing its curve.</summary>
    public static double ResponseDb(IEnumerable<EqBand> bands, double frequency, int sampleRate) =>
        bands.Where(b => b.IsActive).Sum(b => new Biquad(b.Shape, b.Frequency, b.Q, b.GainDb, sampleRate).ResponseDb(frequency, sampleRate));
}
