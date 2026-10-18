using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WinampInspiredVisualizer.Import;

/// <summary>
/// Reads the YouTube Music playlists in a Google Takeout download (takeout.google.com, "YouTube and
/// YouTube Music"). Each playlist is a CSV of video ids; the songs in the user's YouTube Music library
/// are listed with their titles and artists in another CSV. Songs the CSVs don't name are looked up on
/// YouTube by id for their title. Only names are used, to find the songs in the user's own library:
/// nothing is downloaded from YouTube.
/// </summary>
public static class TakeoutReader
{
    /// <summary>A song's title and artist found for a video id, or null.</summary>
    public delegate Task<(string? Title, string? Artist)?> TitleLookup(string videoId, CancellationToken cancel);

    public static async Task<ImportedLibrary> ReadAsync(string folder, TitleLookup? lookup, CancellationToken cancel = default)
    {
        var library = new ImportedLibrary(ImportSource.YouTubeMusic);
        var csvs = Directory.EnumerateFiles(folder, "*.csv", SearchOption.AllDirectories).ToList();
        if (csvs.Count == 0)
        {
            throw new InvalidDataException("There are no YouTube Music playlists in this folder. Choose the unzipped Takeout folder.");
        }

        // Song details first, so the playlists can be named from them.
        var songs = new Dictionary<string, (string? Title, string? Artist, string? Album)>(StringComparer.Ordinal);
        var playlists = new List<(string Name, List<string> Ids)>();
        foreach (var csv in csvs)
        {
            cancel.ThrowIfCancellationRequested();
            var rows = Csv.Parse(ReadText(csv));
            var headerRow = rows.FindIndex(row => row.Any(IsVideoIdHeader));
            if (headerRow < 0)
            {
                continue;
            }

            var header = rows[headerRow].Select(cell => cell.Trim().ToLowerInvariant()).ToList();
            var idColumn = header.FindIndex(IsVideoIdHeader);
            var titleColumn = header.FindIndex(h => h.Contains("song title") || h is "title" or "song name" or "video title");
            var artistColumn = header.FindIndex(h => h.StartsWith("artist name") || h is "artist" or "artists" or "artist names");
            var albumColumn = header.FindIndex(h => h.Contains("album title") || h is "album");
            var data = rows.Skip(headerRow + 1).Where(row => row.Count > idColumn && row[idColumn].Trim().Length > 0).ToList();

            if (titleColumn >= 0)
            {
                foreach (var row in data)
                {
                    songs[row[idColumn].Trim()] = (Cell(row, titleColumn), Cell(row, artistColumn), Cell(row, albumColumn));
                }
            }

            // Library song lists aren't playlists; every CSV under "playlists" is one.
            var inPlaylists = Path.GetDirectoryName(csv)?.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(part => part.Contains("playlist", StringComparison.OrdinalIgnoreCase)) == true;
            if (inPlaylists && !Path.GetFileName(csv).Equals("playlists.csv", StringComparison.OrdinalIgnoreCase))
            {
                playlists.Add((PlaylistName(csv), data.Select(row => row[idColumn].Trim()).ToList()));
            }
        }

        if (playlists.Count == 0 && songs.Count == 0)
        {
            throw new InvalidDataException("There are no YouTube Music playlists in this folder. Choose the unzipped Takeout folder.");
        }

        // Look up the songs the CSVs only give an id for, a few at a time.
        if (lookup is not null)
        {
            var unknown = playlists.SelectMany(p => p.Ids).Distinct().Where(id => !songs.ContainsKey(id)).ToList();
            using var gate = new SemaphoreSlim(4);
            var found = await Task.WhenAll(unknown.Select(async id =>
            {
                await gate.WaitAsync(cancel);
                try
                {
                    return (id, info: await lookup(id, cancel));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException && !cancel.IsCancellationRequested)
                {
                    return (id, info: ((string? Title, string? Artist)?)null);
                }
                finally
                {
                    gate.Release();
                }
            }));
            foreach (var (id, info) in found)
            {
                if (info is { } song)
                {
                    songs[id] = (song.Title, song.Artist, null);
                }
            }
        }

        var tracks = new Dictionary<string, ImportedTrack>(StringComparer.Ordinal);
        ImportedTrack TrackFor(string id, bool loved)
        {
            if (!tracks.TryGetValue(id, out var track))
            {
                var (title, artist, album) = songs.GetValueOrDefault(id);
                track = new ImportedTrack { Title = title ?? $"YouTube video {id}", Artist = artist, Album = album };
                tracks[id] = track;
                library.Tracks.Add(track);
            }

            if (loved && !track.Loved)
            {
                var index = library.Tracks.IndexOf(track);
                track = track with { Loved = true };
                tracks[id] = track;
                library.Tracks[index] = track;
            }

            return track;
        }

        foreach (var (name, ids) in playlists)
        {
            // Songs liked in YouTube Music become favorites.
            var loved = name.Equals("Liked music", StringComparison.OrdinalIgnoreCase);
            var list = ids.Select(id => TrackFor(id, loved)).ToList();
            if (list.Count > 0)
            {
                library.Playlists.Add(new ImportedPlaylist(name, list));
            }
        }

        return library;
    }

    /// <summary>
    /// Finds a video's title through YouTube's public oEmbed page, which needs no account or key. Music
    /// videos are usually titled "Artist - Song"; YouTube Music's own uploads put the artist in the
    /// channel name, "Artist - Topic".
    /// </summary>
    public static TitleLookup OEmbedLookup(HttpClient http) => async (id, cancel) =>
    {
        var url = "https://www.youtube.com/oembed?format=json&url=" + Uri.EscapeDataString("https://www.youtube.com/watch?v=" + id);
        using var response = await http.GetAsync(url, cancel);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            // Private or deleted.
            return null;
        }

        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var title = json.RootElement.TryGetProperty("title", out var t) ? t.GetString() : null;
        var channel = json.RootElement.TryGetProperty("author_name", out var a) ? a.GetString() : null;
        return SongFromVideo(title, channel);
    };

    /// <summary>"Artist - Song (Official Video)" by "ArtistVEVO", as a title and artist.</summary>
    public static (string? Title, string? Artist) SongFromVideo(string? title, string? channel)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return (null, null);
        }

        var (artist, song) = PlaylistFileReader.SplitArtist(title);
        if (artist is not null)
        {
            return (song, artist);
        }

        channel = channel?.Trim();
        if (channel is not null)
        {
            if (channel.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase))
            {
                channel = channel[..^" - Topic".Length];
            }
            else if (channel.EndsWith("VEVO", StringComparison.Ordinal) && channel.Length > 4)
            {
                channel = channel[..^4];
            }
        }

        return (title.Trim(), string.IsNullOrWhiteSpace(channel) ? null : channel.Trim());
    }

    private static bool IsVideoIdHeader(string cell) =>
        cell.Trim().Replace(" ", string.Empty).Equals("videoid", StringComparison.OrdinalIgnoreCase);

    private static string? Cell(List<string> row, int column) =>
        column >= 0 && column < row.Count && row[column].Trim() is { Length: > 0 } value ? value : null;

    /// <summary>"Road trip-videos.csv" (newer Takeouts) or "Road trip.csv" (older ones) is "Road trip".</summary>
    private static string PlaylistName(string csv)
    {
        var name = Path.GetFileNameWithoutExtension(csv);
        return name.EndsWith("-videos", StringComparison.OrdinalIgnoreCase) ? name[..^"-videos".Length] : name;
    }

    private static string ReadText(string path) => File.ReadAllText(path, Encoding.UTF8).TrimStart('﻿');
}

/// <summary>A small CSV reader: commas, quoted cells with "" for a quote, and newlines inside quotes.</summary>
public static class Csv
{
    public static List<List<string>> Parse(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(cell.ToString());
                cell.Clear();
                if (row.Count > 1 || row[0].Length > 0)
                {
                    rows.Add(row);
                }

                row = new List<string>();
            }
            else
            {
                cell.Append(c);
            }
        }

        row.Add(cell.ToString());
        if (row.Count > 1 || row[0].Length > 0)
        {
            rows.Add(row);
        }

        return rows;
    }
}
