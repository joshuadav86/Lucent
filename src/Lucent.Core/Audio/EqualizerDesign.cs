namespace WinampInspiredVisualizer.Audio;

/// <summary>The kinds of filter an equalizer band can be.</summary>
public enum EqualizerFilterKind
{
    /// <summary>Raises or lowers everything below the band (the lowest slider).</summary>
    LowShelf,

    /// <summary>Raises or lowers a bell-shaped region around the band's centre.</summary>
    Peak,

    /// <summary>Raises or lowers everything above the band (the highest slider).</summary>
    HighShelf,
}

/// <summary>
/// One band's filter shape at one sample rate. The gain is left out so the audio thread can
/// glide a band's gain smoothly, recomputing the coefficients as it goes.
/// </summary>
public readonly struct EqualizerFilterShape
{
    public EqualizerFilterShape(EqualizerFilterKind kind, double hertz, double bandwidthOctaves, int sampleRate)
    {
        Kind = kind;
        var w0 = 2 * Math.PI * hertz / sampleRate;
        Cos = Math.Cos(w0);
        var sin = Math.Sin(w0);
        // Bells use the cookbook bandwidth form (with its correction for the digital frequency
        // warp), so neighbouring bells meet at half their gain. Shelves use the steepest slope
        // that doesn't overshoot.
        Alpha = kind == EqualizerFilterKind.Peak
            ? sin * Math.Sinh(Math.Log(2) / 2 * bandwidthOctaves * w0 / sin)
            : sin / Math.Sqrt(2);
    }

    public EqualizerFilterKind Kind { get; }

    /// <summary>cos(ω₀) of the filter's centre or turnover frequency.</summary>
    public double Cos { get; }

    /// <summary>The bandwidth term from the Audio EQ Cookbook (R. Bristow-Johnson).</summary>
    public double Alpha { get; }

    /// <summary>
    /// Normalised biquad coefficients for this shape at a gain in decibels. At exactly 0 dB the
    /// numerator equals the denominator, so the filter passes audio through unchanged.
    /// </summary>
    public BiquadCoefficients At(double gainDb)
    {
        var a = Math.Pow(10, gainDb / 40);
        if (Kind == EqualizerFilterKind.Peak)
        {
            var a0 = 1 + Alpha / a;
            var shared = -2 * Cos / a0;
            return new BiquadCoefficients((1 + Alpha * a) / a0, shared, (1 - Alpha * a) / a0, shared, (1 - Alpha / a) / a0);
        }

        var root = 2 * Math.Sqrt(a) * Alpha;
        var plus = a + 1;
        var minus = a - 1;
        if (Kind == EqualizerFilterKind.LowShelf)
        {
            var a0 = plus + minus * Cos + root;
            return new BiquadCoefficients(
                a * (plus - minus * Cos + root) / a0,
                2 * a * (minus - plus * Cos) / a0,
                a * (plus - minus * Cos - root) / a0,
                -2 * (minus + plus * Cos) / a0,
                (plus + minus * Cos - root) / a0);
        }
        else
        {
            var a0 = plus - minus * Cos + root;
            return new BiquadCoefficients(
                a * (plus + minus * Cos + root) / a0,
                -2 * a * (minus + plus * Cos) / a0,
                a * (plus + minus * Cos - root) / a0,
                2 * (minus - plus * Cos) / a0,
                (plus - minus * Cos - root) / a0);
        }
    }
}

/// <summary>A biquad filter's coefficients, already divided through by a0.</summary>
public readonly record struct BiquadCoefficients(double B0, double B1, double B2, double A1, double A2)
{
    /// <summary>The filter's gain in decibels at a frequency given as cos(ω) and cos(2ω).</summary>
    public double ResponseDb(double cosW, double cos2W)
    {
        var numerator = B0 * B0 + B1 * B1 + B2 * B2 + 2 * (B0 * B1 + B1 * B2) * cosW + 2 * B0 * B2 * cos2W;
        var denominator = 1 + A1 * A1 + A2 * A2 + 2 * (A1 + A1 * A2) * cosW + 2 * A2 * cos2W;
        return 10 * Math.Log10(Math.Max(numerator, 1e-30) / Math.Max(denominator, 1e-30));
    }
}

/// <summary>
/// Everything the audio thread needs for one set of slider positions at one sample rate.
/// Built on the UI thread and never changed afterwards, so it can be handed over by swapping
/// a reference.
/// </summary>
public sealed class EqualizerDesign
{
    /// <summary>
    /// The most any single filter is pushed. Lifting one slider of many closely spaced ones means
    /// driving its filter harder than the slider and pulling its neighbours back.
    /// </summary>
    public const double MaxFilterGainDb = 36;

    // Frequencies checked when looking for the curve's loudest point.
    private const int ResponseGridPoints = 160;

    private readonly BiquadCoefficients[] _coefficients;

    private EqualizerDesign(EqualizerBands bands, int sampleRate, EqualizerFilterShape[] shapes, double[] filterGainsDb,
                            double preampDb, double headroomDb, bool isBypass)
    {
        Bands = bands;
        SampleRate = sampleRate;
        Shapes = shapes;
        FilterGainsDb = filterGainsDb;
        PreampDb = preampDb;
        HeadroomDb = headroomDb;
        IsBypass = isBypass;
        OutputGain = isBypass ? 1 : Math.Pow(10, (preampDb - headroomDb) / 20);
        _coefficients = new BiquadCoefficients[shapes.Length];
        for (var band = 0; band < shapes.Length; band++)
        {
            _coefficients[band] = shapes[band].At(filterGainsDb[band]);
        }
    }

    public EqualizerBands Bands { get; }

    public int SampleRate { get; }

    public IReadOnlyList<EqualizerFilterShape> Shapes { get; }

    /// <summary>
    /// The gain each band's filter actually runs at. Bands overlap, so these differ a little from
    /// the sliders: they're solved so the combined sound matches every slider at its centre.
    /// </summary>
    public IReadOnlyList<double> FilterGainsDb { get; }

    public double PreampDb { get; }

    /// <summary>How far the volume is lowered automatically so boosted bands don't distort.</summary>
    public double HeadroomDb { get; }

    /// <summary>The level applied after the filters: the preamp less the automatic headroom.</summary>
    public double OutputGain { get; }

    /// <summary>True when the equalizer is off or flat, so audio passes through untouched.</summary>
    public bool IsBypass { get; }

    /// <summary>Designs filters that make the combined response hit each slider at its band's centre.</summary>
    public static EqualizerDesign Create(EqualizerBands bands, IReadOnlyList<double> sliderGainsDb, double preampDb,
                                         bool enabled, int sampleRate, EqualizerSolver solver)
    {
        var shapes = solver.Shapes;
        var flat = sliderGainsDb.All(gain => gain == 0);
        if (!enabled || (flat && preampDb == 0))
        {
            return new EqualizerDesign(bands, sampleRate, shapes, new double[shapes.Length], 0, 0, isBypass: true);
        }

        var gains = flat ? new double[shapes.Length] : solver.Solve(sliderGainsDb);
        var design = new EqualizerDesign(bands, sampleRate, shapes, gains, preampDb, 0, isBypass: false);

        // Boosting a band can push loud music past full scale, which distorts. Lowering everything
        // by the curve's highest boost keeps the loudest part of the curve at its original level.
        var peak = 0.0;
        if (!flat)
        {
            foreach (var hertz in ResponseGrid(sampleRate))
            {
                peak = Math.Max(peak, design.BandsResponseDb(hertz));
            }

            foreach (var hertz in bands.Centres.Where(hertz => hertz < sampleRate * 0.45))
            {
                peak = Math.Max(peak, design.BandsResponseDb(hertz));
            }
        }

        return peak <= 0 ? design : new EqualizerDesign(bands, sampleRate, shapes, gains, preampDb, peak, isBypass: false);
    }

    /// <summary>
    /// The combined gain of the bands and preamp at a frequency, in decibels, as drawn in the
    /// equalizer window (the automatic headroom is left out, because it's not something people set).
    /// </summary>
    public double ResponseDb(double hertz) => IsBypass ? 0 : BandsResponseDb(hertz) + PreampDb;

    /// <summary>The bands' combined gain at a frequency, in decibels, without the preamp.</summary>
    public double BandsResponseDb(double hertz)
    {
        var w = 2 * Math.PI * Math.Min(hertz, SampleRate * 0.4999) / SampleRate;
        var cosW = Math.Cos(w);
        var cos2W = Math.Cos(2 * w);
        var total = 0.0;
        for (var band = 0; band < Shapes.Count; band++)
        {
            if (FilterGainsDb[band] != 0)
            {
                total += _coefficients[band].ResponseDb(cosW, cos2W);
            }
        }

        return total;
    }

    private static IEnumerable<double> ResponseGrid(int sampleRate)
    {
        var highest = Math.Min(20000, sampleRate * 0.45);
        for (var i = 0; i < ResponseGridPoints; i++)
        {
            yield return 20 * Math.Pow(highest / 20, i / (double)(ResponseGridPoints - 1));
        }
    }
}

/// <summary>
/// Works out how hard to drive each band's filter. Neighbouring bands overlap, so a slider at
/// +6 dB with its neighbours at 0 would otherwise spill a couple of decibels into them. This
/// solves (by weighted least squares, after Välimäki and Liski's accurate graphic equalizer) for
/// filter gains whose combined response follows the smooth curve through the sliders: on each
/// slider at its centre, and close to the curve in between. The overlap pattern only depends on
/// the layout and sample rate, so one solver is kept for each pair.
/// </summary>
public sealed class EqualizerSolver
{
    // The overlap is measured with each filter at this gain, then refined against the real response.
    private const double ProbeGainDb = 12;
    private const int Refinements = 6;

    // Besides each centre, the fit checks this many points between neighbouring centres, which
    // count for less: the sliders themselves matter most.
    private const int PointsBetween = 2;
    private const double BetweenWeight = 0.1;

    private readonly int[] _usedFilters;
    private readonly double[] _fitHertz;
    private readonly double[] _fitCosW;
    private readonly double[] _fitCos2W;
    private readonly double[,] _projection;

    public EqualizerSolver(EqualizerBands bands, int sampleRate)
    {
        Bands = bands;
        SampleRate = sampleRate;
        var count = bands.Count;
        var spacing = bands.SpacingOctaves;
        // Bells a little wider than the gap between bands overlap enough that, with every slider at
        // the same level, the bands sum to within about a decibel of flat. Much wider, and lifting
        // a single slider would need its neighbours pulled down hard to keep it to itself.
        var bandwidth = spacing * 1.3;
        // One filter per band, plus a hidden bell below the lowest band. Without it the low shelf
        // keeps rising below its slider; with it the solver can keep the deep bass level instead.
        var shapes = new EqualizerFilterShape[count + 1];
        var usable = new bool[count + 1];
        for (var band = 0; band < count; band++)
        {
            var centre = bands.Centres[band];
            // The shelves turn over halfway to their neighbour, so the end bands hand over to the
            // middle ones smoothly.
            var (kind, hertz) = band == 0 ? (EqualizerFilterKind.LowShelf, centre * Math.Pow(2, spacing / 2))
                : band == count - 1 ? (EqualizerFilterKind.HighShelf, centre / Math.Pow(2, spacing / 2))
                : (EqualizerFilterKind.Peak, centre);
            // A band too close to the top of what this sample rate can hold is left flat.
            usable[band] = centre < sampleRate * 0.46 && hertz < sampleRate * 0.46;
            shapes[band] = new EqualizerFilterShape(kind, usable[band] ? hertz : 1000, bandwidth, sampleRate);
        }

        var guard = bands.Centres[0] / Math.Pow(2, spacing);
        usable[count] = guard >= 8;
        shapes[count] = new EqualizerFilterShape(EqualizerFilterKind.Peak, usable[count] ? guard : 1000, bandwidth, sampleRate);

        Shapes = shapes;
        _usedFilters = Enumerable.Range(0, count + 1).Where(filter => usable[filter]).ToArray();
        var usableBands = _usedFilters.Where(filter => filter < count).ToArray();

        // The fit is checked at every centre and at a few points in between, so the response follows
        // the smooth curve through the sliders instead of only touching it at the centres.
        var fitHertz = new List<double>();
        var weights = new List<double>();
        void AddBeyond(double hertz)
        {
            // Past the outer sliders the curve stays level, which keeps the shelves from overshooting.
            if (hertz >= 10 && hertz < sampleRate * 0.46)
            {
                fitHertz.Add(hertz);
                weights.Add(BetweenWeight);
            }
        }

        for (var m = PointsBetween + 1; m >= 1; m--)
        {
            AddBeyond(bands.Centres[usableBands[0]] / Math.Pow(2, spacing * m / (PointsBetween + 1)));
        }

        for (var i = 0; i < usableBands.Length; i++)
        {
            var centre = bands.Centres[usableBands[i]];
            fitHertz.Add(centre);
            weights.Add(1);
            if (i + 1 < usableBands.Length)
            {
                for (var m = 1; m <= PointsBetween; m++)
                {
                    fitHertz.Add(centre * Math.Pow(2, spacing * m / (PointsBetween + 1)));
                    weights.Add(BetweenWeight);
                }
            }
        }

        for (var m = 1; m <= PointsBetween; m++)
        {
            AddBeyond(bands.Centres[usableBands[^1]] * Math.Pow(2, spacing * m / (PointsBetween + 1)));
        }

        _fitHertz = fitHertz.ToArray();
        _fitCosW = _fitHertz.Select(hertz => Math.Cos(2 * Math.PI * hertz / sampleRate)).ToArray();
        _fitCos2W = _fitHertz.Select(hertz => Math.Cos(4 * Math.PI * hertz / sampleRate)).ToArray();

        // Row = fit point, column = filter: how much of a filter's gain reaches each point.
        var size = _usedFilters.Length;
        var points = _fitHertz.Length;
        var overlap = new double[points, size];
        for (var column = 0; column < size; column++)
        {
            var probe = shapes[_usedFilters[column]].At(ProbeGainDb);
            for (var row = 0; row < points; row++)
            {
                overlap[row, column] = probe.ResponseDb(_fitCosW[row], _fitCos2W[row]) / ProbeGainDb;
            }
        }

        // Weighted least squares: projection = (Bᵀ W B)⁻¹ Bᵀ W.
        var normal = new double[size, size];
        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < size; j++)
            {
                var sum = 0.0;
                for (var row = 0; row < points; row++)
                {
                    sum += overlap[row, i] * weights[row] * overlap[row, j];
                }

                normal[i, j] = sum;
            }
        }

        var inverse = Invert(normal);
        _projection = new double[size, points];
        for (var i = 0; i < size; i++)
        {
            for (var row = 0; row < points; row++)
            {
                var sum = 0.0;
                for (var k = 0; k < size; k++)
                {
                    sum += inverse[i, k] * overlap[row, k];
                }

                _projection[i, row] = sum * weights[row];
            }
        }
    }

    public EqualizerBands Bands { get; }

    public int SampleRate { get; }

    public EqualizerFilterShape[] Shapes { get; }

    /// <summary>Filter gains, one per band, whose combined response follows the sliders.</summary>
    public double[] Solve(IReadOnlyList<double> sliderGainsDb)
    {
        var size = _usedFilters.Length;
        var points = _fitHertz.Length;
        var curve = EqualizerCurve.FromBands(Bands, sliderGainsDb);
        var target = _fitHertz.Select(curve.GainAt).ToArray();

        var gains = new double[size];
        var coefficients = new BiquadCoefficients[size];
        var error = (double[])target.Clone();
        for (var pass = 0; pass <= Refinements; pass++)
        {
            // Filters don't scale perfectly in decibels, so each pass corrects what's left over.
            for (var column = 0; column < size; column++)
            {
                var step = 0.0;
                for (var row = 0; row < points; row++)
                {
                    step += _projection[column, row] * error[row];
                }

                gains[column] = Math.Clamp(gains[column] + step, -EqualizerDesign.MaxFilterGainDb, EqualizerDesign.MaxFilterGainDb);
            }

            if (pass == Refinements)
            {
                break;
            }

            for (var column = 0; column < size; column++)
            {
                coefficients[column] = Shapes[_usedFilters[column]].At(gains[column]);
            }

            for (var row = 0; row < points; row++)
            {
                var actual = 0.0;
                for (var column = 0; column < size; column++)
                {
                    if (gains[column] != 0)
                    {
                        actual += coefficients[column].ResponseDb(_fitCosW[row], _fitCos2W[row]);
                    }
                }

                error[row] = target[row] - actual;
            }
        }

        var result = new double[Shapes.Length];
        for (var column = 0; column < size; column++)
        {
            // Rounding away tiny gains lets bands with nothing to do switch themselves off.
            result[_usedFilters[column]] = Math.Abs(gains[column]) < 1e-4 ? 0 : gains[column];
        }

        return result;
    }

    // Gauss-Jordan elimination with partial pivoting; the matrices are at most 31 × 31.
    private static double[,] Invert(double[,] matrix)
    {
        var size = matrix.GetLength(0);
        var work = (double[,])matrix.Clone();
        var inverse = new double[size, size];
        for (var i = 0; i < size; i++)
        {
            inverse[i, i] = 1;
        }

        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < size; row++)
            {
                if (Math.Abs(work[row, column]) > Math.Abs(work[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (pivot != column)
            {
                for (var k = 0; k < size; k++)
                {
                    (work[column, k], work[pivot, k]) = (work[pivot, k], work[column, k]);
                    (inverse[column, k], inverse[pivot, k]) = (inverse[pivot, k], inverse[column, k]);
                }
            }

            var scale = work[column, column];
            for (var k = 0; k < size; k++)
            {
                work[column, k] /= scale;
                inverse[column, k] /= scale;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == column || work[row, column] == 0)
                {
                    continue;
                }

                var factor = work[row, column];
                for (var k = 0; k < size; k++)
                {
                    work[row, k] -= factor * work[column, k];
                    inverse[row, k] -= factor * inverse[column, k];
                }
            }
        }

        return inverse;
    }
}
