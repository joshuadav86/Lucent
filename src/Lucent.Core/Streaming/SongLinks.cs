using System.Globalization;
using System.Text.Json;

namespace Lucent.Streaming;

/// <summary>
/// A web link to share another app's song by. The app's own when it knows one (Spotify's, a YouTube video's, a TIDAL or
/// SoundCloud page's: <see cref="IRemoteLinks"/>); otherwise the song is looked up on Apple Music through Apple's public
/// search (no sign-in), and shared as that Apple Music link from Apple Music, or as its song.link page from any other
/// app, which opens it in whichever app the person it's sent to uses.
/// </summary>
public static class SongLinks
{
    private const string SearchUrl = "https://itunes.apple.com/search";
    private const string SongLinkApi = "https://api.song.link/v1-alpha.1/links";

    // Songs already looked up, so sharing one twice asks only once (song.link allows ten lookups a minute).
    private static readonly Dictionary<string, string?> Found = new();

    /// <summary>The song's link, or null when the app doesn't know one and Apple Music doesn't have the song.</summary>
    public static async Task<string?> FindAsync(HttpClient http, IRemotePlayer player, RemoteTrack track, CancellationToken cancel)
    {
        if (player is IRemoteLinks links && links.WebLink(track) is { } own)
        {
            return own;
        }

        var key = $"{player.Name}|{track.Artist}|{track.Title}";
        lock (Found)
        {
            if (Found.TryGetValue(key, out var known))
            {
                return known;
            }
        }

        string? link = null;
        if (await AppleMusicLinkAsync(http, track, cancel) is { } apple)
        {
            link = player.Name == "Apple Music" ? apple : await SongLinkPageAsync(http, apple, cancel) ?? apple;
        }

        lock (Found)
        {
            if (Found.Count > 200)
            {
                Found.Clear();
            }

            Found[key] = link;
        }

        return link;
    }

    // Apple's search, in this computer's country's store, for the song by its title and artist.
    private static async Task<string?> AppleMusicLinkAsync(HttpClient http, RemoteTrack track, CancellationToken cancel)
    {
        if (string.IsNullOrWhiteSpace(track.Title))
        {
            return null;
        }

        var country = RegionInfo.CurrentRegion.TwoLetterISORegionName is { Length: 2 } region ? region : "US";
        var term = Uri.EscapeDataString($"{track.Artist} {track.Title}".Trim());
        try
        {
            using var response = await http.GetAsync($"{SearchUrl}?term={term}&media=music&entity=song&limit=10&country={country}", cancel);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            return PickResult(json.RootElement, track);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    /// <summary>
    /// The link of the first search result with the song's title and one of its artists, or null when none has both, so a
    /// different song is never shared in its place.
    /// </summary>
    internal static string? PickResult(JsonElement root, RemoteTrack track)
    {
        if (!root.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var title = Plain(track.Title);
        if (title.Length == 0)
        {
            return null;
        }

        var artists = track.Artist.Split(new[] { ",", "&", " feat. ", " ft. ", " x " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Plain)
            .Where(artist => artist.Length > 0)
            .ToList();
        foreach (var result in results.EnumerateArray())
        {
            var name = Plain(Text(result, "trackName"));
            var by = Plain(Text(result, "artistName"));
            var sameTitle = name.Length > 0 && (name == title || name.StartsWith(title, StringComparison.Ordinal) || title.StartsWith(name, StringComparison.Ordinal));
            var sameArtist = artists.Count == 0 || artists.Any(artist => by.Contains(artist, StringComparison.Ordinal));
            if (sameTitle && sameArtist && Text(result, "trackViewUrl") is { Length: > 0 } url)
            {
                // Apple adds a tracking parameter (uo=4) that a shared link doesn't need.
                return url.Replace("&uo=4", string.Empty, StringComparison.Ordinal);
            }
        }

        return null;
    }

    // song.link's page for the song, which lists it on every service it's on.
    private static async Task<string?> SongLinkPageAsync(HttpClient http, string appleLink, CancellationToken cancel)
    {
        try
        {
            using var response = await http.GetAsync($"{SongLinkApi}?url={Uri.EscapeDataString(appleLink)}", cancel);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            return Text(json.RootElement, "pageUrl") is { Length: > 0 } page ? page : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !cancel.IsCancellationRequested)
        {
            return null;
        }
    }

    // Lower case, without what's in brackets ("(Remastered 2011)", "[Official Video]") or anything but letters and digits.
    private static string Plain(string text)
    {
        var plain = new System.Text.StringBuilder();
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
            else if (depth == 0 && char.IsLetterOrDigit(c))
            {
                plain.Append(char.ToLowerInvariant(c));
            }
        }

        return plain.ToString();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
}
