using System.Net.Http;
using System.Text.Json;

namespace Lucent.Radio;

/// <summary>
/// Pictures for the artwork window while a station plays: the song's cover, found by its title in the iTunes Store's
/// search, and the station's logo, from the directory or, for SomaFM's channels, SomaFM's own channel art.
/// </summary>
/// <remarks>
/// Neither needs a key or an account. What's found is kept for the session, so a song or station that comes round
/// again isn't looked up twice. A lookup that fails gives null: the artwork window then shows the next best thing.
/// </remarks>
public sealed class StationArt
{
    /// <summary>Larger pictures than this aren't taken: no cover or logo needs to be.</summary>
    private const int MaxBytes = 4_000_000;

    /// <summary>How many songs' covers are kept.</summary>
    private const int KeptSongs = 64;

    private readonly HttpClient _http;
    private readonly Dictionary<string, byte[]?> _songs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _songOrder = new();
    private readonly Dictionary<string, byte[]?> _logos = new(StringComparer.OrdinalIgnoreCase);
    private Task<Dictionary<string, string>?>? _somaChannels;
    private readonly int _coverSize;

    /// <param name="coverSize">How many pixels square a song's cover is fetched at.</param>
    public StationArt(HttpClient http, int coverSize = 600)
    {
        _http = http;
        _coverSize = coverSize;
    }

    /// <summary>
    /// The cover of the song a station says is on, or null when none is found. <paramref name="artist"/> may be empty,
    /// for stations that send only a title. Given the <paramref name="album"/>, the song's copy on that album is
    /// preferred to a single or a compilation.
    /// </summary>
    public async Task<byte[]?> SongCoverAsync(string artist, string title, CancellationToken cancel, string? album = null)
    {
        var term = SearchTerm(artist, title);
        if (term is null)
        {
            return null;
        }

        var key = album is { Length: > 0 } ? $"{term}\n{album}" : term;
        lock (_songs)
        {
            if (_songs.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        byte[]? cover = null;
        try
        {
            var address = $"https://itunes.apple.com/search?media=music&entity=song&limit=10&term={Uri.EscapeDataString(term)}";
            using var response = await _http.GetAsync(address, cancel);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancel);
                if (BestCover(json, artist, title, album, _coverSize) is { } picture)
                {
                    cover = await DownloadAsync(picture, cancel);
                }
            }
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            // Not found this time; it's tried again when the song comes round again.
            return null;
        }

        lock (_songs)
        {
            if (_songs.TryAdd(key, cover))
            {
                _songOrder.Enqueue(key);
                while (_songOrder.Count > KeptSongs)
                {
                    _songs.Remove(_songOrder.Dequeue());
                }
            }
        }

        return cover;
    }

    /// <summary>The station's logo: the one the directory lists, or SomaFM's channel art for a SomaFM stream. Null when it has none.</summary>
    public async Task<byte[]?> LogoAsync(RadioStation station, CancellationToken cancel)
    {
        lock (_logos)
        {
            if (_logos.TryGetValue(station.Id, out var known))
            {
                return known;
            }
        }

        byte[]? logo = null;
        try
        {
            // SomaFM's own art first for its channels: the directory's is often a small favicon, or missing.
            if (SomaChannel(station) is { } channel && await SomaChannelArtAsync(channel, cancel) is { } art)
            {
                logo = await DownloadAsync(art, cancel);
            }

            if (logo is null && station.Logo is { } listed && RadioGuide.IsStreamAddress(listed))
            {
                logo = await DownloadAsync(listed.Trim(), cancel);
            }
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            return null;
        }

        lock (_logos)
        {
            _logos[station.Id] = logo;
        }

        return logo;
    }

    /// <summary>"Artist Title" to search for, with what stations add that isn't part of the song taken off; null when there's nothing to go on.</summary>
    internal static string? SearchTerm(string artist, string title)
    {
        title = Tidy(title);
        artist = Tidy(artist);
        if (title.Length == 0)
        {
            return null;
        }

        return artist.Length > 0 ? $"{artist} {title}" : title;
    }

    // Takes off "(Radio Edit)", "[Live]" and the like, which the store often writes differently.
    private static string Tidy(string text)
    {
        var tidy = new System.Text.StringBuilder();
        var depth = 0;
        foreach (var c in text)
        {
            if (c is '(' or '[')
            {
                depth++;
            }
            else if (c is ')' or ']')
            {
                depth = Math.Max(0, depth - 1);
            }
            else if (depth == 0)
            {
                tidy.Append(c);
            }
        }

        return string.Join(' ', tidy.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// The address of the best cover in an iTunes search's answer, made <paramref name="size"/> pixels square: the first
    /// song whose artist matches, or the first song when the station gave no artist, and of those the first on
    /// <paramref name="album"/> when it's given. Null when nothing fits.
    /// </summary>
    internal static string? BestCover(string json, string artist, string title, string? album = null, int size = 600)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var wantedArtist = Simple(Tidy(artist));
        var wantedTitle = Simple(Tidy(title));
        var wantedAlbum = Simple(Tidy(album ?? string.Empty));
        string? fits = null;
        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("artworkUrl100", out var art) || art.GetString() is not { Length: > 0 } picture)
            {
                continue;
            }

            var foundArtist = result.TryGetProperty("artistName", out var name) ? Simple(name.GetString() ?? string.Empty) : string.Empty;
            var foundTitle = result.TryGetProperty("trackName", out var track) ? Simple(Tidy(track.GetString() ?? string.Empty)) : string.Empty;
            var artistFits = wantedArtist.Length == 0 || foundArtist.Contains(wantedArtist) || wantedArtist.Contains(foundArtist);
            var titleFits = foundTitle.Contains(wantedTitle) || wantedTitle.Contains(foundTitle);
            if (artistFits && titleFits && foundTitle.Length > 0)
            {
                var sized = picture.Replace("100x100bb", $"{size}x{size}bb");
                var foundAlbum = result.TryGetProperty("collectionName", out var collection) ? Simple(Tidy(collection.GetString() ?? string.Empty)) : string.Empty;
                if (wantedAlbum.Length == 0 || foundAlbum == wantedAlbum)
                {
                    return sized;
                }

                fits ??= sized;
            }
        }

        return fits;
    }

    // Letters and digits only, lowercase, so "Bonobo & Nicole Miglis" finds "Bonobo and Nicole Miglis" closely enough.
    private static string Simple(string text) =>
        new(text.ToLowerInvariant().Replace(" and ", " ").Where(char.IsLetterOrDigit).ToArray());

    /// <summary>The SomaFM channel a station streams ("groovesalad"), or null when it isn't SomaFM's.</summary>
    internal static string? SomaChannel(RadioStation station)
    {
        if (station.Id.StartsWith("somafm:", StringComparison.OrdinalIgnoreCase))
        {
            return station.Id["somafm:".Length..];
        }

        if (!Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("somafm.com", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // ice1.somafm.com/groovesalad-256-mp3, somafm.com/groovesalad130.pls, hls.somafm.com/hls/groovesalad/FLAC/program.m3u8
        var parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var part = parts.Length > 1 && parts[0].Equals("hls", StringComparison.OrdinalIgnoreCase) ? parts[1] : parts.FirstOrDefault();
        if (part is null)
        {
            return null;
        }

        var name = new string(part.TakeWhile(char.IsLetter).ToArray()).ToLowerInvariant();
        return name.Length > 0 ? name : null;
    }

    // SomaFM's list of channels, read once: each channel's id and its largest picture.
    private async Task<string?> SomaChannelArtAsync(string channel, CancellationToken cancel)
    {
        var task = _somaChannels;
        if (task is null || (task.IsCompleted && task.Result is null))
        {
            task = _somaChannels = ReadSomaChannelsAsync();
        }

        var channels = await task.WaitAsync(cancel);
        return channels is not null && channels.TryGetValue(channel, out var art) ? art : null;
    }

    private async Task<Dictionary<string, string>?> ReadSomaChannelsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync("https://api.somafm.com/channels.json");
            return SomaChannelArt(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Each channel's largest picture, from SomaFM's channels.json.</summary>
    internal static Dictionary<string, string> SomaChannelArt(string json)
    {
        var art = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array)
        {
            return art;
        }

        foreach (var channel in channels.EnumerateArray())
        {
            if (!channel.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } name)
            {
                continue;
            }

            foreach (var size in new[] { "xlimage", "largeimage", "image" })
            {
                if (channel.TryGetProperty(size, out var picture) && picture.GetString() is { Length: > 0 } address && RadioGuide.IsStreamAddress(address))
                {
                    art[name] = address;
                    break;
                }
            }
        }

        return art;
    }

    /// <summary>A picture at an address (a show's, say), or null when it can't be had or is too large.</summary>
    public Task<byte[]?> PictureAsync(string address, CancellationToken cancel) => DownloadAsync(address, cancel);

    private async Task<byte[]?> DownloadAsync(string address, CancellationToken cancel)
    {
        using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        return bytes.Length is > 0 and <= MaxBytes ? bytes : null;
    }
}
