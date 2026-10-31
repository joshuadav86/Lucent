using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

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
            ResizeMode = ResizeMode.CanMinimize;
            MinWidth = 0;
            MaxWidth = double.PositiveInfinity;
            LucentPlayer.LayoutTransform = Transform.Identity;
            return;
        }

        ResizeMode = ResizeMode.CanResize;
        MinWidth = PlayerWidth;
        MaxWidth = PlayerWidth * LargestPlayerScale;
        SizeToContent = SizeToContent.Height;
        Width = PlayerWidth * PlayerScale;
        ScalePlayer(PlayerScale);
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
            || _snapper.IsDragging || InSingleWindow)
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
        if (IsClassic || InSingleWindow || SizeToContent == SizeToContent.Height)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsClassic && !InSingleWindow && WindowState == WindowState.Normal)
            {
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
