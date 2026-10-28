using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Lucent.Lastfm;

/// <summary>Something Last.fm said no to, or couldn't be asked, worded for the user.</summary>
public sealed class LastfmProblem : Exception
{
    public LastfmProblem(string message, int code = 0, Exception? inner = null)
        : base(message, inner)
    {
        Code = code;
    }

    /// <summary>Last.fm's error number, or 0 when it didn't give one.</summary>
    public int Code { get; }

    /// <summary>True when the sign-in no longer works (taken back on Last.fm's site), so the user has to sign in again.</summary>
    public bool SignInGone => Code == 9;

    /// <summary>True when Last.fm will never take what was sent, so trying again won't help.</summary>
    public bool Unsendable => Code == 6;
}

/// <summary>The Last.fm account Lucent scrobbles to: its user name, and the session key that signs every scrobble.</summary>
public sealed record LastfmSession(string UserName, string Key);

/// <summary>
/// Talks to Last.fm's API: signs in with its desktop sign-in (the browser opens Last.fm's page, the user says yes,
/// and Lucent picks the answer up), then sends "now playing" and scrobbles.
/// </summary>
/// <remarks>
/// Every call is signed with Lucent's Last.fm app (last.fm/api/account): its API key and shared secret. The build puts
/// them in (the LASTFM_API_KEY and LASTFM_SHARED_SECRET repository secrets, as the LastfmApiKey and LastfmSharedSecret
/// properties), as every desktop scrobbler carries its own; LUCENT_LASTFM_API_KEY and LUCENT_LASTFM_SHARED_SECRET
/// override them for trying out another app. A sign-in never runs out: it lasts until the user takes it back on
/// Last.fm's site.
/// </remarks>
public sealed class LastfmClient : IScrobbleService
{
    private const string ApiUrl = "https://ws.audioscrobbler.com/2.0/";
    private const string SignInPage = "https://www.last.fm/api/auth/";
    private const string UserAgent = "Lucent (https://lucentaudio.net)";

    private static readonly TimeSpan GiveUpAfter = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan AskEvery = TimeSpan.FromSeconds(3);

    private readonly HttpClient _http;
    private readonly LastfmApp _app;
    private readonly string _sessionKey;

    public LastfmClient(HttpClient http, string sessionKey)
    {
        _http = http;
        _app = App ?? throw new LastfmProblem(NoApp);
        _sessionKey = sessionKey;
    }

    private const string NoApp = "This copy of Lucent wasn't built with a Last.fm app to sign in with.";

    /// <summary>Lucent's Last.fm app, or null when this build wasn't given one.</summary>
    public static LastfmApp? App
    {
        get
        {
            var key = Setting("LUCENT_LASTFM_API_KEY", "LastfmApiKey");
            var secret = Setting("LUCENT_LASTFM_SHARED_SECRET", "LastfmSharedSecret");
            return key is not null && secret is not null ? new LastfmApp(key, secret) : null;
        }
    }

    private static string? Setting(string environment, string metadata)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(environment);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var built = typeof(LastfmClient).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == metadata)?.Value;
        return string.IsNullOrWhiteSpace(built) ? null : built.Trim();
    }

    /// <summary>
    /// Opens Last.fm's sign-in page with <paramref name="openBrowser"/> and waits for the user to allow Lucent there.
    /// Throws <see cref="LastfmProblem"/> when it doesn't work out.
    /// </summary>
    public static async Task<LastfmSession> SignInAsync(HttpClient http, Action<string> openBrowser, CancellationToken cancel)
    {
        var app = App ?? throw new LastfmProblem(NoApp);
        var token = (await CallAsync(http, app, new() { ["method"] = "auth.getToken" }, post: false, cancel))
            .GetProperty("token").GetString() ?? throw new LastfmProblem("Last.fm didn't start the sign-in. Try again.");
        openBrowser($"{SignInPage}?api_key={Uri.EscapeDataString(app.Key)}&token={Uri.EscapeDataString(token)}");

        var giveUpAt = DateTime.UtcNow + GiveUpAfter;
        while (true)
        {
            await Task.Delay(AskEvery, cancel);
            try
            {
                var session = (await CallAsync(http, app, new() { ["method"] = "auth.getSession", ["token"] = token }, post: false, cancel))
                    .GetProperty("session");
                return new LastfmSession(session.GetProperty("name").GetString() ?? string.Empty,
                    session.GetProperty("key").GetString() ?? throw new LastfmProblem("Last.fm signed in but didn't say how to stay signed in. Try again."));
            }
            catch (LastfmProblem problem) when (problem.Code == 14 && DateTime.UtcNow < giveUpAt)
            {
                // Not allowed on the page yet.
            }
            catch (LastfmProblem problem) when (problem.Code is 14 or 15 or 4)
            {
                throw new LastfmProblem("Last.fm's sign-in page wasn't answered in time. Try again.", problem.Code, problem);
            }
        }
    }

    public async Task NowPlayingAsync(ScrobbleSong song, CancellationToken cancel)
    {
        var fields = new Dictionary<string, string>
        {
            ["method"] = "track.updateNowPlaying",
            ["artist"] = song.Artist.Trim(),
            ["track"] = song.Title.Trim(),
            ["sk"] = _sessionKey,
        };
        AddIfKnown(fields, "album", song.Album);
        AddIfKnown(fields, "albumArtist", song.AlbumArtist);
        if (song.Duration > TimeSpan.Zero)
        {
            fields["duration"] = ((int)Math.Round(song.Duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        }

        await CallAsync(_http, _app, fields, post: true, cancel);
    }

    public async Task ScrobbleAsync(IReadOnlyList<Scrobble> scrobbles, CancellationToken cancel)
    {
        var fields = new Dictionary<string, string> { ["method"] = "track.scrobble", ["sk"] = _sessionKey };
        for (var i = 0; i < scrobbles.Count && i < Scrobbler.MaxBatch; i++)
        {
            var scrobble = scrobbles[i];
            fields[$"artist[{i}]"] = scrobble.Artist;
            fields[$"track[{i}]"] = scrobble.Title;
            fields[$"timestamp[{i}]"] = scrobble.StartedAt.ToString(CultureInfo.InvariantCulture);
            fields[$"chosenByUser[{i}]"] = scrobble.ChosenByUser ? "1" : "0";
            AddIfKnown(fields, $"album[{i}]", scrobble.Album);
            AddIfKnown(fields, $"albumArtist[{i}]", scrobble.AlbumArtist);
            if (scrobble.DurationSeconds > 0)
            {
                fields[$"duration[{i}]"] = scrobble.DurationSeconds.ToString(CultureInfo.InvariantCulture);
            }
        }

        await CallAsync(_http, _app, fields, post: true, cancel);
    }

    public async Task LoveAsync(string artist, string title, bool love, CancellationToken cancel) =>
        await CallAsync(_http, _app, new Dictionary<string, string>
        {
            ["method"] = love ? "track.love" : "track.unlove",
            ["artist"] = artist,
            ["track"] = title,
            ["sk"] = _sessionKey,
        }, post: true, cancel);

    /// <summary>
    /// The signature Last.fm checks every call by: each field's name and value, in order of name (leaving out
    /// format), then the app's secret, hashed with MD5.
    /// </summary>
    public static string Sign(IReadOnlyDictionary<string, string> fields, string secret)
    {
        var text = new StringBuilder();
        foreach (var (name, value) in fields.Where(field => field.Key != "format").OrderBy(field => field.Key, StringComparer.Ordinal))
        {
            text.Append(name).Append(value);
        }

        text.Append(secret);
        return Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    private static void AddIfKnown(Dictionary<string, string> fields, string name, string value)
    {
        if (value.Trim().Length > 0)
        {
            fields[name] = value.Trim();
        }
    }

    private static async Task<JsonElement> CallAsync(HttpClient http, LastfmApp app, Dictionary<string, string> fields, bool post,
        CancellationToken cancel)
    {
        fields["api_key"] = app.Key;
        fields["api_sig"] = Sign(fields, app.Secret);
        fields["format"] = "json";

        using var request = post
            ? new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = new FormUrlEncodedContent(fields) }
            : new HttpRequestMessage(HttpMethod.Get, ApiUrl + "?" + string.Join("&",
                fields.Select(field => $"{Uri.EscapeDataString(field.Key)}={Uri.EscapeDataString(field.Value)}")));
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        using var response = await http.SendAsync(request, cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);

        JsonElement answer;
        try
        {
            using var document = JsonDocument.Parse(body);
            answer = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new HttpRequestException($"Last.fm answered {(int)response.StatusCode} with something that isn't JSON.", ex);
        }

        if (answer.ValueKind == JsonValueKind.Object && answer.TryGetProperty("error", out var error) && error.TryGetInt32(out var code))
        {
            var message = answer.TryGetProperty("message", out var said) ? said.GetString() : null;
            throw new LastfmProblem(Explain(code, message), code);
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Last.fm answered {(int)response.StatusCode}.");
        }

        return answer;
    }

    private static string Explain(int code, string? message) => code switch
    {
        9 => "Last.fm signed Lucent out. Sign in again to keep scrobbling.",
        11 or 16 or 8 => "Last.fm isn't answering right now.",
        29 => "Last.fm asked Lucent to slow down.",
        10 or 26 => "Last.fm turned down Lucent's app key. An update to Lucent should fix it.",
        _ => $"Last.fm said: {message?.Trim().TrimEnd('.') ?? $"error {code}"}.",
    };
}

/// <summary>Lucent's Last.fm app: its API key and the shared secret calls are signed with.</summary>
public sealed record LastfmApp(string Key, string Secret);
