using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace WinampInspiredVisualizer.Recording;

/// <summary>
/// A DJ's own ACRCloud project: the host it lives on and its access key and secret, from the project's page
/// in ACRCloud's console. Each DJ uses their own, so lookups are counted (and paid for) on their account.
/// Kept by each app where only this user can read it (Audio/SongIdAccount.cs).
/// </summary>
/// <param name="Host">Like identify-eu-west-1.acrcloud.com.</param>
public sealed record AcrCloudKey(string Host, string AccessKey, string AccessSecret)
{
    /// <summary>The host, without https:// or a slash at the end, as pasted from the console.</summary>
    public string CleanHost => Host.Trim().Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
        .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase).Trim('/');

    public bool IsComplete => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(AccessSecret);

    /// <summary>The key as one piece of text, for the app's secret store.</summary>
    public string ToText() => JsonSerializer.Serialize(new[] { CleanHost, AccessKey.Trim(), AccessSecret.Trim() });

    /// <summary>A key saved with <see cref="ToText"/>, or null if the text isn't one.</summary>
    public static AcrCloudKey? FromText(string? text)
    {
        try
        {
            return text is not null && JsonSerializer.Deserialize<string[]>(text) is { Length: 3 } parts
                ? new AcrCloudKey(parts[0], parts[1], parts[2])
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>A song ACRCloud recognised.</summary>
/// <param name="Score">How sure it is, 0 to 100.</param>
public sealed record SongMatch(string Artist, string Title, string? Album, int Score)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Artist) ? Title : $"{Artist} - {Title}";
}

/// <summary>What came back from asking ACRCloud about a stretch of sound.</summary>
/// <param name="Song">The song, or null when it didn't know it (or the question couldn't be asked).</param>
/// <param name="Problem">Why it couldn't answer, in words for the Record window, when it's something to fix (a wrong key, say).</param>
public sealed record SongLookup(SongMatch? Song, string? Problem = null)
{
    /// <summary>The key or account can't be used: asking again won't help until it's changed.</summary>
    public bool KeyProblem { get; init; }
}

/// <summary>
/// Names songs from a few seconds of them, through ACRCloud's identification service (the one built for
/// DJ mixes and broadcasts, where songs are sped up, pitched and mixed over each other). The sound is sent
/// as a short mono WAV; ACRCloud fingerprints it and looks it up in its catalogue of tens of millions of songs.
/// </summary>
public sealed class SongIdentifier
{
    /// <summary>How long a stretch is sent. ACRCloud asks for 10 seconds or so; more doesn't help.</summary>
    public static readonly TimeSpan SampleLength = TimeSpan.FromSeconds(12);

    // Fingerprints are made from the sound under 4 kHz or so, so 16 kHz keeps everything that matters at a quarter of the size.
    private const int SendRate = 16000;

    private readonly HttpClient _http;

    public SongIdentifier(HttpClient http, AcrCloudKey key)
    {
        _http = http;
        Key = key;
    }

    public AcrCloudKey Key { get; }

    /// <summary>
    /// Asks ACRCloud which song this is: mono sound at <paramref name="sampleRate"/>, about
    /// <see cref="SampleLength"/> of it. Never throws for a network or service problem; the result says what went wrong.
    /// </summary>
    public async Task<SongLookup> IdentifyAsync(ReadOnlyMemory<float> mono, int sampleRate, CancellationToken cancel = default)
    {
        var wav = MakeWav(mono.Span, sampleRate);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        using var form = new MultipartFormDataContent
        {
            { new StringContent(Key.AccessKey.Trim()), "access_key" },
            { new StringContent(wav.Length.ToString(CultureInfo.InvariantCulture)), "sample_bytes" },
            { new StringContent(timestamp), "timestamp" },
            { new StringContent(Sign(Key, timestamp)), "signature" },
            { new StringContent("audio"), "data_type" },
            { new StringContent("1"), "signature_version" },
        };
        var sample = new ByteArrayContent(wav);
        sample.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/wav");
        form.Add(sample, "sample", "sample.wav");

        string body;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            using var response = await _http.PostAsync($"https://{Key.CleanHost}/v1/identify", form, timeout.Token).ConfigureAwait(false);
            body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body))
            {
                return new SongLookup(null, $"ACRCloud answered {(int)response.StatusCode}.");
            }
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            return new SongLookup(null, "ACRCloud didn't answer in time.");
        }
        catch (HttpRequestException ex)
        {
            return new SongLookup(null, $"Couldn't reach ACRCloud at {Key.CleanHost}: {ex.Message}");
        }

        return Read(body);
    }

    /// <summary>The request's signature: HMAC-SHA1 of the request's parts with the access secret, as ACRCloud's protocol has it.</summary>
    internal static string Sign(AcrCloudKey key, string timestamp)
    {
        var text = $"POST\n/v1/identify\n{key.AccessKey.Trim()}\naudio\n1\n{timestamp}";
        using var hmac = new HMACSHA1(Encoding.UTF8.GetBytes(key.AccessSecret.Trim()));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(text)));
    }

    /// <summary>Reads ACRCloud's answer.</summary>
    internal static SongLookup Read(string body)
    {
        try
        {
            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var code = root.TryGetProperty("status", out var status) && status.TryGetProperty("code", out var c) && c.TryGetInt32(out var n) ? n : -1;
            var message = status.ValueKind == JsonValueKind.Object && status.TryGetProperty("msg", out var m) ? m.GetString() : null;
            switch (code)
            {
                case 0:
                    break;
                case 1001:
                    // Heard, but not a song it knows (an unreleased edit, a live mashup, talking).
                    return new SongLookup(null);
                case 2004:
                    return new SongLookup(null, "That stretch was too quiet or too short to recognise.");
                case 3001 or 3014:
                    return new SongLookup(null, "ACRCloud didn't accept the access key or secret. Check them against your project in ACRCloud's console.") { KeyProblem = true };
                case 3003 or 3015:
                    return new SongLookup(null, "Your ACRCloud project has used up its lookups for now (or is asking too fast).") { KeyProblem = code == 3003 };
                default:
                    return new SongLookup(null, $"ACRCloud said: {message ?? "an error"} ({code}).") { KeyProblem = code is >= 3000 and < 4000 };
            }

            if (!root.TryGetProperty("metadata", out var metadata) || !metadata.TryGetProperty("music", out var music)
                || music.ValueKind != JsonValueKind.Array || music.GetArrayLength() == 0)
            {
                return new SongLookup(null);
            }

            var best = music.EnumerateArray().OrderByDescending(Score).First();
            var title = best.TryGetProperty("title", out var t) ? t.GetString() : null;
            if (string.IsNullOrWhiteSpace(title))
            {
                return new SongLookup(null);
            }

            var artists = best.TryGetProperty("artists", out var a) && a.ValueKind == JsonValueKind.Array
                ? string.Join(", ", a.EnumerateArray().Select(x => x.TryGetProperty("name", out var name) ? name.GetString() : null).Where(x => !string.IsNullOrWhiteSpace(x)))
                : string.Empty;
            var album = best.TryGetProperty("album", out var al) && al.TryGetProperty("name", out var albumName) ? albumName.GetString() : null;
            return new SongLookup(new SongMatch(artists, title.Trim(), album, Score(best)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            return new SongLookup(null, "ACRCloud's answer couldn't be read.");
        }
    }

    private static int Score(JsonElement song) =>
        song.TryGetProperty("score", out var s) && s.TryGetDouble(out var score) ? (int)Math.Round(score) : 0;

    /// <summary>The sound as a 16-bit mono WAV at <see cref="SendRate"/> (or the sound's own rate if that's lower).</summary>
    internal static byte[] MakeWav(ReadOnlySpan<float> mono, int sampleRate)
    {
        // Averaging each step's samples is a plain low-pass, ample for a fingerprint.
        var step = Math.Max(1, sampleRate / SendRate);
        var rate = sampleRate / step;
        var frames = mono.Length / step;
        using var stream = new MemoryStream(44 + frames * 2);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + frames * 2);
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(frames * 2);
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var j = 0; j < step; j++)
            {
                sum += mono[i * step + j];
            }

            writer.Write((short)Math.Clamp(sum / step * 32767f, -32768f, 32767f));
        }

        writer.Flush();
        return stream.ToArray();
    }
}
