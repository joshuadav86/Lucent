using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;

namespace Lucent.Radio;

/// <summary>
/// An HLS station (HTTP Live Streaming) read as one stream of MP3 or AAC frames. Instead of one long stream, HLS
/// sends a playlist of short pieces (segments) a few seconds each, and keeps adding new ones to the end; this
/// fetches them in turn, reloading the playlist for more, and passes on the audio inside them for the decoders to
/// read as they read any station. Segments are plain audio (ADTS AAC, or MP3), MPEG transport stream (.ts), or MP4
/// pieces (fragmented MP4, see <see cref="Mp4Pieces"/>), which the audio is taken out of. Songs come as ID3 tags
/// inside the segments.
/// </summary>
/// <remarks>
/// A station's top playlist may list the same station at several qualities; the best one Lucent can play is taken.
/// Encrypted streams aren't supported, nor MP4 pieces of sound other than AAC and MP3 (FLAC, Opus), which the video
/// player (libVLC) plays instead. Reading blocks while segments are fetched, so it's read on the station's own thread.
/// </remarks>
internal sealed class HlsStream : Stream
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// How far back from the end of a live playlist playing starts. HLS asks for at least three segments; going back
    /// further gives the player that much in hand (the segments are already there, so they come at once), and a
    /// segment that's late to appear, or slow to fetch, plays through instead of being heard as a gap.
    /// </summary>
    private static readonly TimeSpan LiveStartBack = TimeSpan.FromSeconds(30);

    private const int LiveStartSegments = 3;

    private static readonly int[] CommonBitrates = { 32, 48, 64, 96, 128, 160, 192, 256, 320 };

    private readonly HttpClient _http;
    private readonly string _userAgent;
    private readonly Uri _playlist;
    private readonly CancellationTokenSource _stop = new();
    private readonly Queue<Segment> _segments = new();
    private long _lastSequence = -1;
    private TimeSpan _targetDuration = TimeSpan.FromSeconds(6);
    private bool _ended;
    private DateTime _nextReload;
    private byte[] _current = Array.Empty<byte>();
    private int _offset;
    private TransportStream _transport = new();
    private Mp4Pieces? _mp4;
    private Map? _mp4Map;
    private volatile string? _title;

    private HlsStream(HttpClient http, Uri playlist, string userAgent, int bitrate)
    {
        _http = http;
        _playlist = playlist;
        _userAgent = userAgent;
        Bitrate = bitrate;
    }

    /// <summary>The song that's on, as the station last said, or null.</summary>
    public string? Title => _title;

    /// <summary>Kilobits per second, as the playlist (or an MP4 stream's first piece) says, or 0 when it doesn't.</summary>
    public int Bitrate { get; private set; }

    /// <summary>The audio in the segments.</summary>
    public RadioCodec Codec { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// True when <paramref name="playlist"/> is an HLS playlist rather than a plain .m3u list of streams.
    /// </summary>
    public static bool IsHls(string playlist) => playlist.Contains("#EXT-X-", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Starts reading the HLS station whose playlist, from <paramref name="address"/>, is <paramref name="playlist"/>:
    /// picks the quality, loads its playlist, and fetches the first segment to learn what's in it.
    /// </summary>
    public static async Task<HlsStream> OpenAsync(HttpClient http, Uri address, string playlist, string userAgent, CancellationToken cancel)
    {
        var bitrate = 0;
        if (playlist.Contains("#EXT-X-STREAM-INF", StringComparison.OrdinalIgnoreCase))
        {
            var variant = BestVariant(playlist, address)
                ?? throw new RadioProblem("The station's HLS stream doesn't offer audio Lucent can play (MP3 or AAC).") { Unreadable = true };
            address = variant.Address;
            bitrate = variant.Kbps;
            playlist = await GetTextAsync(http, address, userAgent, cancel);
        }

        var stream = new HlsStream(http, address, userAgent, bitrate);
        try
        {
            stream.Load(playlist, starting: true);
            if (!await stream.FetchNextAsync(cancel))
            {
                throw new RadioProblem("The station's HLS stream has nothing to play right now. It may be off the air.");
            }

            stream.Codec = stream._mp4?.Codec ?? stream._transport.Codec ?? (IsAdts(stream._current, stream._offset) ? RadioCodec.Aac : RadioCodec.Mp3);
            if (stream.Bitrate == 0 && stream._mp4 is { Kbps: > 0 } mp4)
            {
                stream.Bitrate = Kbps(mp4.Kbps * 1000);
            }
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        while (_offset >= _current.Length)
        {
            try
            {
                if (!FetchNextAsync(_stop.Token).GetAwaiter().GetResult())
                {
                    return 0;
                }
            }
            catch (OperationCanceledException) when (_stop.IsCancellationRequested)
            {
                return 0;
            }
        }

        var read = Math.Min(count, _current.Length - _offset);
        Buffer.BlockCopy(_current, _offset, buffer, offset, read);
        _offset += read;
        return read;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_stop.IsCancellationRequested)
        {
            _stop.Cancel();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// What to play from a top (master) playlist: an audio-only version when it lists one, the best quality first.
    /// A video station's sound may come as its own playlist (an audio rendition), which is taken when there is one;
    /// otherwise its smallest video version is, for the sound inside it (only its audio is fetched out of each piece).
    /// </summary>
    internal static (Uri Address, int Kbps)? BestVariant(string playlist, Uri from)
    {
        var variants = new List<(Uri Address, int Bandwidth, int Rank)>();
        (Uri Address, bool Default)? rendition = null;
        Dictionary<string, string>? pending = null;
        foreach (var line in Lines(playlist))
        {
            if (line.StartsWith("#EXT-X-MEDIA:", StringComparison.OrdinalIgnoreCase))
            {
                var media = Attributes(line[(line.IndexOf(':') + 1)..]);
                var isDefault = media.GetValueOrDefault("DEFAULT", "NO").Equals("YES", StringComparison.OrdinalIgnoreCase);
                if (media.GetValueOrDefault("TYPE", string.Empty).Equals("AUDIO", StringComparison.OrdinalIgnoreCase)
                    && media.TryGetValue("URI", out var uri) && Uri.TryCreate(from, uri, out var address)
                    && (rendition is null || (isDefault && !rendition.Value.Default)))
                {
                    rendition = (address, isDefault);
                }
            }
            else if (line.StartsWith("#EXT-X-STREAM-INF:", StringComparison.OrdinalIgnoreCase))
            {
                pending = Attributes(line[(line.IndexOf(':') + 1)..]);
            }
            else if (!line.StartsWith('#') && pending is not null)
            {
                var codecs = pending.GetValueOrDefault("CODECS", string.Empty).ToLowerInvariant()
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var bandwidth = int.TryParse(pending.GetValueOrDefault("AVERAGE-BANDWIDTH") ?? pending.GetValueOrDefault("BANDWIDTH"),
                    NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits) ? bits : 0;
                var audio = codecs.Where(IsAudioCodec).ToList();
                var playable = audio.Count > 0 && audio.All(IsPlayableCodec);
                // 0: audio Lucent plays, and nothing else. 1: it doesn't say. 2: video, with audio Lucent plays. -1: neither.
                var rank = codecs.Length == 0 ? 1 : !playable ? -1 : audio.Count == codecs.Length ? 0 : 2;

                if (rank >= 0 && Uri.TryCreate(from, line, out var address))
                {
                    variants.Add((address, bandwidth, rank));
                }

                pending = null;
            }
        }

        var audioOnly = variants.Where(variant => variant.Rank < 2).OrderBy(variant => variant.Rank).ThenByDescending(variant => variant.Bandwidth).ToList();
        if (audioOnly.Count > 0)
        {
            return (audioOnly[0].Address, Kbps(audioOnly[0].Bandwidth));
        }

        if (rendition is { } sound)
        {
            return (sound.Address, 0);
        }

        // A video's bandwidth is mostly picture, so it says nothing of the sound's quality.
        var video = variants.OrderBy(variant => variant.Bandwidth).FirstOrDefault();
        return video.Address is null ? null : (video.Address, 0);
    }

    // mp4a.40.2 is AAC-LC, .5 HE-AAC, .29 HE-AAC v2, .34 and mp4a.6b MP3.
    private static bool IsPlayableCodec(string codec) =>
        codec is "mp4a.40.2" or "mp4a.40.5" or "mp4a.40.29" or "mp4a.40.34" or "mp4a.6b" or "mp4a.69" or "mp3";

    private static bool IsAudioCodec(string codec) =>
        codec.StartsWith("mp4a") || codec is "mp3" or "ac-3" or "ec-3" or "opus" or "flac" or "alac";

    // Playlists give bits per second, counting the container too: 334,000 is a 320k stream.
    private static int Kbps(int bandwidth)
    {
        if (bandwidth <= 0)
        {
            return 0;
        }

        var kbps = bandwidth / 1000.0;
        var nearest = CommonBitrates.MinBy(common => Math.Abs(common - kbps));
        return Math.Abs(nearest - kbps) <= nearest * 0.12 ? nearest : (int)Math.Round(kbps);
    }

    /// <summary>Reads a media playlist's segments, queueing the ones not seen yet.</summary>
    private void Load(string playlist, bool starting)
    {
        long sequence = 0;
        var found = new List<Segment>();
        string? extinf = null;
        Map? map = null;
        foreach (var line in Lines(playlist))
        {
            if (line.StartsWith("#EXT-X-TARGETDURATION:", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(line[(line.IndexOf(':') + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
            {
                _targetDuration = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 30));
            }
            else if (line.StartsWith("#EXT-X-MEDIA-SEQUENCE:", StringComparison.OrdinalIgnoreCase))
            {
                long.TryParse(line[(line.IndexOf(':') + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out sequence);
            }
            else if (line.StartsWith("#EXT-X-KEY:", StringComparison.OrdinalIgnoreCase)
                     && !Attributes(line[(line.IndexOf(':') + 1)..]).GetValueOrDefault("METHOD", "NONE").Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                throw new RadioProblem("The station's HLS stream is encrypted, which Lucent can't play.") { Unreadable = true };
            }
            else if (line.StartsWith("#EXT-X-MAP:", StringComparison.OrdinalIgnoreCase))
            {
                // MP4 pieces: this first piece says what's in the ones after it.
                var attributes = Attributes(line[(line.IndexOf(':') + 1)..]);
                if (!attributes.TryGetValue("URI", out var uri) || !Uri.TryCreate(_playlist, uri, out var address))
                {
                    throw new RadioProblem("The station's HLS stream doesn't say where its first MP4 piece is.") { Unreadable = true };
                }

                map = new Map(address, attributes.GetValueOrDefault("BYTERANGE"));
            }
            else if (line.StartsWith("#EXT-X-ENDLIST", StringComparison.OrdinalIgnoreCase))
            {
                _ended = true;
            }
            else if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                extinf = line;
            }
            else if (!line.StartsWith('#'))
            {
                if (Uri.TryCreate(_playlist, line, out var address))
                {
                    found.Add(new Segment(sequence, address, TitleInExtinf(extinf), DurationInExtinf(extinf) ?? _targetDuration, map));
                }

                sequence++;
                extinf = null;
            }
        }

        var start = 0;
        if (starting && !_ended)
        {
            // Back from the end until LiveStartBack is covered, and at least LiveStartSegments.
            start = Math.Max(0, found.Count - LiveStartSegments);
            var back = found.Skip(start).Aggregate(TimeSpan.Zero, (total, segment) => total + segment.Duration);
            while (start > 0 && back < LiveStartBack)
            {
                start--;
                back += found[start].Duration;
            }
        }

        var added = false;
        foreach (var segment in found.Skip(start).Where(segment => segment.Sequence > _lastSequence))
        {
            _segments.Enqueue(segment);
            _lastSequence = segment.Sequence;
            added = true;
        }

        // A playlist that grew is looked at again a segment later; one that didn't, sooner.
        _nextReload = DateTime.UtcNow + (added ? _targetDuration : _targetDuration / 2);
    }

    /// <summary>Fetches the next segment into <see cref="_current"/>, waiting for the playlist to grow if need be. False at the end.</summary>
    private async Task<bool> FetchNextAsync(CancellationToken cancel)
    {
        while (true)
        {
            while (_segments.Count == 0)
            {
                if (_ended)
                {
                    return false;
                }

                var wait = _nextReload - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                {
                    await Task.Delay(wait, cancel);
                }

                Load(await GetTextAsync(_http, _playlist, _userAgent, cancel), starting: false);
            }

            var segment = _segments.Dequeue();
            byte[] bytes;
            try
            {
                bytes = await GetBytesAsync(segment.Address, cancel);
            }
            catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
            {
                // It dropped off the end of the playlist before it was fetched: on to the next.
                continue;
            }

            if (segment.Map is { } map && map != _mp4Map)
            {
                _mp4 = Mp4Pieces.Read(await GetMapAsync(map, cancel));
                _mp4Map = map;
            }

            var audio = segment.Map is not null && _mp4 is { } mp4 ? mp4.Audio(bytes, out var id3Title) : Audio(bytes, out id3Title);
            if (audio.Length == 0)
            {
                continue;
            }

            if ((id3Title ?? segment.Title) is { } title)
            {
                _title = title;
            }

            _current = audio;
            _offset = 0;
            return true;
        }
    }

    /// <summary>The audio in a segment: taken out of a transport stream, or a plain segment with its ID3 tags left off.</summary>
    private byte[] Audio(byte[] segment, out string? title)
    {
        if (TransportStream.Looks(segment))
        {
            return _transport.Audio(segment, out title);
        }

        title = null;
        var start = 0;
        while (Id3.Length(segment.AsSpan(start)) is > 0 and var length)
        {
            title = Id3.Title(segment.AsSpan(start, Math.Min(length, segment.Length - start))) ?? title;
            start += length;
        }

        return start >= segment.Length ? Array.Empty<byte>() : start == 0 ? segment : segment[start..];
    }

    private static bool IsAdts(byte[] audio, int offset)
    {
        for (var i = offset; i + 1 < audio.Length && i < offset + 4096; i++)
        {
            if (audio[i] == 0xFF && (audio[i + 1] & 0xE0) == 0xE0)
            {
                // MPEG audio: a layer of 0 is AAC; MP3's is 1.
                return (audio[i + 1] & 0x06) == 0;
            }
        }

        return false;
    }

    // "#EXTINF:10.0,…": the segment's length in seconds.
    private static TimeSpan? DurationInExtinf(string? extinf)
    {
        if (extinf is null)
        {
            return null;
        }

        var colon = extinf.IndexOf(':');
        var comma = extinf.IndexOf(',');
        var text = comma > colon ? extinf[(colon + 1)..comma] : extinf[(colon + 1)..];
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds is > 0 and < 120
            ? TimeSpan.FromSeconds(seconds)
            : null;
    }

    // "#EXTINF:10.0,Artist - Title" names the song in some playlists; "title=" and the like are left alone.
    private static string? TitleInExtinf(string? extinf)
    {
        var comma = extinf?.IndexOf(',') ?? -1;
        if (comma < 0)
        {
            return null;
        }

        var title = extinf![(comma + 1)..].Trim();
        return title.Length == 0 || title.Contains('=') ? null : title;
    }

    private async Task<byte[]> GetBytesAsync(Uri address, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd(_userAgent);
        using var response = await _http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(timeout.Token);
    }

    // An MP4 stream's first piece, cut to its byte range when it has one.
    private async Task<byte[]> GetMapAsync(Map map, CancellationToken cancel)
    {
        var bytes = await GetBytesAsync(map.Address, cancel);
        var range = map.ByteRange?.Split('@');
        if (range is not null && int.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var length))
        {
            var offset = range.Length > 1 && int.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var start) ? start : 0;
            if (offset >= 0 && length > 0 && offset + length <= bytes.Length)
            {
                return bytes[offset..(offset + length)];
            }
        }

        return bytes;
    }

    private static async Task<string> GetTextAsync(HttpClient http, Uri address, string userAgent, CancellationToken cancel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(RequestTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, address);
        request.Headers.UserAgent.ParseAdd(userAgent);
        using var response = await http.SendAsync(request, timeout.Token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(timeout.Token);
    }

    private static IEnumerable<string> Lines(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>A tag's attributes: <c>BANDWIDTH=334000,CODECS="mp4a.40.2"</c>. Commas inside quotes stay.</summary>
    internal static Dictionary<string, string> Attributes(string list)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var i = 0;
        while (i < list.Length)
        {
            var equals = list.IndexOf('=', i);
            if (equals < 0)
            {
                break;
            }

            var name = list[i..equals].Trim().TrimStart(',').Trim();
            i = equals + 1;
            string value;
            if (i < list.Length && list[i] == '"')
            {
                var close = list.IndexOf('"', i + 1);
                close = close < 0 ? list.Length : close;
                value = list[(i + 1)..close];
                i = close + 1;
            }
            else
            {
                var comma = list.IndexOf(',', i);
                comma = comma < 0 ? list.Length : comma;
                value = list[i..comma].Trim();
                i = comma;
            }

            if (name.Length > 0)
            {
                attributes[name] = value;
            }

            if (i < list.Length && list[i] == ',')
            {
                i++;
            }
        }

        return attributes;
    }

    private sealed record Segment(long Sequence, Uri Address, string? Title, TimeSpan Duration, Map? Map);

    /// <summary>An MP4 stream's first piece: where it is, and its "length@offset" when it's part of a bigger file.</summary>
    private sealed record Map(Uri Address, string? ByteRange);

    /// <summary>
    /// The audio in MPEG transport stream segments: 188-byte packets, each for one stream of the program, which the
    /// tables at the start of each segment (PAT and PMT) say which is which. The audio stream's packets are joined up
    /// with their PES headers left off; a timed ID3 stream, when there is one, gives the songs.
    /// </summary>
    private sealed class TransportStream
    {
        private const int PacketSize = 188;

        private int _pmt = -1;
        private int _audio = -1;
        private int _metadata = -1;

        /// <summary>The audio stream's codec, once the tables have said.</summary>
        public RadioCodec? Codec { get; private set; }

        public static bool Looks(byte[] segment) =>
            segment.Length >= PacketSize * 2 && segment[0] == 0x47 && segment[PacketSize] == 0x47;

        public byte[] Audio(byte[] segment, out string? title)
        {
            title = null;
            using var audio = new MemoryStream(segment.Length);
            MemoryStream? metadata = null;
            for (var at = 0; at + PacketSize <= segment.Length; at += PacketSize)
            {
                if (segment[at] != 0x47)
                {
                    // Lost step: find the next packet.
                    var next = Array.IndexOf(segment, (byte)0x47, at + 1);
                    if (next < 0)
                    {
                        break;
                    }

                    at = next - PacketSize;
                    continue;
                }

                var unitStart = (segment[at + 1] & 0x40) != 0;
                var pid = ((segment[at + 1] & 0x1F) << 8) | segment[at + 2];
                var adaptation = (segment[at + 3] >> 4) & 0x03;
                var payload = at + 4;
                if (adaptation is 0 or 2)
                {
                    continue;
                }

                if (adaptation == 3)
                {
                    payload += 1 + segment[payload];
                }

                var end = at + PacketSize;
                if (payload >= end)
                {
                    continue;
                }

                var data = segment.AsSpan(payload, end - payload);
                if (pid == 0)
                {
                    ReadPat(data, unitStart);
                }
                else if (pid == _pmt)
                {
                    ReadPmt(data, unitStart);
                }
                else if (pid == _audio)
                {
                    audio.Write(unitStart ? PesPayload(data) : data);
                }
                else if (pid == _metadata)
                {
                    if (unitStart)
                    {
                        title = TitleIn(metadata) ?? title;
                        metadata = new MemoryStream();
                        metadata.Write(PesPayload(data));
                    }
                    else
                    {
                        metadata?.Write(data);
                    }
                }
            }

            title = TitleIn(metadata) ?? title;
            return audio.ToArray();
        }

        private static string? TitleIn(MemoryStream? metadata)
        {
            if (metadata is null || metadata.Length == 0)
            {
                return null;
            }

            var tag = metadata.ToArray();
            return Id3.Length(tag) > 0 ? Id3.Title(tag) : null;
        }

        // Program association: where the program's map is.
        private void ReadPat(ReadOnlySpan<byte> data, bool unitStart)
        {
            if (!unitStart || Section(data) is not { } table)
            {
                return;
            }

            for (var i = 8; i + 4 <= table.Length - 4; i += 4)
            {
                var program = (table[i] << 8) | table[i + 1];
                if (program != 0)
                {
                    _pmt = ((table[i + 2] & 0x1F) << 8) | table[i + 3];
                    return;
                }
            }
        }

        // Program map: which stream is the audio, and which the songs.
        private void ReadPmt(ReadOnlySpan<byte> data, bool unitStart)
        {
            if (!unitStart || Section(data) is not { } table || table.Length < 12)
            {
                return;
            }

            var programInfo = ((table[10] & 0x0F) << 8) | table[11];
            for (var i = 12 + programInfo; i + 5 <= table.Length - 4;)
            {
                var type = table[i];
                var pid = ((table[i + 1] & 0x1F) << 8) | table[i + 2];
                var info = ((table[i + 3] & 0x0F) << 8) | table[i + 4];
                RadioCodec? codec = type switch
                {
                    0x0F => RadioCodec.Aac,
                    0x03 or 0x04 => RadioCodec.Mp3,
                    _ => null,
                };
                if (codec is not null && _audio < 0)
                {
                    _audio = pid;
                    Codec = codec;
                }
                else if (type == 0x15)
                {
                    _metadata = pid;
                }

                i += 5 + info;
            }
        }

        // A table section after its pointer field, cut to its length.
        private static byte[]? Section(ReadOnlySpan<byte> data)
        {
            if (data.Length < 1 || 1 + data[0] + 3 > data.Length)
            {
                return null;
            }

            var section = data[(1 + data[0])..];
            var length = ((section[1] & 0x0F) << 8) | section[2];
            return section[..Math.Min(section.Length, 3 + length)].ToArray();
        }

        // A PES packet's data, after its header.
        private static ReadOnlySpan<byte> PesPayload(ReadOnlySpan<byte> data)
        {
            if (data.Length < 9 || data[0] != 0 || data[1] != 0 || data[2] != 1)
            {
                return data;
            }

            var header = 9 + data[8];
            return header < data.Length ? data[header..] : ReadOnlySpan<byte>.Empty;
        }
    }

    /// <summary>ID3v2 tags, as HLS stations put them in segments to say what's on.</summary>
    internal static class Id3
    {
        /// <summary>How long the tag at the start of <paramref name="data"/> is, header and all, or 0 when there isn't one.</summary>
        public static int Length(ReadOnlySpan<byte> data)
        {
            if (data.Length < 10 || data[0] != 'I' || data[1] != 'D' || data[2] != '3')
            {
                return 0;
            }

            var size = SyncSafe(data[6..10]);
            var footer = (data[5] & 0x10) != 0 ? 10 : 0;
            return 10 + size + footer;
        }

        /// <summary>"Artist - Title" from a tag's TPE1 and TIT2, or the title alone, or null when it names no song.</summary>
        public static string? Title(ReadOnlySpan<byte> tag)
        {
            if (Length(tag) == 0)
            {
                return null;
            }

            var version = tag[3];
            var end = Math.Min(tag.Length, Length(tag));
            var i = 10;
            if ((tag[5] & 0x40) != 0 && tag.Length >= 14)
            {
                // An extended header, skipped.
                i += version >= 4 ? SyncSafe(tag[10..14]) : 4 + ReadInt(tag[10..14]);
            }

            string? artist = null;
            string? title = null;
            while (i + 10 <= end && version >= 3)
            {
                var id = Encoding.ASCII.GetString(tag.Slice(i, 4));
                var size = version >= 4 ? SyncSafe(tag.Slice(i + 4, 4)) : ReadInt(tag.Slice(i + 4, 4));
                if (id[0] == '\0' || size <= 0 || i + 10 + size > end)
                {
                    break;
                }

                var body = tag.Slice(i + 10, size);
                if (id == "TIT2")
                {
                    title = Text(body);
                }
                else if (id == "TPE1")
                {
                    artist = Text(body);
                }

                i += 10 + size;
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                return null;
            }

            return string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
        }

        private static string? Text(ReadOnlySpan<byte> body)
        {
            if (body.Length < 2)
            {
                return null;
            }

            var text = body[0] switch
            {
                0 => Encoding.Latin1.GetString(body[1..]),
                1 => Encoding.Unicode.GetString(body[1..]).TrimStart('﻿', '￾'),
                2 => Encoding.BigEndianUnicode.GetString(body[1..]),
                _ => Encoding.UTF8.GetString(body[1..]),
            };
            if (body[0] == 1 && body.Length >= 3 && body[1] == 0xFE && body[2] == 0xFF)
            {
                text = Encoding.BigEndianUnicode.GetString(body[3..]);
            }

            text = text.Split('\0')[0].Trim();
            return text.Length == 0 ? null : text;
        }

        private static int SyncSafe(ReadOnlySpan<byte> bytes) =>
            ((bytes[0] & 0x7F) << 21) | ((bytes[1] & 0x7F) << 14) | ((bytes[2] & 0x7F) << 7) | (bytes[3] & 0x7F);

        private static int ReadInt(ReadOnlySpan<byte> bytes) => (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }
}
