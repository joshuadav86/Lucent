using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinampInspiredVisualizer.Radio;

/// <summary>A show on a live station: its name ("Razor-N-Tape with JKriv"), and its picture's address when it has one.</summary>
public sealed record LiveShow(string Title, string? Picture);

/// <summary>
/// Who's on now, for live DJ stations whose streams don't say: The Lot Radio, from its published schedule, and NTS,
/// from the now-playing feed its own site uses. Other stations give null.
/// </summary>
/// <remarks>
/// Neither needs a key. The Lot's schedule page is read at most every ten minutes and each show's page once; NTS's
/// feed is small and read each time it's asked (the player asks every couple of minutes).
/// </remarks>
public sealed class LiveShows
{
    private const string LotSchedule = "https://www.thelotradio.com/calendar";
    private const string LotSite = "https://www.thelotradio.com";
    private const string NtsLive = "https://www.nts.live/api/v2/live";

    /// <summary>How long The Lot's schedule is used before it's read again.</summary>
    private static readonly TimeSpan ScheduleKept = TimeSpan.FromMinutes(10);

    /// <summary>How long a Lot show is taken to run when the schedule doesn't link the one after it.</summary>
    private static readonly TimeSpan UsualShow = TimeSpan.FromHours(1);

    private static readonly Regex LotShowLink = new(
        @"href=""(?:https://www\.thelotradio\.com)?(/shows/[a-z0-9\-]+/(\d{4})-(\d{2})-(\d{2})-(\d{2})(\d{2}))""[^>]*>(.*?)</a>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Tag = new("<[^>]+>", RegexOptions.Compiled);

    private readonly HttpClient _http;
    private readonly Dictionary<string, LiveShow> _lotShows = new();
    private (DateTime Read, List<LotSlot> Slots)? _lotSchedule;

    public LiveShows(HttpClient http)
    {
        _http = http;
    }

    /// <summary>True for stations Lucent can say the show on for.</summary>
    public static bool Knows(RadioStation station) => IsTheLot(station) || NtsChannel(station) is not null;

    /// <summary>The show on now, or null when it isn't known (between shows, a restream, or the schedule can't be read).</summary>
    public async Task<LiveShow?> NowAsync(RadioStation station, CancellationToken cancel)
    {
        if (IsTheLot(station))
        {
            return await LotNowAsync(DateTimeOffset.UtcNow, cancel);
        }

        if (NtsChannel(station) is { } channel)
        {
            var json = await _http.GetStringAsync(NtsLive, cancel);
            return NtsNow(json, channel);
        }

        return null;
    }

    internal static bool IsTheLot(RadioStation station) =>
        station.Id == "thelot:live"
        || (station.Homepage?.Contains("thelotradio.com", StringComparison.OrdinalIgnoreCase) ?? false);

    /// <summary>"1" or "2" for NTS's two live channels (their streams are …/stream and …/stream2), otherwise null.</summary>
    internal static string? NtsChannel(RadioStation station)
    {
        if (station.Id.StartsWith("nts:", StringComparison.OrdinalIgnoreCase))
        {
            return station.Id["nts:".Length..];
        }

        if (!Uri.TryCreate(station.StreamUrl, UriKind.Absolute, out var uri) || !uri.Host.EndsWith("ntslive.net", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var path = uri.AbsolutePath.TrimEnd('/');
        return path.EndsWith("stream2", StringComparison.OrdinalIgnoreCase) ? "2"
            : path.EndsWith("stream", StringComparison.OrdinalIgnoreCase) ? "1"
            : null;
    }

    // ---- NTS ----

    /// <summary>The show on an NTS channel, from its live feed: results[], each with channel_name and now.broadcast_title.</summary>
    internal static LiveShow? NtsNow(string json, string channel)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var result in results.EnumerateArray())
        {
            if (!result.TryGetProperty("channel_name", out var name) || name.ToString() != channel
                || !result.TryGetProperty("now", out var now) || now.ValueKind != JsonValueKind.Object
                || !now.TryGetProperty("broadcast_title", out var title) || title.GetString() is not { Length: > 0 } text)
            {
                continue;
            }

            string? picture = null;
            if (now.TryGetProperty("embeds", out var embeds) && embeds.ValueKind == JsonValueKind.Object
                && embeds.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object
                && details.TryGetProperty("media", out var media) && media.ValueKind == JsonValueKind.Object)
            {
                foreach (var size in new[] { "picture_large", "background_large", "picture_medium_large", "picture_medium" })
                {
                    if (media.TryGetProperty(size, out var address) && address.GetString() is { Length: > 0 } found && RadioGuide.IsStreamAddress(found))
                    {
                        picture = found;
                        break;
                    }
                }
            }

            return new LiveShow(Clean(text), picture);
        }

        return null;
    }

    // ---- The Lot Radio ----

    private sealed record LotSlot(string Path, DateTimeOffset Start, string LinkText);

    private async Task<LiveShow?> LotNowAsync(DateTimeOffset now, CancellationToken cancel)
    {
        if (_lotSchedule is not { } schedule || DateTime.UtcNow - schedule.Read > ScheduleKept)
        {
            var html = await _http.GetStringAsync(LotSchedule, cancel);
            schedule = (DateTime.UtcNow, LotSlots(html));
            _lotSchedule = schedule;
        }

        if (LotSlotAt(schedule.Slots, now) is not { } slot)
        {
            return null;
        }

        if (_lotShows.TryGetValue(slot.Path, out var known))
        {
            return known;
        }

        // The show's own page names it plainly ("Razor-N-Tape with JKriv - The Lot Radio") and may have its picture.
        var show = new LiveShow(slot.LinkText, null);
        try
        {
            var page = await _http.GetStringAsync(LotSite + slot.Path, cancel);
            show = LotShowPage(page, slot.LinkText);
        }
        catch (Exception) when (!cancel.IsCancellationRequested)
        {
            // The schedule's own words, then.
        }

        if (show.Title.Length == 0)
        {
            return null;
        }

        _lotShows[slot.Path] = show;
        return show;
    }

    /// <summary>The shows The Lot's schedule links to, each with when it starts (its address says, in New York time), in order.</summary>
    private static List<LotSlot> LotSlots(string html)
    {
        var newYork = NewYork();
        var slots = new Dictionary<string, LotSlot>();
        foreach (Match match in LotShowLink.Matches(html))
        {
            var path = match.Groups[1].Value;
            if (slots.ContainsKey(path))
            {
                continue;
            }

            var numbers = Enumerable.Range(2, 5).Select(group => int.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture)).ToArray();
            DateTime local;
            try
            {
                local = new DateTime(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], 0, DateTimeKind.Unspecified);
            }
            catch (ArgumentOutOfRangeException)
            {
                continue;
            }

            var start = new DateTimeOffset(local, newYork.GetUtcOffset(local));
            slots[path] = new LotSlot(path, start, Clean(Tag.Replace(match.Groups[7].Value, " ")));
        }

        return slots.Values.OrderBy(slot => slot.Start).ToList();
    }

    // The show that started last, as long as the next hasn't started and it hasn't run past the usual length when the
    // next isn't linked (the schedule leaves some shows and the restreams unlinked).
    private static LotSlot? LotSlotAt(IReadOnlyList<LotSlot> slots, DateTimeOffset now)
    {
        for (var i = slots.Count - 1; i >= 0; i--)
        {
            if (slots[i].Start > now)
            {
                continue;
            }

            var end = i + 1 < slots.Count ? slots[i + 1].Start : slots[i].Start + UsualShow;
            return now < end && now - slots[i].Start < TimeSpan.FromHours(4) ? slots[i] : null;
        }

        return null;
    }

    /// <summary>A Lot show's name and picture from its page: its title (less " - The Lot Radio") and og:image.</summary>
    private static LiveShow LotShowPage(string html, string fallback)
    {
        var title = Meta(html, "og:title");
        if (title is null && Regex.Match(html, "<title[^>]*>(.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline) is { Success: true } found)
        {
            title = found.Groups[1].Value;
        }

        title = title is null ? fallback : Clean(title);
        foreach (var suffix in new[] { " - The Lot Radio", " | The Lot Radio", " – The Lot Radio" })
        {
            if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                title = title[..^suffix.Length].Trim();
            }
        }

        if (title.Length == 0 || title.Equals("The Lot Radio", StringComparison.OrdinalIgnoreCase))
        {
            title = fallback;
        }

        var picture = Meta(html, "og:image");
        return new LiveShow(title, picture is not null && RadioGuide.IsStreamAddress(picture) ? picture : null);
    }

    // A <meta property="…" content="…"> value, with its attributes either way round.
    private static string? Meta(string html, string property)
    {
        var name = Regex.Escape(property);
        var match = Regex.Match(html, $@"<meta[^>]+(?:property|name)=""{name}""[^>]+content=""([^""]*)""", RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(html, $@"<meta[^>]+content=""([^""]*)""[^>]+(?:property|name)=""{name}""", RegexOptions.IgnoreCase);
        }

        return match.Success && match.Groups[1].Value.Length > 0 ? WebUtility.HtmlDecode(match.Groups[1].Value) : null;
    }

    private static TimeZoneInfo NewYork()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (Exception)
            {
                // Try the other name.
            }
        }

        return TimeZoneInfo.Utc;
    }

    private static string Clean(string text) =>
        string.Join(' ', WebUtility.HtmlDecode(text).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
