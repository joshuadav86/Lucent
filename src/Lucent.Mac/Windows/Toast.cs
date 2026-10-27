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

    /// <summary>Shows <paramref name="text"/> in the middle of <paramref name="over"/>, in place of any toast already showing.</summary>
    public static void Show(Control over, string text)
    {
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
            // Closed by its own timer at normal priority. Awaiting a delay instead came back at the priority of
            // whatever called this, so a toast shown from a background callback (as after Reset to default layout)
            // waited behind the visuals' drawing and could stay up for good.
            DispatcherTimer.RunOnce(() => Close(popup), TimeSpan.FromSeconds(2.5), DispatcherPriority.Normal);
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
