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
            SetPlayerWidth(PlayerWidth * scale);
            ScalePlayer(scale);
        }
        finally
        {
            _applyingPlayerSize = false;
        }

        _settings.PlayerScale = scale;
        _scaleAtResizeEnd = scale;
        KeepPlayerWidthSoon();
    }

    /// <summary>
    /// Sizes the window to <paramref name="width"/>, even when its Width already says so: a drag or a layout
    /// that placed the window directly leaves Width at an old value, and setting it to that again changes
    /// nothing, which once left a "Reset to default layout" player at its usual scale in a window still
    /// stretched to its old size.
    /// </summary>
    private void SetPlayerWidth(double width)
    {
        if (Math.Abs(Width - width) < 0.5 && Math.Abs(Bounds.Width - width) > 0.5)
        {
            ClearValue(WidthProperty);
        }

        Width = width;
    }

    /// <summary>Once the windows have settled, makes sure the player's window is as wide as its scale says.</summary>
    private void KeepPlayerWidthSoon() => Dispatcher.UIThread.Post(KeepPlayerWidth, DispatcherPriority.Background);

    /// <summary>
    /// Makes the player's window as wide as its scale says, with its height following what's in it, and fits
    /// the equalizer docked under it to that width. Only the player's own edges, dragged by hand, change its
    /// scale; anything else that changes its width (a layout putting windows back, a limit squeezing it) is
    /// undone here, so the window can't be left stretched round a player at its usual size.
    /// </summary>
    private void KeepPlayerWidth()
    {
        if (IsClassic || InSingleWindow || WindowState != WindowState.Normal || Bounds.Width <= 0 || _snapper.IsBusy || AppKit.IsMouseDown)
        {
            return;
        }

        var width = PlayerWidth * PlayerScale;
        if (Math.Abs(Bounds.Width - width) <= 1 && SizeToContent == SizeToContent.Height)
        {
            return;
        }

        _applyingPlayerSize = true;
        try
        {
            SetPlayerWidth(width);
            SizeToContent = SizeToContent.Height;
        }
        finally
        {
            _applyingPlayerSize = false;
        }

        Dispatcher.UIThread.Post(FitDockedEqualizer, DispatcherPriority.Background);
    }

    /// <summary>
    /// The equalizer, docked under the player, lays itself out to the player's width, as it does when it first
    /// opens, rather than leaving a gap beside it or hanging past it.
    /// </summary>
    private void FitDockedEqualizer()
    {
        if (IsClassic || InSingleWindow || WindowState != WindowState.Normal || ActualWidth <= 0)
        {
            return;
        }

        if (ColumnUnderPlayer(Top + ActualHeight).FirstOrDefault(window => window is EqualizerWindow) is EqualizerWindow equalizer
            && Math.Abs(equalizer.Left() - Left) < 2)
        {
            equalizer.FitTo(ActualWidth);
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
    /// Scales the player to fill its new width when one of its edges is being dragged, and remembers how big it
    /// is. Any other change to its width is put back to what its scale says.
    /// </summary>
    private void OnPlayerWidthChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || Bounds.Width <= 0 || IsClassic || WindowState != WindowState.Normal || InSingleWindow
            || _applyingPlayerSize)
        {
            return;
        }

        if (!CanResize || _sizeFromSingleWindow || !_snapper.IsResizing(this) && !AppKit.IsMouseDown)
        {
            KeepPlayerWidthSoon();
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
        // The width it was dragged to, with its height following what's in it again (resizing by hand stops
        // that), and the equalizer under it fitted to it, so there's no gap beside it.
        KeepPlayerWidthSoon();
        Dispatcher.UIThread.Post(FitDockedEqualizer, DispatcherPriority.Background);
    }
}
