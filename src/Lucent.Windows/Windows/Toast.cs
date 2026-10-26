using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lucent.Windows;

/// <summary>
/// A short message that shows over a list or panel for a couple of seconds, then goes by itself, like Spotify's
/// "Added to playlist": for something done that the user should see happened. The mouse goes straight through it.
/// </summary>
public static class Toast
{
    private static Popup? s_shown;

    /// <summary>Shows <paramref name="text"/> in the middle of <paramref name="over"/>, in place of any toast already showing.</summary>
    public static async void Show(FrameworkElement over, string text)
    {
        // Only a nicety: the note it repeats is shown anyway, so a toast that can't open is let go.
        try
        {
            if (s_shown is { } old)
            {
                old.IsOpen = false;
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
            await Task.Delay(TimeSpan.FromSeconds(2.5));
            popup.IsOpen = false;
            if (s_shown == popup)
            {
                s_shown = null;
            }
        }
        catch (Exception)
        {
            s_shown = null;
        }
    }
}
