using Lucent.Library;
using Lucent.Radio;

namespace Lucent.Streaming;

/// <summary>
/// For the self-test: checks the larger copies <see cref="SharperCover"/> asks the streaming apps' image servers for,
/// that a picture's width is read from its header, and that the iTunes Store's copy on the playing album is preferred.
/// Nothing is downloaded.
/// </summary>
public static class CoverSelfTest
{
    /// <summary>Returns the number of problems.</summary>
    public static int Run(Action<string> log)
    {
        var failures = 0;
        void Check(string what, bool ok, string got)
        {
            if (!ok)
            {
                log($"::error::Sharper covers: {what}: got {got}.");
                failures++;
            }
        }

        void Copies(string url, params string[] expected)
        {
            var got = SharperCover.LargerCopies(url).ToArray();
            Check(url, got.SequenceEqual(expected), got.Length == 0 ? "nothing" : string.Join(", ", got));
        }

        try
        {
            const string spotify = "https://i.scdn.co/image/ab67616d00001e02";
            const string id = "0123456789abcdef01234567";
            Copies(spotify + id, "https://i.scdn.co/image/ab67616d000082c1" + id, "https://i.scdn.co/image/ab67616d0000b273" + id);
            Copies("https://i.scdn.co/image/ab67616d0000b273" + id, "https://i.scdn.co/image/ab67616d000082c1" + id);
            Copies("https://i.scdn.co/image/ab6765630000f68d" + id, "https://i.scdn.co/image/ab6765630000ba8a" + id);
            Copies("https://is1-ssl.mzstatic.com/image/thumb/Music126/v4/aa/bb/cc/x.jpg/600x600bb.jpg",
                "https://is1-ssl.mzstatic.com/image/thumb/Music126/v4/aa/bb/cc/x.jpg/1200x1200bb.jpg");
            Copies("https://resources.tidal.com/images/aa/bb/cc/320x320.jpg",
                "https://resources.tidal.com/images/aa/bb/cc/1280x1280.jpg", "https://resources.tidal.com/images/aa/bb/cc/640x640.jpg");
            Copies("https://content-images.p-cdn.com/images/aa/bb/cc/dd/abc_500W_500H.jpg",
                "https://content-images.p-cdn.com/images/aa/bb/cc/dd/abc_1080W_1080H.jpg");
            Copies("https://lh3.googleusercontent.com/abc-def=w544-h544-l90-rj", "https://lh3.googleusercontent.com/abc-def=w1200-h1200-l90-rj");
            Copies("https://i.ytimg.com/vi/dQw4w9WgXcQ/hqdefault.jpg?sqp=abc",
                "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg", "https://i.ytimg.com/vi/dQw4w9WgXcQ/sddefault.jpg");
            Copies("https://i1.sndcdn.com/artworks-000-abc-large.jpg", "https://i1.sndcdn.com/artworks-000-abc-t500x500.jpg");
            Copies("https://i1.sndcdn.com/artworks-000-abc-t500x500.jpg");
            Copies("https://example.com/cover-300x300.jpg");

            // A PNG's header, and a JPEG's with a start-of-frame segment after its JFIF one.
            byte[] png = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R', 0, 0, 2, 0x58, 0, 0, 2, 0x58];
            byte[] jpeg =
            [
                0xFF, 0xD8, 0xFF, 0xE0, 0, 16, (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0, 1, 1, 0, 0, 1, 0, 1, 0, 0,
                0xFF, 0xC0, 0, 17, 8, 0x01, 0x2C, 0x04, 0xB0, 3, 1, 0x22, 0, 2, 0x11, 1, 3, 0x11, 1,
            ];
            Check("PNG width", ImageFormats.WidthOf(png) == 600, ImageFormats.WidthOf(png).ToString());
            Check("JPEG width", ImageFormats.WidthOf(jpeg) == 1200, ImageFormats.WidthOf(jpeg).ToString());
            Check("JPEG is sharp", SharperCover.IsSharp(jpeg) && !SharperCover.IsSharp(png), "the wrong answer");

            const string store = """
                {"results":[
                  {"artistName":"Justice","trackName":"Neverender","collectionName":"Neverender - Single","artworkUrl100":"https://a/single/100x100bb.jpg"},
                  {"artistName":"Justice","trackName":"Neverender","collectionName":"Hyperdrama","artworkUrl100":"https://a/album/100x100bb.jpg"}
                ]}
                """;
            var best = StationArt.BestCover(store, "Justice", "Neverender", "Hyperdrama", 1200);
            Check("album preferred", best == "https://a/album/1200x1200bb.jpg", best ?? "nothing");
            best = StationArt.BestCover(store, "Justice", "Neverender");
            Check("first fit without an album", best == "https://a/single/600x600bb.jpg", best ?? "nothing");
        }
        catch (Exception ex)
        {
            log($"::error::Sharper covers: {ex.GetType().Name}: {ex.Message}");
            return failures + 1;
        }

        if (failures == 0)
        {
            log("Sharper covers: larger copies, picture widths and album matches as expected");
        }

        return failures;
    }
}
