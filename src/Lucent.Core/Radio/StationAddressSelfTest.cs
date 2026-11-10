using System.Net;
using System.Net.Http;
using System.Text;

namespace Lucent.Radio;

/// <summary>
/// The apps' --self-test check of Add a station's address box (<see cref="StationAddress"/>), against made-up pages
/// served from memory: addresses typed without https://, a station's page that links its stream, one that doesn't
/// (searched for by name), a directory's home page (no station to search for), words, and a station that answers
/// only http://.
/// </summary>
public static class StationAddressSelfTest
{
    private const string DirectoryHome =
        """
        <html><head><title>radio.net - Radio online | Listen to live radio, podcasts</title>
        <meta property="og:site_name" content="radio.net"></head>
        <body><script>{"streams":[{"url":"https://stream.example.net/top-station.mp3"}]}</script></body></html>
        """;

    private const string DirectoryStation =
        """
        <html><head><meta property="og:title" content="Listen to FRISKY live | radio.net" />
        <meta property="og:site_name" content="radio.net" /></head>
        <body><script>window.station={"streams":[{"url":"https://stream.frisky.example/frisky_mp3_hi"}]}</script></body></html>
        """;

    private const string StationPage =
        """
        <html><head><title>SomaFM: Groove Salad: A nicely chilled plate of ambient/downtempo beats and grooves.</title></head>
        <body><a href="/groovesalad256.pls">256k MP3</a> <a href='/groovesalad130.pls'>128k AAC</a></body></html>
        """;

    private const string StationPageWithoutLinks =
        """
        <html><head><title>SomaFM: Groove Salad: A nicely chilled plate of ambient/downtempo beats and grooves.</title></head>
        <body><div id="player"></div></body></html>
        """;

    private const string StationHome =
        """
        <html><head><title>FRISKY | Feel good electronic music</title></head>
        <body><audio controls src="https://stream.frisky.example/frisky_mp3_hi"></audio></body></html>
        """;

    private const string StationHomeWithoutPlayer =
        """
        <html><head><title>Feel good electronic music | FRISKY</title></head><body></body></html>
        """;

    private const string VideoOnly =
        """
        <html><head><meta property="og:title" content="Night Drive - Live"></head>
        <body><video><source src="/clips/intro.mp4" type="video/mp4"></video>
        <audio><source src="https://live.example.org/nightdrive.aac" type="audio/aac"></audio></body></html>
        """;

    /// <summary>Returns the number of problems, after writing what it found (GitHub's ::error:: lines for problems).</summary>
    public static int Run(Action<string> log)
    {
        var server = new FakeServer();
        server.Page("https://radio.net/", DirectoryHome, finalAddress: "https://www.radio.net/");
        server.Page("https://radio.net/s/friskyradio", DirectoryStation);
        server.Page("https://somafm.com/groovesalad/", StationPage);
        server.Page("https://somafm.example/groovesalad/", StationPageWithoutLinks);
        server.Page("https://frisky.example/", StationHome);
        server.Page("https://www.frisky.example/", StationHomeWithoutPlayer);
        server.Page("https://nightdrive.example/live", VideoOnly);
        server.Page("https://blocked.example/station", "<html><title>Access denied</title></html>", HttpStatusCode.Forbidden);
        server.Stream("http://ice.example.com:8000/stream");
        server.Stream("https://ice1.somafm.com/groovesalad-256-mp3");

        var problems = 0;
        void Expect(string typed, string? name, StationLookup expected)
        {
            StationLookup found;
            try
            {
                found = StationAddress.LookUpAsync(new HttpClient(server), typed, name, "Lucent self-test", CancellationToken.None)
                    .GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                problems++;
                log($"::error::Add a station: \"{typed}\" failed: {ex.GetType().Name}: {ex.Message}");
                return;
            }

            var same = found.Address == expected.Address && found.Search == expected.Search && (found.Problem is null) == (expected.Problem is null);
            if (!same)
            {
                problems++;
                log($"::error::Add a station: \"{typed}\" gave {Show(found)}, not {Show(expected)}.");
            }
        }

        // A directory's home page: no station to search for, and not a stream hidden in its scripts.
        Expect("radio.net", null, new StationLookup { Problem = "" });
        // A directory's station page: searched for by the station's name in its title.
        Expect("radio.net/s/friskyradio", null, new StationLookup { Search = "FRISKY" });
        // A station's page that links its playlist, made whole.
        Expect("https://somafm.com/groovesalad/", null, new StationLookup { Address = "https://somafm.com/groovesalad256.pls" });
        // A station's page that doesn't: the part of the title that isn't the site's name.
        Expect("somafm.example/groovesalad/", null, new StationLookup { Search = "Groove Salad" });
        // A name typed for the station is searched for instead.
        Expect("somafm.example/groovesalad/", "Groove Salad 2", new StationLookup { Search = "Groove Salad 2" });
        // A station's home page with a player, and one without (searched for by the site's name).
        Expect("frisky.example", null, new StationLookup { Address = "https://stream.frisky.example/frisky_mp3_hi" });
        Expect("www.frisky.example", null, new StationLookup { Search = "FRISKY" });
        // The audio player's source, not the video's.
        Expect("https://nightdrive.example/live", null, new StationLookup { Address = "https://live.example.org/nightdrive.aac" });
        // A page that turns Lucent away says so.
        Expect("https://blocked.example/station", null, new StationLookup { Problem = "" });
        // A stream that answers only http://.
        Expect("ice.example.com:8000/stream", null, new StationLookup { Address = "http://ice.example.com:8000/stream" });
        // A stream typed in full plays as it is, and a playlist isn't even looked at.
        Expect("https://ice1.somafm.com/groovesalad-256-mp3", null, new StationLookup { Address = "https://ice1.somafm.com/groovesalad-256-mp3" });
        Expect("https://somafm.com/groovesalad256.pls", null, new StationLookup { Address = "https://somafm.com/groovesalad256.pls" });
        // Words are searched for, and other kinds of address aren't played.
        Expect("frisky", null, new StationLookup { Search = "frisky" });
        Expect("groove salad", null, new StationLookup { Search = "groove salad" });
        Expect("rtmp://live.example.org/radio", null, new StationLookup { Problem = "" });

        if (server.Asked.Contains("https://somafm.com/groovesalad256.pls"))
        {
            problems++;
            log("::error::Add a station: a playlist's address was looked at before playing it.");
        }

        if (problems == 0)
        {
            log("Add a station: addresses without https://, station pages, directory pages, and words all found the right thing");
        }

        return problems;
    }

    private static string Show(StationLookup found) =>
        found.Address is { } address ? $"the stream {address}"
        : found.Search is { } search ? $"a search for \"{search}\""
        : found.Problem is { } problem ? $"the problem \"{problem}\""
        : "nothing";

    /// <summary>Answers from memory, as the stations' and directories' servers would. Anything else doesn't connect.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> _answers = new();

        public List<string> Asked { get; } = new();

        public void Page(string address, string html, HttpStatusCode status = HttpStatusCode.OK, string? finalAddress = null) =>
            _answers[address] = request => new HttpResponseMessage(status)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html"),
                // As if redirected there.
                RequestMessage = new HttpRequestMessage(HttpMethod.Get, finalAddress ?? address),
            };

        public void Stream(string address) =>
            _answers[address] = request =>
            {
                var content = new ByteArrayContent(new byte[4096]);
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("audio/mpeg");
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request };
            };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var address = request.RequestUri!.ToString();
            Asked.Add(address);
            return _answers.TryGetValue(address, out var answer)
                ? Task.FromResult(answer(request))
                : Task.FromException<HttpResponseMessage>(new HttpRequestException($"Couldn't connect to {request.RequestUri.Host}."));
        }
    }
}
