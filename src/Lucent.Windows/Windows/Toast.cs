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

    /// <summary>The clock that closes the toast showing now.</summary>
    private static Timer? s_closing;

    /// <summary>Shows <paramref name="text"/> in the middle of <paramref name="over"/>, in place of any toast already showing.</summary>
    public static void Show(FrameworkElement over, string text)
    {
        if (Open(over, Label(text)) is { } popup)
        {
            CloseAfter(popup, TimeSpan.FromSeconds(2.5));
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> over <paramref name="over"/> with a bar under it, and keeps it up until the
    /// returned note is done with: for something slow (a big folder being read) that should be seen to be going.
    /// </summary>
    public static Busy ShowBusy(FrameworkElement over, string text)
    {
        var label = Label(text);
        var fill = new Border { Width = 0, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(1.5) };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentGradientBrush");
        var track = new Border { Width = Busy.BarWidth, Height = 3, Margin = new Thickness(0, 8, 0, 0), CornerRadius = new CornerRadius(1.5), Child = fill };
        track.SetResourceReference(Border.BackgroundProperty, "HairlineBrush");
        var content = new StackPanel { Children = { label, track } };
        return new Busy(Open(over, content), label, fill);
    }

    /// <summary>A note left up by <see cref="ShowBusy"/>: say how far things have got, then let it go.</summary>
    public sealed class Busy
    {
        internal const double BarWidth = 220;

        private readonly Popup? _popup;
        private readonly TextBlock _label;
        private readonly Border _fill;

        internal Busy(Popup? popup, TextBlock label, Border fill)
        {
            _popup = popup;
            _label = label;
            _fill = fill;
        }

        /// <summary>Changes what it says, and fills the bar to <paramref name="fraction"/> (0 to 1).</summary>
        public void Update(string text, double fraction)
        {
            _label.Text = text;
            _fill.Width = BarWidth * Math.Clamp(fraction, 0, 1);
        }

        /// <summary>Takes it down, unless another toast has already taken its place.</summary>
        public void Close()
        {
            if (_popup is not null && s_shown == _popup)
            {
                Toast.Close(_popup);
            }
        }
    }

    private static TextBlock Label(string text)
    {
        var label = new TextBlock { Text = text, FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 320, TextAlignment = TextAlignment.Center };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        return label;
    }

    /// <summary>Opens a toast holding <paramref name="content"/> over <paramref name="over"/>, or nothing if it can't.</summary>
    private static Popup? Open(FrameworkElement over, UIElement content)
    {
        // Only a nicety: the note it repeats is shown anyway, so a toast that can't open is let go.
        try
        {
            if (s_shown is { } old)
            {
                old.IsOpen = false;
            }

            s_closing?.Dispose();
            s_closing = null;

            // Placed over what's in a window rather than the window itself, and only over something on the screen:
            // a popup that can't find where its target is opens in the corner of the screen.
            // A window that's a panel in the single window is out of sight: the toast goes over its panel.
            over = SingleWindow.Shown(over);
            if (over is Window { Content: FrameworkElement windowContent })
            {
                over = windowContent;
            }

            if (!over.IsVisible || PresentationSource.FromVisual(over) is null)
            {
                return null;
            }

            var badge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                BorderThickness = new Thickness(1),
                Child = content,
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
            return popup;
        }
        catch (Exception)
        {
            s_shown = null;
            return null;
        }
    }

    private static void CloseAfter(Popup popup, TimeSpan wait)
    {
        // Closed from a clock off the UI thread, sent back at the top priority. A DispatcherTimer (or an awaited
        // delay) needs the dispatcher to get round to it: its tick waits for a WM_TIMER, which Windows only sends
        // once nothing else is waiting, and while the visuals and the player's meter draw every frame something
        // nearly always is, so a toast (as after Reset to default layout) could stay up for good.
        var dispatcher = popup.Dispatcher;
        s_closing?.Dispose();
        s_closing = new Timer(_ => dispatcher.BeginInvoke(DispatcherPriority.Send, () => Close(popup)),
            null, wait, Timeout.InfiniteTimeSpan);
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
