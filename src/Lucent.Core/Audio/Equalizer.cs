using NAudio.Wave;
using Lucent.Settings;

namespace Lucent.Audio;

/// <summary>
/// A graphic equalizer that sits in the playback chain. One instance lasts for the whole session:
/// each new track is plugged in with <see cref="Attach"/>, and the sliders carry over.
/// </summary>
/// <remarks>
/// Settings are changed on the UI thread, which designs a new, unchangeable
/// <see cref="EqualizerDesign"/> and hands it to the audio thread by swapping one reference.
/// The audio thread then glides each filter towards its new gain, so moving a slider never clicks.
/// When the equalizer is off or flat, audio passes through untouched and nothing is computed.
/// </remarks>
public sealed class Equalizer : ISampleProvider
{
    /// <summary>The furthest a slider (or the preamp) goes up or down, in decibels.</summary>
    public const double MaxGainDb = 12;

    // Used to draw the curve before any track has been opened.
    private const int PreviewSampleRate = 48000;

    private readonly object _gate = new();
    private readonly Dictionary<(int Bands, int SampleRate), EqualizerSolver> _solvers = new();
    private EqualizerBands _bands = EqualizerBands.ForCount(10);
    private double[] _gains = new double[10];
    private double _preampDb;
    private bool _enabled = true;
    private EqualizerDesign _design;
    private EqualizerDesign _preview;
    private volatile EqualizerStream? _stream;
    private volatile float _balance;

    public Equalizer()
    {
        _design = _preview = BuildDesign(PreviewSampleRate, _enabled);
    }

    public WaveFormat WaveFormat => _stream?.WaveFormat ?? WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);

    /// <summary>What the equalizer is doing to the current track right now.</summary>
    public EqualizerDesign Design
    {
        get
        {
            lock (_gate)
            {
                return _design;
            }
        }
    }

    /// <summary>
    /// The current curve as if the equalizer were on, for drawing: the window still shows the
    /// curve while it's switched off.
    /// </summary>
    public EqualizerDesign Preview
    {
        get
        {
            lock (_gate)
            {
                return _preview;
            }
        }
    }

    /// <summary>Plugs a newly opened track into the equalizer and returns the equalized audio.</summary>
    public ISampleProvider Attach(ISampleProvider source)
    {
        lock (_gate)
        {
            UpdateDesigns(source.WaveFormat.SampleRate);
            _stream = new EqualizerStream(source, _design);
        }

        return this;
    }

    /// <summary>Lets go of the current track once it has been closed.</summary>
    public void Detach()
    {
        lock (_gate)
        {
            _stream = null;
        }
    }

    /// <summary>
    /// How far the music is being turned down right now, in decibels, so the boosts don't push it
    /// past full scale. It follows the music: boosting bass a track doesn't have costs nothing.
    /// </summary>
    public double HeadroomDb => _stream?.HeadroomDb ?? 0;

    /// <summary>
    /// Left and right balance, from -1 (left only) to 1 (right only), as on Winamp's balance slider. The side
    /// it leans away from is turned down; centred (0), the sound isn't touched.
    /// </summary>
    public double Balance
    {
        get => _balance;
        set => _balance = (float)Math.Clamp(value, -1, 1);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        // Read the stream once: a track change on the UI thread may swap it mid-call.
        var stream = _stream;
        var read = stream?.Read(buffer, offset, count) ?? 0;
        var balance = _balance;
        if (balance != 0 && stream is { WaveFormat.Channels: 2 })
        {
            var left = Math.Min(1f, 1f - balance);
            var right = Math.Min(1f, 1f + balance);
            for (var i = offset; i + 1 < offset + read; i += 2)
            {
                buffer[i] *= left;
                buffer[i + 1] *= right;
            }
        }

        return read;
    }

    /// <summary>Takes on saved settings: on or off, band count, slider gains and preamp.</summary>
    public void Apply(EqualizerSettings settings)
    {
        var bands = BandsFor(settings);
        Configure(settings.Enabled, bands, SliderGains(settings, bands), settings.PreampDb);
    }

    /// <summary>Changes every setting at once. Call from the UI thread.</summary>
    public void Configure(bool enabled, EqualizerBands bands, IReadOnlyList<double> gainsDb, double preampDb)
    {
        lock (_gate)
        {
            _enabled = enabled;
            _bands = bands;
            _gains = Enumerable.Range(0, bands.Count)
                .Select(band => band < gainsDb.Count ? Math.Clamp(gainsDb[band], -MaxGainDb, MaxGainDb) : 0)
                .ToArray();
            _preampDb = Math.Clamp(preampDb, -MaxGainDb, MaxGainDb);

            var stream = _stream;
            UpdateDesigns(stream?.WaveFormat.SampleRate ?? _design.SampleRate);
            stream?.Retarget(_design);
        }
    }

    /// <summary>The band layout saved settings are for: one of Lucent's, or the classic skin's ten.</summary>
    public static EqualizerBands BandsFor(EqualizerSettings settings) =>
        settings.BandCount == EqualizerBands.Classic.Count ? EqualizerBands.Classic : EqualizerBands.ForCount(settings.BandCount);

    /// <summary>
    /// Moves saved settings to another band layout, keeping the same sound: a preset is redrawn at the new
    /// bands, and a hand-made curve is traced through its sliders and sampled at them.
    /// </summary>
    public static void ChangeBands(EqualizerSettings settings, EqualizerBands bands)
    {
        var current = BandsFor(settings);
        var gains = SliderGains(settings, current);
        if (ReferenceEquals(current, bands))
        {
            settings.Gains = gains.ToList();
            return;
        }

        var curve = EqualizerPresets.Find(settings.PresetName)?.Curve ?? EqualizerCurve.FromBands(current, gains);
        settings.BandCount = bands.Count;
        settings.Gains = curve.Sample(bands).ToList();
    }

    /// <summary>
    /// The slider gains saved settings describe for a layout: the saved gains when they match it,
    /// otherwise the named preset, otherwise the saved sliders redrawn for the new layout when they
    /// came from a band count Lucent no longer offers, otherwise flat.
    /// </summary>
    public static double[] SliderGains(EqualizerSettings settings, EqualizerBands bands)
    {
        if (settings.Gains.Count == bands.Count)
        {
            return settings.Gains.Select(gain => double.IsFinite(gain) ? Math.Clamp(gain, -MaxGainDb, MaxGainDb) : 0).ToArray();
        }

        if (EqualizerPresets.Find(settings.PresetName) is { } preset)
        {
            return preset.Curve.Sample(bands);
        }

        return EqualizerBands.Former(settings.Gains.Count) is { } former
            ? EqualizerCurve.FromBands(former, settings.Gains).Sample(bands).Select(gain => Math.Clamp(gain, -MaxGainDb, MaxGainDb)).ToArray()
            : new double[bands.Count];
    }

    private void UpdateDesigns(int sampleRate)
    {
        _design = BuildDesign(sampleRate, _enabled);
        _preview = _enabled ? _design : BuildDesign(sampleRate, enabled: true);
    }

    private EqualizerDesign BuildDesign(int sampleRate, bool enabled)
    {
        if (!_solvers.TryGetValue((_bands.Count, sampleRate), out var solver))
        {
            solver = new EqualizerSolver(_bands, sampleRate);
            _solvers[(_bands.Count, sampleRate)] = solver;
        }

        return EqualizerDesign.Create(_bands, _gains, _preampDb, enabled, sampleRate, solver);
    }
}

/// <summary>
/// The equalizer's work for one track, all of it on the audio thread. The UI thread only ever
/// swaps in a new design through <see cref="Retarget"/>.
/// </summary>
internal sealed class EqualizerStream
{
    // Audio is filtered in blocks this many frames long, or in short ones while gains are gliding,
    // so each step of a glide is too small to hear.
    private const int BlockFrames = 1024;
    private const int GlideBlockFrames = 32;

    // How quickly gains glide to a new setting (a time constant: most of the way in ~50 ms).
    private const double GlideSeconds = 0.015;

    // The safety limiter only touches samples that would otherwise reach full scale.
    private const double LimiterCeiling = 0.989;
    private const double LimiterReleaseSeconds = 0.15;

    // Ahead of the limiter, the headroom turns the music down smoothly when the boosts make it
    // reach full scale, and only then: it eases down over a few milliseconds, holds while the music
    // stays within a decibel or so of the top (longer than a cycle of deep bass, so it doesn't
    // wobble with the waveform), then slowly lets go.
    private const double HeadroomAttackSeconds = 0.005;
    private const double HeadroomHoldSeconds = 0.08;
    private const double HeadroomReleaseSeconds = 0.8;
    private const double HeadroomHoldLevel = 0.88;

    // Superseded filter chains (after a band count change) still fading out.
    private const int MaxChains = 4;

    private readonly ISampleProvider _source;
    private readonly int _channels;
    private readonly double[] _planar;
    private readonly double _glideStep;
    private readonly double _limiterRelease;
    private readonly double _headroomAttack;
    private readonly double _headroomRelease;
    private readonly int _headroomHoldFrames;
    private readonly List<EqualizerFilterChain> _chains = new();
    private volatile EqualizerDesign _pending;
    private EqualizerDesign _applied;
    private double _outputGain;
    private double _targetOutputGain;
    private double _limiterGain = 1;
    private double _headroomGain = 1;
    private int _headroomHoldLeft;
    private double _reportedHeadroomDb;
    private bool _bypassed;

    public EqualizerStream(ISampleProvider source, EqualizerDesign design)
    {
        _source = source;
        _channels = Math.Max(1, source.WaveFormat.Channels);
        _planar = new double[_channels * BlockFrames];
        var sampleRate = source.WaveFormat.SampleRate;
        _glideStep = 1 - Math.Exp(-GlideBlockFrames / (GlideSeconds * sampleRate));
        _limiterRelease = 1 - Math.Exp(-1 / (LimiterReleaseSeconds * sampleRate));
        _headroomAttack = 1 - Math.Exp(-1 / (HeadroomAttackSeconds * sampleRate));
        _headroomRelease = 1 - Math.Exp(-1 / (HeadroomReleaseSeconds * sampleRate));
        _headroomHoldFrames = (int)(HeadroomHoldSeconds * sampleRate);

        // A new track starts at the current settings straight away: there's nothing to glide from.
        _pending = design;
        _applied = design;
        _chains.Add(new EqualizerFilterChain(design, _channels));
        _outputGain = _targetOutputGain = design.OutputGain;
        _bypassed = design.IsBypass;
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    /// <summary>How far the headroom is turning the music down, in decibels. Safe to read from any thread.</summary>
    public double HeadroomDb => Volatile.Read(ref _reportedHeadroomDb);

    /// <summary>Hands over new settings. Safe to call from any thread.</summary>
    public void Retarget(EqualizerDesign design) => _pending = design;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        if (read <= 0)
        {
            return read;
        }

        var design = _pending;
        if (!ReferenceEquals(design, _applied))
        {
            Apply(design);
        }

        if (!_bypassed)
        {
            Process(buffer, offset, read / _channels);
        }

        return read;
    }

    private void Apply(EqualizerDesign design)
    {
        _applied = design;
        var newest = _chains[^1];
        if (ReferenceEquals(newest.Bands, design.Bands))
        {
            newest.SetTargets(design.FilterGainsDb);
        }
        else
        {
            // A different band count: fade the old filters out while the new ones fade in. Running
            // them one after the other means each starts and stops from exactly flat, so the
            // changeover is seamless.
            foreach (var chain in _chains)
            {
                chain.Retire();
            }

            if (_chains.Count >= MaxChains)
            {
                _chains.RemoveAt(0);
            }

            var replacement = new EqualizerFilterChain(design, _channels, startFlat: true);
            replacement.SetTargets(design.FilterGainsDb);
            _chains.Add(replacement);
        }

        _targetOutputGain = design.OutputGain;
        _bypassed = false;
    }

    private void Process(float[] buffer, int offset, int frames)
    {
        var channels = _channels;
        var done = 0;
        while (done < frames)
        {
            var gliding = _outputGain != _targetOutputGain;
            foreach (var chain in _chains)
            {
                gliding |= chain.IsGliding;
            }

            var length = Math.Min(frames - done, gliding ? GlideBlockFrames : BlockFrames);
            var start = offset + done * channels;
            var fromGain = _outputGain;
            if (gliding)
            {
                foreach (var chain in _chains)
                {
                    chain.Glide(_glideStep);
                }

                _outputGain += (_targetOutputGain - _outputGain) * _glideStep;
                if (Math.Abs(_targetOutputGain - _outputGain) < 1e-6 * _targetOutputGain)
                {
                    _outputGain = _targetOutputGain;
                }
            }

            for (var channel = 0; channel < channels; channel++)
            {
                var row = channel * BlockFrames;
                for (var frame = 0; frame < length; frame++)
                {
                    _planar[row + frame] = buffer[start + frame * channels + channel];
                }
            }

            foreach (var chain in _chains)
            {
                chain.Run(_planar, BlockFrames, length);
            }

            WriteOutput(buffer, start, length, fromGain, _outputGain);
            done += length;
        }

        // Drop filter chains that have faded out completely, and stop processing altogether once
        // the equalizer is off or flat and everything has settled.
        for (var i = _chains.Count - 2; i >= 0; i--)
        {
            if (_chains[i].IsRetired && !_chains[i].IsActive)
            {
                _chains.RemoveAt(i);
            }
        }

        if (_applied.IsBypass && _outputGain == 1 && _headroomGain == 1 && _chains.TrueForAll(chain => !chain.IsActive))
        {
            if (_chains.Count > 1)
            {
                _chains.RemoveRange(0, _chains.Count - 1);
            }

            _bypassed = true;
            _limiterGain = 1;
        }

        Volatile.Write(ref _reportedHeadroomDb, _bypassed ? 0 : -20 * Math.Log10(_headroomGain));
    }

    // Applies the preamp (gliding across the block), then the headroom and the safety limiter, and
    // writes back.
    private void WriteOutput(float[] buffer, int start, int length, double fromGain, double toGain)
    {
        var channels = _channels;
        var gainStep = (toGain - fromGain) / length;
        // On the way to switching off, the headroom only lets go, and quickly: the music is about to
        // play untouched, so there's nothing to make room for.
        var settling = _applied.IsBypass;
        for (var frame = 0; frame < length; frame++)
        {
            var gain = fromGain + gainStep * (frame + 1);
            var peak = 0.0;
            for (var channel = 0; channel < channels; channel++)
            {
                peak = Math.Max(peak, Math.Abs(_planar[channel * BlockFrames + frame] * gain));
            }

            if (!settling && peak * _headroomGain > LimiterCeiling)
            {
                _headroomGain += (LimiterCeiling / peak - _headroomGain) * _headroomAttack;
                _headroomHoldLeft = _headroomHoldFrames;
            }
            else if (!settling && peak * _headroomGain > LimiterCeiling * HeadroomHoldLevel)
            {
                _headroomHoldLeft = _headroomHoldFrames;
            }
            else if (_headroomHoldLeft > 0 && !settling)
            {
                _headroomHoldLeft--;
            }
            else if (_headroomGain != 1)
            {
                _headroomGain += (1 - _headroomGain) * (settling ? _limiterRelease : _headroomRelease);
                if (1 - _headroomGain < 1e-4)
                {
                    _headroomGain = 1;
                }
            }

            peak *= _headroomGain;

            // The limiter clamps down at once on a sample that would reach full scale, then lets go
            // over 150 ms or so, which is far gentler on the ear than hard clipping.
            _limiterGain += (1 - _limiterGain) * _limiterRelease;
            if (1 - _limiterGain < 1e-9)
            {
                _limiterGain = 1;
            }

            if (peak * _limiterGain > LimiterCeiling)
            {
                _limiterGain = LimiterCeiling / peak;
            }

            var total = gain * _headroomGain * _limiterGain;
            for (var channel = 0; channel < channels; channel++)
            {
                buffer[start + frame * channels + channel] = (float)(_planar[channel * BlockFrames + frame] * total);
            }
        }
    }
}

/// <summary>
/// One set of band filters with their own memory for each channel. Bands at 0 dB with nothing
/// left ringing are skipped, so a mostly flat curve costs little.
/// </summary>
internal sealed class EqualizerFilterChain
{
    // A band counts as settled when it's this close to its target, in decibels.
    private const double SettledDb = 0.001;

    // A flat band is switched off once what's left ringing in it is far below hearing.
    private const double SilentState = 1e-10;

    private readonly EqualizerFilterShape[] _shapes;
    private readonly int _channels;
    private readonly double[] _current;
    private readonly double[] _target;
    private readonly BiquadCoefficients[] _coefficients;
    private readonly BiquadCoefficients[] _previous;
    private readonly bool[] _changing;
    private readonly bool[] _active;
    private readonly double[] _state1;
    private readonly double[] _state2;

    public EqualizerFilterChain(EqualizerDesign design, int channels, bool startFlat = false)
    {
        Bands = design.Bands;
        _shapes = design.Shapes.ToArray();
        _channels = channels;
        var count = _shapes.Length;
        _current = startFlat ? new double[count] : design.FilterGainsDb.ToArray();
        _target = (double[])_current.Clone();
        _coefficients = new BiquadCoefficients[count];
        _previous = new BiquadCoefficients[count];
        _changing = new bool[count];
        _active = new bool[count];
        _state1 = new double[count * channels];
        _state2 = new double[count * channels];
        for (var band = 0; band < count; band++)
        {
            _coefficients[band] = _shapes[band].At(_current[band]);
            _active[band] = _current[band] != 0;
        }
    }

    public EqualizerBands Bands { get; }

    public bool IsGliding { get; private set; }

    public bool IsRetired { get; private set; }

    public bool IsActive => Array.IndexOf(_active, true) >= 0;

    public void SetTargets(IReadOnlyList<double> gainsDb)
    {
        for (var band = 0; band < _target.Length; band++)
        {
            _target[band] = gainsDb[band];
            if (_target[band] != 0 && !_active[band])
            {
                // The band's memory is clear and its coefficients are flat, so it starts seamlessly.
                _active[band] = true;
            }
        }

        IsGliding = !_current.AsSpan().SequenceEqual(_target);
    }

    /// <summary>Fades this chain out to flat, because a different band layout is taking over.</summary>
    public void Retire()
    {
        IsRetired = true;
        SetTargets(new double[_target.Length]);
    }

    /// <summary>Moves each gain one step towards its target.</summary>
    public void Glide(double step)
    {
        if (!IsGliding)
        {
            return;
        }

        var stillGliding = false;
        for (var band = 0; band < _current.Length; band++)
        {
            if (_current[band] == _target[band])
            {
                continue;
            }

            _current[band] += (_target[band] - _current[band]) * step;
            if (Math.Abs(_target[band] - _current[band]) < SettledDb)
            {
                _current[band] = _target[band];
            }
            else
            {
                stillGliding = true;
            }

            _previous[band] = _coefficients[band];
            _coefficients[band] = _shapes[band].At(_current[band]);
            _changing[band] = true;
        }

        IsGliding = stillGliding;
    }

    /// <summary>Filters each channel's block in place (channels are rows of <paramref name="stride"/>).</summary>
    public void Run(double[] planar, int stride, int length)
    {
        for (var band = 0; band < _shapes.Length; band++)
        {
            if (!_active[band])
            {
                continue;
            }

            var flat = _current[band] == 0 && _target[band] == 0;
            var silent = true;
            for (var channel = 0; channel < _channels; channel++)
            {
                var slot = band * _channels + channel;
                var s1 = _state1[slot];
                var s2 = _state2[slot];
                var row = channel * stride;
                if (_changing[band])
                {
                    RunChanging(planar, row, length, _previous[band], _coefficients[band], ref s1, ref s2);
                }
                else
                {
                    var (b0, b1, b2, a1, a2) = _coefficients[band];
                    for (var frame = 0; frame < length; frame++)
                    {
                        // Transposed direct form II, in double precision so low bands stay clean.
                        var x = planar[row + frame];
                        var y = b0 * x + s1;
                        s1 = b1 * x - a1 * y + s2;
                        s2 = b2 * x - a2 * y;
                        planar[row + frame] = y;
                    }
                }

                // Flush values too tiny to matter, which would otherwise slow the processor down.
                if (Math.Abs(s1) < 1e-25)
                {
                    s1 = 0;
                }

                if (Math.Abs(s2) < 1e-25)
                {
                    s2 = 0;
                }

                _state1[slot] = s1;
                _state2[slot] = s2;
                silent &= Math.Abs(s1) < SilentState && Math.Abs(s2) < SilentState;
            }

            _changing[band] = false;
            if (flat && silent)
            {
                _active[band] = false;
                for (var channel = 0; channel < _channels; channel++)
                {
                    _state1[band * _channels + channel] = 0;
                    _state2[band * _channels + channel] = 0;
                }
            }
        }
    }

    // While a gain glides, the coefficients slide sample by sample from the last block's to this
    // block's. Both sets are stable and the set of stable filters is convex, so every filter
    // along the way is stable too.
    private static void RunChanging(double[] planar, int row, int length, BiquadCoefficients from, BiquadCoefficients to,
                                    ref double s1, ref double s2)
    {
        var step = 1.0 / length;
        var (b0, b1, b2, a1, a2) = from;
        var db0 = (to.B0 - b0) * step;
        var db1 = (to.B1 - b1) * step;
        var db2 = (to.B2 - b2) * step;
        var da1 = (to.A1 - a1) * step;
        var da2 = (to.A2 - a2) * step;
        for (var frame = 0; frame < length; frame++)
        {
            b0 += db0;
            b1 += db1;
            b2 += db2;
            a1 += da1;
            a2 += da2;
            var x = planar[row + frame];
            var y = b0 * x + s1;
            s1 = b1 * x - a1 * y + s2;
            s2 = b2 * x - a2 * y;
            planar[row + frame] = y;
        }
    }
}
