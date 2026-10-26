using System.IO;
using System.Text;
using System.Text.Json;
using Lucent.Library;
using Lucent.Playlist;
using Lucent.Settings;

namespace Lucent.Import;

/// <summary>What bringing a library over did.</summary>
public sealed record LibraryImportResult
{
    public ImportSource Source { get; init; }

    /// <summary>Songs the other player had.</summary>
    public int Songs { get; init; }

    /// <summary>Of those, the ones found on this computer.</summary>
    public int Found { get; init; }

    /// <summary>Songs whose plays or rating were brought over.</summary>
    public int StatsUpdated { get; init; }

    /// <summary>The playlists saved, by their names in Lucent.</summary>
    public IReadOnlyList<string> Playlists { get; init; } = Array.Empty<string>();

    /// <summary>Loved songs, to become favorites.</summary>
    public IReadOnlyList<string> Loved { get; init; } = Array.Empty<string>();

    /// <summary>"Artist – Title" of each song that couldn't be found.</summary>
    public IReadOnlyList<string> Missing { get; init; } = Array.Empty<string>();

    /// <summary>
    /// A folder holding songs that were found but aren't in the library (iTunes' own music folder, say),
    /// worth offering to add to it. Null when there's nothing to offer.
    /// </summary>
    public string? FolderToLink { get; init; }

    public int FolderToLinkSongs { get; init; }
}

/// <summary>
/// Brings another player's library into Lucent: finds each song on this computer, carries its plays and
/// rating over, and saves its playlists as Lucent playlists. Importing the same source again updates
/// the playlists it made before rather than adding copies.
/// </summary>
public static class LibraryImporter
{
    public static LibraryImportResult Apply(ImportedLibrary library, LibraryMatcher matcher, TrackStats stats, Func<string, Track?> tags,
        IProgress<string>? progress = null, CancellationToken cancel = default)
    {
        progress?.Report("Finding your songs…");
        var found = new Dictionary<ImportedTrack, string?>(ReferenceEqualityComparer.Instance);
        string? FindOnce(ImportedTrack track)
        {
            if (!found.TryGetValue(track, out var path))
            {
                cancel.ThrowIfCancellationRequested();
                path = matcher.Find(track);
                found[track] = path;
            }

            return path;
        }

        var all = library.Tracks.Concat(library.Playlists.SelectMany(p => p.Tracks)).Distinct(ReferenceEqualityComparer.Instance).Cast<ImportedTrack>().ToList();
        var updated = 0;
        var loved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var missing = new List<string>();
        var outside = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in all)
        {
            var path = FindOnce(track);
            if (path is null)
            {
                missing.Add(track.Label);
                continue;
            }

            if (seen.Add(path) && !matcher.InLibrary(path))
            {
                outside.Add(path);
            }

            if (track.HasStats && stats.MergeImported(path, track.Plays, track.Rating, track.LastPlayed))
            {
                updated++;
            }

            if (track.Loved)
            {
                loved.Add(path);
            }
        }

        if (updated > 0)
        {
            stats.Save();
        }

        progress?.Report("Saving playlists…");
        var record = ImportRecord.Load();
        var saved = new List<string>();
        var tracksByPath = new Dictionary<string, Track>(StringComparer.OrdinalIgnoreCase);
        foreach (var playlist in library.Playlists)
        {
            cancel.ThrowIfCancellationRequested();
            var paths = playlist.Tracks.Select(FindOnce).OfType<string>().ToList();
            if (paths.Count == 0)
            {
                continue;
            }

            var name = record.NameFor(library.Source, playlist.Name);
            var songs = paths.Select(path =>
            {
                if (!tracksByPath.TryGetValue(path, out var song))
                {
                    song = tags(path) ?? Track.FromFile(path);
                    tracksByPath[path] = song;
                }

                return song;
            }).ToList();
            SavedPlaylists.Write(name, songs);
            record.Remember(library.Source, playlist.Name, name);
            saved.Add(name);
        }

        record.Save();
        var (folder, folderSongs) = CommonFolder(outside);
        return new LibraryImportResult
        {
            Source = library.Source,
            Songs = all.Count,
            Found = all.Count - missing.Count,
            StatsUpdated = updated,
            Playlists = saved,
            Loved = loved.ToList(),
            Missing = missing.Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(m => m, StringComparer.CurrentCultureIgnoreCase).ToList(),
            FolderToLink = folder,
            FolderToLinkSongs = folderSongs,
        };
    }

    /// <summary>
    /// The folder most of the songs outside the library share, if it's a real music folder and not just a
    /// drive or the user's home folder.
    /// </summary>
    private static (string? Folder, int Songs) CommonFolder(IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return (null, 0);
        }

        // Each song's grandparent ("Artist" above "Album"), and the folder most of them have in common.
        var groups = paths
            .Select(path => Path.GetDirectoryName(Path.GetDirectoryName(path)))
            .OfType<string>()
            .Select(folder => Path.GetDirectoryName(folder) ?? folder)
            .GroupBy(folder => folder, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ToList();
        if (groups.Count == 0)
        {
            return (null, 0);
        }

        var top = groups[0].Key;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (Path.GetPathRoot(top) is { } root && (string.Equals(Path.TrimEndingDirectorySeparator(root), Path.TrimEndingDirectorySeparator(top), StringComparison.OrdinalIgnoreCase)
            || string.Equals(Path.TrimEndingDirectorySeparator(top), Path.TrimEndingDirectorySeparator(home), StringComparison.OrdinalIgnoreCase)
            || top.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length < 2))
        {
            return (null, 0);
        }

        var count = paths.Count(path => path.StartsWith(Path.TrimEndingDirectorySeparator(top) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        return (top, count);
    }

    /// <summary>Writes the songs that weren't found to a text file, for the user to look over. Returns its path.</summary>
    public static string WriteMissingList(LibraryImportResult result)
    {
        var folder = Path.Combine(AppDataFolder.Path, "Imports");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"Not found from {ImportSources.Label(result.Source)}.txt");
        var text = new StringBuilder()
            .AppendLine($"Songs from {ImportSources.Label(result.Source)} that Lucent couldn't find on this computer ({result.Missing.Count:N0}).")
            .AppendLine("Add the folders they're in to the library, then import again.")
            .AppendLine();
        foreach (var song in result.Missing)
        {
            text.AppendLine(song);
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(true));
        return path;
    }

    /// <summary>Which Lucent playlists came from which source's playlists, so importing again replaces them.</summary>
    private sealed class ImportRecord
    {
        private static string FilePath => Path.Combine(AppDataFolder.Path, "imported-playlists.json");

        // "AppleMusic/Road trip" → "Road trip".
        private readonly Dictionary<string, string> _names;

        private ImportRecord(Dictionary<string, string> names) => _names = names;

        public static ImportRecord Load()
        {
            try
            {
                if (File.Exists(FilePath) && JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(FilePath)) is { } names)
                {
                    return new ImportRecord(new Dictionary<string, string>(names, StringComparer.OrdinalIgnoreCase));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                // Importing again then makes new playlists beside the old ones.
            }

            return new ImportRecord(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The name this playlist had last time, if it's still there; else its own name if that's free;
        /// else its name with the source after it, "Road trip (Plex)".
        /// </summary>
        public string NameFor(ImportSource source, string playlist)
        {
            var key = Key(source, playlist);
            if (_names.TryGetValue(key, out var earlier) && SavedPlaylists.Exists(earlier))
            {
                return earlier;
            }

            var clean = SavedPlaylists.CleanName(playlist);
            if (clean.Length == 0)
            {
                clean = ImportSources.Label(source);
            }

            if (!SavedPlaylists.Exists(clean))
            {
                return clean;
            }

            var label = ImportSources.Label(source);
            var name = SavedPlaylists.CleanName($"{clean} ({label})");
            for (var copy = 2; SavedPlaylists.Exists(name); copy++)
            {
                name = SavedPlaylists.CleanName($"{clean} ({label} {copy})");
            }

            return name;
        }

        public void Remember(ImportSource source, string playlist, string name) => _names[Key(source, playlist)] = name;

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(AppDataFolder.Path);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_names));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Importing again then makes new playlists beside these.
            }
        }

        private static string Key(ImportSource source, string playlist) => $"{source}/{playlist}";
    }
}
