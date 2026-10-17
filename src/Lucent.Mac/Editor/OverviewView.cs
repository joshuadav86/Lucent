using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using WinampInspiredVisualizer.Editor;

namespace Lucent.Mac.Editor;

/// <summary>
/// A thin strip showing the whole song, with the part visible in the main waveform marked.
/// Click or drag on it to move the view there; the wheel zooms.
/// </summary>
public sealed class OverviewView : Control
{
    public static readonly StyledProperty<IBrush?> WaveBrushProperty = AvaloniaProperty.Register<OverviewView, IBrush?>(nameof(WaveBrush));
    public static readonly StyledProperty<IBrush?> WindowBrushProperty = AvaloniaProperty.Register<OverviewView, IBrush?>(nameof(WindowBrush));
    public static readonly StyledProperty<IBrush?> WindowBorderBrushProperty = AvaloniaProperty.Register<OverviewView, IBrush?>(nameof(WindowBorderBrush));
    public static readonly StyledProperty<IBrush?> BackgroundBrushProperty = AvaloniaProperty.Register<OverviewView, IBrush?>(nameof(BackgroundBrush));

    private PeakCache? _peaks;
    private StreamGeometry? _shape;
    private double _shapeWidth;
    private double _viewStart;
    private double _viewLength;
    private bool _dragging;

    static OverviewView()
    {
        AffectsRender<OverviewView>(WaveBrushProperty, WindowBrushProperty, WindowBorderBrushProperty, BackgroundBrushProperty);
    }

    public OverviewView()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
        PropertyChanged += (_, e) =>
        {
            if (e.Property == BoundsProperty)
            {
                _shape = null;
                InvalidateVisual();
            }
        };
    }

    /// <summary>The user wants the view centred on this frame.</summary>
    public event EventHandler<double>? NavigateRequested;

    /// <summary>The user turned the wheel over the strip (positive is towards them: zoom out).</summary>
    public event EventHandler<double>? ZoomRequested;

    public IBrush? WaveBrush { get => GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }

    public IBrush? WindowBrush { get => GetValue(WindowBrushProperty); set => SetValue(WindowBrushProperty, value); }

    public IBrush? WindowBorderBrush { get => GetValue(WindowBorderBrushProperty); set => SetValue(WindowBorderBrushProperty, value); }

    public IBrush? BackgroundBrush { get => GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }

    public PeakCache? Peaks
    {
        get => _peaks;
        set
        {
            _peaks = value;
            _shape = null;
            InvalidateVisual();
        }
    }

    /// <summary>Marks the visible part of the main view.</summary>
    public void SetVisibleRange(double start, double length)
    {
        _viewStart = start;
        _viewLength = length;
        InvalidateVisual();
    }

    public override void Render(DrawingContext dc)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        dc.DrawRectangle(BackgroundBrush ?? Brushes.Transparent, null, new Rect(0, 0, width, height), 6, 6);
        if (_peaks is null || width <= 0 || _peaks.Audio.Frames == 0)
        {
            return;
        }

        if (_shape is null || Math.Abs(_shapeWidth - width) > 0.5)
        {
            _shape = BuildShape(width, height);
            _shapeWidth = width;
        }

        dc.DrawGeometry(WaveBrush, null, _shape);

        var frames = (double)_peaks.Audio.Frames;
        var x0 = _viewStart / frames * width;
        var x1 = Math.Min(width, (_viewStart + _viewLength) / frames * width);
        if (x1 - x0 < width - 1)
        {
            // A see-through tint over the visible part, so the waveform shows through it.
            var visible = new Rect(x0 + 0.5, 0.5, Math.Max(3, x1 - x0 - 1), height - 1);
            using (dc.PushOpacity(0.3))
            {
                dc.DrawRectangle(WindowBrush, null, visible, 4, 4);
            }

            dc.DrawRectangle(null, new Pen(WindowBorderBrush ?? Brushes.White, 1), visible, 4, 4);
        }
    }

    /// <summary>The loudest sample of any channel in each column, mirrored around the middle.</summary>
    private StreamGeometry BuildShape(double width, double height)
    {
        var columns = Math.Max(1, (int)Math.Ceiling(width));
        var frames = (double)_peaks!.Audio.Frames;
        var middle = height / 2;
        var half = height / 2 - 3;
        var levels = new double[columns];
        for (var x = 0; x < columns; x++)
        {
            var from = x * frames / columns;
            var to = (x + 1) * frames / columns;
            double level = 0;
            for (var c = 0; c < _peaks.Audio.ChannelCount; c++)
            {
                var (min, max) = _peaks.Range(c, from, to);
                level = Math.Max(level, Math.Max(Math.Abs(min), Math.Abs(max)));
            }

            levels[x] = Math.Max(0.5 / half, Math.Min(1, level));
        }

        var shape = new StreamGeometry();
        using (var g = shape.Open())
        {
            g.BeginFigure(new Point(0, middle - levels[0] * half), true);
            for (var x = 0; x < columns; x++)
            {
                g.LineTo(new Point(x + 0.5, middle - levels[x] * half));
            }

            for (var x = columns - 1; x >= 0; x--)
            {
                g.LineTo(new Point(x + 0.5, middle + levels[x] * half));
            }

            g.EndFigure(true);
        }

        return shape;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Pointer.Capture(this);
        _dragging = true;
        Navigate(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            Navigate(e.GetPosition(this).X);
        }
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
        // Avalonia's wheel delta is in notches, up positive, like WPF's Delta / 120.
        ZoomRequested?.Invoke(this, e.Delta.Y);
        e.Handled = true;
    }

    private void Navigate(double x)
    {
        if (_peaks is not null && Bounds.Width > 0)
        {
            NavigateRequested?.Invoke(this, Math.Clamp(x / Bounds.Width, 0, 1) * _peaks.Audio.Frames);
        }
    }
}
