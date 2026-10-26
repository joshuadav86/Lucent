using System.Globalization;

namespace Lucent.Radio;

/// <summary>A genre in the radio browser's left column, and the directory tag that finds its stations.</summary>
/// <param name="Name">What the column shows.</param>
/// <param name="Tag">The Radio Browser tag it searches for. Tags match by substring, so "house" finds "deep house" too.</param>
public sealed record Genre(string Name, string Tag);

/// <summary>The genres the radio browser lists, picked for Lucent's listeners rather than the directory's thousands of tags.</summary>
public static class Genres
{
    public static readonly IReadOnlyList<Genre> All = new Genre[]
    {
        new("Ambient", "ambient"),
        new("Chillout", "chill"),
        new("Downtempo", "downtempo"),
        new("Lounge", "lounge"),
        new("Electronic", "electronic"),
        new("House", "house"),
        new("Techno", "techno"),
        new("Trance", "trance"),
        new("Drum and bass", "drum and bass"),
        new("Dubstep", "dubstep"),
        new("Synthwave", "synthwave"),
        new("Lo-fi", "lofi"),
        new("Jazz", "jazz"),
        new("Classical", "classical"),
        new("Rock", "rock"),
        new("Indie", "indie"),
        new("Alternative", "alternative"),
        new("Metal", "metal"),
        new("Pop", "pop"),
        new("Hip hop", "hip hop"),
        new("Soul and funk", "soul"),
        new("Reggae", "reggae"),
        new("Blues", "blues"),
        new("Country", "country"),
        new("Folk", "folk"),
        new("World", "world"),
        new("Oldies", "oldies"),
        new("80s", "80s"),
        new("90s", "90s"),
        new("News", "news"),
        new("Talk", "talk"),
    };

    private static readonly HashSet<string> Initialisms = new(StringComparer.OrdinalIgnoreCase) { "idm", "edm", "dnb", "ebm", "r&b", "uk", "usa" };

    /// <summary>A directory tag as a label: "drum and bass" becomes "Drum and bass".</summary>
    public static string Display(string tag)
    {
        tag = tag.Trim();
        if (tag.Length == 0)
        {
            return tag;
        }

        if (Initialisms.Contains(tag))
        {
            return tag.ToUpperInvariant();
        }

        // Shouty and odd-cased tags ("EDM", "SomaFM") are kept as the station wrote them.
        return tag.Any(char.IsUpper) ? tag : char.ToUpper(tag[0], CultureInfo.InvariantCulture) + tag[1..];
    }
}
