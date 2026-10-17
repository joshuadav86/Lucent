namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// The families and tags a visual can name in its "// Family:" and "// Tags:" header lines.
/// Families are the folders in the visual picker; tags are its tick boxes.
/// </summary>
public static class VisualStyles
{
    /// <summary>The folder for visuals that don't say which family they belong to.</summary>
    public const string OtherFamily = "Other";

    /// <summary>The families, in the order their folders are listed.</summary>
    public static IReadOnlyList<string> Families { get; } = new[]
    {
        "Fly-through", "Kaleidoscope", "Geometric", "Minimal", "Fractal", "Music-shaped", "Cosmic", "Organic", "Particles", "Ray traced", "Cyberpunk",
    };

    /// <summary>The tag every particle visual gets, whether or not its header lists it.</summary>
    public const string ParticlesTag = "Particles";

    /// <summary>The tags, in the order their tick boxes are listed.</summary>
    public static IReadOnlyList<string> Tags { get; } = new[]
    {
        "Calm", "Intense", "Colourful", "Dark", "Neon", "Space", "Water", "Fire", "Geometric", "Minimal", "Retro", "3D", ParticlesTag,
    };

    // Other spellings that mean the same thing, so "Colorful" or "Raytraced" still land in the right place.
    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["colorful"] = "Colourful",
        ["raymarched"] = "Ray traced",
    };

    /// <summary>The family as the picker shows it: the usual spelling of a known one, or "Other" when blank.</summary>
    public static string FamilyName(string? written) =>
        string.IsNullOrWhiteSpace(written) ? OtherFamily : KnownName(written.Trim(), Families);

    /// <summary>The tags in a "Calm, Neon, 3D" list, in their usual spelling, without repeats.</summary>
    public static IReadOnlyList<string> TagNames(string? written)
    {
        if (string.IsNullOrWhiteSpace(written))
        {
            return Array.Empty<string>();
        }

        return written.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => KnownName(tag, Tags))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Sorts family folders: the known families in their usual order, then any new ones A to Z, then "Other".</summary>
    public static int FamilyOrder(string family) => Order(family, Families);

    /// <summary>Sorts tags: the known tags in their usual order, then any new ones A to Z.</summary>
    public static int TagOrder(string tag) => Order(tag, Tags);

    private static int Order(string name, IReadOnlyList<string> known)
    {
        if (string.Equals(name, OtherFamily, StringComparison.OrdinalIgnoreCase))
        {
            return known.Count + 1;
        }

        for (var i = 0; i < known.Count; i++)
        {
            if (string.Equals(known[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return known.Count;
    }

    private static string KnownName(string written, IReadOnlyList<string> known)
    {
        var key = Key(written);
        if (Aliases.TryGetValue(key, out var alias) && known.Contains(alias))
        {
            return alias;
        }

        return known.FirstOrDefault(name => Key(name) == key) ?? written;
    }

    // Compares names without caring about case, spaces or hyphens ("Fly through" = "Fly-through").
    private static string Key(string name) => new string(name.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
}
