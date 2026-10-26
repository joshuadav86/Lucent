using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Lucent.Playlist;

namespace Lucent.Import;

/// <summary>
/// Finds the file in Lucent's library for a song from another player. First by its path, if the file is
/// there; then by its file name and the folders around it, for a library that moved to another drive or
/// another computer; then by artist and title (and length, when there's a choice), for sources that only
/// know songs by name.
/// </summary>
public sealed partial class LibraryMatcher
{
    private readonly IReadOnlyList<string> _files;
    private readonly Func<string, Track?> _tags;
    private readonly HashSet<string> _known;
    private readonly Dictionary<string, List<string>> _byName = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, List<Track>>? _byArtistTitle;
    private Dictionary<string, List<Track>>? _byTitle;

    /// <param name="libraryFiles">Every song in the library.</param>
    /// <param name="tags">A song's tags; the library's own if it has read them, so they aren't read again.</param>
    public LibraryMatcher(IReadOnlyList<string> libraryFiles, Func<string, Track?> tags)
    {
        _files = libraryFiles;
        _tags = tags;
        _known = new HashSet<string>(libraryFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var file in libraryFiles)
        {
            var name = FileName(file);
            if (!_byName.TryGetValue(name, out var list))
            {
                _byName[name] = list = new List<string>();
            }

            list.Add(file);
        }
    }

    /// <summary>Raised with how many songs have been read, while the library's tags are read for matching by name.</summary>
    public IProgress<(int Done, int Total)>? TagProgress { get; set; }

    public CancellationToken Cancel { get; set; }

    /// <summary>Whether a path is one of the library's songs.</summary>
    public bool InLibrary(string path) => _known.Contains(path);

    /// <summary>The library song for this one, a file on this computer that isn't in the library, or null.</summary>
    public string? Find(ImportedTrack track)
    {
        if (track.Path is { Length: > 0 } path)
        {
            if (_known.Contains(path))
            {
                return path;
            }

            if (ByFolders(path, track) is { } moved)
            {
                return moved;
            }

            if (Exists(path))
            {
                return path;
            }
        }

        return ByName(track);
    }

    private static bool Exists(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && File.Exists(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// The library file with the same name whose folders match the most of the other path's, from the
    /// end: "Artist\Album\01 Song.mp3" is a better match than another "01 Song.mp3".
    /// </summary>
    private string? ByFolders(string path, ImportedTrack track)
    {
        if (!_byName.TryGetValue(FileName(path), out var candidates))
        {
            return null;
        }

        var parts = Parts(path);
        var scored = candidates
            .Select(candidate => (candidate, score: SharedTail(parts, Parts(candidate))))
            .OrderByDescending(c => c.score)
            .ToList();
        var best = scored[0];
        var unique = scored.Count == 1 || scored[1].score < best.score;
        if (unique && best.score >= 2)
        {
            return best.candidate;
        }

        // Only the file name matches ("01 Track 1.mp3" is in every album), so check it's the same song.
        if (unique && track.Title is { } title && _tags(best.candidate) is { } tags
            && Normalize(tags.Title) == Normalize(title)
            && (track.Artist is null || ArtistsMatch(tags.Artist, track.Artist)))
        {
            return best.candidate;
        }

        return null;
    }

    private string? ByName(ImportedTrack track)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
        {
            return null;
        }

        BuildTagIndex();
        var title = Normalize(track.Title);
        if (title.Length == 0)
        {
            return null;
        }

        List<Track>? candidates = null;
        if (track.Artist is { } artist && _byArtistTitle!.TryGetValue(Key(artist, track.Title), out var exact))
        {
            candidates = exact;
        }
        else if (_byTitle!.TryGetValue(title, out var sameTitle))
        {
            // "Daft Punk" and "Daft Punk feat. Pharrell", or a source that doesn't know the artist.
            candidates = track.Artist is null
                ? sameTitle
                : sameTitle.Where(t => ArtistsMatch(t.Artist, track.Artist)).ToList();
        }

        if (candidates is not { Count: > 0 })
        {
            return null;
        }

        // Several (the album version and a live one, say): the same album, then the closest length.
        var best = candidates
            .OrderByDescending(t => track.Album is not null && Normalize(t.Album) == Normalize(track.Album))
            .ThenBy(t => track.Duration > TimeSpan.Zero && t.Duration > TimeSpan.Zero ? Math.Abs((t.Duration - track.Duration).TotalSeconds) : 0)
            .First();
        if (track.Duration > TimeSpan.Zero && best.Duration > TimeSpan.Zero && Math.Abs((best.Duration - track.Duration).TotalSeconds) > 15
            && candidates.Count > 1)
        {
            return null;
        }

        return best.Path;
    }

    private void BuildTagIndex()
    {
        if (_byArtistTitle is not null)
        {
            return;
        }

        var byArtistTitle = new Dictionary<string, List<Track>>(StringComparer.Ordinal);
        var byTitle = new Dictionary<string, List<Track>>(StringComparer.Ordinal);
        for (var i = 0; i < _files.Count; i++)
        {
            Cancel.ThrowIfCancellationRequested();
            if (i % 50 == 0)
            {
                TagProgress?.Report((i, _files.Count));
            }

            if (_tags(_files[i]) is not { } track)
            {
                continue;
            }

            Add(byArtistTitle, Key(track.Artist, track.Title), track);
            Add(byTitle, Normalize(track.Title), track);
        }

        TagProgress?.Report((_files.Count, _files.Count));
        _byArtistTitle = byArtistTitle;
        _byTitle = byTitle;

        static void Add(Dictionary<string, List<Track>> index, string key, Track track)
        {
            if (!index.TryGetValue(key, out var list))
            {
                index[key] = list = new List<Track>();
            }

            list.Add(track);
        }
    }

    private static string Key(string? artist, string? title) => NormalizeArtist(artist) + "\u0001" + Normalize(title);

    private static bool ArtistsMatch(string? a, string? b)
    {
        var x = NormalizeArtist(a);
        var y = NormalizeArtist(b);
        return x.Length > 0 && y.Length > 0 && (x == y || x.StartsWith(y + " ", StringComparison.Ordinal) || y.StartsWith(x + " ", StringComparison.Ordinal));
    }

    /// <summary>
    /// A title for comparing: lower case, no accents or punctuation, and without the parts players and
    /// YouTube disagree on: "(Remastered 2011)", "[Official Video]", "feat. Someone".
    /// </summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var stripped = Brackets().Replace(text, " ");
        stripped = Featuring().Replace(stripped, " ");
        if (stripped.Trim().Length == 0)
        {
            // The whole title was in brackets.
            stripped = text;
        }

        var decomposed = stripped.Replace("&", " and ").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var space = true;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
                space = false;
            }
            else if (!space && c is not ('\'' or '’' or '.'))
            {
                builder.Append(' ');
                space = true;
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>An artist for comparing: as <see cref="Normalize"/>, without "The" in front.</summary>
    public static string NormalizeArtist(string? artist)
    {
        var name = Normalize(artist);
        return name.StartsWith("the ", StringComparison.Ordinal) ? name[4..] : name;
    }

    private static string FileName(string path) => Parts(path) is { Length: > 0 } parts ? parts[^1] : path;

    private static string[] Parts(string path) => path.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);

    private static int SharedTail(string[] a, string[] b)
    {
        var count = 0;
        while (count < a.Length && count < b.Length
            && string.Equals(a[a.Length - 1 - count], b[b.Length - 1 - count], StringComparison.OrdinalIgnoreCase))
        {
            count++;
        }

        return count;
    }

    [GeneratedRegex(@"\([^)]*\)|\[[^\]]*\]")]
    private static partial Regex Brackets();

    [GeneratedRegex(@"\s(feat|ft|featuring)\.?\s.*$", RegexOptions.IgnoreCase)]
    private static partial Regex Featuring();
}
