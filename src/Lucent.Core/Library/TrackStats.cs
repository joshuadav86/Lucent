using System.IO;
using System.Text.Json;
using Lucent.Settings;

namespace Lucent.Library;

/// <summary>
/// How often each song has been played, when it was last played, and its star rating, by file path.
/// Plays are counted when a song plays to its end. Plays brought over from other players are kept apart
/// from Lucent's own, so importing the same library again doesn't count them twice. Saved in
/// track-stats.json beside the settings.
/// </summary>
public sealed class TrackStats
{
    private static readonly Lazy<TrackStats> SharedStats = new(() => new TrackStats(Path.Combine(AppDataFolder.Path, "track-stats.json")));

    private readonly object _gate = new();
    private readonly string _path;
    private readonly Dictionary<string, Entry> _entries;

    public TrackStats(string path)
    {
        _path = path;
        _entries = Load(path);
    }

    /// <summary>The one the app uses.</summary>
    public static TrackStats Shared => SharedStats.Value;

    /// <summary>Raised (on whichever thread made the change) when plays or ratings change.</summary>
    public event EventHandler? Changed;

    public sealed class Entry
    {
        /// <summary>Plays in Lucent.</summary>
        public int Plays { get; set; }

        /// <summary>Plays brought over from other players: the most any one import found.</summary>
        public int ImportedPlays { get; set; }

        /// <summary>Stars, 1 to 5, or 0 for none.</summary>
        public int Rating { get; set; }

        /// <summary>In UTC.</summary>
        public DateTime? LastPlayed { get; set; }
    }

    public int PlaysOf(string path)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(path, out var entry) ? entry.Plays + entry.ImportedPlays : 0;
        }
    }

    public int RatingOf(string path)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(path, out var entry) ? entry.Rating : 0;
        }
    }

    public DateTime? LastPlayedOf(string path)
    {
        lock (_gate)
        {
            return _entries.TryGetValue(path, out var entry) ? entry.LastPlayed : null;
        }
    }

    /// <summary>Counts a play of a song that played to its end.</summary>
    public void CountPlay(string path)
    {
        lock (_gate)
        {
            var entry = EntryFor(path);
            entry.Plays++;
            entry.LastPlayed = DateTime.UtcNow;
        }

        Save();
    }

    /// <summary>Gives songs a rating, 1 to 5 stars, or takes it off with 0.</summary>
    public void SetRating(IEnumerable<string> paths, int stars)
    {
        stars = Math.Clamp(stars, 0, 5);
        lock (_gate)
        {
            foreach (var path in paths)
            {
                EntryFor(path).Rating = stars;
            }
        }

        Save();
    }

    /// <summary>
    /// Brings over a song's plays and rating from another player. A rating given in Lucent stays; the
    /// imported plays are the most any import has found. True if anything changed. Call <see cref="Save"/> after.
    /// </summary>
    public bool MergeImported(string path, int plays, int rating, DateTime? lastPlayed)
    {
        lock (_gate)
        {
            var entry = EntryFor(path);
            var changed = false;
            if (plays > entry.ImportedPlays)
            {
                entry.ImportedPlays = plays;
                changed = true;
            }

            if (rating > 0 && entry.Rating == 0)
            {
                entry.Rating = Math.Clamp(rating, 1, 5);
                changed = true;
            }

            if (lastPlayed is { } played && (entry.LastPlayed is null || played > entry.LastPlayed))
            {
                entry.LastPlayed = played;
                changed = true;
            }

            return changed;
        }
    }

    /// <summary>
    /// Hands a removed copy's plays and rating to the copy that's kept: the plays are added together, the
    /// better rating and the later last play are kept, and the removed copy is forgotten. Call <see cref="Save"/> after.
    /// </summary>
    public void MoveInto(string from, string to)
    {
        lock (_gate)
        {
            if (!_entries.Remove(from, out var old))
            {
                return;
            }

            var entry = EntryFor(to);
            entry.Plays += old.Plays;
            entry.ImportedPlays += old.ImportedPlays;
            entry.Rating = Math.Max(entry.Rating, old.Rating);
            if (old.LastPlayed is { } played && (entry.LastPlayed is null || played > entry.LastPlayed))
            {
                entry.LastPlayed = played;
            }
        }
    }

    public void Save()
    {
        string json;
        lock (_gate)
        {
            // Songs with nothing to remember aren't kept.
            json = JsonSerializer.Serialize(_entries
                .Where(pair => pair.Value.Plays > 0 || pair.Value.ImportedPlays > 0 || pair.Value.Rating > 0 || pair.Value.LastPlayed is not null)
                .ToDictionary(pair => pair.Key, pair => pair.Value));
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
                var temporary = _path + ".saving";
                File.WriteAllText(temporary, json);
                File.Move(temporary, _path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept in memory; the next save tries again.
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private Entry EntryFor(string path)
    {
        if (!_entries.TryGetValue(path, out var entry))
        {
            entry = new Entry();
            _entries[path] = entry;
        }

        return entry;
    }

    private static Dictionary<string, Entry> Load(string path)
    {
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(path)) is { } saved)
            {
                return new Dictionary<string, Entry>(saved, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A damaged file starts the counts again rather than stopping the app.
        }

        return new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>"★★★★" for four stars, or "".</summary>
    public static string Stars(int rating) => rating > 0 ? new string('★', Math.Min(rating, 5)) : string.Empty;
}
