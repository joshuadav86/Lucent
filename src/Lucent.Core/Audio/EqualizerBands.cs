using System.Globalization;

namespace Lucent.Audio;

/// <summary>
/// Where an equalizer's bands sit: their centre frequencies, spread evenly on a logarithmic
/// (musical) scale, and the short labels shown under each slider.
/// </summary>
public sealed class EqualizerBands
{
    /// <summary>The band counts people can choose between.</summary>
    public static readonly IReadOnlyList<int> Counts = new[] { 4, 8, 16, 24, 32 };

    private static readonly Dictionary<int, EqualizerBands> ByCount = Counts.ToDictionary(count => count, Create);

    private EqualizerBands(int count, double[] centres, string[] labels)
    {
        Count = count;
        Centres = centres;
        Labels = labels;
        SpacingOctaves = Math.Log2(centres[1] / centres[0]);
    }

    public int Count { get; }

    /// <summary>Each band's centre frequency in hertz, lowest first.</summary>
    public IReadOnlyList<double> Centres { get; }

    /// <summary>Each band's label, such as "60", "1k" or "16k".</summary>
    public IReadOnlyList<string> Labels { get; }

    /// <summary>How far apart neighbouring bands are, in octaves (the same for every pair).</summary>
    public double SpacingOctaves { get; }

    /// <summary>
    /// The layout an older version of Lucent used for this many bands (3, 5, 10 and 31 were offered
    /// once), so sliders saved with it can be redrawn at a band count offered now. Null for others.
    /// </summary>
    public static EqualizerBands? Former(int count) => count is 3 or 5 or 10 or 31 ? Create(count) : null;

    /// <summary>
    /// The ten bands of a classic skin's equalizer, which has Winamp's ten sliders: the ISO octaves, 31 Hz to
    /// 16 kHz. Not one of the <see cref="Counts"/> Lucent's own equalizer offers.
    /// </summary>
    public static EqualizerBands Classic { get; } = Create(10);

    /// <summary>The layout for one of the <see cref="Counts"/>; anything else gets the nearest one.</summary>
    public static EqualizerBands ForCount(int count)
    {
        if (ByCount.TryGetValue(count, out var bands))
        {
            return bands;
        }

        var nearest = Counts.OrderBy(candidate => Math.Abs(candidate - count)).First();
        return ByCount[nearest];
    }

    /// <summary>A frequency written the way the sliders are labelled: "63", "250", "1k", "2.5k", "16k".</summary>
    public static string FormatFrequency(double hertz)
    {
        if (hertz < 1000)
        {
            var rounded = hertz < 100 ? Math.Round(hertz) : RoundTo(hertz, 2);
            return rounded.ToString("0", CultureInfo.InvariantCulture);
        }

        var kilohertz = hertz / 1000;
        var text = kilohertz < 10 ? RoundTo(kilohertz, 2).ToString("0.#", CultureInfo.InvariantCulture)
                                  : Math.Round(kilohertz).ToString("0", CultureInfo.InvariantCulture);
        return text + "k";
    }

    private static EqualizerBands Create(int count)
    {
        double[] centres;
        string[]? labels = null;
        switch (count)
        {
            case 10:
                // The ISO octave bands: 31.5 Hz to 16 kHz.
                centres = IsoSeries(-15, 12, 3);
                labels = new[] { "31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
                break;
            case 31:
                // The ISO third-octave bands: 20 Hz to 20 kHz.
                centres = IsoSeries(-17, 13, 1);
                labels = new[]
                {
                    "20", "25", "31", "40", "50", "63", "80", "100", "125", "160", "200", "250", "315", "400", "500",
                    "630", "800", "1k", "1.2k", "1.6k", "2k", "2.5k", "3.1k", "4k", "5k", "6.3k", "8k", "10k", "12k",
                    "16k", "20k",
                };
                break;
            case 16:
                // The ISO two-thirds-octave bands: 20 Hz to 20 kHz.
                centres = IsoSeries(-17, 13, 2);
                labels = new[]
                {
                    "20", "31", "50", "80", "125", "200", "315", "500", "800", "1.2k", "2k", "3.1k", "5k", "8k", "12k", "20k",
                };
                break;
            default:
                // Other counts are spread evenly between a lowest and highest band picked to suit
                // that many sliders: three bands are bass, middle and treble.
                var (lowest, highest) = count switch
                {
                    3 => (100.0, 10000.0),
                    4 => (80.0, 10000.0),
                    5 => (63.0, 16000.0),
                    8 => (50.0, 16000.0),
                    _ => (20.0, 20000.0),
                };
                centres = LogSpaced(lowest, highest, count);
                break;
        }

        labels ??= centres.Select(FormatFrequency).ToArray();
        return new EqualizerBands(count, centres, labels);
    }

    // ISO 266 preferred frequencies are powers of ten in tenths of a decade: 1000 × 10^(n/10).
    private static double[] IsoSeries(int first, int last, int step)
    {
        var centres = new List<double>();
        for (var n = first; n <= last; n += step)
        {
            centres.Add(1000 * Math.Pow(10, n / 10.0));
        }

        return centres.ToArray();
    }

    private static double[] LogSpaced(double lowest, double highest, int count)
    {
        var centres = new double[count];
        for (var i = 0; i < count; i++)
        {
            centres[i] = lowest * Math.Pow(highest / lowest, i / (double)(count - 1));
        }

        return centres;
    }

    private static double RoundTo(double value, int significantFigures)
    {
        var scale = Math.Pow(10, significantFigures - 1 - (int)Math.Floor(Math.Log10(value)));
        return Math.Round(value * scale) / scale;
    }
}
