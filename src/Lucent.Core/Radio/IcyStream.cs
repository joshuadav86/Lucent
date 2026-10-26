using System.Text;

namespace Lucent.Radio;

/// <summary>
/// A station's audio with the song titles taken out. Shoutcast and Icecast put a block of metadata into the stream
/// every so many bytes (the <c>icy-metaint</c> header says how many) when asked to; this reads them off, keeping
/// the latest <c>StreamTitle</c>, and passes the audio on. Reads always fill the buffer unless the stream ends, as
/// NAudio's MP3 frame reader needs, and the position counts audio bytes only.
/// </summary>
internal sealed class IcyStream : Stream
{
    private readonly Stream _inner;
    private readonly int _metaInterval;
    private int _untilMeta;
    private long _position;
    private volatile string? _title;

    public IcyStream(Stream inner, int metaInterval)
    {
        _inner = inner;
        _metaInterval = metaInterval;
        _untilMeta = metaInterval;
    }

    /// <summary>The song the station last said is on, or null.</summary>
    public string? Title => _title;

    /// <summary>When above 0, reading stops (as if the stream had ended) once this many bytes have been read.</summary>
    public long Limit { get; set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (Limit > 0)
        {
            count = (int)Math.Min(count, Math.Max(0, Limit - _position));
        }

        var total = 0;
        while (total < count)
        {
            if (_metaInterval > 0 && _untilMeta == 0)
            {
                if (!ReadMetadata())
                {
                    break;
                }

                _untilMeta = _metaInterval;
                continue;
            }

            var wanted = count - total;
            if (_metaInterval > 0)
            {
                wanted = Math.Min(wanted, _untilMeta);
            }

            var read = _inner.Read(buffer, offset + total, wanted);
            if (read == 0)
            {
                break;
            }

            total += read;
            _position += read;
            if (_metaInterval > 0)
            {
                _untilMeta -= read;
            }
        }

        return total;
    }

    /// <summary>The title in a metadata block: <c>StreamTitle='Artist - Title';StreamUrl='…';</c>. Null when it has none.</summary>
    internal static string? TitleIn(string metadata)
    {
        const string key = "StreamTitle='";
        var start = metadata.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return null;
        }

        start += key.Length;
        // Titles can hold apostrophes ("Don't Stop"), so it ends at the "';" before the next field, or the end.
        var end = metadata.IndexOf("';", start, StringComparison.Ordinal);
        if (end < 0)
        {
            end = metadata.LastIndexOf('\'');
        }

        var title = (end > start ? metadata[start..end] : metadata[start..]).Trim().Trim('-').Trim();
        return title.Length == 0 ? null : title;
    }

    // One length byte (in 16-byte steps), then that much metadata, padded with zeros. Most blocks are empty.
    private bool ReadMetadata()
    {
        var length = new byte[1];
        if (_inner.Read(length, 0, 1) == 0)
        {
            return false;
        }

        var size = length[0] * 16;
        if (size == 0)
        {
            return true;
        }

        var block = new byte[size];
        var total = 0;
        while (total < size)
        {
            var read = _inner.Read(block, total, size - total);
            if (read == 0)
            {
                return false;
            }

            total += read;
        }

        var text = Decode(block).TrimEnd('\0');
        if (TitleIn(text) is { } title)
        {
            _title = title;
        }

        return true;
    }

    // Stations mostly send UTF-8; older ones Latin-1, which UTF-8 can't read.
    private static string Decode(byte[] block)
    {
        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(block);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(block);
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// The connection to a station, made to look like HTTP to .NET's HTTP client. Old Shoutcast servers answer
/// "ICY 200 OK" where HTTP says "HTTP/1.0 200 OK", which the client won't read; this changes the one word as the
/// answer arrives and leaves everything else as it is. Over HTTPS the answer starts with the TLS handshake instead,
/// so nothing is changed.
/// </summary>
internal sealed class IcyStatusStream : Stream
{
    private static readonly byte[] Icy = "ICY "u8.ToArray();
    private static readonly byte[] Http = "HTTP/1.0 "u8.ToArray();

    private readonly Stream _inner;
    private bool _checked;
    private byte[] _pending = Array.Empty<byte>();
    private int _pendingAt;

    public IcyStatusStream(Stream inner) => _inner = inner;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (!_checked && !ReadFirstAsync(CancellationToken.None).AsTask().GetAwaiter().GetResult())
        {
            return 0;
        }

        return _pendingAt < _pending.Length ? Pending(buffer) : _inner.Read(buffer);
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (!_checked && !await ReadFirstAsync(cancellationToken))
        {
            return 0;
        }

        return _pendingAt < _pending.Length ? Pending(buffer.Span) : await _inner.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    // The answer's first four bytes, changed to HTTP's if they're Shoutcast's. False when the connection closed first.
    private async ValueTask<bool> ReadFirstAsync(CancellationToken cancel)
    {
        var first = new byte[Icy.Length];
        var total = 0;
        while (total < first.Length)
        {
            var read = await _inner.ReadAsync(first.AsMemory(total), cancel);
            if (read == 0)
            {
                break;
            }

            total += read;
            // Anything other than "ICY " shows by its first byte that it's something else (TLS, or "HTTP").
            if (first[0] != Icy[0])
            {
                break;
            }
        }

        _checked = true;
        _pending = total == Icy.Length && first.AsSpan().SequenceEqual(Icy) ? Http : first[..total];
        _pendingAt = 0;
        return total > 0;
    }

    private int Pending(Span<byte> buffer)
    {
        var count = Math.Min(buffer.Length, _pending.Length - _pendingAt);
        _pending.AsSpan(_pendingAt, count).CopyTo(buffer);
        _pendingAt += count;
        return count;
    }

    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _inner.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        _inner.WriteAsync(buffer, offset, count, cancellationToken);

    public override void Flush() => _inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }

    public override ValueTask DisposeAsync() => _inner.DisposeAsync();
}
