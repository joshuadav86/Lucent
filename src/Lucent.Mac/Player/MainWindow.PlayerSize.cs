using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Lucent.Mac.Interop;

namespace Lucent.Mac;

// The player's size. Lucent's own player grows as a whole: dragging any of its edges scales everything in
// it, keeping its shape, so its type and buttons get bigger on a big or far-away screen. The windows docked
// to it move and stretch to its new edges, as with any window. The classic player keeps its skin's size
// (or double size) instead.
public partial class MainWindow
{
    /// <summary>The player's width at its usual size, in points.</summary>
    private const double PlayerWidth = 520;

    /// <summary>The most the player can be scaled up by.</summary>
    private const double LargestPlayerScale = 2.5;

    private bool _everyWindowSnaps;

    private double PlayerScale => Math.Clamp(_settings.PlayerScale, 1, LargestPlayerScale);

    /// <summary>
    /// Every Lucent window snaps to the others and docks to the player, whichever window it is: the radio,
    /// the stems, the track info, all of them, as soon as it opens.
    /// </summary>
    private void SnapEveryWindow()
    {
        if (_everyWindowSnaps)
        {
            return;
        }

        _everyWindowSnaps = true;
        Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            if (window != this && !IsShuttingDown)
            {
                _snapper.Add(window);
            }
        });
    }

    /// <summary>Lets Lucent's own player be resized, at the size the settings say. The classic player can't be.</summary>
    private void ApplyPlayerSize()
    {
        if (IsClassic)
        {
            CanResize = false;
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            PlayerScaler.LayoutTransform = null;
            return;
        }

        CanResize = true;
        MinWidth = PlayerWidth;
        MaxWidth = PlayerWidth * LargestPlayerScale;
        SizeToContent = SizeToContent.Height;
        Width = PlayerWidth * PlayerScale;
        ScalePlayer(PlayerScale);
    }

    /// <summary>Scales the player to fill its new width, and remembers how big it is.</summary>
    private void OnPlayerWidthChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || Bounds.Width <= 0 || IsClassic || WindowState != WindowState.Normal || !CanResize || InSingleWindow)
        {
            return;
        }

        var scale = Math.Clamp(Bounds.Width / PlayerWidth, 1, LargestPlayerScale);
        _settings.PlayerScale = scale;
        ScalePlayer(scale);
    }

    private void ScalePlayer(double scale) =>
        PlayerScaler.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? null : new ScaleTransform(scale, scale);

    /// <summary>
    /// Tells macOS the player's shape (its usual size), so it keeps it while an edge is dragged: dragging
    /// the side makes it taller too, and dragging the top or bottom makes it wider.
    /// </summary>
    private void KeepPlayerShape()
    {
        var size = LucentPlayer.Bounds.Size;
        if (!OperatingSystem.IsMacOS() || IsClassic || InSingleWindow || size.Width <= 0 || size.Height <= 0)
        {
            return;
        }

        var native = NativeWindow(this);
        if (native == IntPtr.Zero)
        {
            return;
        }

        ObjC.SendVoid(native, "setContentAspectRatio:", new ObjC.CGSize { Width = size.Width, Height = size.Height });
    }

    /// <summary>
    /// Once a resize ends, the player's height goes back to following what's in it (resizing by hand
    /// stops that), so it fits exactly at its new scale.
    /// </summary>
    private void OnPlayerResizeFinished()
    {
        if (IsClassic || InSingleWindow || SizeToContent == SizeToContent.Height)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsClassic && !InSingleWindow && WindowState == WindowState.Normal)
            {
                SizeToContent = SizeToContent.Height;
            }
        }, DispatcherPriority.Background);
    }
}
