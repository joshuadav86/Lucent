using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Lucent.Mac.Audio;
using NAudio.Dsp;
using WinampInspiredVisualizer.Audio;

namespace Lucent.Mac.Windows;

/// <summary>
/// The live spectrum behind the equalizer's response curve, like a studio EQ's analyzer: how loud
/// each frequency is in the music right now, on the same frequency scale as the sliders, so a
/// boomy or harsh frequency shows up as a peak right above the slider that tames it.
/// </summary>
public sealed class EqualizerSpectrum : Control
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

    private static readonly IBrush FillBrush = new ImmutableLinearGradientBrush(
        new[]
        {
            new ImmutableGradientStop(0, Color.FromArgb(0x55, 0xF2, 0xF0, 0xEA)),
            new ImmutableGradientStop(1, Color.FromArgb(0x08, 0xF2, 0xF0, 0xEA)),
        },
        startPoint: new RelativePoint(0, 0, RelativeUnit.Relative),
        endPoint: new RelativePoint(0, 1, RelativeUnit.Relative));

    private static readonly IPen LinePen = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromArgb(0x99, 0xF2, 0xF0, 0xEA)), 1);

    private readonly AudioCapture _capture;
    private readonly float[] _samples = new float[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private float[] _levels = Array.Empty<float>();
    private float[] _targets = Array.Empty<float>();
    private Point[] _points = Array.Empty<Point>();
    private double _lowestHz = 20;
    private double _octaves = 10;
    private TimeSpan _lastFrame;
    private bool _running;
    private bool _frameRequested;

    public EqualizerSpectrum(AudioCapture capture)
    {
        _capture = capture;
        IsHitTestVisible = false;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
        }
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

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Stop();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                Start();
            }
            else
            {
                Stop();
            }
        }
    }

    private void Start()
    {
        if (!_running && IsVisible && VisualRoot is not null)
        {
            _running = true;
            _lastFrame = TimeSpan.Zero;
            RequestFrame();
        }
    }

    private void Stop() => _running = false;

    // Once a screen frame, like WPF's CompositionTarget.Rendering, for as long as it's showing.
    private void RequestFrame()
    {
        if (_running && !_frameRequested && TopLevel.GetTopLevel(this) is { } top)
        {
            _frameRequested = true;
            top.RequestAnimationFrame(OnFrame);
        }
    }

    private void OnFrame(TimeSpan time)
    {
        _frameRequested = false;
        if (!_running)
        {
            return;
        }

        var deltaTime = _lastFrame == TimeSpan.Zero ? 1 / 60f : (float)Math.Clamp((time - _lastFrame).TotalSeconds, 0, 0.1);
        _lastFrame = time;
        Advance(deltaTime);
        RequestFrame();
    }

    /// <summary>Moves the spectrum on by <paramref name="deltaTime"/> seconds. (Screenshots call this directly.)</summary>
    internal void Advance(float deltaTime)
    {
        var columns = (int)(Bounds.Width / Step) + 1;
        if (columns < 2 || Bounds.Height <= 0)
        {
            return;
        }

        if (_levels.Length != columns)
        {
            _levels = new float[columns];
            _targets = new float[columns];
            _points = new Point[columns];
        }

        if (_capture.IsSilent || Paused)
        {
            // Nothing playing: let what's showing sink away, then stop redrawing.
            if (Array.TrueForAll(_levels, level => level <= 0.001f))
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

    public override void Render(DrawingContext dc)
    {
        var height = Bounds.Height;
        var count = _levels.Length;
        if (count < 2 || height <= 0 || Array.TrueForAll(_levels, level => level <= 0.001f))
        {
            return;
        }

        for (var i = 0; i < count; i++)
        {
            // A little smoothing across neighbours, so it reads as one flowing shape.
            var level = (_levels[Math.Max(0, i - 1)] + 2 * _levels[i] + _levels[Math.Min(count - 1, i + 1)]) / 4;
            _points[i] = new Point(i * Step, height - level * height);
        }

        var area = new StreamGeometry();
        using (var context = area.Open())
        {
            context.BeginFigure(new Point(0, height), true);
            for (var i = 0; i < count; i++)
            {
                context.LineTo(_points[i]);
            }

            context.LineTo(new Point(_points[count - 1].X, height));
            context.EndFigure(true);
        }

        var outline = new StreamGeometry();
        using (var context = outline.Open())
        {
            context.BeginFigure(_points[0], false);
            for (var i = 1; i < count; i++)
            {
                context.LineTo(_points[i]);
            }

            context.EndFigure(false);
        }

        dc.DrawGeometry(FillBrush, null, area);
        dc.DrawGeometry(null, LinePen, outline);
    }
}
