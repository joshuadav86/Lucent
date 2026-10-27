using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace Lucent.Windows;

/// <summary>
/// A short message that shows over a list or panel for a couple of seconds, then goes by itself, like Spotify's
/// "Added to playlist": for something done that the user should see happened. The mouse goes straight through it.
/// </summary>
public static class Toast
{
    private static Popup? s_shown;

    /// <summary>Shows <paramref name="text"/> in the middle of <paramref name="over"/>, in place of any toast already showing.</summary>
    public static void Show(FrameworkElement over, string text)
    {
        // Only a nicety: the note it repeats is shown anyway, so a toast that can't open is let go.
        try
        {
            if (s_shown is { } old)
            {
                old.IsOpen = false;
            }

            // Placed over what's in a window rather than the window itself, and only over something on the screen:
            // a popup that can't find where its target is opens in the corner of the screen.
            if (over is Window { Content: FrameworkElement content })
            {
                over = content;
            }

            if (!over.IsVisible || PresentationSource.FromVisual(over) is null)
            {
                return;
            }

            var label = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, TextAlignment = TextAlignment.Center };
            label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var badge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                BorderThickness = new Thickness(1),
                Child = label,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 16, ShadowDepth = 2, Opacity = 0.4, Color = Colors.Black },
            };
            badge.SetResourceReference(Border.BackgroundProperty, "SurfaceRaisedBrush");
            badge.SetResourceReference(Border.BorderBrushProperty, "AccentBrush");
            var popup = new Popup
            {
                PlacementTarget = over,
                Placement = PlacementMode.Center,
                AllowsTransparency = true,
                StaysOpen = true,
                IsHitTestVisible = false,
                Focusable = false,
                Child = new Border { Padding = new Thickness(12), Child = badge },
            };
            s_shown = popup;
            popup.IsOpen = true;
            // Closed by its own timer at normal priority. Awaiting a delay instead came back at the priority of
            // whatever called this, so a toast shown from a background callback (as after Reset to default layout)
            // waited behind the visuals' drawing and could stay up for good.
            var timer = new DispatcherTimer(DispatcherPriority.Normal, popup.Dispatcher) { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Close(popup);
            };
            timer.Start();
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
            popup.Child = null;
            popup.PlacementTarget = null;
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
