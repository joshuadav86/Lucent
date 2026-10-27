using System.ComponentModel;
using System.Globalization;
using System.Text;
using Lucent.Playlist;

namespace Lucent.Library;

/// <summary>Copies of one song found in the library: the one to keep first, then the rest.</summary>
public sealed class DuplicateGroup
{
    public DuplicateGroup(IReadOnlyList<LibraryItem> copies)
    {
        Copies = copies;
        Rows = copies.Select((item, index) => new DuplicateCopy(item, isBest: index == 0)).ToList();
    }

    /// <summary>Best first: see <see cref="DuplicateFinder.CompareCopies"/>.</summary>
    public IReadOnlyList<LibraryItem> Copies { get; }

    /// <summary>The copies as the duplicates window lists them, each with its tick for removing it.</summary>
    public IReadOnlyList<DuplicateCopy> Rows { get; }

    /// <summary>"Bicep  ·  Atlas", the group's heading.</summary>
    public string Heading => Artist.Length == 0 ? Title : $"{Artist}  ·  {Title}";

    /// <summary>The copy suggested for keeping.</summary>
    public LibraryItem Best => Copies[0];

    public string Title => Best.Title;

    public string Artist => Best.Artist;
}

/// <summary>One copy in the duplicates window: ticked to go to the Recycle Bin (or Trash). All but the best start ticked.</summary>
public sealed class DuplicateCopy : INotifyPropertyChanged
{
    private bool _remove;

    public DuplicateCopy(LibraryItem item, bool isBest)
    {
        Item = item;
        IsBest = isBest;
        _remove = !isBest;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public LibraryItem Item { get; }

    /// <summary>The copy suggested for keeping: the best quality, or else the most played.</summary>
    public bool IsBest { get; }

    public bool Remove
    {
        get => _remove;
        set
        {
            if (_remove != value)
            {
                _remove = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Remove)));
            }
        }
    }

    /// <summary>The file's name, with its extension.</summary>
    public string Name => Path.GetFileName(Item.Path);

    /// <summary>The folder it's in.</summary>
    public string Location => Path.GetDirectoryName(Item.Path) ?? string.Empty;

    /// <summary>"FLAC  ·  1,411 kbps  ·  44.1 kHz  ·  5:52  ·  ★★★★  ·  12 plays", leaving out what isn't known.</summary>
    public string Details
    {
        get
        {
            var parts = new List<string> { DuplicateFinder.QualityText(Item), Item.DurationText };
            if (Item.Rating > 0)
            {
                parts.Add(TrackStats.Stars(Item.Rating));
            }

            if (Item.Plays > 0)
            {
                parts.Add($"{Item.Plays:N0} {(Item.Plays == 1 ? "play" : "plays")}");
            }

            return string.Join("  ·  ", parts.Where(part => part.Length > 0));
        }
    }
}

/// <summary>
/// The ticked copies to remove, and for each the copy its plays, rating, favourite and places in saved playlists
/// go to: the best copy left in its group, or none when every copy is ticked.
/// </summary>
public sealed record DuplicateRemoval(IReadOnlyList<string> Remove, IReadOnlyDictionary<string, string> KeptCopy);

/// <summary>
/// Finds songs that are in the library more than once: the same artist and title (ignoring case, accents and
/// punctuation) and lengths within a few seconds of each other, so a live take or a long mix of the same name
/// isn't counted. Songs with no artist only match on a file name and length that line up just as closely.
/// The best copy of each comes first, as the one to keep.
/// </summary>
public static class DuplicateFinder
{
    /// <summary>How far apart two copies' lengths can be; encoders pad the ends of a song a little differently.</summary>
    public static readonly TimeSpan LengthTolerance = TimeSpan.FromSeconds(3);

    private static readonly HashSet<string> LosslessExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".flac", ".wav", ".aif", ".aiff", ".ape", ".wv", ".alac",
    };

    public static List<DuplicateGroup> Find(IEnumerable<LibraryItem> items)
    {
        var groups = new List<DuplicateGroup>();
        var songs = items
            .Where(item => !item.IsPlaceholder && item.RemotePlaylist is null && item.Track is { Duration.Ticks: > 0 })
            .DistinctBy(item => item.Path, StringComparer.OrdinalIgnoreCase);
        foreach (var sameName in songs.GroupBy(MatchKey).Where(group => group.Key.Length > 1 && group.Skip(1).Any()))
        {
            // Copies whose lengths chain together within the tolerance are one song.
            var byLength = sameName.OrderBy(item => item.Track!.Duration).ToList();
            var run = new List<LibraryItem> { byLength[0] };
            for (var i = 1; i <= byLength.Count; i++)
            {
                if (i < byLength.Count && byLength[i].Track!.Duration - run[^1].Track!.Duration <= LengthTolerance)
                {
                    run.Add(byLength[i]);
                    continue;
                }

                if (run.Count > 1)
                {
                    run.Sort(CompareCopies);
                    groups.Add(new DuplicateGroup(run));
                }

                if (i < byLength.Count)
                {
                    run = new List<LibraryItem> { byLength[i] };
                }
            }
        }

        return groups
            .OrderBy(group => group.Artist.Length == 0)
            .ThenBy(group => group.Artist, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(group => group.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// "artist\ntitle", folded so that "The Beatles - Let It Be!" and "beatles: let it be" match. A song with no
    /// artist goes by its file name instead of its title, since an untagged title is just "Track 01".
    /// </summary>
    public static string MatchKey(LibraryItem item)
    {
        var artist = Fold(item.Artist);
        if (artist.StartsWith("the ", StringComparison.Ordinal))
        {
            artist = artist[4..];
        }

        return artist.Length == 0 ? "\n" + Fold(item.FileName) : artist + "\n" + Fold(item.Title);
    }

    /// <summary>
    /// Better copies first: lossless before lossy, then the higher sample rate and bitrate, then the one with
    /// more plays and a rating, then the one with more of its tags filled in, then the one that was there first.
    /// </summary>
    public static int CompareCopies(LibraryItem a, LibraryItem b)
    {
        var aTrack = a.Track!;
        var bTrack = b.Track!;
        var result = IsLossless(b).CompareTo(IsLossless(a));
        if (result == 0)
        {
            result = IsLossless(a)
                ? FirstOf(bTrack.SampleRate.CompareTo(aTrack.SampleRate), bTrack.Bitrate.CompareTo(aTrack.Bitrate))
                : FirstOf(bTrack.Bitrate.CompareTo(aTrack.Bitrate), bTrack.SampleRate.CompareTo(aTrack.SampleRate));
        }

        if (result == 0)
        {
            result = (b.Plays + b.Rating).CompareTo(a.Plays + a.Rating);
        }

        if (result == 0)
        {
            result = TagsFilledIn(b).CompareTo(TagsFilledIn(a));
        }

        if (result == 0)
        {
            result = a.Added.CompareTo(b.Added);
        }

        return result != 0 ? result : string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A lossless file: FLAC, WAV, AIFF and the like, or an M4A at a lossless bitrate (Apple Lossless; AAC
    /// never comes near 700 kbps).
    /// </summary>
    public static bool IsLossless(LibraryItem item)
    {
        var extension = Path.GetExtension(item.Path);
        return LosslessExtensions.Contains(extension)
            || (extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) && item.Track?.Bitrate >= 700);
    }

    /// <summary>What the ticks in the duplicates window ask for.</summary>
    public static DuplicateRemoval Plan(IEnumerable<DuplicateGroup> groups)
    {
        var remove = new List<string>();
        var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            var keep = group.Rows.FirstOrDefault(row => !row.Remove)?.Item.Path;
            foreach (var row in group.Rows.Where(row => row.Remove))
            {
                remove.Add(row.Item.Path);
                if (keep is not null)
                {
                    kept[row.Item.Path] = keep;
                }
            }
        }

        return new DuplicateRemoval(remove, kept);
    }

    /// <summary>
    /// After the copies have gone: hands each removed copy's plays and rating to the copy kept in its place, and
    /// points saved playlists at it. Returns the kept copies that become favourites because a removed copy was one.
    /// </summary>
    public static List<string> CarryOver(DuplicateRemoval removal, IEnumerable<string> removed, IReadOnlyCollection<string> favorites)
    {
        var favoriteSet = new HashSet<string>(favorites, StringComparer.OrdinalIgnoreCase);
        var moves = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var newFavorites = new List<string>();
        foreach (var path in removed)
        {
            if (!removal.KeptCopy.TryGetValue(path, out var keep))
            {
                continue;
            }

            moves[path] = keep;
            TrackStats.Shared.MoveInto(path, keep);
            if (favoriteSet.Contains(path) && !favoriteSet.Contains(keep))
            {
                favoriteSet.Add(keep);
                newFavorites.Add(keep);
            }
        }

        TrackStats.Shared.Save();
        SavedPlaylists.ReplacePaths(moves);
        return newFavorites;
    }

    /// <summary>"FLAC  ·  1,411 kbps  ·  44.1 kHz", leaving out what isn't known.</summary>
    public static string QualityText(LibraryItem item)
    {
        var parts = new List<string> { LibraryColumns.FormatOf(item) };
        if (item.Track is { Bitrate: > 0 } track)
        {
            parts.Add($"{track.Bitrate:N0} kbps");
        }

        if (item.Track is { SampleRate: > 0 } rate)
        {
            parts.Add($"{rate.SampleRate / 1000.0:0.#} kHz");
        }

        return string.Join("  ·  ", parts.Where(part => part.Length > 0));
    }

    private static int FirstOf(int first, int second) => first != 0 ? first : second;

    private static int TagsFilledIn(LibraryItem item)
    {
        var tags = item.Track!.Tags;
        return new[] { item.Album, tags.AlbumArtist, tags.Genre, tags.Key }.Count(text => text.Length > 0)
            + new[] { tags.Year, tags.TrackNumber, tags.Bpm }.Count(number => number > 0);
    }

    /// <summary>Lower case, without accents, with every run of punctuation and spaces made one space.</summary>
    private static string Fold(string text)
    {
        var folded = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (char.IsLetterOrDigit(c))
            {
                folded.Append(char.ToLowerInvariant(c));
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && folded.Length > 0 && folded[^1] != ' ')
            {
                folded.Append(' ');
            }
        }

        return folded.ToString().TrimEnd();
    }
}
