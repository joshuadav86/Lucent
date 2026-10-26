using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text.Json.Serialization;

namespace Lucent.Radio;

/// <summary>How the radio browser orders stations.</summary>
public enum RadioSort
{
    /// <summary>Most played by Radio Browser's listeners lately.</summary>
    Popular,

    /// <summary>Most voted for.</summary>
    Votes,

    /// <summary>Gaining the most listeners today.</summary>
    Trending,

    Name,

    /// <summary>Best sound first.</summary>
    Bitrate,
}

/// <summary>What to look for in the directory. Empty parts aren't used.</summary>
/// <param name="Text">Words in the station's name (or, as a second search, its tags).</param>
/// <param name="Tag">A genre tag the stations must have, by substring.</param>
/// <param name="CountryCode">The two-letter country the stations must be in.</param>
/// <param name="VideoOnly">Only stations whose format names a picture as well as sound.</param>
public sealed record RadioQuery(string Text, string? Tag, string? CountryCode, int MinBitrate, RadioSort Sort, int Limit = 150, bool VideoOnly = false);

/// <summary>
/// The Radio Browser directory (radio-browser.info): a free, open list of about 50,000 stations around the world,
/// kept up by its listeners, with no account or key. Lucent plays MP3 and AAC stations itself, and every other format
/// (video stations, Opus, Ogg, FLAC) through libVLC, and leaves out the ones whose stream was down at the directory's last check.
/// </summary>
/// <remarks>
/// The directory runs on a few servers. Radio Browser asks apps to find them through
/// <c>all.api.radio-browser.info</c> and spread the load, so a server is picked at random and the next one tried
/// when it doesn't answer.
/// </remarks>
public sealed class RadioDirectory
{
    private static readonly string[] KnownServers = { "de1.api.radio-browser.info", "fi1.api.radio-browser.info", "de2.api.radio-browser.info" };

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    // The quality some listeners write into a station's name: "(256k MP3)", "128 kbps", "AAC+".
    private static readonly Regex QualityInName = new(
        @"\b\d+\s*(k|kb|kbps|kbit|kbit/s)\b|\b(mp3|aac\+?|aacp|he-aac|kbps|stereo)(?![\w+])",
        RegexOptions.CultureInvariant);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _serverLock = new(1, 1);
    private List<string>? _servers;
    private IReadOnlyList<string>? _videoCodecs;

    public RadioDirectory(HttpClient http, string userAgent)
    {
        _http = http;
        UserAgent = userAgent;
    }

    /// <summary>Who's asking, as Radio Browser asks every app to say ("Lucent/1.0.200").</summary>
    public string UserAgent { get; }

    /// <summary>Stations that match <paramref name="query"/>, in its order. Searching words also finds them in the stations' tags.</summary>
    public async Task<IReadOnlyList<RadioStation>> SearchAsync(RadioQuery query, CancellationToken cancel)
    {
        var text = query.Text.Trim();
        if (text.Length == 0)
        {
            return await SearchOnceAsync(query, name: null, tag: query.Tag, cancel);
        }

        // "ambient" should find SomaFM's Drone Zone, whose name doesn't say so; "groove" should find Groove Salad.
        var byName = SearchOnceAsync(query, name: text, tag: query.Tag, cancel);
        var byTag = query.Tag is null
            ? SearchOnceAsync(query, name: null, tag: text.ToLowerInvariant(), cancel)
            : Task.FromResult<IReadOnlyList<RadioStation>>(Array.Empty<RadioStation>());
        var found = (await byName).Concat(await byTag).DistinctBy(station => station.Id).ToList();
        return WithoutDuplicates(RadioGuide.Sort(found, query.Sort)).Take(query.Limit).ToList();
    }

    /// <summary>How many playable stations each genre has, by tag, for the counts beside the genres.</summary>
    public async Task<IReadOnlyDictionary<string, int>> GenreCountsAsync(CancellationToken cancel)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var tags = await GetAsync<List<DirectoryCount>>("json/tags?order=stationcount&reverse=true&hidebroken=true&limit=2000", cancel);
        foreach (var genre in Genres.All)
        {
            // A genre's tag matches by substring, as its search does, so "house" counts "deep house" too.
            counts[genre.Tag] = tags.Where(tag => tag.Name.Contains(genre.Tag, StringComparison.OrdinalIgnoreCase)).Sum(tag => tag.StationCount);
        }

        return counts;
    }

    /// <summary>The countries with the most stations, by two-letter code, most first.</summary>
    public async Task<IReadOnlyList<RadioCountry>> CountriesAsync(CancellationToken cancel)
    {
        var countries = await GetAsync<List<DirectoryCountry>>("json/countries?order=stationcount&reverse=true&hidebroken=true", cancel);
        return countries
            .Where(country => country.Iso3166_1.Length == 2 && country.StationCount > 0)
            .Select(country => new RadioCountry(country.Iso3166_1.ToUpperInvariant(), country.Name, country.StationCount))
            .ToList();
    }

    /// <summary>
    /// Tells the directory a station was played, as Radio Browser asks apps to: it's how its listeners' favorites rise to
    /// the top of Popular. Problems are ignored; it's only a count.
    /// </summary>
    public async Task CountPlayAsync(RadioStation station)
    {
        if (!station.IsFromDirectory)
        {
            return;
        }

        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await GetAsync<JsonElement>($"json/url/{Uri.EscapeDataString(station.Id)}", timeout.Token);
        }
        catch (Exception)
        {
            // Not counted, that's all.
        }
    }

    private async Task<IReadOnlyList<RadioStation>> SearchOnceAsync(RadioQuery query, string? name, string? tag, CancellationToken cancel)
    {
        if (!query.VideoOnly)
        {
            return await SearchCodecAsync(query, name, tag, codec: null, cancel);
        }

        // The directory takes one format at a time, and lists video stations under their sound and picture together
        // ("AAC,H.264", "AAC+H.264", "MP3,H.264"…), so each of those is asked for, and the answers merged.
        var codecs = await VideoCodecsAsync(cancel);
        var answers = await Task.WhenAll(codecs.Select(codec => SearchCodecAsync(query, name, tag, codec, cancel)));
        return WithoutDuplicates(RadioGuide.Sort(answers.SelectMany(found => found).DistinctBy(station => station.Id), query.Sort))
            .Take(query.Limit)
            .ToList();
    }

    /// <summary>The formats the directory lists that name a picture, the most used first.</summary>
    private async Task<IReadOnlyList<string>> VideoCodecsAsync(CancellationToken cancel)
    {
        if (_videoCodecs is { } known)
        {
            return known;
        }

        var codecs = await GetAsync<List<DirectoryCount>>("json/codecs?order=stationcount&reverse=true&hidebroken=true", cancel);
        return _videoCodecs = codecs
            .Where(codec => codec.StationCount > 0 && RadioStation.IsVideoCodec(codec.Name))
            .Select(codec => codec.Name)
            .Take(12)
            .ToList();
    }

    private async Task<IReadOnlyList<RadioStation>> SearchCodecAsync(RadioQuery query, string? name, string? tag, string? codec, CancellationToken cancel)
    {
        var (order, reverse) = query.Sort switch
        {
            RadioSort.Votes => ("votes", true),
            RadioSort.Trending => ("clicktrend", true),
            RadioSort.Name => ("name", false),
            RadioSort.Bitrate => ("bitrate", true),
            _ => ("clickcount", true),
        };
        var parts = new List<string>
        {
            "hidebroken=true",
            $"order={order}",
            $"reverse={(reverse ? "true" : "false")}",
            // Asked for more than are shown, as the same station listed again is left out here.
            $"limit={query.Limit * 2}",
        };
        if (codec is not null)
        {
            parts.Add("codec=" + Uri.EscapeDataString(codec));
        }
        if (!string.IsNullOrWhiteSpace(name))
        {
            parts.Add("name=" + Uri.EscapeDataString(name));
        }

        if (!string.IsNullOrWhiteSpace(tag))
        {
            parts.Add("tag=" + Uri.EscapeDataString(tag));
        }

        if (!string.IsNullOrWhiteSpace(query.CountryCode))
        {
            parts.Add("countrycode=" + Uri.EscapeDataString(query.CountryCode));
        }

        if (query.MinBitrate > 0)
        {
            parts.Add("bitrateMin=" + query.MinBitrate.ToString(CultureInfo.InvariantCulture));
        }

        var stations = await GetAsync<List<DirectoryStation>>("json/stations/search?" + string.Join("&", parts), cancel);
        var playable = stations.Select(ToStation).Where(station => station.StreamUrl.Length > 0);
        return WithoutDuplicates(playable)
            .Take(query.Limit)
            .ToList();
    }

    /// <summary>
    /// Leaves out the stations listed more than once. Listeners add the same station again and again, so Groove Salad
    /// shows up as "SomaFM Groove Salad (256k MP3)", "Soma FM Groove Salad 256K MP3", and so on. A station counts as
    /// listed already when its stream is, or when its name (without the quality written in it) and its quality are.
    /// The first of each is kept, so the order decides which: the most played, when sorted by Popular.
    /// </summary>
    public static IEnumerable<RadioStation> WithoutDuplicates(IEnumerable<RadioStation> stations)
    {
        var streams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var station in stations)
        {
            var stream = StreamKey(station.StreamUrl);
            var name = $"{NameKey(station.Name)}|{station.Codec.Trim().ToUpperInvariant()}|{station.Bitrate}";
            // Both are noted either way, so a station that matches one copy by stream and another by name is one station.
            var seen = !streams.Add(stream) | !names.Add(name);
            if (!seen)
            {
                yield return station;
            }
        }
    }

    // "https://ice4.somafm.com/groovesalad-256-mp3/" and "http://ice4.somafm.com/groovesalad-256-mp3" are one stream.
    private static string StreamKey(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri)
            ? (uri.Host + uri.AbsolutePath.TrimEnd('/')).ToLowerInvariant()
            : address.Trim().ToLowerInvariant();

    /// <summary>A station's name without the quality written in it: "SomaFM Groove Salad (256k MP3)" and "Soma FM Groove Salad 256K MP3" are both "somafmgroovesalad".</summary>
    public static string NameKey(string name)
    {
        var plain = QualityInName.Replace(name.ToLowerInvariant(), " ");
        var key = new string(plain.Where(char.IsLetterOrDigit).ToArray());
        return key.Length > 0 ? key : name.Trim().ToLowerInvariant();
    }

    private static RadioStation ToStation(DirectoryStation station) => new()
    {
        Id = station.StationUuid,
        Name = station.Name.Trim(),
        StreamUrl = string.IsNullOrWhiteSpace(station.UrlResolved) ? station.Url.Trim() : station.UrlResolved.Trim(),
        Homepage = string.IsNullOrWhiteSpace(station.Homepage) ? null : station.Homepage.Trim(),
        Logo = string.IsNullOrWhiteSpace(station.Favicon) ? null : station.Favicon.Trim(),
        Tags = station.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(tag => tag.ToLowerInvariant())
            .Distinct()
            .ToList(),
        Country = station.Country.Trim(),
        CountryCode = station.CountryCode.Trim().ToUpperInvariant(),
        Codec = station.Codec.Trim(),
        Bitrate = station.Bitrate,
        Votes = station.Votes,
        Clicks = station.ClickCount,
        Trend = station.ClickTrend,
    };

    /// <summary>Asks a directory server, moving on to the next when one doesn't answer.</summary>
    private async Task<T> GetAsync<T>(string path, CancellationToken cancel)
    {
        var servers = await ServersAsync(cancel);
        Exception? last = null;
        List<string> order;
        lock (servers)
        {
            order = servers.ToList();
        }

        foreach (var server in order)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, $"https://{server}/{path}");
                request.Headers.UserAgent.ParseAdd(UserAgent);
                using var response = await _http.SendAsync(request, cancel);
                response.EnsureSuccessStatusCode();
                await using var body = await response.Content.ReadAsStreamAsync(cancel);
                return await JsonSerializer.DeserializeAsync<T>(body, JsonOptions, cancel)
                    ?? throw new InvalidDataException("The radio directory sent nothing back.");
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancel.IsCancellationRequested)
            {
                last = ex;
                // Try the others first next time.
                lock (servers)
                {
                    servers.Remove(server);
                    servers.Add(server);
                }
            }
        }

        throw new RadioProblem("Lucent couldn't reach the radio directory. Check your internet connection and try again.", last);
    }

    /// <summary>The directory's servers, in a random order (looked up once, then remembered).</summary>
    private async Task<List<string>> ServersAsync(CancellationToken cancel)
    {
        await _serverLock.WaitAsync(cancel);
        try
        {
            if (_servers is not null)
            {
                return _servers;
            }

            var names = new List<string>();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://all.api.radio-browser.info/json/servers");
                request.Headers.UserAgent.ParseAdd(UserAgent);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                using var response = await _http.SendAsync(request, timeout.Token);
                response.EnsureSuccessStatusCode();
                var servers = await JsonSerializer.DeserializeAsync<List<DirectoryServer>>(
                    await response.Content.ReadAsStreamAsync(timeout.Token), JsonOptions, timeout.Token);
                names.AddRange((servers ?? new()).Select(server => server.Name).Where(name => name.EndsWith(".radio-browser.info", StringComparison.OrdinalIgnoreCase)));
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                // The servers Radio Browser has run for years, if the list itself can't be had.
            }

            if (names.Count == 0)
            {
                names.AddRange(KnownServers);
            }

            _servers = names.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(_ => Random.Shared.Next()).ToList();
            return _servers;
        }
        finally
        {
            _serverLock.Release();
        }
    }

    // Radio Browser's JSON, as much of it as Lucent uses.
    private sealed class DirectoryStation
    {
        public string StationUuid { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Url { get; set; } = string.Empty;

        [JsonPropertyName("url_resolved")]
        public string UrlResolved { get; set; } = string.Empty;

        public string Homepage { get; set; } = string.Empty;

        public string Favicon { get; set; } = string.Empty;

        public string Tags { get; set; } = string.Empty;

        public string Country { get; set; } = string.Empty;

        public string CountryCode { get; set; } = string.Empty;

        public string Codec { get; set; } = string.Empty;

        public int Bitrate { get; set; }

        public int Votes { get; set; }

        public int ClickCount { get; set; }

        public int ClickTrend { get; set; }
    }

    private sealed class DirectoryCount
    {
        public string Name { get; set; } = string.Empty;

        public int StationCount { get; set; }
    }

    private sealed class DirectoryCountry
    {
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("iso_3166_1")]
        public string Iso3166_1 { get; set; } = string.Empty;

        public int StationCount { get; set; }
    }

    private sealed class DirectoryServer
    {
        public string Name { get; set; } = string.Empty;
    }
}

/// <summary>A country in the radio browser's country filter.</summary>
public sealed record RadioCountry(string Code, string Name, int Stations);

/// <summary>Something about online radio that went wrong, in words fit to show.</summary>
public sealed class RadioProblem : Exception
{
    public RadioProblem(string message, Exception? inner = null)
        : base(message, inner)
    {
    }

    /// <summary>True when the station was reached but sends a format Lucent's own radio player doesn't read.</summary>
    public bool Unreadable { get; init; }

    /// <summary>
    /// For an <see cref="Unreadable"/> station, the stream's address (past any playlist, when it got that far), for the
    /// video player (libVLC), which reads nearly every format, to try instead.
    /// </summary>
    public string? Address { get; init; }
}
