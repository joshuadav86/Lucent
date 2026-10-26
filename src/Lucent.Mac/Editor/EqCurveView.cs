using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Lucent.Editor.Dsp;

namespace Lucent.Mac.Editor;

/// <summary>
/// The equalizer's curve, over the spectrum of the selection: a handle for each band, dragged left and right
/// for its frequency and up and down for its gain. The wheel over a handle makes the band narrower or wider;
/// double-clicking one switches it on or off.
/// </summary>
public sealed class EqCurveView : Control
{
    public const double MinHz = 20;
    public const double MaxHz = 20000;
    public const double RangeDb = 18;
    private const double HandleRadius = 8;

    public static readonly StyledProperty<IBrush?> CurveBrushProperty = AvaloniaProperty.Register<EqCurveView, IBrush?>(nameof(CurveBrush));
    public static readonly StyledProperty<IBrush?> GridBrushProperty = AvaloniaProperty.Register<EqCurveView, IBrush?>(nameof(GridBrush));
    public static readonly StyledProperty<IBrush?> LabelBrushProperty = AvaloniaProperty.Register<EqCurveView, IBrush?>(nameof(LabelBrush));
    public static readonly StyledProperty<IBrush?> SpectrumBrushProperty = AvaloniaProperty.Register<EqCurveView, IBrush?>(nameof(SpectrumBrush));
    public static readonly StyledProperty<IBrush?> HandleTextBrushProperty = AvaloniaProperty.Register<EqCurveView, IBrush?>(nameof(HandleTextBrush));

    private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
    private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);

    private List<EqBand> _bands = EqBand.Defaults();
    private double[]? _spectrum;
    private int _selected = -1;
    private bool _dragging;

    static EqCurveView()
    {
        AffectsRender<EqCurveView>(CurveBrushProperty, GridBrushProperty, LabelBrushProperty, SpectrumBrushProperty, HandleTextBrushProperty);
    }

    public EqCurveView()
    {
        ClipToBounds = true;
        Cursor = ArrowCursor;
    }

    /// <summary>A band was moved, widened or switched on or off, by the mouse.</summary>
    public event EventHandler? BandsChanged;

    /// <summary>A different band was picked.</summary>
    public event EventHandler? SelectedBandChanged;

    public IBrush? CurveBrush { get => GetValue(CurveBrushProperty); set => SetValue(CurveBrushProperty, value); }

    public IBrush? GridBrush { get => GetValue(GridBrushProperty); set => SetValue(GridBrushProperty, value); }

    public IBrush? LabelBrush { get => GetValue(LabelBrushProperty); set => SetValue(LabelBrushProperty, value); }

    public IBrush? SpectrumBrush { get => GetValue(SpectrumBrushProperty); set => SetValue(SpectrumBrushProperty, value); }

    public IBrush? HandleTextBrush { get => GetValue(HandleTextBrushProperty); set => SetValue(HandleTextBrushProperty, value); }

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

    private double X(double hz) => Bounds.Width * Math.Log(hz / MinHz) / Math.Log(MaxHz / MinHz);

    private double Hz(double x) => MinHz * Math.Pow(MaxHz / MinHz, Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1));

    private double Y(double db) => Bounds.Height / 2 - db / RangeDb * (Bounds.Height / 2 - 6);

    private double Db(double y) => Math.Clamp((Bounds.Height / 2 - y) / (Bounds.Height / 2 - 6) * RangeDb, -RangeDb, RangeDb);

    private Point Handle(EqBand band) => new(X(band.Frequency), band.HasGain ? Y(band.GainDb) : Y(0));

    public override void Render(DrawingContext dc)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (width < 10 || height < 10)
        {
            return;
        }

        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, width, height));
        var grid = new Pen(GridBrush ?? Brushes.Gray, 1);

        if (_spectrum is { Length: > 2 } spectrum)
        {
            // The music under the curve, from -90 dB at the bottom to 0 dB at the top.
            var size = (spectrum.Length - 1) * 2;
            var shape = new StreamGeometry();
            using (var g = shape.Open())
            {
                g.BeginFigure(new Point(0, height), true);
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
                    g.LineTo(new Point(x, y));
                }

                g.LineTo(new Point(width, height));
                g.EndFigure(true);
            }

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
                    g.BeginFigure(point, false);
                }
                else
                {
                    g.LineTo(point);
                }
            }

            g.EndFigure(false);
        }

        dc.DrawGeometry(null, new Pen(CurveBrush ?? Brushes.White, 2), curve);

        for (var i = 0; i < _bands.Count; i++)
        {
            var band = _bands[i];
            var at = Handle(band);
            var selected = i == _selected;
            var fill = band.Enabled ? CurveBrush : null;
            using (dc.PushOpacity(band.Enabled ? 1 : 0.5))
            {
                dc.DrawEllipse(fill, new Pen(selected ? HandleTextBrush ?? Brushes.White : CurveBrush ?? Brushes.White, selected ? 2 : 1.2),
                    at, HandleRadius, HandleRadius);
                var label = Text((i + 1).ToString(CultureInfo.CurrentCulture), 10, band.Enabled ? HandleTextBrush : CurveBrush);
                dc.DrawText(label, new Point(at.X - label.Width / 2, at.Y - label.Height / 2));
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var position = e.GetPosition(this);
        if (HandleAt(position) is not { } index)
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
            e.Pointer.Capture(this);
        }

        SelectedBand = index;
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var position = e.GetPosition(this);
        if (!_dragging)
        {
            Cursor = HandleAt(position) is null ? ArrowCursor : MoveCursor;
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

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _dragging = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _dragging = false;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        var index = HandleAt(e.GetPosition(this)) ?? _selected;
        if (index < 0 || index >= _bands.Count)
        {
            return;
        }

        var band = _bands[index];
        // Up makes it narrower.
        band.Q = Math.Clamp(band.Q * Math.Pow(1.15, e.Delta.Y), 0.1, 30);
        SelectedBand = index;
        Changed();
        e.Handled = true;
    }

    private int? HandleAt(Point position)
    {
        for (var i = _bands.Count - 1; i >= 0; i--)
        {
            var at = Handle(_bands[i]);
            var offset = at - position;
            if (Math.Sqrt(offset.X * offset.X + offset.Y * offset.Y) <= HandleRadius + 3)
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

    private FormattedText Text(string text, double size, IBrush? brush) =>
        new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(EditorFonts.Ui(this), FontStyle.Normal, FontWeight.SemiBold), size, brush ?? Brushes.Gray);
}
