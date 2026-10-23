using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Lucent.Mac.Windows;

/// <summary>
/// The outline shown over a window while another is dragged onto it, saying that dropping there swaps
/// the two (<see cref="WindowSnapper"/>). It sits on top of everything, in the accent colour. It never
/// takes the mouse: the dragged window holds on to it for the whole drag.
/// </summary>
public sealed class SwapHighlight : Window
{
    private readonly TextBlock _label;
    private readonly Border _fill;
    private readonly Border _outline;
    private readonly Border _badge;

    public SwapHighlight()
    {
        SystemDecorations = SystemDecorations.None;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = 1;
        Height = 1;

        _fill = new Border { Opacity = 0.22 };
        _label = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Center,
        };
        _badge = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = _label,
        };
        _outline = new Border { BorderThickness = new Thickness(3), CornerRadius = new CornerRadius(6) };
        Content = new Grid { Children = { _fill, _outline, _badge } };
    }

    /// <summary>Shows the outline over this rectangle of the screen (in the windows' units), naming the window it's over.</summary>
    public void ShowAt(double left, double top, double width, double height, string? name)
    {
        _label.Text = string.IsNullOrEmpty(name) ? "Swap" : $"Swap with {name}";
        // The theme's accent colour, as it is now (it changes with the theme).
        if (Application.Current?.TryFindResource("AccentBrush", Application.Current.ActualThemeVariant, out var accent) == true && accent is IBrush brush)
        {
            _fill.Background = brush;
            _outline.BorderBrush = brush;
            _badge.Background = brush;
        }

        Placement.Place(this, left, top, width, height);
        if (!IsVisible)
        {
            Show();
            // Placed again now it's on a screen, in case that screen's scaling isn't the one it was placed with.
            Placement.Place(this, left, top, width, height);
        }
    }
}
