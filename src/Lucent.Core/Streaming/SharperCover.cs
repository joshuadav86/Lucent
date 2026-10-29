using System.Net.Http;
using System.Text.RegularExpressions;
using Lucent.Library;
using Lucent.Radio;

namespace Lucent.Streaming;

/// <summary>
/// A sharper cover for the song another app is playing, when the one the app gives is small. Windows' media controls
/// only pass on a thumbnail (Spotify's, Apple Music's and TIDAL's are about 300 pixels), Spotify's Mac app gives the
/// address of its 300-pixel copy, and web players often show a smaller copy than their image server keeps.
/// </summary>
/// <remarks>
/// The same picture is asked for at a larger size from the server it came from first; failing that, when the cover is
/// still small (or there is none), the song's cover in the iTunes Store, found by title, artist and album as radio
/// songs' are (<see cref="StationArt"/>). Neither needs a key or an account. When neither has anything better, the
/// app's own cover stays.
/// </remarks>
public sealed partial class SharperCover
{
    /// <summary>Covers at least this many pixels wide are sharp enough, even in a full-screen artwork window.</summary>
    public const int SharpWidth = 1000;

    /// <summary>Below this many pixels wide, a cover is looked up in the iTunes Store when its own server has no larger copy.</summary>
    private const int LookUpBelow = 600;

    /// <summary>How large a cover is fetched from the iTunes Store, in pixels square.</summary>
    private const int StoreSize = 1200;

    /// <summary>Larger pictures than this aren't taken.</summary>
    private const int MaxBytes = 8_000_000;

    private readonly HttpClient _http;
    private readonly StationArt _store;

    public SharperCover(HttpClient http)
    {
        _http = http;
        _store = new StationArt(http, StoreSize);
    }

    /// <summary>True when <paramref name="cover"/> is wide enough not to need a sharper copy.</summary>
    public static bool IsSharp(byte[]? cover) => cover is not null && ImageFormats.WidthOf(cover) >= SharpWidth;

    /// <summary>
    /// A larger copy of <paramref name="track"/>'s cover than <paramref name="shown"/> (the app's own, or null when it
    /// gave none), or null when there's nothing better.
    /// </summary>
    public async Task<byte[]?> FindAsync(RemoteTrack track, byte[]? shown, CancellationToken cancel)
    {
        var width = shown is null ? 0 : ImageFormats.WidthOf(shown);
        if (width >= SharpWidth)
        {
            return null;
        }

        if (track.ArtworkUrl is { } url)
        {
            foreach (var larger in LargerCopies(url))
            {
                byte[]? picture;
                try
                {
                    picture = await DownloadAsync(larger, cancel);
                }
                catch (Exception) when (!cancel.IsCancellationRequested)
                {
                    continue;
                }

                if (IsBetter(picture, shown, width))
                {
                    return picture;
                }
            }
        }

        if (width >= LookUpBelow || track.Title.Trim().Length == 0)
        {
            return null;
        }

        var found = await _store.SongCoverAsync(track.Artist, track.Title, cancel, track.Album);
        return IsBetter(found, shown, width) ? found : null;
    }

    // Any picture beats none; otherwise only a wider one.
    private static bool IsBetter(byte[]? picture, byte[]? shown, int shownWidth) =>
        picture is not null && (shown is null || ImageFormats.WidthOf(picture) > shownWidth);

    /// <summary>
    /// The same picture as <paramref name="url"/> at larger sizes, largest first, from the image servers the streaming
    /// apps use, or none for a server Lucent doesn't know. Not every size exists for every picture: one that doesn't
    /// fails to download, and the next is tried.
    /// </summary>
    internal static IEnumerable<string> LargerCopies(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            yield break;
        }

        var host = uri.Host.ToLowerInvariant();

        // Spotify: i.scdn.co/image/ab67616d0000b273…, where the four characters before the picture's ID are its size.
        // An album's are 82c1 (the full size, which not every album has), b273 (640), 1e02 (300) and 4851 (64); a
        // podcast's are ba8a (640), 5f1f (300) and f68d (64).
        if (host == "i.scdn.co" && SpotifyImage().Match(url) is { Success: true } spotify)
        {
            var sizes = spotify.Groups["kind"].Value == "ab67616d" ? new[] { "82c1", "b273" } : new[] { "ba8a" };
            var size = spotify.Groups["size"];
            foreach (var larger in sizes.TakeWhile(each => !each.Equals(size.Value, StringComparison.OrdinalIgnoreCase)))
            {
                yield return url[..size.Index] + larger + url[(size.Index + size.Length)..];
            }

            yield break;
        }

        // Apple Music: is1-ssl.mzstatic.com/image/thumb/…/600x600bb.jpg, which makes any size up to the original.
        if (host.EndsWith(".mzstatic.com", StringComparison.Ordinal) && AppleImage().Match(url) is { Success: true } apple
            && int.Parse(apple.Groups["width"].Value) < StoreSize)
        {
            yield return $"{apple.Groups["path"].Value}{StoreSize}x{StoreSize}bb.jpg";
            yield break;
        }

        // TIDAL: resources.tidal.com/images/…/640x640.jpg, kept at 80, 160, 320, 640 and 1280.
        if (host == "resources.tidal.com" && SizedPath().Match(url) is { Success: true } tidal && int.Parse(tidal.Groups["width"].Value) < 1280)
        {
            yield return $"{tidal.Groups["path"].Value}1280x1280.jpg";
            if (int.Parse(tidal.Groups["width"].Value) < 640)
            {
                yield return $"{tidal.Groups["path"].Value}640x640.jpg";
            }

            yield break;
        }

        // Pandora: …/_500W_500H.jpg, kept at up to 1080 by 1080.
        if ((host.EndsWith("p-cdn.com", StringComparison.Ordinal) || host.EndsWith("pandora.com", StringComparison.Ordinal))
            && PandoraSize().Match(url) is { Success: true } pandora && int.Parse(pandora.Groups["width"].Value) < 1080)
        {
            yield return url[..pandora.Index] + "1080W_1080H" + url[(pandora.Index + pandora.Length)..];
            if (int.Parse(pandora.Groups["width"].Value) < 500)
            {
                yield return url[..pandora.Index] + "500W_500H" + url[(pandora.Index + pandora.Length)..];
            }

            yield break;
        }

        // YouTube Music: lh3.googleusercontent.com/…=w544-h544-l90-rj (or =s544), which makes any size asked for.
        if ((host.EndsWith("googleusercontent.com", StringComparison.Ordinal) || host.EndsWith("ggpht.com", StringComparison.Ordinal))
            && GoogleSize().Match(url) is { Success: true } google)
        {
            var width = int.Parse(google.Groups["width"].Value);
            if (width < StoreSize)
            {
                var asked = google.Groups["height"].Success ? $"w{StoreSize}-h{StoreSize}" : $"s{StoreSize}";
                yield return url[..google.Index] + asked + url[(google.Index + google.Length)..];
            }

            yield break;
        }

        // YouTube: i.ytimg.com/vi/<video>/hqdefault.jpg (480 by 360), kept larger as maxresdefault (1280 by 720, for
        // videos uploaded in HD) and sddefault (640 by 480).
        if (host == "i.ytimg.com" && YouTubeThumb().Match(uri.AbsolutePath) is { Success: true } youtube && youtube.Groups["name"].Value != "maxresdefault")
        {
            var video = youtube.Groups["video"].Value;
            yield return $"https://i.ytimg.com/vi/{video}/maxresdefault.jpg";
            if (youtube.Groups["name"].Value != "sddefault")
            {
                yield return $"https://i.ytimg.com/vi/{video}/sddefault.jpg";
            }

            yield break;
        }

        // SoundCloud: i1.sndcdn.com/artworks-…-large.jpg (100), -t300x300 or -t500x500, the largest it keeps.
        if (host.EndsWith("sndcdn.com", StringComparison.Ordinal) && SoundCloudSize().Match(url) is { Success: true } soundCloud
            && soundCloud.Value != "-t500x500.")
        {
            yield return url[..soundCloud.Index] + "-t500x500." + url[(soundCloud.Index + soundCloud.Length)..];
        }
    }

    private async Task<byte[]?> DownloadAsync(string address, CancellationToken cancel)
    {
        using var response = await _http.GetAsync(address, HttpCompletionOption.ResponseHeadersRead, cancel);
        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxBytes)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancel);
        return bytes.Length is > 0 and <= MaxBytes ? bytes : null;
    }

    [GeneratedRegex("^https://i\\.scdn\\.co/image/(?<kind>ab67616d|ab676563)0000(?<size>[0-9a-f]{4})[0-9a-f]+$", RegexOptions.IgnoreCase)]
    private static partial Regex SpotifyImage();

    [GeneratedRegex("^(?<path>https://[^?#]+/)(?<width>\\d+)x\\d+[a-z]*(-\\d+)?\\.(jpg|jpeg|png|webp)$", RegexOptions.IgnoreCase)]
    private static partial Regex AppleImage();

    [GeneratedRegex("^(?<path>https://[^?#]+/)(?<width>\\d+)x\\d+\\.jpg$", RegexOptions.IgnoreCase)]
    private static partial Regex SizedPath();

    [GeneratedRegex("(?<width>\\d+)W_\\d+H")]
    private static partial Regex PandoraSize();

    [GeneratedRegex("(?<==)(w(?<width>\\d+)-h(?<height>\\d+)|s(?<width>\\d+))(?=-|$)")]
    private static partial Regex GoogleSize();

    [GeneratedRegex("^/vi(_webp)?/(?<video>[A-Za-z0-9_-]+)/(?<name>default|mqdefault|hqdefault|sddefault|hq720|maxresdefault)\\.(jpg|webp)$")]
    private static partial Regex YouTubeThumb();

    [GeneratedRegex("-(large|t\\d+x\\d+)\\.(?=(jpg|png)(\\?|$))")]
    private static partial Regex SoundCloudSize();
}
