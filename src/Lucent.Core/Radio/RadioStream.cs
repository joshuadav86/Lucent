using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using NAudio.Wave;

namespace WinampInspiredVisualizer.Radio;

/// <summary>
/// An online radio station, playing: its MP3 or AAC stream (or HLS pieces, see <see cref="HlsStream"/>) decoded as it arrives, with a few seconds held in hand so a
/// slow moment on the network doesn't stop the music. The player plays it like a track that never ends, so the
/// equalizer, the visuals, the meter and the analyzer all get it as they do a file.
/// </summary>
/// <remarks>
/// Shoutcast and Icecast stations send the song that's on in the stream itself (ICY metadata); that's read off as it
/// goes by and reported with <see cref="TitleChanged"/>. When the connection drops, it reconnects by itself a few
/// times, playing silence meanwhile, before giving up. The decoders are written in C# (see <see cref="RadioDecoder"/>),
/// so a station sounds the same on every platform.
/// </remarks>
public sealed class RadioStream : WaveStream, ILiveStream
{
    /// <summary>How much audio is held in hand before playing starts, and again after the stream ran dry.</summary>
    private static readonly TimeSpan Prebuffer = TimeSpan.FromSeconds(1.5);

    /// <summary>How much decoded audio is held at most. A live stream never gets this far ahead; HLS would.</summary>
    private static readonly TimeSpan Hold = TimeSpan.FromSeconds(12);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private const int MaxReconnects = 4;

    private readonly HttpClient _http;
    private readonly string _address;
    private readonly string _userAgent;
    private readonly string? _codecHint;
    private readonly BufferedWaveProvider _buffer;
    private readonly CancellationTokenSource _stop = new();
    private readonly int _prebufferBytes;
    private readonly int _holdBytes;
    private Connection? _connection;
    private Task? _reading;
    private long _position;
    private volatile bool _ended;
    private volatile bool _rebuffering = true;
    private string? _nowPlaying;
    private string? _heardTitle;
    private volatile ComingTitle? _comingTitle;

    private RadioStream(HttpClient http, Connection connection, string address, string userAgent, string? codec)
    {
        _codecHint = codec;
        _http = http;
        _connection = connection;
        _address = address;
        _userAgent = userAgent;
        var format = connection.Decoder.Format;
        _buffer = new BufferedWaveProvider(format)
        {
            BufferDuration = TimeSpan.FromSeconds(20),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        _prebufferBytes = (int)(format.AverageBytesPerSecond * Prebuffer.TotalSeconds) / format.BlockAlign * format.BlockAlign;
        _holdBytes = (int)Math.Min(_buffer.BufferLength, format.AverageBytesPerSecond * Hold.TotalSeconds);
        Codec = connection.Decoder.Codec;
        StationName = connection.StationName;
        Genre = connection.Genre;
        Bitrate = connection.Bitrate;
    }

    /// <summary>
    /// Raised (on a background thread) when the station says a new song is on, with its title as the station
    /// writes it, usually "Artist - Title".
    /// </summary>
    public event Action<string>? TitleChanged;

    /// <summary>
    /// Raised (on a background thread) when the station has gone for good: it stopped sending and reconnecting
    /// didn't bring it back. The player runs to the end of what's left, and stops.
    /// </summary>
    public event Action<Exception?>? Lost;

    /// <summary>The name the stream gives itself, if it says.</summary>
    public string? StationName { get; }

    /// <summary>The genre the stream gives itself, if it says.</summary>
    public string? Genre { get; }

    /// <summary>Kilobits per second.</summary>
    public int Bitrate { get; }

    /// <summary>"MP3", "AAC" or "AAC+".</summary>
    public string Codec { get; }

    WaveStream ILiveStream.Sound => this;

    /// <summary>The song that's on, as the station last said, or null when it doesn't say.</summary>
    public string? NowPlaying => _nowPlaying;

    /// <summary>True while it's waiting for the stream to catch up (playing silence).</summary>
    public bool IsBuffering => _rebuffering && !_ended;

    public override WaveFormat WaveFormat => _buffer.WaveFormat;

    public override bool CanSeek => false;

    /// <summary>Nothing: a station has no end.</summary>
    public override long Length => 0;

    /// <summary>How much has been played since it was tuned in, in bytes. It can't be moved.</summary>
    public override long Position
    {
        get => Interlocked.Read(ref _position);
        set
        {
            // A live stream can't be moved through; the player's rewind on Stop lands here and does nothing.
        }
    }

    /// <summary>
    /// Tunes in: finds the stream (through a .pls or .m3u playlist if that's what the address is), checks it's MP3 or AAC,
    /// and waits for the first moments of audio. Throws <see cref="RadioProblem"/> with words fit to show when the
    /// station can't be played. <paramref name="codec"/> is what the directory says the stream is ("AAC", say), for
    /// servers that don't say themselves.
    /// </summary>
    public static async Task<RadioStream> OpenAsync(string address, string userAgent, CancellationToken cancel, string? codec = null)
    {
        var http = CreateClient();
        try
        {
            var connection = await Connection.OpenAsync(http, address, userAgent, codec, cancel);
            var stream = new RadioStream(http, connection, address, userAgent, codec);
            try
            {
                stream._reading = Task.Factory.StartNew(stream.ReadLoop, TaskCreationOptions.LongRunning);
                await stream.WaitForAudioAsync(cancel);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
        catch (Exception) when (cancel.IsCancellationRequested)
        {
            http.Dispose();
            throw new OperationCanceledException(cancel);
        }
        catch (Exception ex) when (ex is not RadioProblem)
        {
            http.Dispose();
            throw new RadioProblem(Describe(ex), ex);
        }
        catch (RadioProblem problem) when (problem is { Unreadable: true, Address: null })
        {
            http.Dispose();
            throw new RadioProblem(problem.Message, problem) { Unreadable = true, Address = address };
        }
        catch
        {
            http.Dispose();
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var available = _buffer.BufferedBytes;
        if (_ended && available == 0)
        {
            return 0;
        }

        // Ran dry: silence until a moment's worth has arrived again, so it doesn't stutter in and out.
        if (!_ended && (available < count || (_rebuffering && available < _prebufferBytes)))
        {
            _rebuffering = true;
            Array.Clear(buffer, offset, count);
            Interlocked.Add(ref _position, count);
            return count;
        }

        _rebuffering = false;
        var read = _buffer.Read(buffer, offset, count);
        var position = Interlocked.Add(ref _position, read);
        // The song changes when its sound is heard, not when it arrived, which with HLS can be many seconds before.
        if (_comingTitle is { } coming && position >= coming.At)
        {
            _comingTitle = null;
            _nowPlaying = coming.Title;
            TitleChanged?.Invoke(coming.Title);
        }

        return read;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_stop.IsCancellationRequested)
        {
            _stop.Cancel();
            Interlocked.Exchange(ref _connection, null)?.Dispose();
            _http.Dispose();
            _ended = true;
        }

        base.Dispose(disposing);
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectTimeout = ConnectTimeout,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8,
            ConnectCallback = async (context, cancel) =>
            {
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                try
                {
                    await socket.ConnectAsync(context.DnsEndPoint, cancel);
                    return new IcyStatusStream(new NetworkStream(socket, ownsSocket: true));
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }
            },
        };

        // No overall timeout: the stream is read for as long as the station plays. Connecting has its own.
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }

    private async Task WaitForAudioAsync(CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(ConnectTimeout);
        while (_buffer.BufferedBytes < _prebufferBytes)
        {
            if (_ended)
            {
                throw new RadioProblem("The station stopped sending before any music arrived. It may be off the air.");
            }

            try
            {
                await Task.Delay(50, timeout.Token);
            }
            catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
            {
                // Slow, but something arrived: play what there is, and let it catch up.
                if (_buffer.BufferedBytes > 0)
                {
                    return;
                }

                throw new RadioProblem("The station connected but didn't send any music. It may be off the air.");
            }
        }
    }

    // On its own thread: decodes frames as they arrive, reconnecting when the stream drops.
    private void ReadLoop()
    {
        var pcm = new byte[RadioDecoder.MaxFrameBytes];
        var reconnects = 0;
        Exception? problem = null;
        var connection = _connection;
        while (!_stop.IsCancellationRequested && connection is not null)
        {
            try
            {
                int bytes;
                while (!_stop.IsCancellationRequested && (bytes = connection.Decoder.Next(pcm)) >= 0)
                {
                    if (bytes > 0)
                    {
                        // HLS arrives a piece at a time, faster than it plays: it waits here for room rather than lose any.
                        while (_buffer.BufferedBytes + bytes > _holdBytes && !_stop.IsCancellationRequested)
                        {
                            _stop.Token.WaitHandle.WaitOne(50);
                        }

                        _buffer.AddSamples(pcm, 0, bytes);
                        UpdateTitle(bytes);
                        reconnects = 0;
                    }
                }

                problem = null;
            }
            catch (Exception ex)
            {
                problem = ex;
            }

            if (_stop.IsCancellationRequested)
            {
                break;
            }

            // The stream ended or dropped. Stations do that (a server restarting, a network hiccup): try again a few times.
            connection.Dispose();
            connection = null;
            while (connection is null && reconnects < MaxReconnects && !_stop.IsCancellationRequested)
            {
                reconnects++;
                try
                {
                    Task.Delay(TimeSpan.FromSeconds(reconnects), _stop.Token).Wait();
                    connection = Connection.OpenAsync(_http, _address, _userAgent, _codecHint, _stop.Token).GetAwaiter().GetResult();
                    // The sound has to carry on as it was; a station that came back in another format is started again by hand.
                    if (!connection.Decoder.Format.Equals(_buffer.WaveFormat))
                    {
                        connection.Dispose();
                        connection = null;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    problem = ex;
                }
            }

            if (connection is null)
            {
                break;
            }

            if (Interlocked.Exchange(ref _connection, connection) is null)
            {
                // Closed meanwhile.
                connection.Dispose();
                break;
            }
        }

        _ended = true;
        if (!_stop.IsCancellationRequested)
        {
            Lost?.Invoke(problem);
        }
    }

    // Called with the bytes just added, which are the first of the new song's.
    private void UpdateTitle(int added)
    {
        if (_connection?.Title is { } title && title != _heardTitle)
        {
            _heardTitle = title;
            if (_nowPlaying is null)
            {
                // The first title is the song that's on as it tunes in.
                _nowPlaying = title;
                TitleChanged?.Invoke(title);
            }
            else
            {
                // Said once the sound already in hand has played.
                _comingTitle = new ComingTitle(title, Interlocked.Read(ref _position) + _buffer.BufferedBytes - added);
            }
        }
    }

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "The station's address doesn't work any more (the server says it isn't there).",
        HttpRequestException { StatusCode: { } status } => $"The station's server turned Lucent away ({(int)status} {status}).",
        HttpRequestException => "Lucent couldn't connect to the station. Check your internet connection, or try another station.",
        TaskCanceledException or TimeoutException => "The station took too long to answer. It may be off the air.",
        _ => $"Lucent couldn't play the station. {ex.Message}",
    };

    private sealed record ComingTitle(string Title, long At);

    /// <summary>One connection to the station: its response (or HLS stream), its audio, and the decoder reading it.</summary>
    private sealed class Connection : IDisposable
    {
        private readonly IDisposable _source;
        private readonly HlsStream? _hls;

        private Connection(HttpResponseMessage response, IcyStream audio, RadioDecoder decoder)
        {
            _source = response;
            Audio = audio;
            Decoder = decoder;
            StationName = Header(response, "icy-name");
            Genre = Header(response, "icy-genre");
            Bitrate = int.TryParse(Header(response, "icy-br")?.Split(',')[0], out var kbps) && kbps > 0 ? kbps : decoder.Bitrate;
        }

        private Connection(HlsStream hls, IcyStream audio, RadioDecoder decoder)
        {
            _source = hls;
            _hls = hls;
            Audio = audio;
            Decoder = decoder;
            Bitrate = hls.Bitrate > 0 ? hls.Bitrate : decoder.Bitrate;
        }

        public IcyStream Audio { get; }

        /// <summary>The song that's on, as the station last said.</summary>
        public string? Title => _hls?.Title ?? Audio.Title;

        public RadioDecoder Decoder { get; }

        public string? StationName { get; }

        public string? Genre { get; }

        public int Bitrate { get; }

        public static async Task<Connection> OpenAsync(HttpClient http, string address, string userAgent, string? codecHint, CancellationToken cancel)
        {
            for (var hops = 0; ; hops++)
            {
                using var connecting = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                connecting.CancelAfter(ConnectTimeout);
                var request = new HttpRequestMessage(HttpMethod.Get, address);
                request.Headers.UserAgent.ParseAdd(userAgent);
                request.Headers.Add("Icy-MetaData", "1");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, connecting.Token);
                try
                {
                    response.EnsureSuccessStatusCode();
                    var type = response.Content.Headers.ContentType?.MediaType?.ToLowerInvariant() ?? string.Empty;
                    if (IsPlaylist(address, type))
                    {
                        if (hops >= 3)
                        {
                            throw new RadioProblem("The station's playlist only points at more playlists.");
                        }

                        var text = await ReadTextAsync(response, connecting.Token);
                        var from = response.RequestMessage?.RequestUri ?? new Uri(address);
                        if (HlsStream.IsHls(text))
                        {
                            response.Dispose();
                            return await OpenHlsAsync(http, from, text, userAgent, connecting.Token);
                        }

                        address = StreamInPlaylist(text, from)
                            ?? throw new RadioProblem("The station's playlist doesn't say where its stream is.");
                        continue;
                    }

                    if (Unsupported(type) is { } format)
                    {
                        throw new RadioProblem($"This station streams {format}, which Lucent's own radio player doesn't read.") { Unreadable = true, Address = address };
                    }

                    var codec = IsAac(type, address, codecHint) ? RadioCodec.Aac : RadioCodec.Mp3;

                    var metaInterval = int.TryParse(Header(response, "icy-metaint"), out var interval) && interval > 0 ? interval : 0;
                    var body = await response.Content.ReadAsStreamAsync(connecting.Token);
                    var audio = new IcyStream(body, metaInterval);
                    // Reading the first frames blocks, so a station that connects and then sends nothing is given up on in time.
                    using var stall = connecting.Token.Register(() => response.Dispose());
                    var decoder = await Task.Run(() => RadioDecoder.Open(audio, codec), connecting.Token);
                    return new Connection(response, audio, decoder);
                }
                catch (Exception ex) when (ex is not RadioProblem && connecting.IsCancellationRequested && !cancel.IsCancellationRequested)
                {
                    response.Dispose();
                    throw new RadioProblem("The station took too long to answer. It may be off the air.", ex);
                }
                catch
                {
                    response.Dispose();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            _source.Dispose();
            Decoder.Dispose();
        }

        private static async Task<Connection> OpenHlsAsync(HttpClient http, Uri address, string playlist, string userAgent, CancellationToken cancel)
        {
            var hls = await HlsStream.OpenAsync(http, address, playlist, userAgent, cancel);
            try
            {
                var audio = new IcyStream(hls, 0);
                using var stall = cancel.Register(hls.Dispose);
                var decoder = await Task.Run(() => RadioDecoder.Open(audio, hls.Codec), cancel);
                return new Connection(hls, audio, decoder);
            }
            catch
            {
                hls.Dispose();
                throw;
            }
        }

        private static bool IsPlaylist(string address, string type)
        {
            var path = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.AbsolutePath.ToLowerInvariant() : string.Empty;
            return type is "audio/x-scpls" or "audio/scpls" or "application/pls+xml" or "audio/x-mpegurl" or "audio/mpegurl"
                       or "application/x-mpegurl" or "application/vnd.apple.mpegurl" or "text/plain"
                   || path.EndsWith(".pls") || path.EndsWith(".m3u") || path.EndsWith(".m3u8");
        }

        /// <summary>
        /// True for AAC streams, by their type, or by their address when the server doesn't say (or says something
        /// generic). Everything else is tried as MP3.
        /// </summary>
        internal static bool IsAac(string type, string address, string? codecHint)
        {
            if (type is "audio/aac" or "audio/aacp" or "audio/x-aac" or "audio/x-aacp" or "audio/aac-adts")
            {
                return true;
            }

            if (type is not ("" or "application/octet-stream" or "audio/x-unknown"))
            {
                return false;
            }

            if (codecHint?.Trim().ToUpperInvariant() is { } hint && hint.StartsWith("AAC"))
            {
                return true;
            }

            var path = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.AbsolutePath.ToLowerInvariant() : address.ToLowerInvariant();
            return path.EndsWith(".aac") || path.EndsWith(".aacp") || path.Contains("aac");
        }

        private static string? Unsupported(string type) => type switch
        {
            // MP4 and M4A are AAC in a container made for files, not the ADTS frames stations send.
            "audio/mp4" or "audio/x-m4a" => "AAC in MP4",
            "application/ogg" or "audio/ogg" or "audio/vorbis" => "Ogg",
            "audio/opus" => "Opus",
            "audio/flac" or "audio/x-flac" => "FLAC",
            _ when type.StartsWith("video/") => "video",
            _ => null,
        };

        private static async Task<string> ReadTextAsync(HttpResponseMessage response, CancellationToken cancel)
        {
            await using var body = await response.Content.ReadAsStreamAsync(cancel);
            var bytes = new byte[64 * 1024];
            var total = 0;
            int read;
            while (total < bytes.Length && (read = await body.ReadAsync(bytes.AsMemory(total), cancel)) > 0)
            {
                total += read;
            }

            return Encoding.UTF8.GetString(bytes, 0, total);
        }

        /// <summary>The first stream a .pls or .m3u playlist lists. (HLS playlists are read by <see cref="HlsStream"/>.)</summary>
        internal static string? StreamInPlaylist(string text, Uri from)
        {
            foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var line = raw;
                // .pls: "File1=http://…"
                if (line.StartsWith("File", StringComparison.OrdinalIgnoreCase) && line.IndexOf('=') is > 4 and var equals)
                {
                    line = line[(equals + 1)..].Trim();
                }

                if (line.StartsWith('#') || line.StartsWith('['))
                {
                    continue;
                }

                if (Uri.TryCreate(from, line, out var uri) && uri.Scheme is "http" or "https")
                {
                    return uri.ToString();
                }
            }

            return null;
        }

        private static string? Header(HttpResponseMessage response, string name)
        {
            if (response.Headers.TryGetValues(name, out var values) || response.Content.Headers.TryGetValues(name, out values))
            {
                var value = values.FirstOrDefault()?.Trim();
                return string.IsNullOrEmpty(value) ? null : value;
            }

            return null;
        }
    }
}
