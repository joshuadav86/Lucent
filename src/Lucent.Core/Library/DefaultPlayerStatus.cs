namespace WinampInspiredVisualizer.Library;

/// <summary>
/// What Options > Updates says about Lucent as the default player, from what each music and video type opens in
/// now. Both apps ask their system, then word it here, so a click that didn't take is never reported as done.
/// </summary>
public static class DefaultPlayerStatus
{
    private const int MostNamed = 5;

    /// <summary>
    /// A sentence or two for the Default player row. <paramref name="elsewhere"/> is each music or video type
    /// (its extension, ".wav") that opens in another app, with that app's name, or null if it has none.
    /// </summary>
    public static string Describe(IReadOnlyList<(string Extension, string? App)> elsewhere)
    {
        if (elsewhere.Count == 0)
        {
            return "Lucent is your default player: music and videos you double-click open in Lucent.";
        }

        var byApp = elsewhere
            .GroupBy(type => type.App ?? "")
            .OrderByDescending(group => group.Count())
            .Select(group => $"{Types(group.Select(type => type.Extension).ToList())} in {(group.Key == "" ? "another app" : group.Key)}")
            .ToList();
        var lines = byApp.Count <= 2 ? string.Join(", and ", byApp) : $"{byApp[0]}, and other types in other apps";
        return $"Lucent isn't your default player yet: {lines}.";
    }

    // ".wav", ".flac" and ".ogg" as "WAV, FLAC, and OGG files open", naming at most five.
    private static string Types(IReadOnlyList<string> extensions)
    {
        var names = extensions.Select(extension => extension.TrimStart('.').ToUpperInvariant()).Order().ToList();
        var files = " files";
        if (names.Count > MostNamed)
        {
            names = [.. names.Take(MostNamed - 1), $"{names.Count - (MostNamed - 1)} other types"];
            files = "";
        }

        var list = names.Count switch
        {
            1 => names[0],
            2 => $"{names[0]} and {names[1]}",
            _ => $"{string.Join(", ", names.Take(names.Count - 1))}, and {names[^1]}",
        };
        return $"{list}{files} open";
    }
}
