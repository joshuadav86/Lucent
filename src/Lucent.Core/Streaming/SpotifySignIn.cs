using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Signs in to Spotify with OAuth's PKCE flow, which needs no app secret: the browser opens Spotify's
/// sign-in page, and Spotify sends the browser back to a little web server Lucent runs on this computer
/// only (127.0.0.1), which takes the answer and swaps it for the tokens.
/// </summary>
/// <remarks>
/// Lucent's Spotify app (developer.spotify.com/dashboard) has to list <see cref="RedirectUri"/> exactly as
/// its redirect URI. Its client ID isn't a secret, but it's kept out of the code anyway: the build puts it
/// in (the SPOTIFY_CLIENT_ID repository secret, as the SpotifyClientId property), and the
/// LUCENT_SPOTIFY_CLIENT_ID environment variable overrides it for trying out another app.
/// </remarks>
public static class SpotifySignIn
{
    public const int Port = 43821;
    public const string RedirectUri = "http://127.0.0.1:43821/callback";

    /// <summary>
    /// What Lucent asks to do: see what's playing, play, pause, skip and seek, read the playlists and Liked Songs it
    /// plays from, and add songs to them. Someone who signed in before Lucent did one of those can't until they sign in again.
    /// </summary>
    private const string Scopes = "user-read-playback-state user-modify-playback-state user-read-currently-playing "
        + "playlist-read-private playlist-read-collaborative user-library-read "
        + "playlist-modify-private playlist-modify-public user-library-modify";

    private const string AuthorizeUrl = "https://accounts.spotify.com/authorize";
    internal const string TokenUrl = "https://accounts.spotify.com/api/token";

    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);

    /// <summary>Lucent's Spotify app's client ID, or null when this build wasn't given one.</summary>
    public static string? ClientId
    {
        get
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("LUCENT_SPOTIFY_CLIENT_ID");
            if (!string.IsNullOrWhiteSpace(fromEnvironment))
            {
                return fromEnvironment.Trim();
            }

            var built = typeof(SpotifySignIn).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(attribute => attribute.Key == "SpotifyClientId")?.Value;
            return string.IsNullOrWhiteSpace(built) ? null : built.Trim();
        }
    }

    /// <summary>
    /// Opens Spotify's sign-in page with <paramref name="openBrowser"/> and waits for the answer. Returns the
    /// refresh token, which keeps Lucent signed in. Throws <see cref="RemoteProblem"/> when it doesn't work out.
    /// </summary>
    public static async Task<string> SignInAsync(HttpClient http, Action<string> openBrowser, CancellationToken cancel)
    {
        var clientId = ClientId ?? throw new RemoteProblem("This copy of Lucent wasn't built with a Spotify app to sign in with.");
        var verifier = RandomText(64);
        var state = RandomText(16);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

        TcpListener listener;
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
        }
        catch (SocketException ex)
        {
            throw new RemoteProblem($"Lucent couldn't wait for Spotify's answer: something else on this computer is using port {Port}.", ex);
        }

        try
        {
            openBrowser($"{AuthorizeUrl}?client_id={Uri.EscapeDataString(clientId)}&response_type=code"
                + $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}&code_challenge_method=S256&code_challenge={challenge}"
                + $"&state={state}&scope={Uri.EscapeDataString(Scopes)}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(GiveUpAfter);
            var code = await WaitForCodeAsync(listener, state, timeout.Token);
            var tokens = await PostTokenAsync(http, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["client_id"] = clientId,
                ["code_verifier"] = verifier,
            }, cancel);
            return tokens.RefreshToken ?? throw new RemoteProblem("Spotify signed in but didn't say how to stay signed in. Try again.");
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new RemoteProblem("Spotify's sign-in page didn't answer in time. Try again.");
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>
    /// A short-lived access token, the refresh token to use next time when Spotify hands out a new one, and what the
    /// sign-in allows (the scopes, space-separated), when Spotify says.
    /// </summary>
    internal sealed record Tokens(string AccessToken, DateTime ExpiresAt, string? RefreshToken, string? Scope = null);

    internal static async Task<Tokens> PostTokenAsync(HttpClient http, Dictionary<string, string> form, CancellationToken cancel)
    {
        using var response = await http.PostAsync(TokenUrl, new FormUrlEncodedContent(form), cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);
        if (!response.IsSuccessStatusCode)
        {
            // invalid_grant: the sign-in was taken back (signed out on Spotify's site, or the app was removed).
            throw new RemoteProblem(body.Contains("invalid_grant", StringComparison.Ordinal)
                ? "Spotify signed Lucent out. Sign in again from the Add music menu."
                : $"Spotify wouldn't sign Lucent in ({(int)response.StatusCode}).");
        }

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;
        var access = root.GetProperty("access_token").GetString() ?? string.Empty;
        var seconds = root.TryGetProperty("expires_in", out var expires) ? expires.GetInt32() : 3600;
        var refresh = root.TryGetProperty("refresh_token", out var token) ? token.GetString() : null;
        var scope = root.TryGetProperty("scope", out var scopes) ? scopes.GetString() : null;
        // A minute early, so a request never goes out with a token that runs out on the way.
        return new Tokens(access, DateTime.UtcNow.AddSeconds(seconds - 60), refresh, scope);
    }

    // Takes connections until one is Spotify's redirect back, answers the browser, and returns the code.
    private static async Task<string> WaitForCodeAsync(TcpListener listener, string state, CancellationToken cancel)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancel);
            await using var stream = client.GetStream();
            var target = await ReadRequestTargetAsync(stream, cancel);
            if (target is null || !target.StartsWith("/callback", StringComparison.Ordinal))
            {
                // Something else (the browser asking for a favicon, say).
                await AnswerAsync(stream, "404 Not Found", "Not found.", cancel);
                continue;
            }

            var query = ParseQuery(target);
            if (!query.TryGetValue("state", out var answeredState) || answeredState != state)
            {
                await AnswerAsync(stream, "400 Bad Request", "This sign-in link has expired. Go back to Lucent and try again.", cancel);
                continue;
            }

            if (query.TryGetValue("error", out var error))
            {
                await AnswerAsync(stream, "200 OK", "Lucent wasn't given access to Spotify. You can close this tab.", cancel);
                throw new RemoteProblem(error == "access_denied" ? "Spotify sign-in was cancelled." : $"Spotify sign-in didn't work ({error}).");
            }

            if (query.TryGetValue("code", out var code) && code.Length > 0)
            {
                await AnswerAsync(stream, "200 OK", "Lucent is signed in to Spotify. You can close this tab and go back to Lucent.", cancel);
                return code;
            }

            await AnswerAsync(stream, "400 Bad Request", "Spotify's answer was missing something. Go back to Lucent and try again.", cancel);
        }
    }

    // The path and query of an HTTP request's first line ("GET /callback?code=… HTTP/1.1").
    private static async Task<string?> ReadRequestTargetAsync(Stream stream, CancellationToken cancel)
    {
        var buffer = new byte[8192];
        var length = 0;
        while (length < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(length), cancel);
            if (read == 0)
            {
                break;
            }

            length += read;
            if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        var line = Encoding.ASCII.GetString(buffer, 0, length).Split("\r\n")[0].Split(' ');
        return line.Length >= 2 && line[0] == "GET" ? line[1] : null;
    }

    private static async Task AnswerAsync(Stream stream, string status, string message, CancellationToken cancel)
    {
        var html = "<!doctype html><meta charset=\"utf-8\"><title>Lucent</title>"
            + "<body style=\"font-family:system-ui,sans-serif;background:#111;color:#eee;display:grid;place-items:center;height:90vh\">"
            + $"<p style=\"font-size:18px\">{WebUtility.HtmlEncode(message)}</p></body>";
        var body = Encoding.UTF8.GetBytes(html);
        var head = Encoding.ASCII.GetBytes($"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(head, cancel);
        await stream.WriteAsync(body, cancel);
    }

    internal static Dictionary<string, string> ParseQuery(string target)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var at = target.IndexOf('?');
        if (at < 0)
        {
            return result;
        }

        foreach (var pair in target[(at + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=');
            var key = Uri.UnescapeDataString((equals < 0 ? pair : pair[..equals]).Replace('+', ' '));
            var value = equals < 0 ? string.Empty : Uri.UnescapeDataString(pair[(equals + 1)..].Replace('+', ' '));
            result[key] = value;
        }

        return result;
    }

    private static string RandomText(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
