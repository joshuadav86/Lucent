using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;

namespace Lucent.Mac.Windows;

/// <summary>
/// A short message that shows over a list or panel for a couple of seconds, then goes by itself, like Spotify's
/// "Added to playlist": for something done that the user should see happened. The mouse goes straight through it.
/// </summary>
public static class Toast
{
    private static Popup? s_shown;

    /// <summary>The clock that closes the toast showing now.</summary>
    private static Timer? s_closing;

    /// <summary>Shows <paramref name="text"/> in the middle of <paramref name="over"/>, in place of any toast already showing.</summary>
    public static void Show(Control over, string text)
    {
        // A window that's a panel in the single window is out of sight: the toast goes over its panel.
        over = SingleWindow.Shown(over);
        // Only a nicety: the note it repeats is shown anyway, so a toast that can't open is let go.
        try
        {
            if (s_shown is { } old)
            {
                old.IsOpen = false;
            }

            // The popup isn't part of the window's tree, so it takes the theme's colours from the control it shows over.
            IBrush? Brush(string key) => over.TryFindResource(key, over.ActualThemeVariant, out var found) ? found as IBrush : null;
            var badge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                BorderThickness = new Thickness(1),
                Background = Brush("SurfaceRaisedBrush") ?? Brushes.Black,
                BorderBrush = Brush("AccentBrush"),
                BoxShadow = BoxShadows.Parse("0 2 16 0 #66000000"),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 320,
                    TextAlignment = TextAlignment.Center,
                    Foreground = Brush("TextBrush") ?? Brushes.White,
                },
            };
            var popup = new Popup
            {
                PlacementTarget = over,
                Placement = PlacementMode.Center,
                IsLightDismissEnabled = false,
                IsHitTestVisible = false,
                Focusable = false,
                Child = new Border { Padding = new Thickness(12), Child = badge },
            };
            // Opened over the control, a popup needs it as its parent, though it isn't one of its children.
            ((ISetLogicalParent)popup).SetParent(over);
            s_shown = popup;
            popup.IsOpen = true;
            // Closed from a clock off the UI thread, sent back at the top priority, as on Windows: a timer on the
            // dispatcher (or an awaited delay) waits for it to get round to it, and with the visuals drawing every
            // frame a toast (as after Reset to default layout) could stay up for good.
            s_closing?.Dispose();
            s_closing = new Timer(_ => Dispatcher.UIThread.Post(() => Close(popup), DispatcherPriority.Send),
                null, TimeSpan.FromSeconds(2.5), Timeout.InfiniteTimeSpan);
        }
        catch (Exception)
        {
            s_shown = null;
        }
    }

    private static void Close(Popup popup)
    {
        try
        {
            popup.IsOpen = false;
            ((ISetLogicalParent)popup).SetParent(null);
        }
        catch (Exception)
        {
            // Let go all the same.
        }

        if (s_shown == popup)
        {
            s_shown = null;
        }
    }
}
