using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace WinampInspiredVisualizer.Themes;

/// <summary>How a skin finishes its surfaces: the light, edges and texture laid over the theme's colours.</summary>
public enum SkinFinish
{
    /// <summary>Flat panels and hairlines (Lucent's own look).</summary>
    Flat,
    /// <summary>Raised and sunken edges, a black LCD display with scan lines.</summary>
    Bevel,
    /// <summary>Frosted panes with soft highlights and glossy buttons.</summary>
    Glass,
    /// <summary>Accent-coloured edges that glow, over scan lines.</summary>
    Neon,
    /// <summary>Brushed metal panels, metal buttons and an inset display.</summary>
    Metal,
}

/// <summary>
/// A skin: the shape, type and finish of everything around the visuals. A theme picks the colours;
/// a skin decides how those colours are laid out, so any skin works with any theme. Each skin
/// comes with a theme it was designed around, which is chosen with it.
/// </summary>
/// <param name="Rounding">How rounded corners are, compared with Lucent's own (1).</param>
/// <param name="RoundShapes">Whether pills, knobs and the play button are fully round.</param>
public sealed record Skin(
    string Name,
    string Description,
    string Theme,
    string UiFont,
    string DisplayFont,
    FontWeight DisplayWeight,
    double Rounding,
    bool RoundShapes,
    SkinFinish Finish);

/// <summary>
/// The built-in skins, and swapping between them. App.xaml holds the Lucent skin's resources;
/// applying a skin replaces them with ones built from the skin and the current theme's colours,
/// and everything that uses them through DynamicResource reshapes and repaints.
/// </summary>
public static class SkinManager
{
    public static IReadOnlyList<Skin> Skins { get; } = new[]
    {
        new Skin("Lucent", "Soft corners, hairlines, and calm, modern type.", "Midnight",
            "Segoe UI Variable Text, Segoe UI", "Segoe UI Variable Text, Segoe UI", FontWeights.SemiBold,
            Rounding: 1, RoundShapes: true, SkinFinish.Flat),
        new Skin("Classic", "Square corners, raised buttons, and a black LCD, like the players of 1999.", "Emerald",
            "Tahoma, Segoe UI", "Lucida Console, Consolas", FontWeights.Normal,
            Rounding: 0, RoundShapes: false, SkinFinish.Bevel),
        new Skin("Glass", "Frosted panes, soft light, and big round buttons.", "Deep Ocean",
            "Segoe UI Variable Display, Segoe UI", "Segoe UI Variable Display, Segoe UI", FontWeights.Light,
            Rounding: 1.6, RoundShapes: true, SkinFinish.Glass),
        new Skin("Neon", "Hard edges that glow in the accent colour, over scan lines.", "Ultraviolet",
            "Bahnschrift, Segoe UI", "Cascadia Mono, Consolas", FontWeights.SemiBold,
            Rounding: 0.25, RoundShapes: false, SkinFinish.Neon),
        new Skin("Studio", "Brushed metal and an inset display, like rack-mounted hi-fi gear.", "Graphite",
            "Bahnschrift, Segoe UI", "Bahnschrift, Segoe UI", FontWeights.Normal,
            Rounding: 0.5, RoundShapes: true, SkinFinish.Metal),
    };

    // Lucent's corner sizes, which a skin's Rounding scales. Each is a resource App.xaml also defines.
    private static readonly (string Key, double Radius)[] Corners =
    {
        ("TinyCorner", 6), ("SegmentCorner", 7), ("ControlCorner", 8), ("BarCorner", 9),
        ("PanelCorner", 10), ("CardCorner", 12), ("TrackCorner", 3),
    };

    // Resources only some skins set; the others leave them out, so whatever uses them falls back to nothing.
    private static readonly string[] OptionalKeys = { "DisplayGlowEffect" };

    public static Skin Find(string? name) =>
        Skins.FirstOrDefault(skin => string.Equals(skin.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Skins[0];

    /// <summary>Applies a theme's colours, then lays a skin over them.</summary>
    public static void Apply(Skin skin, Theme theme)
    {
        ThemeManager.Apply(theme);
        var resources = Application.Current.Resources;
        foreach (var key in OptionalKeys)
        {
            resources.Remove(key);
        }
        foreach (var (key, value) in Build(skin, theme))
        {
            resources[key] = value;
        }
    }

    /// <summary>Every resource a skin sets, built from its theme's colours.</summary>
    private static Dictionary<string, object> Build(Skin skin, Theme theme)
    {
        var background = Parse(theme.Background);
        var surface = Parse(theme.Surface);
        var raised = Parse(theme.SurfaceRaised);
        var hairline = Parse(theme.Hairline);
        var text = Parse(theme.Text);
        var muted = Parse(theme.MutedText);
        var accent = Parse(theme.Accent);
        var light = IsLight(background);
        var black = Colors.Black;
        var white = Colors.White;

        var values = new Dictionary<string, object>
        {
            ["UiFont"] = new FontFamily(skin.UiFont),
            ["DisplayFont"] = new FontFamily(skin.DisplayFont),
            ["DisplayWeight"] = skin.DisplayWeight,
            ["RoundCorner"] = new CornerRadius(skin.RoundShapes ? 999 : Math.Max(1, 3 * skin.Rounding)),
        };
        foreach (var (key, radius) in Corners)
        {
            values[key] = new CornerRadius(radius * skin.Rounding);
        }

        // Lucent's own look: flat, with hairline edges. Each finish below changes some of these.
        Brush frame = Solid(hairline);
        Brush sheen = Brushes.Transparent;
        Brush titleBar = Brushes.Transparent;
        Brush display = Solid(background);
        Brush displayEdge = Solid(hairline);
        Brush displayInset = Brushes.Transparent;
        Brush displayLines = Brushes.Transparent;
        Brush timeText = Solid(text);
        Brush titleText = Solid(text);
        Brush buttonFace = Brushes.Transparent;
        Brush buttonLight = Brushes.Transparent;
        Brush buttonDark = Brushes.Transparent;
        Brush buttonGlyph = Solid(muted);
        Brush playGloss = Brushes.Transparent;
        double playGlow = 0;

        switch (skin.Finish)
        {
            case SkinFinish.Bevel:
                // Raised windows and buttons (light along the top, dark along the bottom) around a sunken LCD.
                // Top to bottom rather than corner to corner, so a wide window's top edge is one colour
                // all the way along instead of turning dark halfway across.
                frame = Vertical(Mix(raised, white, 0.35), Mix(background, black, 0.5));
                // A raised bar with light catching its top. (Ridges behind the title ran through its text.)
                titleBar = Vertical(Fade(white, light ? 0.45 : 0.07), Fade(black, light ? 0.04 : 0.22));
                display = Solid(Mix(background, black, light ? 0.85 : 0.6));
                displayEdge = Vertical(Mix(background, black, 0.6), Mix(raised, white, 0.25));
                displayInset = Solid(Fade(black, 0.6));
                displayLines = Lines(Fade(black, 0.35), 3);
                timeText = Solid(accent);
                titleText = Solid(accent);
                buttonFace = Vertical(Mix(raised, white, 0.06), surface);
                buttonLight = Solid(Fade(white, light ? 0.9 : 0.22));
                buttonDark = Solid(Fade(black, light ? 0.35 : 0.7));
                buttonGlyph = Solid(text);
                break;

            case SkinFinish.Glass:
                // Light catching the top of every pane, see-through panels, and glossy round buttons.
                frame = Vertical(Fade(white, light ? 0.95 : 0.28), Fade(white, light ? 0.5 : 0.06));
                sheen = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Fade(white, light ? 0.5 : 0.08), 0),
                    new(Fade(white, 0), 0.42),
                    new(Fade(accent, 0.05), 1),
                }, new Point(0, 0), new Point(0.6, 1));
                display = Solid(Fade(background, 0.55));
                displayEdge = Vertical(Fade(white, light ? 0.9 : 0.2), light ? Fade(black, 0.1) : Fade(white, 0.04));
                buttonFace = Vertical(Fade(white, light ? 0.7 : 0.08), Fade(white, light ? 0.3 : 0.02));
                buttonLight = Solid(Fade(white, light ? 0.95 : 0.2));
                buttonDark = Solid(light ? Fade(black, 0.14) : Fade(white, 0.05));
                buttonGlyph = Solid(text);
                playGloss = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Fade(white, 0.45), 0),
                    new(Fade(white, 0.1), 0.48),
                    new(Fade(white, 0), 0.5),
                }, new Point(0, 0), new Point(0, 1));
                break;

            case SkinFinish.Neon:
                // Edges drawn in the accent, a glow round the display and the play button, scan lines on the display.
                frame = Solid(Fade(accent, 0.75));
                sheen = Vertical(Fade(accent, 0), Fade(accent, 0.07));
                displayEdge = Solid(accent);
                displayLines = Lines(Fade(black, light ? 0.06 : 0.3), 3);
                timeText = Solid(accent);
                buttonLight = Solid(Fade(accent, 0.45));
                buttonDark = Solid(Fade(accent, 0.45));
                buttonGlyph = Solid(accent);
                playGlow = 0.55;
                values["DisplayGlowEffect"] = Freeze(new DropShadowEffect
                {
                    Color = accent,
                    BlurRadius = 22,
                    ShadowDepth = 0,
                    Opacity = 1,
                });
                break;

            case SkinFinish.Metal:
                // Brushed metal: a bright band at the top, a dip through the middle, darker toward the bottom.
                frame = Vertical(Fade(white, light ? 0.9 : 0.16), Fade(black, light ? 0.3 : 0.75));
                sheen = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Fade(white, light ? 0.6 : 0.09), 0),
                    new(Fade(white, light ? 0.2 : 0.03), 0.35),
                    new(Fade(white, 0), 0.5),
                    new(Fade(white, light ? 0.15 : 0.02), 0.65),
                    new(Fade(black, light ? 0.06 : 0.18), 1),
                }, new Point(0, 0), new Point(0, 1));
                display = Solid(Mix(background, black, light ? 0.05 : 0.45));
                displayEdge = Vertical(Fade(black, 0.6), Fade(white, light ? 0.9 : 0.14));
                displayInset = Solid(Fade(black, light ? 0.12 : 0.5));
                buttonFace = Vertical(Mix(raised, text, light ? 0.02 : 0.16), Mix(surface, text, light ? 0 : 0.04));
                buttonLight = Solid(Fade(white, light ? 0.95 : 0.18));
                buttonDark = Solid(Fade(black, light ? 0.25 : 0.65));
                buttonGlyph = Solid(text);
                playGloss = new LinearGradientBrush(new GradientStopCollection
                {
                    new(Fade(white, 0.3), 0),
                    new(Fade(white, 0), 0.55),
                    new(Fade(black, 0.15), 1),
                }, new Point(0, 0), new Point(0, 1));
                break;
        }

        values["WindowFrameBrush"] = Freeze(frame);
        values["PanelSheenBrush"] = Freeze(sheen);
        values["TitleBarBrush"] = Freeze(titleBar);
        values["DisplayBrush"] = Freeze(display);
        values["DisplayEdgeBrush"] = Freeze(displayEdge);
        values["DisplayInsetBrush"] = Freeze(displayInset);
        values["DisplayLinesBrush"] = Freeze(displayLines);
        values["DisplayTimeBrush"] = Freeze(timeText);
        values["DisplayTitleBrush"] = Freeze(titleText);
        values["ButtonFaceBrush"] = Freeze(buttonFace);
        values["ButtonLightEdgeBrush"] = Freeze(buttonLight);
        values["ButtonDarkEdgeBrush"] = Freeze(buttonDark);
        values["ButtonGlyphBrush"] = Freeze(buttonGlyph);
        values["PlayGlossBrush"] = Freeze(playGloss);
        values["PlayGlowRest"] = playGlow;
        return values;
    }

    /// <summary>
    /// A small preview of a skin for the skin picker: its play button and a bit of its display,
    /// in its own shapes, type and finish, coloured by the theme that's on now.
    /// </summary>
    public static UIElement CreatePreview(Skin skin, Theme theme)
    {
        var look = Build(skin, theme);
        var round = (CornerRadius)look["RoundCorner"];
        var accentGradient = ThemeManager.Gradient(theme);

        var play = new Grid { Width = 20, Height = 20, VerticalAlignment = VerticalAlignment.Center };
        play.Children.Add(new Border { CornerRadius = round, Background = accentGradient });
        play.Children.Add(new Border { CornerRadius = round, Background = (Brush)look["PlayGlossBrush"] });
        play.Children.Add(new Border
        {
            CornerRadius = round,
            BorderThickness = new Thickness(1, 1, 0, 0),
            BorderBrush = (Brush)look["ButtonLightEdgeBrush"],
        });
        play.Children.Add(new Border
        {
            CornerRadius = round,
            BorderThickness = new Thickness(0, 0, 1, 1),
            BorderBrush = (Brush)look["ButtonDarkEdgeBrush"],
        });
        play.Children.Add(new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M0,0 L6,3.5 L0,7 Z"),
            Fill = ThemeManager.Solid(theme.AccentForeground),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(1.5, 0, 0, 0),
        });

        var name = new TextBlock
        {
            Text = skin.Name,
            Margin = new Thickness(9, 0, 6, 0),
            FontFamily = (FontFamily)look["UiFont"],
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = ThemeManager.Solid(theme.Text),
            VerticalAlignment = VerticalAlignment.Center,
        };

        var time = new Grid { VerticalAlignment = VerticalAlignment.Center };
        time.Children.Add(new Border
        {
            CornerRadius = (CornerRadius)look["TinyCorner"],
            Background = (Brush)look["DisplayBrush"],
            BorderBrush = (Brush)look["DisplayEdgeBrush"],
            BorderThickness = new Thickness(1),
            Effect = look.TryGetValue("DisplayGlowEffect", out var glow) ? (Effect)glow : null,
        });
        time.Children.Add(new Border
        {
            CornerRadius = (CornerRadius)look["TinyCorner"],
            Background = (Brush)look["DisplayLinesBrush"],
        });
        time.Children.Add(new TextBlock
        {
            Text = "1:23",
            Margin = new Thickness(6, 2, 6, 2),
            FontFamily = (FontFamily)look["DisplayFont"],
            FontWeight = skin.DisplayWeight,
            FontSize = 11,
            Foreground = (Brush)look["DisplayTimeBrush"],
        });

        var row = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(play, Dock.Left);
        DockPanel.SetDock(time, Dock.Right);
        row.Children.Add(play);
        row.Children.Add(time);
        row.Children.Add(name);

        // A short seek bar under them, a third of the way through, so the swatch reads as a little player.
        var seek = new Grid { Height = 3, Margin = new Thickness(0, 6, 0, 0) };
        seek.Children.Add(new Border
        {
            CornerRadius = (CornerRadius)look["TrackCorner"],
            Background = ThemeManager.Solid(theme.Hairline),
        });
        seek.Children.Add(new Border
        {
            CornerRadius = (CornerRadius)look["TrackCorner"],
            Background = accentGradient,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 44,
        });
        var player = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        player.Children.Add(row);
        player.Children.Add(seek);

        var corner = (CornerRadius)look["BarCorner"];
        var card = new Grid { Height = 52, ToolTip = skin.Description };
        card.Children.Add(new Border { CornerRadius = corner, Background = ThemeManager.Solid(theme.Surface) });
        card.Children.Add(new Border
        {
            CornerRadius = corner,
            Background = (Brush)look["PanelSheenBrush"],
            BorderBrush = (Brush)look["WindowFrameBrush"],
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 0, 8, 0),
            Child = player,
        });
        return card;
    }

    private static bool IsLight(Color colour) => (0.299 * colour.R + 0.587 * colour.G + 0.114 * colour.B) / 255 > 0.5;

    /// <summary>Part of the way from one colour to another (0 = all <paramref name="from"/>, 1 = all <paramref name="to"/>).</summary>
    private static Color Mix(Color from, Color to, double amount) => Color.FromArgb(
        (byte)Math.Round(from.A + (to.A - from.A) * amount),
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));

    /// <summary>A colour made see-through, from 0 (invisible) to 1 (solid).</summary>
    private static Color Fade(Color colour, double opacity) =>
        Color.FromArgb((byte)Math.Round(255 * opacity), colour.R, colour.G, colour.B);

    private static Brush Solid(Color colour) => new SolidColorBrush(colour);

    private static Brush Vertical(Color top, Color bottom) => new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));

    /// <summary>Thin horizontal lines, one every <paramref name="spacing"/> pixels: scan lines and ridges.</summary>
    private static Brush Lines(Color colour, double spacing) => new LinearGradientBrush(new GradientStopCollection
    {
        new(Colors.Transparent, 0),
        new(Colors.Transparent, 1 - 1 / spacing),
        new(colour, 1 - 1 / spacing),
        new(colour, 1),
    }, new Point(0, 0), new Point(0, spacing))
    {
        MappingMode = BrushMappingMode.Absolute,
        SpreadMethod = GradientSpreadMethod.Repeat,
    };

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }

    private static Color Parse(string colour) => (Color)ColorConverter.ConvertFromString(colour);
}
