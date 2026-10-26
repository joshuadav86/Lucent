using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinampInspiredVisualizer.ClassicSkins;

namespace WinampInspiredVisualizer.Windows;

/// <summary>
/// Shows one of a classic skin's windows (the equalizer, or the playlist's frame) blown up with hard pixel
/// edges, and passes the mouse to it, as <see cref="ClassicPlayerView"/> does for the player. Lucent.Core
/// draws it; this shows its pixels, redrawing after each click and drag and whenever <see cref="Refresh"/> is called.
/// </summary>
public sealed class ClassicSurfaceView : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private double _zoom = 2;

    public ClassicSurfaceView(IClassicSurface surface)
    {
        Surface = surface;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public IClassicSurface Surface { get; }

    /// <summary>How many times its size the window is drawn: 1, or 2 for double size.</summary>
    public double Zoom
    {
        get => _zoom;
        set
        {
            _zoom = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Screen units to a skin pixel, rounded to a whole number of physical pixels so every skin pixel is the
    /// same size.
    /// </summary>
    public double Scale
    {
        get
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return Math.Max(1, Math.Round(_zoom * dpi)) / dpi;
        }
    }

    /// <summary>Draws the window again as things are now, and shows it.</summary>
    public void Refresh()
    {
        Surface.Render();
        if (_bitmap is null || _bitmap.PixelWidth != Surface.Width || _bitmap.PixelHeight != Surface.Height)
        {
            _bitmap = new WriteableBitmap(Surface.Width, Surface.Height, 96, 96, PixelFormats.Bgra32, null);
            InvalidateMeasure();
        }

        _bitmap.WritePixels(new Int32Rect(0, 0, Surface.Width, Surface.Height), Surface.Pixels, Surface.Width * 4, 0);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Surface.Width * Scale, Surface.Height * Scale);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();

    protected override void OnRender(DrawingContext drawing)
    {
        if (_bitmap is not null)
        {
            drawing.DrawImage(_bitmap, new Rect(0, 0, Surface.Width * Scale, Surface.Height * Scale));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var (x, y) = SkinPoint(e);
        e.Handled = true;
        if (Surface.PointerDown(x, y, e.ClickCount))
        {
            // Not on a control: the whole window is a handle, as in Winamp.
            if (e.ClickCount == 1 && Window.GetWindow(this) is { } window)
            {
                window.DragMove();
            }

            return;
        }

        CaptureMouse();
        Refresh();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            var (x, y) = SkinPoint(e);
            Surface.PointerMove(x, y);
            Refresh();
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (!IsMouseCaptured)
        {
            return;
        }

        var (x, y) = SkinPoint(e);
        ReleaseMouseCapture();
        e.Handled = true;
        Surface.PointerUp(x, y);
        Refresh();
    }

    private (double X, double Y) SkinPoint(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var scale = Scale;
        return (point.X / scale, point.Y / scale);
    }
}
