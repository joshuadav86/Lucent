using System.Globalization;

namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>
/// How a classic skin's playlist shows its songs, from pledit.txt: the text colour, the playing song's colour,
/// the background, the selected rows' background (all 0xAARRGGBB), and a font name.
/// </summary>
public sealed record ClassicPlaylistStyle(uint Normal, uint Current, uint NormalBackground, uint SelectedBackground, string Font)
{
    /// <summary>Lucent Classic's, used when nothing else gives any.</summary>
    public static ClassicPlaylistStyle Default { get; } = new(0xFFB8C4E0, 0xFF22D3EE, 0xFF07070C, 0xFF2A2350, "Arial");

    /// <summary>
    /// pledit.txt: an .ini file whose [Text] section has lines like "Normal=#00FF00". Anything missing or
    /// unreadable keeps <paramref name="defaults"/>'.
    /// </summary>
    public static ClassicPlaylistStyle Parse(string text, ClassicPlaylistStyle? defaults)
    {
        var style = defaults ?? Default;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            var equals = line.IndexOf('=');
            if (equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (key.Equals("Font", StringComparison.OrdinalIgnoreCase))
            {
                style = value.Length > 0 ? style with { Font = value } : style;
                continue;
            }

            if (Colour(value) is not { } colour)
            {
                continue;
            }

            style = key.ToLowerInvariant() switch
            {
                "normal" => style with { Normal = colour },
                "current" => style with { Current = colour },
                "normalbg" => style with { NormalBackground = colour },
                "selectedbg" => style with { SelectedBackground = colour },
                _ => style,
            };
        }

        return style;
    }

    // "#00FF00", and the "00FF00" some skins write.
    private static uint? Colour(string value)
    {
        var hex = value.TrimStart('#');
        if (hex.Length >= 6 && uint.TryParse(hex[..6], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return 0xFF000000 | rgb;
        }

        return null;
    }
}
