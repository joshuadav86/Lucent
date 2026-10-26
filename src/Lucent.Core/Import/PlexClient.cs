using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Xml.Linq;

namespace Lucent.Import;

/// <summary>
/// Brings the music library over from a Plex Media Server: every track with its play count, rating and
/// file, and the music playlists. Signing in uses Plex's own page in the browser (a PIN link, the way
/// Plex's TV apps sign in), so Lucent never sees the password. The token lasts only while Lucent is open.
/// </summary>
public sealed class PlexClient : IDisposable
{
    private const string Product = "Lucent";
    private readonly HttpClient _http;
    private readonly string _clientId;

    public PlexClient(string clientId, HttpMessageHandler? handler = null)
    {
        _clientId = clientId;
        _http = new HttpClient(handler ?? new SocketsHttpHandler()) { Timeout = TimeSpan.FromSeconds(30) };
    }

    public string? Token { get; private set; }

    public string? ServerName { get; private set; }

    public string? ServerUrl { get; private set; }

    public bool IsSignedIn => Token is not null;

    public sealed record Pin(long Id, string Code, string SignInUrl);

    /// <summary>Starts signing in: the user opens the returned page, and <see cref="WaitForSignInAsync"/> waits for them.</summary>
    public async Task<Pin> StartSignInAsync(CancellationToken cancel)
    {
        using var request = Request(HttpMethod.Post, "https://plex.tv/api/v2/pins?strong=true", null);
        using var response = await _http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var id = json.RootElement.GetProperty("id").GetInt64();
        var code = json.RootElement.GetProperty("code").GetString() ?? throw new InvalidDataException("Plex didn't send a sign-in code.");
        var url = "https://app.plex.tv/auth#?clientID=" + Uri.EscapeDataString(_clientId)
            + "&code=" + Uri.EscapeDataString(code)
            + "&context%5Bdevice%5D%5Bproduct%5D=" + Product;
        return new Pin(id, code, url);
    }

    /// <summary>Waits until the user has signed in on Plex's page (or it's cancelled), then finds their server.</summary>
    public async Task WaitForSignInAsync(Pin pin, CancellationToken cancel)
    {
        var deadline = DateTime.UtcNow.AddMinutes(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancel);
            using var request = Request(HttpMethod.Get, $"https://plex.tv/api/v2/pins/{pin.Id}", null);
            using var response = await _http.SendAsync(request, cancel);
            if (!response.IsSuccessStatusCode)
            {
                continue;
            }

            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            if (json.RootElement.TryGetProperty("authToken", out var token) && token.GetString() is { Length: > 0 } value)
            {
                Token = value;
                await FindServerAsync(cancel);
                return;
            }
        }

        throw new TimeoutException("Signing in to Plex took too long. Try again.");
    }

    public void SignOut()
    {
        Token = null;
        ServerName = null;
        ServerUrl = null;
    }

    /// <summary>
    /// Finds the user's media server, trying this computer first, then each address Plex knows for it
    /// (on the home network first), and keeps the first that answers.
    /// </summary>
    private async Task FindServerAsync(CancellationToken cancel)
    {
        var candidates = new List<(string Name, string Url, string Token)>();
        try
        {
            using var request = Request(HttpMethod.Get, "https://plex.tv/api/v2/resources?includeHttps=1&includeRelay=1", Token);
            using var response = await _http.SendAsync(request, cancel);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
            foreach (var resource in json.RootElement.EnumerateArray())
            {
                var provides = resource.TryGetProperty("provides", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                if (!provides.Contains("server", StringComparison.Ordinal))
                {
                    continue;
                }

                var name = resource.TryGetProperty("name", out var n) ? n.GetString() ?? "Plex" : "Plex";
                var token = resource.TryGetProperty("accessToken", out var t) && t.GetString() is { Length: > 0 } access ? access : Token!;
                if (!resource.TryGetProperty("connections", out var connections))
                {
                    continue;
                }

                foreach (var connection in connections.EnumerateArray()
                    .OrderBy(c => c.TryGetProperty("relay", out var relay) && relay.GetBoolean())
                    .ThenBy(c => !(c.TryGetProperty("local", out var local) && local.GetBoolean())))
                {
                    if (connection.TryGetProperty("uri", out var uri) && uri.GetString() is { Length: > 0 } address)
                    {
                        candidates.Add((name, address, token));
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException or KeyNotFoundException)
        {
            // Still worth trying this computer.
        }

        candidates.Insert(0, ("this computer", "http://127.0.0.1:32400", Token!));
        foreach (var (name, url, token) in candidates)
        {
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(4));
                using var request = Request(HttpMethod.Get, url.TrimEnd('/') + "/identity", token);
                using var response = await _http.SendAsync(request, timeout.Token);
                if (response.IsSuccessStatusCode)
                {
                    ServerName = name;
                    ServerUrl = url.TrimEnd('/');
                    Token = token;
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancel.IsCancellationRequested)
            {
                // Try the next address.
            }
        }

        throw new HttpRequestException("Couldn't reach your Plex Media Server. Make sure it's running, then sign in again.");
    }

    /// <summary>Reads every music library on the server, and the music playlists.</summary>
    public async Task<ImportedLibrary> ReadLibraryAsync(IProgress<string>? progress, CancellationToken cancel)
    {
        if (ServerUrl is null || Token is null)
        {
            throw new InvalidOperationException("Sign in to Plex first.");
        }

        var library = new ImportedLibrary(ImportSource.Plex);
        var byKey = new Dictionary<string, ImportedTrack>(StringComparer.Ordinal);
        var sections = await GetXmlAsync("/library/sections", cancel);
        foreach (var section in sections.Root!.Elements("Directory").Where(d => (string?)d.Attribute("type") == "artist"))
        {
            var key = (string?)section.Attribute("key");
            progress?.Report($"Reading {(string?)section.Attribute("title") ?? "music"} from Plex…");
            await foreach (var element in PagedAsync($"/library/sections/{key}/all?type=10", cancel))
            {
                var track = ToTrack(element);
                library.Tracks.Add(track);
                if ((string?)element.Attribute("ratingKey") is { } ratingKey)
                {
                    byKey[ratingKey] = track;
                }
            }
        }

        progress?.Report("Reading playlists from Plex…");
        var playlists = await GetXmlAsync("/playlists?playlistType=audio", cancel);
        foreach (var playlist in playlists.Root!.Elements("Playlist"))
        {
            var title = (string?)playlist.Attribute("title");
            var key = (string?)playlist.Attribute("ratingKey");
            if (title is null || key is null)
            {
                continue;
            }

            var tracks = new List<ImportedTrack>();
            await foreach (var element in PagedAsync($"/playlists/{key}/items", cancel))
            {
                tracks.Add((string?)element.Attribute("ratingKey") is { } ratingKey && byKey.TryGetValue(ratingKey, out var known)
                    ? known
                    : ToTrack(element));
            }

            if (tracks.Count > 0)
            {
                library.Playlists.Add(new ImportedPlaylist(title, tracks));
            }
        }

        return library;
    }

    private static ImportedTrack ToTrack(XElement element)
    {
        var rating = double.TryParse((string?)element.Attribute("userRating"), NumberStyles.Float, CultureInfo.InvariantCulture, out var r) ? r : 0;
        var lastViewed = long.TryParse((string?)element.Attribute("lastViewedAt"), out var seconds) && seconds > 0
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : (DateTime?)null;
        return new ImportedTrack
        {
            Path = element.Elements("Media").Elements("Part").Select(part => (string?)part.Attribute("file")).FirstOrDefault(file => file is { Length: > 0 }),
            Title = (string?)element.Attribute("title"),

            // A track's own artist, when it differs from the album's, is its "original title".
            Artist = (string?)element.Attribute("originalTitle") ?? (string?)element.Attribute("grandparentTitle"),
            Album = (string?)element.Attribute("parentTitle"),
            Duration = long.TryParse((string?)element.Attribute("duration"), out var ms) ? TimeSpan.FromMilliseconds(ms) : TimeSpan.Zero,
            Plays = int.TryParse((string?)element.Attribute("viewCount"), out var plays) ? plays : 0,

            // Plex rates out of 10: two per star.
            Rating = rating <= 0 ? 0 : Math.Clamp((int)Math.Round(rating / 2, MidpointRounding.AwayFromZero), 1, 5),
            LastPlayed = lastViewed,
        };
    }

    /// <summary>The elements of a long list, fetched a page at a time.</summary>
    private async IAsyncEnumerable<XElement> PagedAsync(string path, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancel)
    {
        const int pageSize = 1000;
        for (var start = 0; ; start += pageSize)
        {
            var page = await GetXmlAsync(path, cancel, start, pageSize);
            var items = page.Root!.Elements().Where(e => e.Name.LocalName is "Track" or "Metadata").ToList();
            foreach (var item in items)
            {
                yield return item;
            }

            var total = int.TryParse((string?)page.Root.Attribute("totalSize"), out var size) ? size : -1;
            if (items.Count < pageSize || (total >= 0 && start + items.Count >= total))
            {
                yield break;
            }
        }
    }

    private async Task<XDocument> GetXmlAsync(string path, CancellationToken cancel, int start = 0, int size = 0)
    {
        using var request = Request(HttpMethod.Get, ServerUrl + path, Token);
        request.Headers.Accept.ParseAdd("application/xml");
        if (size > 0)
        {
            request.Headers.Add("X-Plex-Container-Start", start.ToString(CultureInfo.InvariantCulture));
            request.Headers.Add("X-Plex-Container-Size", size.ToString(CultureInfo.InvariantCulture));
        }

        using var response = await _http.SendAsync(request, cancel);
        response.EnsureSuccessStatusCode();
        return XDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    private HttpRequestMessage Request(HttpMethod method, string url, string? token)
    {
        var request = new HttpRequestMessage(method, url);
        if (!url.Contains("/library/") && !url.Contains("/playlists") && !url.EndsWith("/identity", StringComparison.Ordinal))
        {
            request.Headers.Accept.ParseAdd("application/json");
        }

        request.Headers.Add("X-Plex-Product", Product);
        request.Headers.Add("X-Plex-Client-Identifier", _clientId);
        if (token is not null)
        {
            request.Headers.Add("X-Plex-Token", token);
        }

        return request;
    }

    public void Dispose() => _http.Dispose();
}
