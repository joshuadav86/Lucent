using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lucent.Windows;

namespace Lucent;

// The player's size. Lucent's own player grows as a whole: dragging any of its edges scales everything in
// it, keeping its shape, so its type and buttons get bigger on a big or far-away screen. The windows docked
// to it move and stretch to its new edges, as with any window. The classic player keeps its skin's size
// (or double size) instead.
public partial class MainWindow
{
    /// <summary>The player's width at its usual size, in pixels at 100% scaling.</summary>
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
    internal const string NormalSizeGesture = "Ctrl+0";

    private double PlayerScale => Math.Clamp(_settings.PlayerScale, 1, LargestPlayerScale);

    /// <summary>Whether Lucent's own player is bigger than its usual size.</summary>
    internal bool PlayerIsEnlarged => !IsClassic && PlayerScale > 1.005;

    /// <summary>
    /// Puts the player back to its usual size (Ctrl+0, or right-click › Normal player size), leaving every
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
        EventManager.RegisterClassHandler(typeof(Window), LoadedEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is Window window && window != this && !IsShuttingDown)
            {
                _snapper.Add(window);
            }
        }));
    }

    /// <summary>Lets Lucent's own player be resized, at the size the settings say. The classic player can't be.</summary>
    private void ApplyPlayerSize()
    {
        if (IsClassic)
        {
            CaptionRepaint.SetResizeMode(this, ResizeMode.CanMinimize);
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            LucentPlayer.LayoutTransform = Transform.Identity;
            return;
        }

        // The scale is read before the limits change: a window wider than the player's largest size (the
        // single window it just was, say) is squeezed to that size as they're set, which once read as the
        // player being dragged to 2.5 times its size.
        var scale = PlayerScale;
        _applyingPlayerSize = true;
        try
        {
            CaptionRepaint.SetResizeMode(this, ResizeMode.CanResize);
            MinWidth = PlayerWidth;
            MaxWidth = PlayerWidth * LargestPlayerScale;
            SizeToContent = SizeToContent.Height;
            SetPlayerWidth(PlayerWidth * scale);
            ScalePlayer(scale);
        }
        finally
        {
            _applyingPlayerSize = false;
        }

        _settings.PlayerScale = scale;
        _scaleAtResizeEnd = scale;
        Dispatcher.InvokeAsync(KeepPlayerWidth, DispatcherPriority.Background);
    }

    /// <summary>
    /// Sizes the window to <paramref name="width"/>, even when its Width already says so: a drag or a layout
    /// that placed the window directly leaves Width at an old value, and setting it to that again changes
    /// nothing, which once left a "Reset to default layout" player at its usual scale in a window still
    /// stretched to its old size.
    /// </summary>
    private void SetPlayerWidth(double width)
    {
        if (Math.Abs(Width - width) < 0.5 && Math.Abs(ActualWidth - width) > 0.5)
        {
            ClearValue(WidthProperty);
        }

        Width = width;
    }

    /// <summary>Once the windows have settled, makes sure the player's window is as wide as its scale says.</summary>
    private void KeepPlayerWidth()
    {
        if (IsClassic || InSingleWindow || WindowState != WindowState.Normal || ActualWidth <= 0)
        {
            return;
        }

        var width = PlayerWidth * PlayerScale;
        if (Math.Abs(ActualWidth - width) > 1)
        {
            _applyingPlayerSize = true;
            try
            {
                SetPlayerWidth(width);
            }
            finally
            {
                _applyingPlayerSize = false;
            }
        }
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

    /// <summary>
    /// The player's shape (its height over its width) for the snapper, which keeps it while an edge is
    /// dragged. Null while it can't be resized.
    /// </summary>
    private double? PlayerShape() =>
        IsClassic || InSingleWindow || LucentPlayer.ActualWidth <= 0 || LucentPlayer.ActualHeight <= 0
            ? null
            : LucentPlayer.ActualHeight / LucentPlayer.ActualWidth;

    /// <summary>Scales the player to fill its new width, and remembers how big it is.</summary>
    private void OnPlayerWidthChanged(object sender, SizeChangedEventArgs e)
    {
        // A drag only moves the player; any width change then is rounding as it changes scale on another screen.
        if (!e.WidthChanged || ActualWidth <= 0 || IsClassic || WindowState != WindowState.Normal || ResizeMode != ResizeMode.CanResize
            || _snapper.IsDragging || InSingleWindow || _applyingPlayerSize || _sizeFromSingleWindow)
        {
            return;
        }

        var scale = Math.Clamp(ActualWidth / PlayerWidth, 1, LargestPlayerScale);
        _settings.PlayerScale = scale;
        ScalePlayer(scale);
    }

    private void ScalePlayer(double scale) =>
        LucentPlayer.LayoutTransform = Math.Abs(scale - 1) < 0.001 ? Transform.Identity : new ScaleTransform(scale, scale);

    /// <summary>
    /// Once a resize ends, the player's height goes back to following what's in it (Windows stops that
    /// while an edge is dragged), so it fits exactly at its new scale.
    /// </summary>
    private void OnPlayerResizeFinished()
    {
        if (IsClassic || InSingleWindow)
        {
            return;
        }

        Dispatcher.InvokeAsync(TipNormalSize, DispatcherPriority.Background);
        if (SizeToContent == SizeToContent.Height)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsClassic && !InSingleWindow && WindowState == WindowState.Normal)
            {
                // The width it was dragged to, first: going back to following its content sizes the window
                // from its Width, which a drag doesn't change, so the player sprang back to its old size.
                SetPlayerWidth(PlayerWidth * PlayerScale);
                SizeToContent = SizeToContent.Height;
            }
        }, DispatcherPriority.Background);
    }

    private const int WM_SYSCOMMAND = 0x0112;
    private const int WM_NCLBUTTONDBLCLK = 0x00A3;
    private const int SC_MAXIMIZE = 0xF030;
    private const int HTCAPTION = 2;

    /// <summary>
    /// The player is never maximized; it grows by its edges instead. A double-click on its title bar (or
    /// Maximize from its window menu) is turned away before Windows maximizes it, so it doesn't fill the
    /// screen for a moment and then jump back. The double-click turns Lucent full screen on or off instead
    /// (Lucent's own player only: a classic skin draws its own title bar).
    /// </summary>
    private IntPtr RefuseMaximize(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_NCLBUTTONDBLCLK && wParam.ToInt64() == HTCAPTION)
        {
            handled = true;
            if (!IsClassic)
            {
                Dispatcher.InvokeAsync(ToggleLucentFullScreen);
            }
        }
        else if (msg == WM_SYSCOMMAND && (wParam.ToInt64() & 0xFFF0) == SC_MAXIMIZE)
        {
            handled = true;
        }

        return IntPtr.Zero;
    }

    /// <summary>Anything that still maximizes the player (Win+Up, dragging it to the top of the screen) is undone.</summary>
    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
    }
}
