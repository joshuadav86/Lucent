using Lucent.Library;
using Lucent.Playlist;

namespace Lucent.Import;

/// <summary>
/// One import, start to finish, off the UI thread: reads the source, finds its songs in the library, and
/// brings the plays, ratings and playlists over. The Windows and Mac import windows both run it.
/// </summary>
public static class ImportRunner
{
    public static Task<LibraryImportResult> RunAsync(Func<CancellationToken, Task<ImportedLibrary>> read, IReadOnlyList<string> libraryFiles,
        Func<string, Track?> tags, IProgress<string> progress, CancellationToken cancel) => Task.Run(async () =>
    {
        var library = await read(cancel);
        cancel.ThrowIfCancellationRequested();
        var matcher = new LibraryMatcher(libraryFiles, tags)
        {
            Cancel = cancel,
            TagProgress = new Progress<(int Done, int Total)>(p =>
                progress.Report($"Reading your library's tags to find songs by name ({p.Done:N0} of {p.Total:N0})…")),
        };
        return LibraryImporter.Apply(library, matcher, TrackStats.Shared, tags, progress, cancel);
    }, cancel);

    /// <summary>What happened, in a sentence or three, for the import window.</summary>
    public static string Summary(LibraryImportResult result, int newFavorites)
    {
        var source = ImportSources.Label(result.Source);
        if (result.Songs == 0)
        {
            return $"There were no songs to bring over from {source}.";
        }

        if (result.Found == 0)
        {
            return $"None of the {Count(result.Songs, "song")} from {source} are on this computer. Add the folders they're in to the library, then import again.";
        }

        var parts = new List<string>();
        if (result.Playlists.Count > 0)
        {
            parts.Add(Count(result.Playlists.Count, "playlist"));
        }

        if (result.StatsUpdated > 0)
        {
            parts.Add($"the plays and ratings of {Count(result.StatsUpdated, "song")}");
        }

        var text = parts.Count > 0
            ? $"Brought over {string.Join(" and ", parts)} from {source}."
            : $"Found {Count(result.Found, "song")} from {source}, with nothing new to bring over.";
        if (newFavorites > 0)
        {
            text += $" {Count(newFavorites, "loved song")} {(newFavorites == 1 ? "is a favorite" : "are favorites")} now.";
        }

        if (result.Missing.Count > 0)
        {
            text += $" {Count(result.Missing.Count, "song")} couldn't be found on this computer.";
        }

        return text;
    }

    /// <summary>The offer to add a folder of found songs to the library, or null.</summary>
    public static string? FolderOffer(LibraryImportResult result) => result.FolderToLink is { } folder
        ? $"{Count(result.FolderToLinkSongs, "song")} {(result.FolderToLinkSongs == 1 ? "is" : "are")} in {folder}, which isn't in your library."
        : null;

    private static string Count(int count, string noun) => $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}";
}
