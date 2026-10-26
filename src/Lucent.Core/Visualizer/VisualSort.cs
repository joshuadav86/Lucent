namespace Lucent.Visualizer;

/// <summary>The orders the visual picker can list visuals in (inside each family folder, when they're on).</summary>
public enum VisualSort
{
    Name,
    Family,
    RecentlyUsed,
    FavoritesFirst,
    Newest,
}

public static class VisualSorts
{
    /// <summary>How many visuals "Recently used" remembers.</summary>
    public const int RecentLimit = 100;

    public static IReadOnlyList<VisualSort> All { get; } = Enum.GetValues<VisualSort>();

    public static string Label(VisualSort sort) => sort switch
    {
        VisualSort.Family => "Family",
        VisualSort.RecentlyUsed => "Recently used",
        VisualSort.FavoritesFirst => "Favorites first",
        VisualSort.Newest => "Newest first",
        _ => "A to Z",
    };

    /// <summary>
    /// The visuals in the order the picker lists them, which is also the order N and B step through them.
    /// In folders, folder by folder in the usual family order and sorted inside each; otherwise one sorted list.
    /// Ties fall back to A to Z, so the order is stable.
    /// </summary>
    /// <param name="recent">Visual names, most recently picked first.</param>
    public static List<VisualChoice> Order(IEnumerable<VisualChoice> choices, VisualSort sort, bool inFolders, IReadOnlyList<string> recent)
    {
        var recentRank = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < recent.Count; i++)
        {
            recentRank.TryAdd(recent[i], i);
        }

        var byFamily = inFolders || sort == VisualSort.Family;
        long Key(VisualChoice choice) => sort switch
        {
            // Never-picked visuals come after the picked ones.
            VisualSort.RecentlyUsed => recentRank.TryGetValue(choice.Name, out var rank) ? rank : int.MaxValue,
            VisualSort.FavoritesFirst => choice.IsFavorite ? 0 : 1,
            VisualSort.Newest => -(choice.Added ?? DateTime.MinValue).Ticks,
            _ => 0,
        };

        // Visuals are numbered A to Z by name, so the number puts ties in name order.
        return choices
            .OrderBy(choice => byFamily ? VisualStyles.FamilyOrder(choice.Family) : 0)
            .ThenBy(choice => byFamily ? choice.Family : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(Key)
            .ThenBy(choice => choice.Index)
            .ToList();
    }

    /// <summary>Moves a visual to the front of the recently used list, keeping it to <see cref="RecentLimit"/> names.</summary>
    public static void Remember(List<string> recent, string name)
    {
        recent.RemoveAll(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, name);
        if (recent.Count > RecentLimit)
        {
            recent.RemoveRange(RecentLimit, recent.Count - RecentLimit);
        }
    }
}
