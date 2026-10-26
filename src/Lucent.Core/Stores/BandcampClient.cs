using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Lucent.Stores;

/// <summary>One release or track in a Bandcamp collection.</summary>
/// <param name="Id">Bandcamp's id for the sale, like "a1234" (an album) or "t5678" (a track).</param>
/// <param name="DownloadPage">The page Bandcamp gives fans to download it again.</param>
public sealed record BandcampPurchase(string Id, string Artist, string Title, string DownloadPage, bool IsPreorder);

/// <summary>A purchase's file in one format: a zip for a release, or the song itself for a single track.</summary>
public sealed record BandcampDownload(string Url, string Format, bool IsZip, string Artist, string Title);

/// <summary>The sign-in didn't work: it expired, or the user signed out on the website.</summary>
public sealed class BandcampSignInException : Exception
{
    public BandcampSignInException(string message)
        : base(message)
    {
    }
}

/// <summary>Bandcamp changed its pages and the collection can't be read any more.</summary>
public sealed class BandcampPageException : Exception
{
    public BandcampPageException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// Reads a fan's Bandcamp collection and downloads it. Bandcamp has no public API for fans, so this reads
/// the same pages and calls the website itself makes, signed in with the fan's own cookies. It goes no
/// faster than a few requests a second, and waits whenever Bandcamp asks it to slow down.
/// </summary>
public sealed class BandcampClient : IDisposable
{
    private const string Site = "https://bandcamp.com/";
    private static readonly TimeSpan RequestGap = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan SlowDownWait = TimeSpan.FromSeconds(10);
    private const int MaxRetries = 5;

    /// <summary>Bandcamp's formats in the order to fall back through when the chosen one isn't offered.</summary>
    public static IReadOnlyList<string> Formats { get; } = new[] { "flac", "mp3-320", "mp3-v0", "alac", "aac-hi", "vorbis", "wav", "aiff-lossless" };

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTime _lastRequest = DateTime.MinValue;

    /// <param name="cookies">The bandcamp.com cookies from signing in, as "name=value; name=value".</param>
    public BandcampClient(string cookies, HttpMessageHandler? handler = null)
    {
        _http = new HttpClient(handler ?? new SocketsHttpHandler { UseCookies = false, AutomaticDecompression = DecompressionMethods.All })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookies);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) Lucent");
    }

    /// <summary>The signed-in fan's username, from the identity Bandcamp's home page carries.</summary>
    public async Task<string> GetUsernameAsync(CancellationToken token)
    {
        var page = await GetTextAsync(Site, token);
        var blob = DataBlob(page, "HomepageApp") ?? throw new BandcampPageException("Bandcamp's home page has changed, so your account can't be found.");
        using var data = JsonDocument.Parse(blob);
        if (!data.RootElement.TryGetProperty("pageContext", out var context) || !context.TryGetProperty("identity", out var identity))
        {
            throw new BandcampPageException("Bandcamp's home page has changed, so your account can't be found.");
        }

        if (!(identity.TryGetProperty("isLoggedIn", out var loggedIn) && loggedIn.ValueKind == JsonValueKind.True))
        {
            throw new BandcampSignInException("You're signed out of Bandcamp. Sign in again to keep syncing.");
        }

        var username = identity.TryGetProperty("fanUsername", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null;
        return string.IsNullOrWhiteSpace(username)
            ? throw new BandcampSignInException("This Bandcamp account has no fan collection.")
            : username;
    }

    /// <summary>Everything in the fan's collection that can be downloaded, newest purchase first. Hidden items are left out.</summary>
    public async Task<IReadOnlyList<BandcampPurchase>> GetPurchasesAsync(string username, CancellationToken token)
    {
        var page = await GetTextAsync(Site + Uri.EscapeDataString(username), token);
        var blob = DataBlob(page, "pagedata") ?? throw new BandcampPageException("Bandcamp's collection page has changed, so it can't be read.");
        using var data = JsonDocument.Parse(blob);
        var root = data.RootElement;
        if (!root.TryGetProperty("fan_data", out var fan) || !(fan.TryGetProperty("is_own_page", out var own) && own.ValueKind == JsonValueKind.True))
        {
            throw new BandcampSignInException("Bandcamp didn't recognise the sign-in. Sign in again to keep syncing.");
        }

        var fanId = fan.TryGetProperty("fan_id", out var id) ? id.ToString() : throw new BandcampPageException("Bandcamp's collection page has changed.");
        var purchases = new List<BandcampPurchase>();
        var seen = new HashSet<string>();
        var collection = root.GetProperty("collection_data");
        if (root.TryGetProperty("item_cache", out var cache) && cache.TryGetProperty("collection", out var items))
        {
            AddPurchases(purchases, seen, items.EnumerateObject().Select(item => item.Value), collection);
        }

        // The page holds the first batch; the rest comes from the same call the page makes as you scroll.
        var more = Number(collection, "item_count") > Number(collection, "batch_size");
        var lastToken = String(collection, "last_token");
        while (more && lastToken is not null)
        {
            var body = JsonSerializer.Serialize(new Dictionary<string, object> { ["fan_id"] = long.Parse(fanId), ["older_than_token"] = lastToken });
            var json = await SendAsync(() => new HttpRequestMessage(HttpMethod.Post, Site + "api/fancollection/1/collection_items")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            }, token);
            using var batch = JsonDocument.Parse(json);
            var next = batch.RootElement;
            var before = purchases.Count;
            if (next.TryGetProperty("items", out var batchItems))
            {
                AddPurchases(purchases, seen, batchItems.EnumerateArray(), next);
            }

            more = next.TryGetProperty("more_available", out var available) && available.ValueKind == JsonValueKind.True && purchases.Count > before;
            lastToken = String(next, "last_token");
        }

        return purchases;
    }

    /// <summary>
    /// The download for a purchase in <paramref name="format"/>, or the nearest one offered. Null when the
    /// purchase has nothing to download (merch only, refunded, or not released yet).
    /// </summary>
    public async Task<BandcampDownload?> GetDownloadAsync(BandcampPurchase purchase, string format, CancellationToken token)
    {
        string page;
        try
        {
            page = await GetTextAsync(purchase.DownloadPage, token);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        var blob = DataBlob(page, "pagedata");
        if (blob is null)
        {
            return null;
        }

        using var data = JsonDocument.Parse(blob);
        if (!data.RootElement.TryGetProperty("digital_items", out var digital) || digital.ValueKind != JsonValueKind.Array
            || digital.GetArrayLength() == 0)
        {
            return null;
        }

        var item = digital[0];
        if (!item.TryGetProperty("downloads", out var downloads) || downloads.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var choice in Formats.Prepend(format).Distinct())
        {
            if (downloads.TryGetProperty(choice, out var download) && String(download, "url") is { } url)
            {
                var isTrack = String(item, "download_type") == "t" || String(item, "download_type_str") == "track" || String(item, "item_type") == "track";
                return new BandcampDownload(url, choice, !isTrack, String(item, "artist") ?? purchase.Artist, String(item, "title") ?? purchase.Title);
            }
        }

        return null;
    }

    /// <summary>Downloads a file, reporting progress from 0 to 1 when Bandcamp says how big it is.</summary>
    public async Task DownloadAsync(BandcampDownload download, string path, IProgress<double>? progress, CancellationToken token)
    {
        using var response = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Get, download.Url), HttpCompletionOption.ResponseHeadersRead, token);
        var length = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(token);
        await using var target = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
        var buffer = new byte[1 << 16];
        long copied = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, token)) > 0)
        {
            await target.WriteAsync(buffer.AsMemory(0, read), token);
            copied += read;
            if (length > 0)
            {
                progress?.Report((double)copied / length.Value);
            }
        }

        if (length > 0 && copied != length)
        {
            throw new IOException("The download stopped partway through.");
        }
    }

    /// <summary>The file extension Bandcamp uses for a single track in each format.</summary>
    public static string TrackExtension(string format) => format switch
    {
        "flac" => ".flac",
        "mp3-320" or "mp3-v0" => ".mp3",
        "alac" or "aac-hi" => ".m4a",
        "vorbis" => ".ogg",
        "wav" => ".wav",
        "aiff-lossless" => ".aiff",
        _ => ".bin",
    };

    /// <summary>The JSON Bandcamp puts in an element's data-blob attribute, or null if there's no such element.</summary>
    public static string? DataBlob(string html, string elementId)
    {
        var element = Regex.Match(html, $"<[^>]*\\bid=\"{Regex.Escape(elementId)}\"[^>]*>", RegexOptions.IgnoreCase);
        if (!element.Success)
        {
            return null;
        }

        var blob = Regex.Match(element.Value, "\\bdata-blob=(?:\"([^\"]*)\"|'([^']*)')", RegexOptions.IgnoreCase);
        return blob.Success ? WebUtility.HtmlDecode(blob.Groups[1].Success ? blob.Groups[1].Value : blob.Groups[2].Value) : null;
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }

    private static void AddPurchases(List<BandcampPurchase> purchases, HashSet<string> seen, IEnumerable<JsonElement> items, JsonElement urlsHolder)
    {
        if (!urlsHolder.TryGetProperty("redownload_urls", out var urls) || urls.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var item in items)
        {
            var id = String(item, "sale_item_type") + (item.TryGetProperty("sale_item_id", out var saleId) ? saleId.ToString() : string.Empty);
            if (!seen.Add(id) || !urls.TryGetProperty(id, out var url) || url.GetString() is not { } page)
            {
                continue;
            }

            var preorder = item.TryGetProperty("is_preorder", out var flag) && flag.ValueKind == JsonValueKind.True;
            purchases.Add(new BandcampPurchase(id, String(item, "band_name") ?? "Unknown artist",
                String(item, "item_title") ?? "Untitled", page, preorder));
        }
    }

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long Number(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.TryGetInt64(out var number) ? number : 0;

    private Task<string> GetTextAsync(string url, CancellationToken token) =>
        SendAsync(() => new HttpRequestMessage(HttpMethod.Get, url), token);

    private async Task<string> SendAsync(Func<HttpRequestMessage> request, CancellationToken token)
    {
        using var response = await SendRawAsync(request, HttpCompletionOption.ResponseContentRead, token);
        return await response.Content.ReadAsStringAsync(token);
    }

    /// <summary>Sends a request no sooner than the gap after the last, waiting and retrying when Bandcamp asks to slow down.</summary>
    private async Task<HttpResponseMessage> SendRawAsync(Func<HttpRequestMessage> request, HttpCompletionOption completion, CancellationToken token)
    {
        for (var attempt = 0; ; attempt++)
        {
            await _gate.WaitAsync(token);
            HttpResponseMessage response;
            try
            {
                var wait = _lastRequest + RequestGap - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, token);
                }

                _lastRequest = DateTime.UtcNow;
                using var message = request();
                response = await _http.SendAsync(message, completion, token);
            }
            finally
            {
                _gate.Release();
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxRetries)
            {
                response.Dispose();
                await Task.Delay(SlowDownWait, token);
                continue;
            }

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                response.Dispose();
                throw new BandcampSignInException("Bandcamp turned the sign-in down. Sign in again to keep syncing.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var status = response.StatusCode;
                response.Dispose();
                throw new HttpRequestException($"Bandcamp answered {(int)status} ({status}).", null, status);
            }

            return response;
        }
    }
}
