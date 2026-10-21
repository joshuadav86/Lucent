using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace WinampInspiredVisualizer;

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
        IsClassic || LucentPlayer.ActualWidth <= 0 || LucentPlayer.ActualHeight <= 0
            ? null
            : LucentPlayer.ActualHeight / LucentPlayer.ActualWidth;

    /// <summary>Scales the player to fill its new width, and remembers how big it is.</summary>
    private void OnPlayerWidthChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || ActualWidth <= 0 || IsClassic || WindowState != WindowState.Normal || ResizeMode != ResizeMode.CanResize)
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
        if (IsClassic || SizeToContent == SizeToContent.Height)
        {
            return;
        }

        Dispatcher.InvokeAsync(() =>
        {
            if (!IsClassic && WindowState == WindowState.Normal)
            {
                SizeToContent = SizeToContent.Height;
            }
        }, DispatcherPriority.Background);
    }

    /// <summary>The player is never maximized (a double-click on its title bar would); it grows by its edges instead.</summary>
    private void OnPlayerStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
    }
}
