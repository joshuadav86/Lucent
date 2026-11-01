using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Lucent.Mac.Interop;
using Lucent.Mac.Windows;

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

    /// <summary>The player's scale when a resize last ended, so the tip shows only when a drag changed it.</summary>
    private double _scaleAtResizeEnd = double.NaN;

    /// <summary>
    /// True while <see cref="ApplyPlayerSize"/> sets the player's limits and width, so the sizes the window
    /// passes through on the way (squeezed to its new largest width, say) aren't taken for a resize.
    /// </summary>
    private bool _applyingPlayerSize;

    /// <summary>The key that puts the player back to its usual size, as menus and the tip show it.</summary>
    internal const string NormalSizeGesture = "⌘0";

    private double PlayerScale => Math.Clamp(_settings.PlayerScale, 1, LargestPlayerScale);

    /// <summary>Whether Lucent's own player is bigger than its usual size.</summary>
    internal bool PlayerIsEnlarged => !IsClassic && PlayerScale > 1.005;

    /// <summary>
    /// Puts the player back to its usual size (⌘0, or right-click › Normal player size), leaving every
    /// other window where it is. The windows docked to it follow its edges back in, as when an edge is dragged.
    /// </summary>
    internal void NormalPlayerSize()
    {
        if (!PlayerIsEnlarged)
        {
            return;
        }

        _settings.PlayerScale = 1;
        _scaleAtResizeEnd = 1;
        if (InSingleWindow)
        {
            ScalePlayer(1);
            PlayerRoot.Width = PlayerPaneWidth;
            RetileSoon();
        }
        else
        {
            ApplyPlayerSize();
        }

        _settings.Save();
    }

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

        // The scale is read before the limits change: a window wider than the player's largest size (the
        // single window it just was, say) is squeezed to that size as they're set, which once read as the
        // player being dragged to 2.5 times its size.
        var scale = PlayerScale;
        _applyingPlayerSize = true;
        try
        {
            CanResize = true;
            MinWidth = PlayerWidth;
            MaxWidth = PlayerWidth * LargestPlayerScale;
            SizeToContent = SizeToContent.Height;
            Width = PlayerWidth * scale;
            ScalePlayer(scale);
        }
        finally
        {
            _applyingPlayerSize = false;
        }

        _settings.PlayerScale = scale;
        _scaleAtResizeEnd = scale;
    }

    /// <summary>
    /// After a drag that left the player bigger than usual, says how big it is and how to put it back, since
    /// an edge of the player (the bottom one, right above the equalizer, say) is easy to catch by accident.
    /// </summary>
    private void TipNormalSize()
    {
        var scale = PlayerScale;
        var changed = !double.IsNaN(_scaleAtResizeEnd) && Math.Abs(scale - _scaleAtResizeEnd) > 0.01;
        _scaleAtResizeEnd = scale;
        if (changed && PlayerIsEnlarged && !InSingleWindow)
        {
            Toast.Show(this, $"Player at {Math.Round(scale * 100)}%. Press {NormalSizeGesture} for normal size.");
        }
    }

    /// <summary>Scales the player to fill its new width, and remembers how big it is.</summary>
    private void OnPlayerWidthChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || Bounds.Width <= 0 || IsClassic || WindowState != WindowState.Normal || !CanResize || InSingleWindow
            || _applyingPlayerSize || _sizeFromSingleWindow)
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
        if (IsClassic || InSingleWindow)
        {
            return;
        }

        Dispatcher.UIThread.Post(TipNormalSize, DispatcherPriority.Background);
        if (SizeToContent == SizeToContent.Height)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (!IsClassic && !InSingleWindow && WindowState == WindowState.Normal)
            {
                // The width it was dragged to, first: going back to following its content sizes the window
                // from its Width, which a drag doesn't change, so the player sprang back to its old size.
                Width = PlayerWidth * PlayerScale;
                SizeToContent = SizeToContent.Height;
            }
        }, DispatcherPriority.Background);
    }
}
