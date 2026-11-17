namespace Lucent.Visualizer;

/// <summary>
/// The visuals shown most recently, by name, so Auto can leave them alone for a while rather than come back to
/// one a few minutes after it played. Names, not indexes, so the list still holds the next time the app opens.
/// </summary>
public static class RecentVisuals
{
    /// <summary>How many it remembers, which is also the most Auto can be asked to skip.</summary>
    public const int Limit = 100;

    /// <summary>How many recent visuals Auto can be asked to skip (besides none at all).</summary>
    public static readonly int[] Choices = { 25, 50, 75, 100 };

    /// <summary>"Off" or "50 visuals", for menus.</summary>
    public static string Text(int count) => count <= 0 ? "Off" : $"{count} visuals";

    /// <summary>
    /// The list with a visual that's on screen now put at its end (and taken out of wherever it was before),
    /// as a new list; the oldest are forgotten past <see cref="Limit"/>.
    /// </summary>
    public static string[] Add(IReadOnlyList<string>? shown, string name)
    {
        var list = new List<string>((shown?.Count ?? 0) + 1);
        if (shown is not null)
        {
            list.AddRange(shown.Where(earlier => !string.Equals(earlier, name, StringComparison.OrdinalIgnoreCase)));
        }

        list.Add(name);
        if (list.Count > Limit)
        {
            list.RemoveRange(0, list.Count - Limit);
        }

        return list.ToArray();
    }

    /// <summary>The last <paramref name="count"/> visuals shown, newest first.</summary>
    public static string[] Newest(IReadOnlyList<string>? shown, int count)
    {
        count = Math.Clamp(count, 0, Math.Min(Limit, shown?.Count ?? 0));
        var newest = new string[count];
        for (var back = 0; back < count; back++)
        {
            newest[back] = shown![shown.Count - 1 - back];
        }

        return newest;
    }

    /// <summary>
    /// The visuals Auto may pick from: those <paramref name="ready"/> to play, less the ones in
    /// <paramref name="recent"/> (newest first). When there are too few to go round, a short or narrowed list,
    /// say, the ones shown longest ago come back, so there's always at least a quarter of the list to pick from.
    /// </summary>
    public static List<int> Fresh(List<int> ready, IReadOnlyList<int>? recent)
    {
        if (recent is null || recent.Count == 0 || ready.Count < 2)
        {
            return ready;
        }

        var most = ready.Count - Math.Max(1, ready.Count / 4);
        var playable = new HashSet<int>(ready);
        var skipped = new HashSet<int>();
        foreach (var index in recent)
        {
            if (skipped.Count >= most)
            {
                break;
            }

            if (playable.Contains(index))
            {
                skipped.Add(index);
            }
        }

        return skipped.Count == 0 ? ready : ready.Where(index => !skipped.Contains(index)).ToList();
    }
}
