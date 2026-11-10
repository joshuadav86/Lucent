namespace Lucent.Text;

/// <summary>
/// Section names in Title Case, for the sidebars that list them (Online Radio's sections and genres, the editor's
/// effects and plugins): "All stations" shows as "All Stations", "Drum and bass" as "Drum and Bass". Only what's shown
/// changes. The names stay as they are in code and settings, so nothing saved needs moving.
/// </summary>
public static class TitleCase
{
    // Short words that stay small inside a title (but not at its start or end).
    private static readonly HashSet<string> Small = new(StringComparer.Ordinal)
    {
        "a", "an", "and", "as", "at", "but", "by", "for", "in", "nor", "of", "on", "or", "the", "to", "vs", "with",
    };

    /// <summary><paramref name="name"/> with each word capitalized, except short joining words in the middle.</summary>
    public static string Of(string name)
    {
        var words = name.Split(' ');
        var last = Array.FindLastIndex(words, word => word.Length > 0);
        for (var i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0 || (i > 0 && i < last && Small.Contains(words[i])))
            {
                continue;
            }

            // Each part of a hyphenated word ("Lo-Fi"). Words already capitalized ("DJs", "EQ", "80s") stay as they are.
            words[i] = string.Join('-', words[i].Split('-').Select(part => part.Length > 0 && char.IsLower(part[0])
                ? char.ToUpperInvariant(part[0]) + part[1..]
                : part));
        }

        return string.Join(' ', words);
    }
}
