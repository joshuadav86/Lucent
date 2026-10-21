using System.Windows;
using System.Windows.Media;
using NAudio.Dsp;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// The live spectrum behind the equalizer's response curve, like a studio EQ's analyzer: how loud
/// each frequency is in the music right now, on the same frequency scale as the sliders, so a
/// boomy or harsh frequency shows up as a peak right above the slider that tames it.
/// </summary>
public sealed class EqualizerSpectrum : FrameworkElement
{
    private const int FftExponent = 13;
    private const int FftSize = 1 << FftExponent;
    private const double Step = 2;

    // The loudness range shown, bottom to top, after the tilt below.
    private const float FloorDb = -84;
    private const float CeilingDb = -6;

    // Music has far more energy in the bass, so each octave up is lifted 4.5 dB (as studio
    // analyzers do) to make a balanced mix look roughly level.
    private const float TiltDbPerOctave = 4.5f;

    private static readonly Brush FillBrush = Frozen(new LinearGradientBrush(
        Color.FromArgb(0x55, 0xF2, 0xF0, 0xEA), Color.FromArgb(0x08, 0xF2, 0xF0, 0xEA), 90));

    private static readonly Pen LinePen = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(0x99, 0xF2, 0xF0, 0xEA)), 1));

    private readonly AudioCapture _capture;
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private float[] _levels = Array.Empty<float>();
    private float[] _targets = Array.Empty<float>();
    private double _lowestHz = 20;
    private double _octaves = 10;
    private DateTime _lastFrame = DateTime.UtcNow;
    private bool _running;

    public EqualizerSpectrum(AudioCapture capture)
    {
        _capture = capture;
        IsHitTestVisible = false;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
        }

        Loaded += (_, _) => Start();
        Unloaded += (_, _) => Stop();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible)
            {
                Start();
            }
            else
            {
                Stop();
            }
        };
    }

    /// <summary>
    /// Shows nothing, as if the music were silent: set while the audio editor plays, which the spectrum would
    /// otherwise show (it hears every app) although the EQ isn't shaping it.
    /// </summary>
    public bool Paused { get; set; }

    /// <summary>Lines the spectrum up with the curve: the frequency at the left edge and how many octaves fit across.</summary>
    public void SetScale(double lowestHz, double octaves)
    {
        _lowestHz = lowestHz;
        _octaves = octaves;
        InvalidateVisual();
    }

    private void Start()
    {
        if (!_running && IsLoaded && IsVisible)
        {
            _running = true;
            _lastFrame = DateTime.UtcNow;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    private void Stop()
    {
        if (_running)
        {
            _running = false;
            CompositionTarget.Rendering -= OnFrame;
        }
    }

    private void OnFrame(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var deltaTime = (float)Math.Min(0.1, (now - _lastFrame).TotalSeconds);
        _lastFrame = now;

        var columns = (int)(ActualWidth / Step) + 1;
        if (columns < 2 || ActualHeight <= 0)
        {
            return;
        }

        if (_levels.Length != columns)
        {
            _levels = new float[columns];
            _targets = new float[columns];
        }

        var silent = _capture.IsSilent || Paused;
        if (silent)
        {
            // Nothing playing: let what's showing sink away, then stop redrawing.
            if (_levels.All(level => level <= 0.001f))
            {
                return;
            }

            Array.Clear(_targets);
        }
        else
        {
            Analyze();
        }

        // Rises quickly, falls back slowly, so peaks are easy to spot without flickering.
        var attack = 1f - MathF.Exp(-deltaTime * 30f);
        var release = 1f - MathF.Exp(-deltaTime * 5f);
        for (var i = 0; i < columns; i++)
        {
            var current = _levels[i];
            var target = _targets[i];
            _levels[i] = current + (target - current) * (target > current ? attack : release);
        }

        InvalidateVisual();
    }

    private void Analyze()
    {
        var sampleRate = _capture.SampleRate;
        var delay = _capture.DelaySamples;
        _capture.ReadLatest(_samples, delay);
        for (var i = 0; i < FftSize; i++)
        {
            _fft[i].X = _samples[i] * _window[i];
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftExponent, _fft);

        var binHz = (double)sampleRate / FftSize;
        var lastBin = FftSize / 2 - 1;
        var columns = _targets.Length;
        var width = (columns - 1) * Step;
        for (var i = 0; i < columns; i++)
        {
            // The slice of frequencies this column covers, half a step either side.
            var low = Frequency((i - 0.5) * Step, width);
            var high = Frequency((i + 0.5) * Step, width);
            var first = Math.Clamp((int)Math.Floor(low / binHz), 1, lastBin);
            var last = Math.Clamp((int)Math.Ceiling(high / binHz), first, lastBin);

            // Low down a column is narrower than one bin, so it reads between the two nearest;
            // higher up it takes the loudest bin it covers.
            float power;
            if (last - first <= 1)
            {
                var position = Math.Clamp(Frequency(i * Step, width) / binHz, 1, lastBin - 1);
                var below = (int)position;
                var blend = (float)(position - below);
                power = Power(below) * (1 - blend) + Power(below + 1) * blend;
            }
            else
            {
                power = 0;
                for (var bin = first; bin <= last; bin++)
                {
                    power = MathF.Max(power, Power(bin));
                }
            }

            var hertz = Frequency(i * Step, width);
            var decibels = 10f * MathF.Log10(power + 1e-12f) + TiltDbPerOctave * (float)Math.Log2(hertz / 1000);
            _targets[i] = Math.Clamp((decibels - FloorDb) / (CeilingDb - FloorDb), 0f, 1f);
        }
    }

    private float Power(int bin) => _fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y;

    private double Frequency(double x, double width) => _lowestHz * Math.Pow(2, x / Math.Max(1, width) * _octaves);

    protected override void OnRender(DrawingContext dc)
    {
        var height = ActualHeight;
        if (_levels.Length < 2 || height <= 0 || _levels.All(level => level <= 0.001f))
        {
            return;
        }

        var points = new Point[_levels.Length];
        for (var i = 0; i < _levels.Length; i++)
        {
            // A little smoothing across neighbours, so it reads as one flowing shape.
            var level = (_levels[Math.Max(0, i - 1)] + 2 * _levels[i] + _levels[Math.Min(_levels.Length - 1, i + 1)]) / 4;
            points[i] = new Point(i * Step, height - level * height);
        }

        var outline = new StreamGeometry();
        using (var context = outline.Open())
        {
            context.BeginFigure(points[0], false, false);
            context.PolyLineTo(points.Skip(1).ToList(), true, true);
        }

        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(0, height), true, true);
            context.PolyLineTo(points.Append(new Point(points[^1].X, height)).ToList(), false, true);
        }

        outline.Freeze();
        area.Freeze();
        dc.DrawGeometry(FillBrush, null, area);
        dc.DrawGeometry(null, LinePen, outline);
    }

    private static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
