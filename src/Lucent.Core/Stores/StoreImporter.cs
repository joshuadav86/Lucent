using System.IO;
using System.IO.Compression;
using System.Text.Json;
using TagLib;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using File = System.IO.File;

namespace WinampInspiredVisualizer.Stores;

/// <summary>A store whose downloads Lucent can recognise and file into the library.</summary>
public enum MusicStore
{
    Bandcamp,
    Beatport,
}

/// <summary>What happened to a file offered to <see cref="StoreImporter"/>.</summary>
public enum StoreImportOutcome
{
    /// <summary>Its music is in the store's folder now.</summary>
    Imported,
    /// <summary>It was imported before, or the same release is already in the store's folder.</summary>
    AlreadyImported,
    /// <summary>It isn't a download from one of the stores being imported.</summary>
    NotFromStore,
    /// <summary>It couldn't be read, probably because it's still downloading. Worth trying again later.</summary>
    NotReady,
}

/// <summary>The result of offering a file to <see cref="StoreImporter"/>.</summary>
public sealed record StoreImportResult(StoreImportOutcome Outcome, MusicStore? Store = null, string? Folder = null, string? Artist = null,
    string? Title = null, int Tracks = 0)
{
    public static StoreImportResult NotFromStore { get; } = new(StoreImportOutcome.NotFromStore);

    public static StoreImportResult NotReady { get; } = new(StoreImportOutcome.NotReady);
}

/// <summary>
/// Files music bought from Bandcamp and Beatport into the library: a release's zip is unpacked into
/// "Bandcamp\Artist\Album" (or "Beatport\..."), and a single track is copied into its release's folder.
/// Each store tags the files it sells: Bandcamp puts "Visit https://artist.bandcamp.com" in the comment,
/// and Beatport links each track to its beatport.com page. That's how their downloads are told apart
/// from any other zip or song. The downloaded file itself is left where it is, and each one is
/// remembered so it isn't imported twice.
/// </summary>
public sealed class StoreImporter
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp" };

    // Bandcamp zips hold the tracks and the cover, sometimes with a booklet or notes as bonus files.
    private static readonly HashSet<string> ExtraExtensions = new(StringComparer.OrdinalIgnoreCase) { ".pdf", ".txt", ".nfo", ".cue", ".log", ".m3u" };

    private readonly object _gate = new();
    private readonly string _recordPath;
    private readonly HashSet<string> _done;

    private readonly IReadOnlySet<MusicStore> _stores;

    /// <param name="musicFolder">Where imported music goes: a folder per store, then per artist and per release.</param>
    /// <param name="recordPath">A file listing the downloads already imported.</param>
    /// <param name="stores">The stores whose downloads are imported; others' are left alone.</param>
    public StoreImporter(string musicFolder, string recordPath, IEnumerable<MusicStore> stores)
    {
        MusicFolder = musicFolder;
        _recordPath = recordPath;
        _stores = stores.ToHashSet();
        _done = LoadRecord(recordPath);
    }

    public string MusicFolder { get; }

    /// <summary>The folder a store's music goes in, like "Music\Bandcamp".</summary>
    public string FolderFor(MusicStore store) => Path.Combine(MusicFolder, store.ToString());

    /// <summary>
    /// Imports a downloaded zip or song if it's from one of the stores. <paramref name="knownStore"/> skips the
    /// check of its tags, for files that came straight from that store.
    /// </summary>
    public StoreImportResult Import(string path, MusicStore? knownStore = null)
    {
        var key = RecordKey(path);
        if (key is null)
        {
            return StoreImportResult.NotReady;
        }

        lock (_gate)
        {
            if (_done.Contains(key))
            {
                return new StoreImportResult(StoreImportOutcome.AlreadyImported);
            }
        }

        StoreImportResult result;
        try
        {
            result = string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase)
                ? ImportZip(path, knownStore)
                : AudioReaderFactory.IsSupported(path) ? ImportTrack(path, knownStore) : StoreImportResult.NotFromStore;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or CorruptFileException)
        {
            // Still being written, locked by the browser or virus scanner, or not a finished zip or song yet.
            return StoreImportResult.NotReady;
        }
        catch (UnsupportedFormatException)
        {
            // A song whose tags TagLib can't read can't be told apart as a store's.
            result = StoreImportResult.NotFromStore;
        }

        // Files that aren't from a store are remembered too, so a big zip isn't opened again every start.
        // Downloads straight from a store are temporary files, so there's nothing to remember them by.
        if (result.Outcome != StoreImportOutcome.NotReady && knownStore is null)
        {
            Remember(key);
        }

        return result;
    }

    /// <summary>Whether this download was looked at before (imported, or found not to be Bandcamp's).</summary>
    public bool WasSeen(string path)
    {
        var key = RecordKey(path);
        lock (_gate)
        {
            return key is not null && _done.Contains(key);
        }
    }

    private StoreImportResult ImportZip(string zipPath, MusicStore? knownStore)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(entry => entry.Length > 0 && !string.IsNullOrEmpty(entry.Name)).ToList();
        var tracks = files.Where(entry => AudioReaderFactory.IsSupported(entry.Name)).ToList();

        // Only zips that are all music, art and notes: anything else isn't a release download.
        if (tracks.Count == 0 || files.Any(entry => !AudioReaderFactory.IsSupported(entry.Name)
                && !ImageExtensions.Contains(Path.GetExtension(entry.Name)) && !ExtraExtensions.Contains(Path.GetExtension(entry.Name))))
        {
            return StoreImportResult.NotFromStore;
        }

        Directory.CreateDirectory(MusicFolder);
        var staging = StagingFolder();
        try
        {
            // The first track decides which store it's from and where it goes, before the rest is unpacked.
            var first = tracks.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase).First();
            var firstPath = Path.Combine(staging, SafeName(first.Name));
            first.ExtractToFile(firstPath);
            var tags = ReadTags(firstPath);
            if ((knownStore ?? tags.Store) is not { } store || (knownStore is null && !_stores.Contains(store)))
            {
                return StoreImportResult.NotFromStore;
            }

            var (zipArtist, zipTitle) = NameParts(Path.GetFileNameWithoutExtension(zipPath));
            var artist = tags.Artist ?? zipArtist ?? "Unknown artist";
            var title = tags.Album ?? zipTitle ?? Path.GetFileNameWithoutExtension(zipPath);

            // Store zips are flat, but only the file names are used, so nothing can land outside the folder.
            foreach (var entry in files.Where(entry => entry != first))
            {
                var target = Path.Combine(staging, SafeName(entry.Name));
                if (!File.Exists(target))
                {
                    entry.ExtractToFile(target);
                }
            }

            var outcome = MoveIntoLibrary(staging, FolderFor(store), artist, title, out var folder);
            return new StoreImportResult(outcome, store, folder, artist, title, tracks.Count);
        }
        finally
        {
            DeleteQuietly(staging);
        }
    }

    private StoreImportResult ImportTrack(string path, MusicStore? knownStore)
    {
        var tags = ReadTags(path);
        if ((knownStore ?? tags.Store) is not { } store || (knownStore is null && !_stores.Contains(store)))
        {
            return StoreImportResult.NotFromStore;
        }

        var (nameArtist, _) = NameParts(Path.GetFileNameWithoutExtension(path));
        var artist = tags.Artist ?? nameArtist ?? "Unknown artist";
        var folder = Path.Combine(FolderFor(store), SafeName(artist), SafeName(tags.Album ?? "Singles"));
        var target = Path.Combine(folder, SafeName(Path.GetFileName(path)));
        if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(path).Length)
        {
            return new StoreImportResult(StoreImportOutcome.AlreadyImported, store, folder, artist, tags.Title, 1);
        }

        Directory.CreateDirectory(folder);
        target = Unused(target);

        // Copied through a hidden temporary file, so the library never lists a half-copied song.
        var temporary = Path.Combine(folder, "." + Guid.NewGuid().ToString("N") + Path.GetExtension(path));
        try
        {
            File.Copy(path, temporary);
            File.Move(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }

        return new StoreImportResult(StoreImportOutcome.Imported, store, folder, artist, tags.Title, 1);
    }

    /// <summary>Moves an unpacked release into "Artist\Title", unless the same files are already there.</summary>
    private static StoreImportOutcome MoveIntoLibrary(string staging, string storeFolder, string artist, string title, out string folder)
    {
        var artistFolder = Path.Combine(storeFolder, SafeName(artist));
        var name = SafeName(title);
        folder = Path.Combine(artistFolder, name);
        for (var copy = 2; Directory.Exists(folder); copy++)
        {
            if (SameFiles(staging, folder))
            {
                return StoreImportOutcome.AlreadyImported;
            }

            // A different release with the same name (a deluxe edition, say) goes beside it.
            folder = Path.Combine(artistFolder, $"{name} ({copy})");
        }

        Directory.CreateDirectory(artistFolder);
        Directory.Move(staging, folder);
        try
        {
            File.SetAttributes(folder, File.GetAttributes(folder) & ~FileAttributes.Hidden);
        }
        catch (Exception)
        {
            // Only matters on Windows, where the staging folder was hidden.
        }

        return StoreImportOutcome.Imported;
    }

    /// <summary>Whether every file unpacked is already in the folder, at the same size.</summary>
    private static bool SameFiles(string staging, string folder) =>
        Directory.EnumerateFiles(staging).All(file =>
        {
            var existing = Path.Combine(folder, Path.GetFileName(file));
            return File.Exists(existing) && new FileInfo(existing).Length == new FileInfo(file).Length;
        });

    /// <summary>A new hidden folder inside the music folder, which the library skips while it fills.</summary>
    private string StagingFolder()
    {
        var staging = Path.Combine(MusicFolder, ".lucent-importing-" + Guid.NewGuid().ToString("N")[..8]);
        var info = Directory.CreateDirectory(staging);
        try
        {
            info.Attributes |= FileAttributes.Hidden;
        }
        catch (Exception)
        {
            // The dot already hides it everywhere but Windows.
        }

        return staging;
    }

    private static string Unused(string path)
    {
        var folder = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var copy = 2; File.Exists(path); copy++)
        {
            path = Path.Combine(folder, $"{name} ({copy}){extension}");
        }

        return path;
    }

    private static void DeleteQuietly(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception)
        {
            // A leftover hidden folder is harmless; the library doesn't list it.
        }
    }

    private sealed record FileTags(string? Artist, string? Album, string? Title, MusicStore? Store);

    private static FileTags ReadTags(string path)
    {
        using var file = TagLib.File.Create(path, TagWriter.TagLibMimeType(path), ReadStyle.None);
        var tag = file.Tag;
        var artist = FirstText(tag.AlbumArtists) ?? FirstText(tag.Performers);
        var store = IsBandcampComment(tag.Comment) ? MusicStore.Bandcamp
            : HasBeatportMark(file) ? MusicStore.Beatport
            : (MusicStore?)null;
        return new FileTags(artist, Text(tag.Album), Text(tag.Title), store);
    }

    /// <summary>Bandcamp's comment: "Visit https://artist.bandcamp.com", or the artist's own domain.</summary>
    public static bool IsBandcampComment(string? comment) =>
        comment is not null
        && (comment.Contains("bandcamp.com", StringComparison.OrdinalIgnoreCase)
            || comment.TrimStart().StartsWith("Visit http", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Beatport's marks: its ID3 tags link the track and label to their beatport.com pages and carry
    /// Beatport's own track id; its FLAC tags name beatport.com in a field.
    /// </summary>
    private static bool HasBeatportMark(TagLib.File file)
    {
        static bool Mentions(string? text) => text?.Contains("beatport", StringComparison.OrdinalIgnoreCase) == true;

        if (Mentions(file.Tag.Comment))
        {
            return true;
        }

        if (file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
        {
            foreach (var frame in id3.GetFrames())
            {
                var mentions = frame switch
                {
                    TagLib.Id3v2.UrlLinkFrame url => url.Text.Any(Mentions),
                    TagLib.Id3v2.UniqueFileIdentifierFrame id => Mentions(id.Owner),
                    TagLib.Id3v2.UserTextInformationFrame text => Mentions(text.Description) || text.Text.Any(Mentions),
                    _ => false,
                };
                if (mentions)
                {
                    return true;
                }
            }
        }

        if (file.GetTag(TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
        {
            return xiph.Cast<string>().Any(field => Mentions(field) || xiph.GetField(field).Any(Mentions));
        }

        return false;
    }

    private static string? FirstText(string[]? values) => values?.Select(Text).FirstOrDefault(value => value is not null);

    private static string? Text(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>"Artist - Title" (Bandcamp's own file naming) split in two, or nulls.</summary>
    public static (string? Artist, string? Title) NameParts(string name)
    {
        var dash = name.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? (Text(name[..dash]), Text(name[(dash + 3)..])) : (null, null);
    }

    /// <summary>A name that's safe as a file or folder name on Windows and the Mac.</summary>
    public static string SafeName(string name)
    {
        var chars = name.Select(c => c < 32 || "<>:\"/\\|?*".Contains(c) ? '_' : c).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.', ' ');
        if (safe.Length > 100)
        {
            safe = safe[..100].TrimEnd('.', ' ');
        }

        if (safe.Length == 0 || safe.All(c => c == '.'))
        {
            return "_";
        }

        // Windows won't make files called CON, NUL, COM1 and so on, whatever their extension.
        var stem = safe.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" || (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && char.IsDigit(stem[3]))
            ? "_" + safe
            : safe;
    }

    /// <summary>Identifies one download: its path, size and time, so a newer download of the same name counts as new.</summary>
    private static string? RecordKey(string path)
    {
        try
        {
            var info = new FileInfo(path);
            return info.Exists && info.Length > 0
                ? $"{Path.GetFullPath(path).ToUpperInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private void Remember(string key)
    {
        lock (_gate)
        {
            if (!_done.Add(key))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_recordPath)!);
                File.WriteAllText(_recordPath, JsonSerializer.Serialize(_done.Order().ToList()));
            }
            catch (Exception)
            {
                // Forgetting means a download may be offered again; the same files are then found already in place.
            }
        }
    }

    private static HashSet<string> LoadRecord(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return new HashSet<string>(JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path)) ?? new List<string>());
            }
        }
        catch (Exception)
        {
            // A damaged record just means checking downloads again.
        }

        return new HashSet<string>();
    }
}
