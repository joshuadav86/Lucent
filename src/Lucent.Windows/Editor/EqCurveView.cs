using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Lucent.Editor.Dsp;

namespace Lucent.Editor;

/// <summary>
/// The equalizer's curve, over the spectrum of the selection: a handle for each band, dragged left and right
/// for its frequency and up and down for its gain. The wheel over a handle makes the band narrower or wider;
/// double-clicking one switches it on or off.
/// </summary>
public sealed class EqCurveView : FrameworkElement
{
    public const double MinHz = 20;
    public const double MaxHz = 20000;
    public const double RangeDb = 18;
    private const double HandleRadius = 8;

    public static readonly DependencyProperty CurveBrushProperty = Register(nameof(CurveBrush));
    public static readonly DependencyProperty GridBrushProperty = Register(nameof(GridBrush));
    public static readonly DependencyProperty LabelBrushProperty = Register(nameof(LabelBrush));
    public static readonly DependencyProperty SpectrumBrushProperty = Register(nameof(SpectrumBrush));
    public static readonly DependencyProperty HandleTextBrushProperty = Register(nameof(HandleTextBrush));

    private List<EqBand> _bands = EqBand.Defaults();
    private double[]? _spectrum;
    private int _selected = -1;
    private bool _dragging;

    public EqCurveView()
    {
        ClipToBounds = true;
        Cursor = Cursors.Arrow;
    }

    /// <summary>A band was moved, widened or switched on or off, by the mouse.</summary>
    public event EventHandler? BandsChanged;

    /// <summary>A different band was picked.</summary>
    public event EventHandler? SelectedBandChanged;

    public Brush? CurveBrush { get => (Brush?)GetValue(CurveBrushProperty); set => SetValue(CurveBrushProperty, value); }

    public Brush? GridBrush { get => (Brush?)GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public Brush? LabelBrush { get => (Brush?)GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public Brush? SpectrumBrush { get => (Brush?)GetValue(SpectrumBrushProperty); set => SetValue(SpectrumBrushProperty, value); }

    public Brush? HandleTextBrush { get => (Brush?)GetValue(HandleTextBrushProperty); set => SetValue(HandleTextBrushProperty, value); }

    public int SampleRate { get; set; } = 44100;

    /// <summary>False to show only the spectrum, as the analysis card does.</summary>
    public bool ShowCurve { get; set; } = true;

    /// <summary>The bands being edited. The view changes them in place.</summary>
    public List<EqBand> Bands
    {
        get => _bands;
        set
        {
            _bands = value;
            InvalidateVisual();
        }
    }

    /// <summary>The selection's average spectrum (dB per FFT bin, from <see cref="AudioAnalysis.AverageSpectrum"/>), drawn behind the curve.</summary>
    public double[]? Spectrum
    {
        get => _spectrum;
        set
        {
            _spectrum = value;
            InvalidateVisual();
        }
    }

    public int SelectedBand
    {
        get => _selected;
        set
        {
            _selected = value;
            InvalidateVisual();
            SelectedBandChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Redraws after the bands were changed somewhere else (the sliders under the curve).</summary>
    public void Refresh() => InvalidateVisual();

    private double X(double hz) => ActualWidth * Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz);

    private double Hz(double x) => MinHz * Math.Pow(MaxHz / MinHz, Math.Clamp(x / Math.Max(1, ActualWidth), 0, 1));

    private double Y(double db) => ActualHeight / 2 - db / RangeDb * (ActualHeight / 2 - 6);

    private double Db(double y) => Math.Clamp((ActualHeight / 2 - y) / (ActualHeight / 2 - 6) * RangeDb, -RangeDb, RangeDb);

    private Point Handle(EqBand band) => new(X(band.Frequency), band.HasGain ? Y(band.GainDb) : Y(0));

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width < 10 || height < 10)
        {
            return;
        }

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var grid = new Pen(GridBrush ?? Brushes.Gray, 1);
        grid.Freeze();

        if (_spectrum is { Length: > 2 } spectrum)
        {
            // The music under the curve, from -90 dB at the bottom to 0 dB at the top.
            var size = (spectrum.Length - 1) * 2;
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                g.BeginFigure(new Point(0, height), true, true);
                for (var x = 0; x <= (int)width; x += 2)
                {
                    var lo = Hz(x) / SampleRate * size;
                    var hi = Hz(x + 2) / SampleRate * size;
                    var db = -200.0;
                    for (var b = (int)lo; b <= Math.Min(spectrum.Length - 1, (int)Math.Ceiling(hi)); b++)
                    {
                        db = Math.Max(db, spectrum[b]);
                    }

                    var y = height - Math.Clamp((db + 90) / 90, 0, 1) * height;
                    g.LineTo(new Point(x, y), true, false);
                }

                g.LineTo(new Point(width, height), true, false);
            }

            shape.Freeze();
            dc.DrawGeometry(SpectrumBrush, null, shape);
        }

        foreach (var hz in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000 })
        {
            var x = Math.Round(X(hz)) + 0.5;
            dc.DrawLine(grid, new Point(x, 0), new Point(x, height));
            dc.DrawText(Text(hz >= 1000 ? $"{hz / 1000:0}k" : $"{hz:0}", 9, LabelBrush), new Point(x + 3, height - 13));
        }

        if (!ShowCurve)
        {
            // Just the spectrum: its own levels, below full scale.
            foreach (var db in new[] { -20.0, -40, -60, -80 })
            {
                var y = Math.Round(height - (db + 90) / 90 * height) + 0.5;
                dc.DrawLine(grid, new Point(0, y), new Point(width, y));
                dc.DrawText(Text($"{db:0;−0} dB", 9, LabelBrush), new Point(3, y - 12));
            }

            return;
        }

        foreach (var db in new[] { -12.0, -6, 0, 6, 12 })
        {
            var y = Math.Round(Y(db)) + 0.5;
            dc.DrawLine(grid, new Point(0, y), new Point(width, y));
            dc.DrawText(Text(db == 0 ? "0 dB" : $"{db:+0;−0}", 9, LabelBrush), new Point(3, y - 12));
        }

        var curve = new StreamGeometry();
        using (var g = curve.Open())
        {
            for (var x = 0; x <= (int)width; x += 2)
            {
                var db = ParametricEq.ResponseDb(_bands, Hz(x), SampleRate);
                var point = new Point(x, Math.Clamp(Y(db), -10, height + 10));
                if (x == 0)
                {
                    g.BeginFigure(point, false, false);
                }
                else
                {
                    g.LineTo(point, true, false);
                }
            }
        }

        curve.Freeze();
        dc.DrawGeometry(null, new Pen(CurveBrush ?? Brushes.White, 2), curve);

        for (var i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i];
            var at = Handle(band);
            var selected = i == _selected;
            var fill = band.Enabled ? CurveBrush : null;
            dc.PushOpacity(band.Enabled ? 1 : 0.5);
            dc.DrawEllipse(fill, new Pen(selected ? HandleTextBrush ?? Brushes.White : CurveBrush ?? Brushes.White, selected ? 2 : 1.2),
                at, HandleRadius, HandleRadius);
            var label = Text((i + 1).ToString(CultureInfo.CurrentCulture), 10, band.Enabled ? HandleTextBrush : CurveBrush);
            dc.DrawText(label, new Point(at.X - label.Width / 2, at.Y - label.Height / 2));
            dc.Pop();
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var position = e.GetPosition(this);
        var hit = HandleAt(position);
        if (hit is not { } index)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            _bands[index].Enabled = !_bands[index].Enabled;
            Changed();
        }
        else
        {
            _dragging = true;
            CaptureMouse();
        }

        SelectedBand = index;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var position = e.GetPosition(this);
        if (!_dragging || !IsMouseCaptured)
        {
            Cursor = HandleAt(position) is null ? Cursors.Arrow : Cursors.SizeAll;
            return;
        }

        if (_selected < 0 || _selected >= _bands.Count)
        {
            return;
        }

        var band = _bands[_selected];
        band.Frequency = Math.Round(Hz(position.X));
        if (band.HasGain)
        {
            band.GainDb = Math.Round(Db(position.Y) * 2) / 2;
        }

        band.Enabled = true;
        Changed();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        _dragging = false;
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var index = HandleAt(e.GetPosition(this)) ?? _selected;
        if (index < 0 || index >= _bands.Count)
        {
            return;
        }

        var band = _bands[index];
        // Up makes it narrower.
        band.Q = Math.Clamp(band.Q * Math.Pow(1.15, e.Delta / 120.0), 0.1, 30);
        SelectedBand = index;
        Changed();
        e.Handled = true;
    }

    private int? HandleAt(Point position)
    {
        for (var i = _bands.Count - 1; i >= 0; i--)
        {
            var at = Handle(_bands[i]);
            if ((at - position).Length <= HandleRadius + 3)
            {
                return i;
            }
        }

        return null;
    }

    private void Changed()
    {
        InvalidateVisual();
        BandsChanged?.Invoke(this, EventArgs.Empty);
    }

    private FormattedText Text(string text, double size, Brush? brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface((FontFamily)(TryFindResource("UiFont") ?? new FontFamily("Segoe UI")), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            size, brush ?? Brushes.Gray, VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(EqCurveView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
