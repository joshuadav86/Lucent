using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WinampInspiredVisualizer.ClassicSkins;

namespace Lucent.Mac.Windows;

/// <summary>
/// Shows one of a classic skin's windows (the equalizer, or the playlist's frame) blown up with hard pixel
/// edges, and passes the mouse to it, as <see cref="ClassicPlayerView"/> does for the player. Lucent.Core
/// draws it; this shows its pixels, redrawing after each click and drag and whenever <see cref="Refresh"/> is called.
/// </summary>
public sealed class ClassicSurfaceView : Control
{
    private WriteableBitmap? _bitmap;
    private double _zoom = 2;

    public ClassicSurfaceView(IClassicSurface surface)
    {
        Surface = surface;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
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
    /// Points to a skin pixel, rounded to a whole number of screen pixels so every skin pixel is the same size.
    /// </summary>
    public double Scale
    {
        get
        {
            var screen = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            return Math.Max(1, Math.Round(_zoom * screen)) / screen;
        }
    }

    /// <summary>Draws the window again as things are now, and shows it.</summary>
    public void Refresh()
    {
        Surface.Render();
        if (_bitmap is null || _bitmap.PixelSize.Width != Surface.Width || _bitmap.PixelSize.Height != Surface.Height)
        {
            _bitmap?.Dispose();
            _bitmap = new WriteableBitmap(new PixelSize(Surface.Width, Surface.Height), new Vector(96, 96),
                PixelFormat.Bgra8888, AlphaFormat.Opaque);
            InvalidateMeasure();
        }

        Copy(Surface.Pixels, _bitmap, Surface.Width, Surface.Height);
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(Surface.Width * Scale, Surface.Height * Scale);

    public override void Render(DrawingContext context)
    {
        if (_bitmap is null)
        {
            return;
        }

        var scale = Scale;
        context.DrawImage(_bitmap, new Rect(0, 0, Surface.Width, Surface.Height),
            new Rect(0, 0, Surface.Width * scale, Surface.Height * scale));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var (x, y) = SkinPoint(e);
        e.Handled = true;
        if (Surface.PointerDown(x, y, e.ClickCount))
        {
            // Not on a control: the whole window is a handle, as in Winamp.
            if (e.ClickCount == 1 && TopLevel.GetTopLevel(this) is Window window)
            {
                window.BeginMoveDrag(e);
            }

            return;
        }

        e.Pointer.Capture(this);
        Refresh();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.Pointer.Captured == this)
        {
            var (x, y) = SkinPoint(e);
            Surface.PointerMove(x, y);
            Refresh();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (e.Pointer.Captured != this)
        {
            return;
        }

        var (x, y) = SkinPoint(e);
        e.Pointer.Capture(null);
        e.Handled = true;
        Surface.PointerUp(x, y);
        Refresh();
    }

    private (double X, double Y) SkinPoint(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var scale = Scale;
        return (point.X / scale, point.Y / scale);
    }

    private static unsafe void Copy(uint[] pixels, WriteableBitmap bitmap, int width, int height)
    {
        using var frame = bitmap.Lock();
        fixed (uint* source = pixels)
        {
            for (var y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(source + y * width, (byte*)frame.Address + y * frame.RowBytes, frame.RowBytes, width * 4);
            }
        }
    }
}
