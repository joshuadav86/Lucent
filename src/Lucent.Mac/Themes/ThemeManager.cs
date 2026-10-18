using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Lucent.Mac.Windows;

namespace Lucent.Mac.Themes;

/// <summary>A colour scheme for everything around the visuals. Colours are #RRGGBB.</summary>
public sealed record Theme(
    string Name,
    string Background,
    string Surface,
    string SurfaceRaised,
    string Hover,
    string Hairline,
    string Text,
    string MutedText,
    string FaintText,
    string Accent,
    string AccentSoft,
    string GradientStart,
    string GradientEnd,
    string AccentForeground);

/// <summary>
/// The built-in themes (the same as on Windows), and swapping between them. App.axaml defines the
/// colour resources (Midnight's colours); applying a theme replaces them, and everything that uses
/// them through DynamicResource repaints.
/// </summary>
public static class ThemeManager
{
    public static IReadOnlyList<Theme> Themes { get; } = new[]
    {
        new Theme("Midnight", "#09090C", "#101015", "#16161D", "#1D1D26", "#1E1E28",
            "#ECECF1", "#7D7D8C", "#4A4A57", "#8B7CFF", "#2A2550", "#8B5CF6", "#22D3EE", "#FFFFFF"),
        new Theme("Deep Ocean", "#050B14", "#0A1422", "#0F1C2E", "#15263C", "#172A40",
            "#E6F1FF", "#7A93B0", "#415670", "#3B9EFF", "#0F2A4A", "#2563EB", "#22D3EE", "#FFFFFF"),
        new Theme("Glacier", "#E9EFF5", "#F5F8FB", "#FFFFFF", "#E1E9F2", "#D3DEE9",
            "#0F1B2A", "#56677B", "#93A3B5", "#2563EB", "#DCE7FB", "#2563EB", "#06B6D4", "#FFFFFF"),
        new Theme("Ultraviolet", "#0B0614", "#120A1F", "#1A0F2B", "#241538", "#2A1840",
            "#F3EAFF", "#9B87B8", "#5A4874", "#D946EF", "#3B1450", "#A855F7", "#EC4899", "#FFFFFF"),
        new Theme("Emerald", "#050F0C", "#0A1813", "#0F2019", "#142A21", "#173026",
            "#E8FFF6", "#7BA394", "#3F6155", "#10D9A0", "#0D3A2D", "#10B981", "#22D3EE", "#04130E"),
        new Theme("Ember", "#120806", "#1A0D09", "#22120C", "#2D1810", "#331C13",
            "#FFF1EA", "#B08E80", "#6B5047", "#FF7A3D", "#4A1F0F", "#F97316", "#EF4444", "#FFFFFF"),
        new Theme("Graphite", "#0C0C0C", "#141414", "#1B1B1B", "#242424", "#262626",
            "#F2F2F2", "#8C8C8C", "#505050", "#E5E5E5", "#2E2E2E", "#FAFAFA", "#A3A3A3", "#0C0C0C"),
        new Theme("Rosé", "#120A0D", "#1A0F14", "#22141A", "#2C1A22", "#331F28",
            "#FFEFF4", "#B48C9A", "#6E4E5A", "#FF8FB1", "#4A1F2E", "#F472B6", "#FB923C", "#FFFFFF"),
    };

    public static Theme Find(string? name) =>
        Themes.FirstOrDefault(theme => string.Equals(theme.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Themes[0];

    public static void Apply(Theme theme)
    {
        var resources = Application.Current!.Resources;
        resources["BackgroundBrush"] = Solid(theme.Background);
        resources["SurfaceBrush"] = Solid(theme.Surface);
        resources["SurfaceRaisedBrush"] = Solid(theme.SurfaceRaised);
        resources["HoverBrush"] = Solid(theme.Hover);
        resources["HairlineBrush"] = Solid(theme.Hairline);
        resources["TextBrush"] = Solid(theme.Text);
        resources["MutedTextBrush"] = Solid(theme.MutedText);
        resources["FaintTextBrush"] = Solid(theme.FaintText);
        resources["AccentBrush"] = Solid(theme.Accent);
        resources["AccentSoftBrush"] = Solid(theme.AccentSoft);
        resources["AccentGradientBrush"] = Gradient(theme);
        resources["AccentForegroundBrush"] = Solid(theme.AccentForeground);

        // Light themes get Avalonia's light controls (text boxes, menus), dark ones its dark controls.
        Application.Current.RequestedThemeVariant = IsLight(Color.Parse(theme.Background))
            ? Avalonia.Styling.ThemeVariant.Light
            : Avalonia.Styling.ThemeVariant.Dark;
    }

    /// <summary>A small preview of a theme in its own colours, for the theme picker.</summary>
    public static Control CreatePreview(Theme theme)
    {
        var dot = new Avalonia.Controls.Shapes.Ellipse { Width = 16, Height = 16, Fill = Gradient(theme), VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock
        {
            Text = theme.Name,
            Margin = new Thickness(10, 0, 0, 0),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Foreground = Solid(theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var accent = new Border
        {
            Width = 14,
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = Solid(theme.Accent),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(dot, Dock.Left);
        DockPanel.SetDock(accent, Dock.Right);
        row.Children.Add(dot);
        row.Children.Add(accent);
        row.Children.Add(name);

        var preview = new Border
        {
            Height = 38,
            Padding = new Thickness(10, 0, 10, 0),
            Background = Solid(theme.Surface),
            BorderBrush = Solid(theme.Hairline),
            BorderThickness = new Thickness(1),
            Child = row,
        };
        // Shaped and lettered by the skin that's on, so the picker matches the rest of the window.
        preview.SetResourceReference(Border.CornerRadiusProperty, "BarCorner");
        name.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
        return preview;
    }

    internal static IBrush Solid(string colour) => new ImmutableSolidColorBrush(Color.Parse(colour));

    internal static IBrush Gradient(Theme theme) => new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(theme.GradientStart), 0),
            new GradientStop(Color.Parse(theme.GradientEnd), 1),
        },
    }.ToImmutable();

    internal static bool IsLight(Color colour) => (0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B) / 255 > 0.5;
}
