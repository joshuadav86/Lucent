using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Lucent.ClassicSkins;

namespace Lucent.Windows;

/// <summary>
/// Shows the classic player (a Winamp skin's main window) blown up with hard pixel edges, and passes the
/// mouse to it. Lucent.Core's <see cref="ClassicPlayer"/> does the drawing and the buttons; this just shows
/// its pixels and starts window drags.
/// </summary>
public sealed class ClassicPlayerView : FrameworkElement
{
    private const int SkinWidth = ClassicSprites.WindowWidth;

    private readonly WriteableBitmap _bitmap = new(SkinWidth, ClassicSprites.WindowHeight, 96, 96, PixelFormats.Bgra32, null);
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _zoom = 2;

    public ClassicPlayerView(ClassicPlayer player)
    {
        Player = player;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        SnapsToDevicePixels = true;
        UseLayoutRounding = true;
    }

    public ClassicPlayer Player { get; }

    /// <summary>How many times its size the player is drawn: 1, or 2 for double size.</summary>
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
    /// Screen pixels to a skin pixel, rounded to a whole number of physical pixels so every skin pixel is
    /// the same size (at 125% scaling, double size is 3 physical pixels rather than an uneven 2.5).
    /// </summary>
    private double Scale
    {
        get
        {
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            return Math.Max(1, Math.Round(_zoom * dpi)) / dpi;
        }
    }

    /// <summary>Moves the player on a frame and shows it. Called by the player window once per screen frame.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        Player.Tick((now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        var height = Player.Height;
        Player.Render();
        _bitmap.WritePixels(new Int32Rect(0, 0, SkinWidth, height), Player.Pixels, SkinWidth * 4, 0);
        if (Math.Abs(DesiredSize.Height - height * Scale) > 0.5)
        {
            // Rolled up into, or down from, the shade strip.
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(SkinWidth * Scale, Player.Height * Scale);

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi) => InvalidateMeasure();

    protected override void OnRender(DrawingContext drawing)
    {
        var scale = Scale;
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, SkinWidth * scale, Player.Height * scale)));
        drawing.DrawImage(_bitmap, new Rect(0, 0, SkinWidth * scale, ClassicSprites.WindowHeight * scale));
        drawing.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        var (x, y) = SkinPoint(e);
        e.Handled = true;
        if (Player.PointerDown(x, y, e.ClickCount))
        {
            // Not on a control: the whole window is a handle, as in Winamp.
            if (e.ClickCount == 1 && Window.GetWindow(this) is { } window)
            {
                window.DragMove();
            }

            return;
        }

        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (IsMouseCaptured)
        {
            var (x, y) = SkinPoint(e);
            Player.PointerMove(x, y);
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
        Player.PointerUp(x, y);
    }

    private (double X, double Y) SkinPoint(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        var scale = Scale;
        return (point.X / scale, point.Y / scale);
    }

    /// <summary>A skin's player as a picture, for the skin picker.</summary>
    public static ImageSource Preview(ClassicSkin skin)
    {
        var bitmap = new WriteableBitmap(SkinWidth, ClassicSprites.WindowHeight, 96, 96, PixelFormats.Bgra32, null);
        bitmap.WritePixels(new Int32Rect(0, 0, SkinWidth, ClassicSprites.WindowHeight), ClassicPlayer.Preview(skin), SkinWidth * 4, 0);
        bitmap.Freeze();
        return bitmap;
    }
}
