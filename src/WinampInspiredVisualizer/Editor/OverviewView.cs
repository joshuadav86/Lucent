using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// A thin strip showing the whole song, with the part visible in the main waveform marked.
/// Click or drag on it to move the view there; the wheel zooms.
/// </summary>
public sealed class OverviewView : FrameworkElement
{
    public static readonly DependencyProperty WaveBrushProperty = Register(nameof(WaveBrush));
    public static readonly DependencyProperty WindowBrushProperty = Register(nameof(WindowBrush));
    public static readonly DependencyProperty WindowBorderBrushProperty = Register(nameof(WindowBorderBrush));
    public static readonly DependencyProperty BackgroundBrushProperty = Register(nameof(BackgroundBrush));

    private PeakCache? _peaks;
    private StreamGeometry? _shape;
    private double _shapeWidth;
    private double _viewStart;
    private double _viewLength;

    public OverviewView()
    {
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) =>
        {
            _shape = null;
            InvalidateVisual();
        };
    }

    /// <summary>The user wants the view centred on this frame.</summary>
    public event EventHandler<double>? NavigateRequested;

    /// <summary>The user turned the wheel over the strip (positive is towards them: zoom out).</summary>
    public event EventHandler<double>? ZoomRequested;

    public Brush? WaveBrush { get => (Brush?)GetValue(WaveBrushProperty); set => SetValue(WaveBrushProperty, value); }

    public Brush? WindowBrush { get => (Brush?)GetValue(WindowBrushProperty); set => SetValue(WindowBrushProperty, value); }

    public Brush? WindowBorderBrush { get => (Brush?)GetValue(WindowBorderBrushProperty); set => SetValue(WindowBorderBrushProperty, value); }

    public Brush? BackgroundBrush { get => (Brush?)GetValue(BackgroundBrushProperty); set => SetValue(BackgroundBrushProperty, value); }

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

    protected override void OnRender(DrawingContext dc)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        dc.DrawRoundedRectangle(BackgroundBrush ?? Brushes.Transparent, null, new Rect(0, 0, width, height), 6, 6);
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
            dc.PushOpacity(0.3);
            dc.DrawRoundedRectangle(WindowBrush, null, visible, 4, 4);
            dc.Pop();
            dc.DrawRoundedRectangle(null, new Pen(WindowBorderBrush ?? Brushes.White, 1), visible, 4, 4);
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
            g.BeginFigure(new Point(0, middle - levels[0] * half), true, true);
            for (var x = 0; x < columns; x++)
            {
                g.LineTo(new Point(x + 0.5, middle - levels[x] * half), false, false);
            }

            for (var x = columns - 1; x >= 0; x--)
            {
                g.LineTo(new Point(x + 0.5, middle + levels[x] * half), false, false);
            }
        }

        shape.Freeze();
        return shape;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        Navigate(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
        {
            Navigate(e.GetPosition(this).X);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ZoomRequested?.Invoke(this, e.Delta / 120.0);
        e.Handled = true;
    }

    private void Navigate(double x)
    {
        if (_peaks is not null && ActualWidth > 0)
        {
            NavigateRequested?.Invoke(this, Math.Clamp(x / ActualWidth, 0, 1) * _peaks.Audio.Frames);
        }
    }

    private static DependencyProperty Register(string name) =>
        DependencyProperty.Register(name, typeof(Brush), typeof(OverviewView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
}
