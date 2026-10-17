namespace WinampInspiredVisualizer.Audio;

/// <summary>A point on an equalizer curve: a gain in decibels at a frequency in hertz.</summary>
public readonly record struct EqualizerPoint(double Hertz, double GainDb);

/// <summary>
/// A smooth equalizer curve drawn through a few points. Defining presets this way, rather than as
/// slider positions, lets the same preset fit any number of bands.
/// </summary>
public sealed class EqualizerCurve
{
    private readonly double[] _octaves;
    private readonly double[] _gains;
    private readonly double[] _slopes;

    public EqualizerCurve(IEnumerable<EqualizerPoint> points)
    {
        var sorted = points.Where(point => point.Hertz > 0).OrderBy(point => point.Hertz).ToArray();
        Points = sorted;
        _octaves = sorted.Select(point => Math.Log2(point.Hertz)).ToArray();
        _gains = sorted.Select(point => point.GainDb).ToArray();
        _slopes = MonotoneSlopes(_octaves, _gains);
    }

    /// <summary>The points the curve passes through, lowest frequency first.</summary>
    public IReadOnlyList<EqualizerPoint> Points { get; }

    /// <summary>The curve's gain at a frequency. Beyond the first and last points it stays level.</summary>
    public double GainAt(double hertz)
    {
        var count = _octaves.Length;
        if (count == 0)
        {
            return 0;
        }

        var x = Math.Log2(hertz);
        if (count == 1 || x <= _octaves[0])
        {
            return _gains[0];
        }

        if (x >= _octaves[count - 1])
        {
            return _gains[count - 1];
        }

        var i = Array.BinarySearch(_octaves, x);
        if (i >= 0)
        {
            return _gains[i];
        }

        i = ~i - 1;
        // Cubic Hermite between the two points either side, on a musical (octave) scale.
        var width = _octaves[i + 1] - _octaves[i];
        var t = (x - _octaves[i]) / width;
        var t2 = t * t;
        var t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * _gains[i] + (t3 - 2 * t2 + t) * width * _slopes[i]
             + (-2 * t3 + 3 * t2) * _gains[i + 1] + (t3 - t2) * width * _slopes[i + 1];
    }

    /// <summary>The curve's gain at each band's centre, ready to set the sliders.</summary>
    public double[] Sample(EqualizerBands bands) =>
        bands.Centres.Select(hertz => Math.Clamp(Math.Round(GainAt(hertz) * 2) / 2, -Equalizer.MaxGainDb, Equalizer.MaxGainDb)).ToArray();

    /// <summary>The curve that passes through each band's slider.</summary>
    public static EqualizerCurve FromBands(EqualizerBands bands, IReadOnlyList<double> gainsDb) =>
        new(bands.Centres.Select((hertz, band) => new EqualizerPoint(hertz, band < gainsDb.Count ? gainsDb[band] : 0)));

    // Fritsch-Carlson slopes: smooth, and never overshooting between points, so a curve that
    // rises from 0 to +6 dB doesn't bulge past +6 on the way.
    private static double[] MonotoneSlopes(double[] x, double[] y)
    {
        var count = x.Length;
        var slopes = new double[count];
        if (count < 2)
        {
            return slopes;
        }

        var secants = new double[count - 1];
        for (var i = 0; i < count - 1; i++)
        {
            secants[i] = (y[i + 1] - y[i]) / (x[i + 1] - x[i]);
        }

        // Level at the ends, so the curve meets the flat run beyond the outer points smoothly.
        for (var i = 1; i < count - 1; i++)
        {
            slopes[i] = secants[i - 1] * secants[i] <= 0 ? 0 : (secants[i - 1] + secants[i]) / 2;
        }

        for (var i = 0; i < count - 1; i++)
        {
            if (secants[i] == 0)
            {
                slopes[i] = 0;
                slopes[i + 1] = 0;
                continue;
            }

            var alpha = slopes[i] / secants[i];
            var beta = slopes[i + 1] / secants[i];
            var length = alpha * alpha + beta * beta;
            if (length > 9)
            {
                var scale = 3 / Math.Sqrt(length);
                slopes[i] = scale * alpha * secants[i];
                slopes[i + 1] = scale * beta * secants[i];
            }
        }

        return slopes;
    }
}

/// <summary>A named equalizer curve.</summary>
public sealed record EqualizerPreset(string Name, EqualizerCurve Curve)
{
    public EqualizerPreset(string name, params (double Hertz, double GainDb)[] points)
        : this(name, new EqualizerCurve(points.Select(point => new EqualizerPoint(point.Hertz, point.GainDb))))
    {
    }
}

/// <summary>The presets that come with Lucent.</summary>
public static class EqualizerPresets
{
    public const string FlatName = "Flat";

    public static readonly IReadOnlyList<EqualizerPreset> BuiltIn = new EqualizerPreset[]
    {
        new(FlatName, (1000, 0)),
        new("Bass Boost", (30, 7), (80, 6.5), (200, 3.5), (500, 0.5), (1000, 0)),
        new("Bass & Treble", (30, 6), (100, 5), (300, 1), (1000, -1), (3000, 1), (8000, 5), (16000, 6)),
        new("Treble Boost", (1000, 0), (3000, 2), (6000, 5), (12000, 7), (16000, 7.5)),
        new("Vocal", (30, -3), (100, -2), (300, 0), (1000, 2.5), (2500, 4), (5000, 3), (10000, 0), (16000, -1)),
        new("Rock", (30, 5), (80, 4), (250, -1), (800, -2), (2000, 1), (5000, 4), (12000, 5), (16000, 5)),
        new("Pop", (30, -1), (80, 1), (250, 3), (1000, 4), (3000, 2), (8000, 0), (16000, -1)),
        new("Dance/Electronic", (30, 7), (60, 6), (150, 2), (500, -1), (1500, 0), (5000, 3), (10000, 5), (16000, 4)),
        new("Hip-Hop", (30, 7), (70, 6), (150, 3), (400, -1), (1000, 0), (3000, 1), (8000, 2), (16000, 1)),
        new("Jazz", (30, 3), (100, 2), (300, 0.5), (1000, -1), (3000, 1), (8000, 3), (16000, 3.5)),
        new("Classical", (30, 2.5), (150, 1), (500, 0), (3000, 0), (8000, 2), (16000, 3)),
        new("Acoustic", (30, 2), (120, 3), (400, 1), (1000, 0), (3000, 2), (8000, 3), (16000, 2)),
        new("Loudness", (30, 7), (80, 5), (250, 1), (1000, 0), (4000, 1), (10000, 4), (16000, 5)),
        // Softer bass and treble, with voices nudged forward, for listening quietly.
        new("Late Night (soft)", (30, -6), (100, -4), (300, -1), (1000, 1), (3000, 1.5), (8000, -2), (16000, -4)),
        // Laptop and phone speakers can't make deep bass, so trying only distorts; the upper bass
        // they can play is lifted instead, with a little extra clarity.
        new("Small Speakers", (30, -8), (60, -5), (120, 3), (250, 4), (600, 1), (2000, 0), (5000, 2), (12000, 3), (16000, 2)),
    };

    public static EqualizerPreset? Find(string? name) =>
        BuiltIn.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
}
