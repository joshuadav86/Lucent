using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
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
        if (Open(over, Label(over, text)) is { } popup)
        {
            CloseAfter(popup, TimeSpan.FromSeconds(2.5));
        }
    }

    /// <summary>
    /// Shows <paramref name="text"/> over <paramref name="over"/> with a bar under it, and keeps it up until the
    /// returned note is done with: for something slow (a big folder being read) that should be seen to be going.
    /// </summary>
    public static Busy ShowBusy(Control over, string text)
    {
        var label = Label(over, text);
        var fill = new Border
        {
            Width = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(1.5),
            Background = Brush(over, "AccentGradientBrush") ?? Brush(over, "AccentBrush"),
        };
        var track = new Border
        {
            Width = Busy.BarWidth,
            Height = 3,
            Margin = new Thickness(0, 8, 0, 0),
            CornerRadius = new CornerRadius(1.5),
            Background = Brush(over, "HairlineBrush"),
            Child = fill,
        };
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

    // The popup isn't part of the window's tree, so it takes the theme's colours from the control it shows over.
    private static IBrush? Brush(Control over, string key) =>
        over.TryFindResource(key, over.ActualThemeVariant, out var found) ? found as IBrush : null;

    private static TextBlock Label(Control over, string text) => new()
    {
        Text = text,
        FontSize = 13,
        TextWrapping = TextWrapping.Wrap,
        MaxWidth = 320,
        TextAlignment = TextAlignment.Center,
        Foreground = Brush(over, "TextBrush") ?? Brushes.White,
    };

    /// <summary>Opens a toast holding <paramref name="content"/> over <paramref name="over"/>, or nothing if it can't.</summary>
    private static Popup? Open(Control over, Control content)
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

            var badge = new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16, 10, 16, 10),
                BorderThickness = new Thickness(1),
                Background = Brush(over, "SurfaceRaisedBrush") ?? Brushes.Black,
                BorderBrush = Brush(over, "AccentBrush"),
                BoxShadow = BoxShadows.Parse("0 2 16 0 #66000000"),
                Child = content,
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
        // Closed from a clock off the UI thread, sent back at the top priority, as on Windows: a timer on the
        // dispatcher (or an awaited delay) waits for it to get round to it, and with the visuals drawing every
        // frame a toast (as after Reset to default layout) could stay up for good.
        s_closing?.Dispose();
        s_closing = new Timer(_ => Dispatcher.UIThread.Post(() => Close(popup), DispatcherPriority.Send),
            null, wait, Timeout.InfiniteTimeSpan);
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
