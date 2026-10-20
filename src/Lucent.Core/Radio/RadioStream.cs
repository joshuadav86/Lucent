using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using NAudio.Wave;
using NLayer.NAudioSupport;

namespace WinampInspiredVisualizer.Radio;

/// <summary>
/// An online radio station, playing: its MP3 stream decoded as it arrives, with a few seconds held in hand so a
/// slow moment on the network doesn't stop the music. The player plays it like a track that never ends, so the
/// equalizer, the visuals, the meter and the analyzer all get it as they do a file.
/// </summary>
/// <remarks>
/// Shoutcast and Icecast stations send the song that's on in the stream itself (ICY metadata); that's read off as it
/// goes by and reported with <see cref="TitleChanged"/>. When the connection drops, it reconnects by itself a few
/// times, playing silence meanwhile, before giving up. MP3 is decoded by NLayer, the same on every platform.
/// </remarks>
public sealed class RadioStream : WaveStream
{
    /// <summary>How much audio is held in hand before playing starts, and again after the stream ran dry.</summary>
    private static readonly TimeSpan Prebuffer = TimeSpan.FromSeconds(1.5);

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(15);

    private const int MaxReconnects = 4;

    private readonly HttpClient _http;
    private readonly string _address;
    private readonly string _userAgent;
    private readonly BufferedWaveProvider _buffer;
    private readonly CancellationTokenSource _stop = new();
    private readonly int _prebufferBytes;
    private Mp3FrameDecompressor _decoder;
    private Connection? _connection;
    private Task? _reading;
    private long _position;
    private volatile bool _ended;
    private volatile bool _rebuffering = true;
    private string? _nowPlaying;

    private RadioStream(HttpClient http, Connection connection, string address, string userAgent)
    {
        _http = http;
        _connection = connection;
        _address = address;
        _userAgent = userAgent;
        _decoder = new Mp3FrameDecompressor(connection.Format);
        _buffer = new BufferedWaveProvider(_decoder.OutputFormat)
        {
            BufferDuration = TimeSpan.FromSeconds(20),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
        _prebufferBytes = (int)(_decoder.OutputFormat.AverageBytesPerSecond * Prebuffer.TotalSeconds) / _decoder.OutputFormat.BlockAlign
            * _decoder.OutputFormat.BlockAlign;
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
    /// Tunes in: finds the stream (through a .pls or .m3u playlist if that's what the address is), checks it's MP3,
    /// and waits for the first moments of audio. Throws <see cref="RadioProblem"/> with words fit to show when the
    /// station can't be played.
    /// </summary>
    public static async Task<RadioStream> OpenAsync(string address, string userAgent, CancellationToken cancel)
    {
        var http = CreateClient();
        try
        {
            var connection = await Connection.OpenAsync(http, address, userAgent, cancel);
            var stream = new RadioStream(http, connection, address, userAgent);
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
        Interlocked.Add(ref _position, read);
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
        var pcm = new byte[16384];
        var reconnects = 0;
        Exception? problem = null;
        var connection = _connection;
        var frame = connection?.FirstFrames.Dequeue();
        while (!_stop.IsCancellationRequested && connection is not null)
        {
            try
            {
                while (frame is not null && !_stop.IsCancellationRequested)
                {
                    Decode(frame, pcm);
                    frame = connection.FirstFrames.Count > 0 ? connection.FirstFrames.Dequeue() : connection.NextFrame();
                    reconnects = 0;
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
                    connection = Connection.OpenAsync(_http, _address, _userAgent, _stop.Token).GetAwaiter().GetResult();
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

            _decoder.Reset();
            frame = connection.FirstFrames.Dequeue();
        }

        _ended = true;
        if (!_stop.IsCancellationRequested)
        {
            Lost?.Invoke(problem);
        }
    }

    private void Decode(Mp3Frame frame, byte[] pcm)
    {
        var format = _decoder.OutputFormat;
        // The odd stray frame that doesn't fit (a false start in the data, a station switching formats) is skipped.
        if (frame.SampleRate != format.SampleRate || Channels(frame) != format.Channels)
        {
            return;
        }

        int bytes;
        try
        {
            bytes = _decoder.DecompressFrame(frame, pcm, 0);
        }
        catch (Exception)
        {
            _decoder.Dispose();
            _decoder = new Mp3FrameDecompressor(new Mp3WaveFormat(frame.SampleRate, Channels(frame), frame.FrameLength, frame.BitRate));
            return;
        }

        _buffer.AddSamples(pcm, 0, bytes);
        UpdateTitle();
    }

    private void UpdateTitle()
    {
        if (_connection?.Audio.Title is { } title && title != _nowPlaying)
        {
            _nowPlaying = title;
            TitleChanged?.Invoke(title);
        }
    }

    private static int Channels(Mp3Frame frame) => frame.ChannelMode == ChannelMode.Mono ? 1 : 2;

    private static string Describe(Exception ex) => ex switch
    {
        HttpRequestException { StatusCode: HttpStatusCode.NotFound } => "The station's address doesn't work any more (the server says it isn't there).",
        HttpRequestException { StatusCode: { } status } => $"The station's server turned Lucent away ({(int)status} {status}).",
        HttpRequestException => "Lucent couldn't connect to the station. Check your internet connection, or try another station.",
        TaskCanceledException or TimeoutException => "The station took too long to answer. It may be off the air.",
        _ => $"Lucent couldn't play the station. {ex.Message}",
    };

    /// <summary>One connection to the station: its response, its audio, and the frames read to check it's MP3.</summary>
    private sealed class Connection : IDisposable
    {
        private readonly HttpResponseMessage _response;

        private Connection(HttpResponseMessage response, IcyStream audio, Queue<Mp3Frame> firstFrames)
        {
            _response = response;
            Audio = audio;
            FirstFrames = firstFrames;
            var first = firstFrames.Peek();
            Format = new Mp3WaveFormat(first.SampleRate, Channels(first), first.FrameLength, first.BitRate);
            StationName = Header(response, "icy-name");
            Genre = Header(response, "icy-genre");
            Bitrate = int.TryParse(Header(response, "icy-br")?.Split(',')[0], out var kbps) ? kbps : first.BitRate / 1000;
        }

        public IcyStream Audio { get; }

        /// <summary>The frames read while checking the stream, to be played first.</summary>
        public Queue<Mp3Frame> FirstFrames { get; }

        public Mp3WaveFormat Format { get; }

        public string? StationName { get; }

        public string? Genre { get; }

        public int Bitrate { get; }

        public static async Task<Connection> OpenAsync(HttpClient http, string address, string userAgent, CancellationToken cancel)
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
                        address = StreamInPlaylist(text, response.RequestMessage?.RequestUri ?? new Uri(address))
                            ?? throw new RadioProblem("The station's playlist doesn't say where its stream is.");
                        continue;
                    }

                    if (Unsupported(type) is { } format)
                    {
                        throw new RadioProblem($"This station streams {format}. Lucent plays MP3 stations for now; try another.");
                    }

                    var metaInterval = int.TryParse(Header(response, "icy-metaint"), out var interval) && interval > 0 ? interval : 0;
                    var body = await response.Content.ReadAsStreamAsync(connecting.Token);
                    var audio = new IcyStream(body, metaInterval);
                    // Reading the first frames blocks, so a station that connects and then sends nothing is given up on in time.
                    using var stall = connecting.Token.Register(() => response.Dispose());
                    var frames = await Task.Run(() => FirstFramesOf(audio), connecting.Token);
                    return new Connection(response, audio, frames);
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

        /// <summary>The next frame, or null when the stream has ended.</summary>
        public Mp3Frame? NextFrame() => Mp3Frame.LoadFromStream(Audio);

        public void Dispose() => _response.Dispose();

        // Finds where the MP3 frames start, and checks it really is MP3: two frames in a row that agree.
        private static Queue<Mp3Frame> FirstFramesOf(IcyStream audio)
        {
            // Plenty for the junk some streams start with, without reading a non-MP3 stream forever.
            audio.Limit = 256 * 1024;
            try
            {
                var previous = Mp3Frame.LoadFromStream(audio);
                while (previous is not null)
                {
                    var next = Mp3Frame.LoadFromStream(audio);
                    if (next is null)
                    {
                        break;
                    }

                    if (next.SampleRate == previous.SampleRate && Channels(next) == Channels(previous) && next.FileOffset == previous.FileOffset + previous.FrameLength)
                    {
                        return new Queue<Mp3Frame>(new[] { previous, next });
                    }

                    previous = next;
                }
            }
            catch (EndOfStreamException)
            {
                // Fall through to the problem below.
            }
            finally
            {
                audio.Limit = 0;
            }

            throw new RadioProblem("The station's stream isn't MP3 audio Lucent can play.");
        }

        private static bool IsPlaylist(string address, string type)
        {
            var path = Uri.TryCreate(address, UriKind.Absolute, out var uri) ? uri.AbsolutePath.ToLowerInvariant() : string.Empty;
            return type is "audio/x-scpls" or "audio/scpls" or "application/pls+xml" or "audio/x-mpegurl" or "audio/mpegurl"
                       or "application/x-mpegurl" or "application/vnd.apple.mpegurl" or "text/plain"
                   || path.EndsWith(".pls") || path.EndsWith(".m3u") || path.EndsWith(".m3u8");
        }

        private static string? Unsupported(string type) => type switch
        {
            "audio/aac" or "audio/aacp" or "audio/x-aac" or "audio/mp4" or "audio/x-m4a" => "AAC",
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

        /// <summary>The first stream a .pls or .m3u playlist lists. HLS playlists (segments, not a stream) aren't supported.</summary>
        internal static string? StreamInPlaylist(string text, Uri from)
        {
            if (text.Contains("#EXT-X-", StringComparison.OrdinalIgnoreCase))
            {
                throw new RadioProblem("This station streams HLS. Lucent plays MP3 stations for now; try another.");
            }

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
