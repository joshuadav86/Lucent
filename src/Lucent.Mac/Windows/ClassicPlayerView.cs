using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using WinampInspiredVisualizer.ClassicSkins;

namespace Lucent.Mac.Windows;

/// <summary>
/// Shows the classic player (a Winamp skin's main window) blown up with hard pixel edges, and passes the
/// mouse to it. Lucent.Core's <see cref="ClassicPlayer"/> does the drawing and the buttons; this just shows
/// its pixels and starts window drags.
/// </summary>
public sealed class ClassicPlayerView : Control
{
    private const int SkinWidth = ClassicSprites.WindowWidth;

    private readonly WriteableBitmap _bitmap = NewBitmap();
    private DateTime _lastFrame = DateTime.UtcNow;
    private double _zoom = 2;
    private int _shownHeight;

    public ClassicPlayerView(ClassicPlayer player)
    {
        Player = player;
        RenderOptions.SetBitmapInterpolationMode(this, BitmapInterpolationMode.None);
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
    /// Points to a skin pixel, rounded to a whole number of screen pixels so every skin pixel is the same
    /// size (on a Retina screen, double size is 4 screen pixels a skin pixel).
    /// </summary>
    private double Scale
    {
        get
        {
            var screen = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            return Math.Max(1, Math.Round(_zoom * screen)) / screen;
        }
    }

    /// <summary>Moves the player on a frame and shows it. Called by the player window once per screen frame.</summary>
    public void Tick()
    {
        var now = DateTime.UtcNow;
        Player.Tick((now - _lastFrame).TotalSeconds);
        _lastFrame = now;
        Player.Render();
        Copy(Player.Pixels, _bitmap, Player.Height);
        if (_shownHeight != Player.Height)
        {
            // Rolled up into, or down from, the shade strip.
            _shownHeight = Player.Height;
            InvalidateMeasure();
        }

        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize) => new(SkinWidth * Scale, Player.Height * Scale);

    public override void Render(DrawingContext context)
    {
        var scale = Scale;
        using (context.PushClip(new Rect(0, 0, SkinWidth * scale, Player.Height * scale)))
        {
            context.DrawImage(_bitmap, new Rect(0, 0, SkinWidth, ClassicSprites.WindowHeight),
                new Rect(0, 0, SkinWidth * scale, ClassicSprites.WindowHeight * scale));
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        var (x, y) = SkinPoint(e);
        e.Handled = true;
        if (Player.PointerDown(x, y, e.ClickCount))
        {
            // Not on a control: the whole window is a handle, as in Winamp.
            if (e.ClickCount == 1 && TopLevel.GetTopLevel(this) is Window window)
            {
                window.BeginMoveDrag(e);
            }

            return;
        }

        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.Pointer.Captured == this)
        {
            var (x, y) = SkinPoint(e);
            Player.PointerMove(x, y);
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
        Player.PointerUp(x, y);
    }

    private (double X, double Y) SkinPoint(PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        var scale = Scale;
        return (point.X / scale, point.Y / scale);
    }

    /// <summary>A skin's player as a picture, for the skin picker.</summary>
    public static Bitmap Preview(ClassicSkin skin)
    {
        var bitmap = NewBitmap();
        Copy(ClassicPlayer.Preview(skin), bitmap, ClassicSprites.WindowHeight);
        return bitmap;
    }

    private static WriteableBitmap NewBitmap() => new(new PixelSize(SkinWidth, ClassicSprites.WindowHeight),
        new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);

    private static unsafe void Copy(uint[] pixels, WriteableBitmap bitmap, int rows)
    {
        using var frame = bitmap.Lock();
        fixed (uint* source = pixels)
        {
            for (var y = 0; y < rows; y++)
            {
                Buffer.MemoryCopy(source + y * SkinWidth, (byte*)frame.Address + y * frame.RowBytes, frame.RowBytes, SkinWidth * 4);
            }
        }
    }
}
