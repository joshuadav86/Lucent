using System.Globalization;
using System.IO;
using System.Text;

namespace WinampInspiredVisualizer.Recording;

/// <summary>A track inside a longer recording, such as one song of a DJ set.</summary>
public sealed record TrackMark(TimeSpan Start, string Title);

/// <summary>
/// Where the songs start in a long recording, so the player's Next and Previous can skip between them.
/// They're read from a WAV file's cue points (as Lucent's recordings have, and Sound Forge, Audition,
/// REAPER and others write) or from a .cue sheet with the same name beside any music file.
/// </summary>
public static class TrackMarkers
{
    /// <summary>Recordings shorter than this aren't looked at: songs don't have tracks inside them.</summary>
    public static readonly TimeSpan MinimumLength = TimeSpan.FromMinutes(8);

    /// <summary>The tracks in a file, in order, or an empty list when it has none (or can't be read).</summary>
    public static IReadOnlyList<TrackMark> Read(string path)
    {
        try
        {
            var sheet = Path.ChangeExtension(path, ".cue");
            if (File.Exists(sheet))
            {
                var fromSheet = CueSheet.Read(sheet);
                if (fromSheet.Count > 0)
                {
                    return fromSheet;
                }
            }

            var extension = Path.GetExtension(path);
            if (extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) || extension.Equals(".wave", StringComparison.OrdinalIgnoreCase))
            {
                return ReadWav(path);
            }
        }
        catch (Exception)
        {
            // Unreadable markers just mean Next goes to the next song, as usual.
        }

        return Array.Empty<TrackMark>();
    }

    /// <summary>The start of the track after <paramref name="position"/>, or null in the last one.</summary>
    public static TimeSpan? Next(IReadOnlyList<TrackMark> marks, TimeSpan position) =>
        marks.Select(m => (TimeSpan?)m.Start).FirstOrDefault(start => start > position + TimeSpan.FromSeconds(0.5));

    /// <summary>
    /// Where Previous goes: the start of this track, or of the one before when it's only just started (as
    /// Previous does with songs). Null before the first marker.
    /// </summary>
    public static TimeSpan? Previous(IReadOnlyList<TrackMark> marks, TimeSpan position)
    {
        var earlier = marks.Where(m => m.Start <= position).ToList();
        if (earlier.Count == 0)
        {
            return null;
        }

        if (position - earlier[^1].Start > TimeSpan.FromSeconds(3))
        {
            return earlier[^1].Start;
        }

        // At the very start of the first song, Previous goes to the file before, as usual.
        return earlier.Count > 1 ? earlier[^2].Start : null;
    }

    /// <summary>The track playing at <paramref name="position"/>, or null before the first.</summary>
    public static TrackMark? At(IReadOnlyList<TrackMark> marks, TimeSpan position) => marks.LastOrDefault(m => m.Start <= position);

    // Walks the file's chunks for its format, cue points and their labels. RF64 files keep their real
    // sizes in a ds64 chunk, but the chunks after the data are found the same way.
    private static IReadOnlyList<TrackMark> ReadWav(string path)
    {
        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file, Encoding.ASCII);
        var riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (riff is not ("RIFF" or "RF64"))
        {
            return Array.Empty<TrackMark>();
        }

        reader.ReadUInt32();
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
        {
            return Array.Empty<TrackMark>();
        }

        var sampleRate = 0;
        long dataSize64 = -1;
        var cues = new List<(int Id, long Frame)>();
        var labels = new Dictionary<int, string>();
        while (file.Position + 8 <= file.Length)
        {
            var id = Encoding.ASCII.GetString(reader.ReadBytes(4));
            long size = reader.ReadUInt32();
            var start = file.Position;
            switch (id)
            {
                case "ds64":
                    reader.ReadInt64();
                    dataSize64 = reader.ReadInt64();
                    break;
                case "fmt ":
                    reader.ReadInt16();
                    reader.ReadInt16();
                    sampleRate = reader.ReadInt32();
                    break;
                case "data":
                    if (size == uint.MaxValue && dataSize64 >= 0)
                    {
                        size = dataSize64;
                    }

                    break;
                case "cue ":
                    var count = reader.ReadInt32();
                    for (var i = 0; i < count && file.Position + 24 <= start + size; i++)
                    {
                        var cueId = reader.ReadInt32();
                        reader.ReadUInt32();
                        reader.ReadBytes(12);
                        cues.Add((cueId, reader.ReadUInt32()));
                    }

                    break;
                case "LIST":
                    if (Encoding.ASCII.GetString(reader.ReadBytes(4)) == "adtl")
                    {
                        while (file.Position + 8 <= start + size)
                        {
                            var subId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                            var subSize = reader.ReadUInt32();
                            var subStart = file.Position;
                            if (subId is "labl" or "note" && subSize >= 4)
                            {
                                var cueId = reader.ReadInt32();
                                var text = Encoding.UTF8.GetString(reader.ReadBytes((int)subSize - 4)).TrimEnd('\0');
                                if (subId == "labl" || !labels.ContainsKey(cueId))
                                {
                                    labels[cueId] = text;
                                }
                            }

                            file.Position = subStart + subSize + subSize % 2;
                        }
                    }

                    break;
            }

            file.Position = start + size + size % 2;
        }

        if (sampleRate <= 0 || cues.Count == 0)
        {
            return Array.Empty<TrackMark>();
        }

        return cues.OrderBy(c => c.Frame)
            .Select((c, index) => new TrackMark(TimeSpan.FromSeconds((double)c.Frame / sampleRate),
                labels.TryGetValue(c.Id, out var label) && !string.IsNullOrWhiteSpace(label) ? label : $"Track {index + 1}"))
            .ToList();
    }
}

/// <summary>
/// .cue sheets: the plain-text track lists CD burners, foobar2000, Traktor, Mixxx and many players read,
/// saved beside a recording so its songs can be skipped between in other software too.
/// </summary>
public static class CueSheet
{
    /// <summary>Writes a sheet for one audio file. Times in a sheet are in CD frames (75 a second).</summary>
    public static void Write(string path, string audioPath, IReadOnlyList<TrackMark> tracks, string? title)
    {
        var text = new StringBuilder();
        text.AppendLine("REM COMMENT \"Recorded with Lucent\"");
        text.AppendLine(CultureInfo.InvariantCulture, $"REM DATE {DateTime.Now:yyyy}");
        if (!string.IsNullOrWhiteSpace(title))
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"TITLE \"{Quote(title)}\"");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"FILE \"{Quote(Path.GetFileName(audioPath))}\" WAVE");
        // A sheet's first track starts at the beginning, even when the first mark came later.
        var all = tracks.Count > 0 && tracks[0].Start > TimeSpan.Zero
            ? new[] { new TrackMark(TimeSpan.Zero, "Intro") }.Concat(tracks).ToList()
            : tracks.ToList();
        for (var i = 0; i < all.Count; i++)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"  TRACK {i + 1:00} AUDIO");
            text.AppendLine(CultureInfo.InvariantCulture, $"    TITLE \"{Quote(all[i].Title)}\"");
            var frames = (long)Math.Round(all[i].Start.TotalSeconds * 75);
            text.AppendLine(CultureInfo.InvariantCulture, $"    INDEX 01 {frames / 75 / 60:00}:{frames / 75 % 60:00}:{frames % 75:00}");
        }

        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
    }

    /// <summary>The tracks in a sheet (of its first file), in order.</summary>
    public static IReadOnlyList<TrackMark> Read(string path)
    {
        var tracks = new List<TrackMark>();
        string? title = null;
        var files = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.StartsWith("FILE ", StringComparison.OrdinalIgnoreCase) && ++files > 1)
            {
                break;
            }

            if (line.StartsWith("TRACK ", StringComparison.OrdinalIgnoreCase))
            {
                title = null;
            }
            else if (line.StartsWith("TITLE ", StringComparison.OrdinalIgnoreCase))
            {
                title = line[6..].Trim().Trim('"');
            }
            else if (line.StartsWith("INDEX 01 ", StringComparison.OrdinalIgnoreCase))
            {
                var parts = line[9..].Trim().Split(':');
                if (parts.Length == 3
                    && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var minutes)
                    && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
                    && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var frames))
                {
                    tracks.Add(new TrackMark(TimeSpan.FromSeconds(minutes * 60 + seconds + frames / 75.0),
                        string.IsNullOrWhiteSpace(title) ? $"Track {tracks.Count + 1}" : title));
                }
            }
        }

        return tracks.OrderBy(t => t.Start).ToList();
    }

    private static string Quote(string text) => text.Replace('"', '\'');
}
