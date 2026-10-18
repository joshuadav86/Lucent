using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Spotify, through its Web API: what's playing on whichever device the account is playing on (the
/// Spotify app on this computer, a phone, a speaker), and its play, pause, skip and seek. Spotify only
/// takes those buttons from Premium accounts; on a free account Lucent can still show what's playing.
/// </summary>
public sealed class SpotifyRemote : IRemotePlayer
{
    private const string Api = "https://api.spotify.com/v1/me/player";

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly Action<string> _saveRefreshToken;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string _refreshToken;
    private SpotifySignIn.Tokens? _tokens;
    private DateTime _waitUntil;

    /// <param name="refreshToken">From <see cref="SpotifySignIn.SignInAsync"/>, or saved from before.</param>
    /// <param name="saveRefreshToken">Called when Spotify hands out a new refresh token, which replaces the saved one.</param>
    public SpotifyRemote(HttpClient http, string refreshToken, Action<string> saveRefreshToken)
    {
        _http = http;
        _clientId = SpotifySignIn.ClientId ?? throw new RemoteProblem("This copy of Lucent wasn't built with a Spotify app to sign in with.");
        _refreshToken = refreshToken;
        _saveRefreshToken = saveRefreshToken;
    }

    public string Name => "Spotify";

    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        using var response = await SendAsync(HttpMethod.Get, string.Empty, cancel);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            // Nothing playing on any of the account's devices.
            return null;
        }

        await ThrowIfFailedAsync(response, cancel);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        return ParseState(json.RootElement, DateTime.UtcNow);
    }

    public Task PlayAsync(CancellationToken cancel) => CommandAsync(HttpMethod.Put, "/play", cancel);

    public Task PauseAsync(CancellationToken cancel) => CommandAsync(HttpMethod.Put, "/pause", cancel);

    public Task NextAsync(CancellationToken cancel) => CommandAsync(HttpMethod.Post, "/next", cancel);

    public Task PreviousAsync(CancellationToken cancel) => CommandAsync(HttpMethod.Post, "/previous", cancel);

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        CommandAsync(HttpMethod.Put, $"/seek?position_ms={(long)Math.Max(0, position.TotalMilliseconds)}", cancel);

    public async Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) =>
        track.ArtworkUrl is { } url ? await _http.GetByteArrayAsync(url, cancel) : null;

    // The Web API's playback state (GET /me/player).
    private static RemoteState? ParseState(JsonElement root, DateTime readAt)
    {
        var playing = root.TryGetProperty("is_playing", out var isPlaying) && isPlaying.ValueKind == JsonValueKind.True;
        var position = root.TryGetProperty("progress_ms", out var progress) && progress.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(progress.GetInt64())
            : TimeSpan.Zero;
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
        {
            // An advert, or a device that's on but has nothing loaded.
            return new RemoteState(null, playing, position, readAt);
        }

        var title = Text(item, "name");
        var duration = item.TryGetProperty("duration_ms", out var length) && length.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(length.GetInt64())
            : TimeSpan.Zero;
        var artist = string.Empty;
        var album = string.Empty;
        string? artwork = null;
        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
        {
            artist = string.Join(", ", artists.EnumerateArray().Select(entry => Text(entry, "name")).Where(name => name.Length > 0));
        }

        if (item.TryGetProperty("album", out var albumJson) && albumJson.ValueKind == JsonValueKind.Object)
        {
            album = Text(albumJson, "name");
            artwork = LargestImage(albumJson);
        }
        else if (item.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
        {
            // A podcast episode: the show stands in for the artist and album.
            artist = Text(show, "publisher");
            album = Text(show, "name");
            artwork = LargestImage(show);
        }

        var id = item.TryGetProperty("id", out var idJson) && idJson.GetString() is { Length: > 0 } value ? value : $"{artist}|{title}";
        return new RemoteState(new RemoteTrack(id, title, artist, album, duration, artwork), playing, position, readAt);
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    // The biggest cover Spotify lists (they come largest first, 640 pixels, but that isn't promised).
    private static string? LargestImage(JsonElement owner)
    {
        if (!owner.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return images.EnumerateArray()
            .Select(image => (Url: Text(image, "url"), Width: image.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number ? width.GetInt32() : 0))
            .Where(image => image.Url.Length > 0)
            .OrderByDescending(image => image.Width)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    private async Task CommandAsync(HttpMethod method, string path, CancellationToken cancel)
    {
        using var response = await SendAsync(method, path, cancel);
        await ThrowIfFailedAsync(response, cancel);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancel)
    {
        if (DateTime.UtcNow < _waitUntil)
        {
            throw new RemoteProblem("Spotify asked Lucent to slow down. It'll carry on in a moment.");
        }

        for (var attempt = 0; ; attempt++)
        {
            var token = await AccessTokenAsync(forceRefresh: attempt > 0, cancel);
            using var request = new HttpRequestMessage(method, Api + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (method != HttpMethod.Get)
            {
                request.Content = new ByteArrayContent(Array.Empty<byte>());
            }

            var response = await _http.SendAsync(request, cancel);
            // A token taken back early: get a new one and try once more.
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _waitUntil = DateTime.UtcNow + wait;
            }

            return response;
        }
    }

    private async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancel);
        throw response.StatusCode switch
        {
            HttpStatusCode.Forbidden when body.Contains("PREMIUM_REQUIRED", StringComparison.Ordinal) =>
                new RemoteProblem("Spotify only lets other apps play, pause and skip with a Premium account."),
            HttpStatusCode.Forbidden =>
                new RemoteProblem("Spotify wouldn't let Lucent do that. If this Spotify app is still in development mode, your account needs adding to it on developer.spotify.com."),
            HttpStatusCode.NotFound =>
                new RemoteProblem("Start playing something in Spotify first, then Lucent can control it."),
            HttpStatusCode.TooManyRequests =>
                new RemoteProblem("Spotify asked Lucent to slow down. It'll carry on in a moment."),
            HttpStatusCode.Unauthorized =>
                new RemoteProblem("Spotify signed Lucent out. Sign in again from the Add music menu."),
            _ => new RemoteProblem($"Spotify had a problem ({(int)response.StatusCode})."),
        };
    }

    private async Task<string> AccessTokenAsync(bool forceRefresh, CancellationToken cancel)
    {
        await _tokenGate.WaitAsync(cancel);
        try
        {
            if (!forceRefresh && _tokens is { } current && DateTime.UtcNow < current.ExpiresAt)
            {
                return current.AccessToken;
            }

            var tokens = await SpotifySignIn.PostTokenAsync(_http, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken,
                ["client_id"] = _clientId,
            }, cancel);
            _tokens = tokens;
            // With PKCE, Spotify hands out a new refresh token each time and the old one stops working.
            if (tokens.RefreshToken is { Length: > 0 } refresh && refresh != _refreshToken)
            {
                _refreshToken = refresh;
                _saveRefreshToken(refresh);
            }

            return tokens.AccessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public void Dispose()
    {
        // Nothing to let go of: the HttpClient is the app's, and a request still on its way is cancelled by its caller.
    }
}
