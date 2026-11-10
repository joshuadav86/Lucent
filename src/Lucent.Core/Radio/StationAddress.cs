using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace Lucent.Radio;

/// <summary>What Add a station found for what was typed: a stream to play, a name to search the directory for, or why neither.</summary>
public sealed record StationLookup
{
    /// <summary>The stream's address, or null.</summary>
    public string? Address { get; init; }

    /// <summary>What to search the directory for instead, or null.</summary>
    public string? Search { get; init; }

    /// <summary>Why there's neither, in words fit to show, or null.</summary>
    public string? Problem { get; init; }
}

/// <summary>
/// Makes sense of what was typed into Add a station's address box. An address without http:// gets https:// (or
/// http:// when that's all the station answers). A web page instead of a stream is read for the stream it links (an
/// og:audio tag, an audio player's source, or a .pls/.m3u/.m3u8 link). A page that links none gives the station's
/// name from its title, to search the directory for, and plain words are searched for as they are.
/// </summary>
public static class StationAddress
{
    public const string NoStreamOnPage =
        "That's a website, not a stream, and Lucent found no stream on it. Search for the station in the list, or paste the stream's own address from the site.";

    private static readonly TimeSpan LookTimeout = TimeSpan.FromSeconds(10);

    // Plenty for a page's head and its links. Anything longer is cut, not refused.
    private const int MostPageBytes = 2 * 1024 * 1024;

    private static readonly string[] StreamEndings = { ".pls", ".m3u", ".m3u8", ".mp3", ".aac", ".aacp", ".ogg", ".opus", ".flac" };

    private static readonly string[] Separators = { " | ", " - ", " – ", " — ", " · ", " • ", " :: ", ": " };

    private static readonly string[] Openings = { "listen live to ", "listen online to ", "listen to ", "welcome to ", "now playing on " };

    private static readonly string[] Endings =
    {
        " listen live", " listen online", " live stream", " radio stream", " online radio", " internet radio", " live online",
        " live", " online", " stream",
    };

    // Words that say a part of a title is about radio in general rather than naming a station.
    private static readonly HashSet<string> Generic = new(StringComparer.OrdinalIgnoreCase)
    {
        "radio", "radios", "online", "internet", "web", "live", "listen", "free", "home", "homepage", "station", "stations",
        "podcast", "podcasts", "music", "stream", "streams", "streaming", "welcome", "official", "site", "website", "page",
        "the", "to", "and", "or", "for", "of", "your", "you", "all", "best", "top", "now", "on", "in", "with", "more", "worldwide",
        "world", "news", "sports", "audio", "player", "app",
    };

    /// <summary>
    /// The addresses <paramref name="typed"/> could mean, best first: itself when it starts http:// or https://, or with
    /// https:// and then http:// in front when it reads like an address without them ("radio.net",
    /// "ice1.somafm.com/groovesalad-256-mp3", "192.168.1.20:8000/stream"). None for words or other kinds of address.
    /// </summary>
    public static IReadOnlyList<string> AddressesFor(string typed)
    {
        typed = typed.Trim();
        if (RadioGuide.IsStreamAddress(typed))
        {
            return new[] { typed };
        }

        if (typed.Length == 0 || typed.Any(char.IsWhiteSpace) || typed.Contains("://") || typed.StartsWith('/'))
        {
            return Array.Empty<string>();
        }

        if (!Uri.TryCreate("https://" + typed, UriKind.Absolute, out var uri))
        {
            return Array.Empty<string>();
        }

        // "radio.net" or "1.fm" (a dot and a top-level name), or four numbers ("3.5" alone would be read as 3.0.0.5).
        var host = uri.Host;
        var named = uri.HostNameType == UriHostNameType.Dns && host.Contains('.') && host[(host.LastIndexOf('.') + 1)..] is { Length: >= 2 } top
            && top.All(char.IsLetter);
        var numbered = uri.HostNameType == UriHostNameType.IPv4 && typed.StartsWith(host, StringComparison.Ordinal);
        return named || numbered || host == "localhost"
            ? new[] { "https://" + typed, "http://" + typed }
            : Array.Empty<string>();
    }

    /// <summary>
    /// Looks at what was typed (<paramref name="name"/> is what was typed as the station's name, if anything). Throws
    /// <see cref="OperationCanceledException"/> when <paramref name="cancel"/> is.
    /// </summary>
    public static async Task<StationLookup> LookUpAsync(string typed, string? name, string userAgent, CancellationToken cancel)
    {
        using var http = RadioStream.CreateClient();
        return await LookUpAsync(http, typed, name, userAgent, cancel);
    }

    internal static async Task<StationLookup> LookUpAsync(HttpClient http, string typed, string? name, string userAgent, CancellationToken cancel)
    {
        typed = typed.Trim();
        name = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        var addresses = AddressesFor(typed);
        if (addresses.Count == 0)
        {
            return typed.Contains("://")
                ? new StationLookup { Problem = "Lucent plays web streams, whose addresses start with http:// or https://." }
                : new StationLookup { Search = typed };
        }

        // A playlist or a stream by its name: nothing to look at, and no need to tune in twice.
        if (addresses.Count == 1 && LooksLikeStream(addresses[0]))
        {
            return new StationLookup { Address = addresses[0] };
        }

        foreach (var address in addresses)
        {
            using var looking = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            looking.CancelAfter(LookTimeout);
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.UserAgent.ParseAdd(userAgent);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, looking.Token);
                if (!IsWebPage(response))
                {
                    // A stream, a playlist, or a server's complaint: playing it reads the first two and explains the last.
                    return new StationLookup { Address = address };
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new StationLookup
                    {
                        Problem = $"That's a web page, and it wouldn't open for Lucent ({(int)response.StatusCode}). Paste the stream's own address, or search for the station in the list.",
                    };
                }

                var page = response.RequestMessage?.RequestUri ?? new Uri(address);
                var html = await ReadPageAsync(response, looking.Token);
                if (StreamOnPage(html, page) is { } stream)
                {
                    return new StationLookup { Address = stream };
                }

                return (name ?? StationOnPage(html, page)) is { } station
                    ? new StationLookup { Search = station }
                    : new StationLookup { Problem = NoStreamOnPage };
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                // No answer this way (or too slow, or nothing sensible): try the next, http:// after https://.
            }
        }

        // Nothing answered. Keep what was asked for, and let playing it say what's wrong.
        return new StationLookup { Address = addresses[0] };
    }

    /// <summary>The stream a station's web page links, made whole against <paramref name="page"/>, or null.</summary>
    internal static string? StreamOnPage(string html, Uri page)
    {
        // Scripts are left out: a directory's page holds other stations' streams in them.
        html = Unseen.Replace(html, " ");
        var found = new[] { Meta(html, "og:audio:secure_url"), Meta(html, "og:audio:url"), Meta(html, "og:audio") }
            .Concat(AudioSources(html))
            .Concat(PlaylistLinks.Matches(html).Select(match => match.Groups["link"].Value));
        foreach (var link in found)
        {
            if (link is not null && Uri.TryCreate(page, WebUtility.HtmlDecode(link).Trim(), out var uri) && uri.Scheme is "http" or "https")
            {
                return uri.ToString();
            }
        }

        return null;
    }

    /// <summary>
    /// The station a page is about, from its title, to search the directory for. On a site's home page that's the
    /// site's own name ("SomaFM"); on any other page, the part of the title that isn't ("Groove Salad" from "SomaFM:
    /// Groove Salad: A nicely chilled plate…"). Null when nothing in it names a station (a directory's home page).
    /// </summary>
    internal static string? StationOnPage(string html, Uri page)
    {
        html = Unseen.Replace(html, " ");
        var title = Meta(html, "og:title") ?? (TitleTag.Match(html) is { Success: true } tag ? tag.Groups[1].Value : null);
        var siteName = Meta(html, "og:site_name");
        var parts = title is null ? new List<string>() : SplitTitle(Clean(title));
        var host = page.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? page.Host[4..] : page.Host;
        var label = host.Contains('.') ? host[..host.LastIndexOf('.')] : host;
        var siteKeys = new[] { Key(host), Key(label), siteName is null ? null : Key(siteName) };
        bool IsSite(string part) => siteKeys.Contains(Key(part));

        var home = page.AbsolutePath is "" or "/" || page.AbsolutePath.StartsWith("/index.", StringComparison.OrdinalIgnoreCase);
        var order = home
            ? new[] { siteName }.Concat(parts.Where(IsSite)).Concat(parts)
            : parts.Where(part => !IsSite(part)).Concat(parts);
        return order.OfType<string>().Select(part => Trimmed(Clean(part))).FirstOrDefault(NamesAStation);
    }

    private static bool LooksLikeStream(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
        && StreamEndings.Any(ending => uri.AbsolutePath.EndsWith(ending, StringComparison.OrdinalIgnoreCase));

    private static bool IsWebPage(HttpResponseMessage response) =>
        response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() is "text/html" or "application/xhtml+xml";

    private static async Task<string> ReadPageAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        await using var body = await response.Content.ReadAsStreamAsync(cancel);
        var bytes = new byte[MostPageBytes];
        var total = 0;
        int read;
        while (total < bytes.Length && (read = await body.ReadAsync(bytes.AsMemory(total), cancel)) > 0)
        {
            total += read;
        }

        Encoding encoding;
        try
        {
            encoding = response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset
                ? Encoding.GetEncoding(charset.Trim('"'))
                : Encoding.UTF8;
        }
        catch (ArgumentException)
        {
            encoding = Encoding.UTF8;
        }

        return encoding.GetString(bytes, 0, total);
    }

    // <audio src="…">, and <source src="…"> unless it's a picture or a video's.
    private static IEnumerable<string> AudioSources(string html)
    {
        foreach (Match match in SourceTags.Matches(html))
        {
            var tag = match.Value;
            if (Attribute(tag, "src") is not { Length: > 0 } source)
            {
                continue;
            }

            var type = Attribute(tag, "type")?.ToLowerInvariant() ?? string.Empty;
            var path = source.Split('?', '#')[0].ToLowerInvariant();
            if (type.StartsWith("video/") || type.StartsWith("image/")
                || new[] { ".mp4", ".webm", ".mov", ".jpg", ".jpeg", ".png", ".gif", ".webp", ".svg" }.Any(path.EndsWith))
            {
                continue;
            }

            yield return source;
        }
    }

    private static string? Attribute(string tag, string name)
    {
        var match = Regex.Match(tag, $@"\s{name}\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)'|(?<value>[^\s>]+))", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups["value"].Value : null;
    }

    // A <meta property="…" content="…"> value, with its attributes either way round.
    private static string? Meta(string html, string property)
    {
        var name = Regex.Escape(property);
        var match = Regex.Match(html, $@"<meta[^>]+(?:property|name)\s*=\s*[""']{name}[""'][^>]*?\scontent\s*=\s*[""']([^""']*)[""']", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(html, $@"<meta[^>]+content\s*=\s*[""']([^""']*)[""'][^>]*?\s(?:property|name)\s*=\s*[""']{name}[""']", RegexOptions.IgnoreCase);
        }

        return match.Success && match.Groups[1].Value.Trim().Length > 0 ? WebUtility.HtmlDecode(match.Groups[1].Value).Trim() : null;
    }

    private static List<string> SplitTitle(string title)
    {
        var parts = new List<string> { title };
        foreach (var separator in Separators)
        {
            parts = parts.SelectMany(part => part.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();
        }

        return parts;
    }

    // "Listen to FRISKY live" → "FRISKY".
    private static string Trimmed(string part)
    {
        for (var changed = true; changed;)
        {
            changed = false;
            if (Openings.FirstOrDefault(opening => part.StartsWith(opening, StringComparison.OrdinalIgnoreCase)) is { } opening)
            {
                part = part[opening.Length..].Trim();
                changed = true;
            }

            if (Endings.FirstOrDefault(ending => part.EndsWith(ending, StringComparison.OrdinalIgnoreCase)) is { } ending)
            {
                part = part[..^ending.Length].Trim();
                changed = true;
            }
        }

        return part.Trim(' ', ',', '.', '!', '-', '|', ':');
    }

    // A name, not a tagline about radio ("Listen to live radio, podcasts") or a web address ("radio.net").
    private static bool NamesAStation(string name) =>
        name.Length is > 0 and <= 60
        && !WebAddress.IsMatch(name)
        && name.Split(new[] { ' ', ',', '&', '/', '+' }, StringSplitOptions.RemoveEmptyEntries).Any(word => !Generic.Contains(word));

    private static string Key(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string Clean(string text) =>
        string.Join(' ', WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static readonly Regex Unseen = new(@"<(script|style|template)\b[^>]*>.*?</\1\s*>|<!--.*?-->", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TitleTag = new(@"<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex SourceTags = new(@"<(?:audio|source)\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PlaylistLinks = new(
        @"\shref\s*=\s*[""'](?<link>[^""'\s]+?\.(?:pls|m3u8?)(?:[?#][^""'\s]*)?)[""']", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex WebAddress = new(@"^[\w-]+(\.[\w-]+)+/?$", RegexOptions.Compiled);
}
