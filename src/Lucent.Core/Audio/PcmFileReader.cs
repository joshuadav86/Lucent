using System.Buffers.Binary;
using System.IO;
using System.Text;
using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>
/// Reads uncompressed sound files with Lucent's own code, the same on Windows and the Mac: WAV (with RF64 and BW64 for
/// files over 4 GB, and the WAVE_FORMAT_EXTENSIBLE header recorders write), Sony Wave64, AIFF and AIFF-C, and Core
/// Audio's CAF. Samples can be 8, 16, 24 or 32-bit whole numbers, 32 or 64-bit floats, or μ-law and A-law, either way
/// round, so a file reads whatever the program that made it chose. Everything comes out as 32-bit float.
/// </summary>
/// <remarks>
/// The systems' own readers each miss some of these (Windows' miss 64-bit floats, Wave64 and most AIFF-C, the Mac's
/// miss Wave64), so the formats were readable on one computer and not the other.
/// </remarks>
public sealed class PcmFileReader : WaveStream
{
    private enum Kind
    {
        Signed,
        Unsigned,
        Float,
        MuLaw,
        ALaw,
    }

    private readonly object _lock = new();
    private readonly Stream _stream;
    private readonly long _dataStart;
    private readonly long _frames;
    private readonly int _bytesPerSample;
    private readonly int _frameBytes;
    private readonly bool _bigEndian;
    private readonly Kind _kind;
    private long _frame;
    private byte[] _raw = Array.Empty<byte>();

    private PcmFileReader(Stream stream, Layout layout)
    {
        _stream = stream;
        _dataStart = layout.DataStart;
        _bytesPerSample = layout.BytesPerSample;
        _frameBytes = layout.BytesPerSample * layout.Channels;
        _bigEndian = layout.BigEndian;
        _kind = layout.Kind;
        var available = Math.Max(0, stream.Length - layout.DataStart);
        var length = layout.DataLength is { } said && said >= 0 ? Math.Min(said, available) : available;
        _frames = length / _frameBytes;
        if (layout.Frames is { } frames and > 0)
        {
            _frames = Math.Min(_frames, frames);
        }

        SourceBits = layout.Kind is Kind.MuLaw or Kind.ALaw ? 16 : layout.BytesPerSample * 8;
        IsSourceFloat = layout.Kind == Kind.Float;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(layout.SampleRate, layout.Channels);
    }

    /// <summary>The file's own bits per sample (24 for a 24-bit file), before being turned into floats.</summary>
    public int SourceBits { get; }

    /// <summary>True when the file holds floating-point samples.</summary>
    public bool IsSourceFloat { get; }

    public override WaveFormat WaveFormat { get; }

    public override long Length => _frames * WaveFormat.BlockAlign;

    public override long Position
    {
        get
        {
            lock (_lock)
            {
                return _frame * WaveFormat.BlockAlign;
            }
        }
        set
        {
            lock (_lock)
            {
                _frame = Math.Clamp(value / WaveFormat.BlockAlign, 0, _frames);
            }
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> if it's an uncompressed file this reads, or returns null (a compressed WAV, an
    /// AIFF-C or CAF holding ALAC or AAC, or anything else) so another decoder can try.
    /// </summary>
    public static PcmFileReader? TryOpen(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        try
        {
            var layout = ReadLayout(stream);
            if (layout is null)
            {
                stream.Dispose();
                return null;
            }

            return new PcmFileReader(stream, layout);
        }
        catch (Exception ex) when (ex is EndOfStreamException or InvalidDataException or OverflowException)
        {
            stream.Dispose();
            return null;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var channels = WaveFormat.Channels;
        lock (_lock)
        {
            var frames = (int)Math.Min(count / WaveFormat.BlockAlign, _frames - _frame);
            if (frames <= 0)
            {
                return 0;
            }

            var bytes = frames * _frameBytes;
            if (_raw.Length < bytes)
            {
                _raw = new byte[bytes];
            }

            _stream.Position = _dataStart + _frame * _frameBytes;
            var got = 0;
            while (got < bytes)
            {
                var read = _stream.Read(_raw, got, bytes - got);
                if (read <= 0)
                {
                    break;
                }

                got += read;
            }

            frames = got / _frameBytes;
            var output = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, frames * channels * 4));
            for (var i = 0; i < output.Length; i++)
            {
                output[i] = Sample(_raw.AsSpan(i * _bytesPerSample, _bytesPerSample));
            }

            _frame += frames;
            return frames * WaveFormat.BlockAlign;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_lock)
            {
                _stream.Dispose();
            }
        }

        base.Dispose(disposing);
    }

    private float Sample(ReadOnlySpan<byte> b)
    {
        switch (_kind)
        {
            case Kind.Unsigned:
                return (b[0] - 128) / 128f;
            case Kind.MuLaw:
                return MuLaw(b[0]);
            case Kind.ALaw:
                return ALaw(b[0]);
            case Kind.Float:
                return _bytesPerSample == 8
                    ? (float)(_bigEndian ? BinaryPrimitives.ReadDoubleBigEndian(b) : BinaryPrimitives.ReadDoubleLittleEndian(b))
                    : _bigEndian ? BinaryPrimitives.ReadSingleBigEndian(b) : BinaryPrimitives.ReadSingleLittleEndian(b);
        }

        switch (_bytesPerSample)
        {
            case 1:
                return (sbyte)b[0] / 128f;
            case 2:
                return (_bigEndian ? BinaryPrimitives.ReadInt16BigEndian(b) : BinaryPrimitives.ReadInt16LittleEndian(b)) / 32768f;
            case 3:
                var value = _bigEndian ? (b[0] << 24) | (b[1] << 16) | (b[2] << 8) : (b[2] << 24) | (b[1] << 16) | (b[0] << 8);
                return value / 2147483648f;
            default:
                return (_bigEndian ? BinaryPrimitives.ReadInt32BigEndian(b) : BinaryPrimitives.ReadInt32LittleEndian(b)) / 2147483648f;
        }
    }

    private static float MuLaw(byte value)
    {
        var u = ~value & 0xFF;
        var magnitude = ((((u & 0x0F) << 3) + 0x84) << ((u & 0x70) >> 4)) - 0x84;
        return ((u & 0x80) != 0 ? -magnitude : magnitude) / 32768f;
    }

    private static float ALaw(byte value)
    {
        var a = value ^ 0x55;
        var exponent = (a & 0x70) >> 4;
        var magnitude = (a & 0x0F) << 4;
        magnitude = exponent == 0 ? magnitude + 8 : (magnitude + 0x108) << (exponent - 1);
        return ((a & 0x80) != 0 ? magnitude : -magnitude) / 32768f;
    }

    /// <summary>Where the samples are and how they're stored, as a file's header says.</summary>
    private sealed record Layout(int SampleRate, int Channels, int BytesPerSample, Kind Kind, bool BigEndian, long DataStart,
        long? DataLength, long? Frames = null);

    private static Layout? ReadLayout(Stream stream)
    {
        var head = new byte[16];
        if (stream.Read(head, 0, 16) < 12)
        {
            return null;
        }

        stream.Position = 0;
        var id = Encoding.ASCII.GetString(head, 0, 4);
        return id switch
        {
            "RIFF" or "RIFX" or "RF64" or "BW64" => ReadWave(stream, id),
            "riff" when head.AsSpan(4, 12).SequenceEqual(Wave64Suffix.AsSpan(4)) => ReadWave64(stream),
            "FORM" => ReadAiff(stream),
            "caff" => ReadCaf(stream),
            _ => null,
        };
    }

    private static readonly byte[] Wave64Suffix = { 0, 0, 0, 0, 0x2E, 0x91, 0xCF, 0x11, 0xA5, 0xD6, 0x28, 0xDB, 0x04, 0xC1, 0x00, 0x00 };

    // Every Wave64 chunk but "riff" is named by four letters and these twelve bytes.
    private static readonly byte[] Wave64ChunkSuffix = { 0xF3, 0xAC, 0xD3, 0x11, 0x8C, 0xD1, 0x00, 0xC0, 0x4F, 0x8E, 0xDB, 0x8A };

    private static Layout? ReadWave(Stream stream, string id)
    {
        var reader = new BinaryReader(stream);
        var bigEndian = id == "RIFX";
        stream.Position = 12;
        if (!HasWaveTag(stream))
        {
            return null;
        }

        long? bigDataSize = null;
        Format? format = null;
        while (stream.Position + 8 <= stream.Length)
        {
            var chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = (long)(bigEndian ? BinaryPrimitives.ReverseEndianness(reader.ReadUInt32()) : reader.ReadUInt32());
            var start = stream.Position;
            switch (chunk)
            {
                case "ds64":
                    reader.ReadUInt64();
                    bigDataSize = (long)reader.ReadUInt64();
                    break;
                case "fmt ":
                    format = ReadWaveFormat(reader, (int)size, bigEndian);
                    break;
                case "data":
                    if (format is null)
                    {
                        return null;
                    }

                    // 0xFFFFFFFF means the real size is in "ds64"; 0 is what some recorders leave if they're stopped
                    // before they finish the header, with the sound there all the same.
                    long? length = size == 0xFFFFFFFF ? bigDataSize : size == 0 ? null : size;
                    return format.Layout(start, length, bigEndian);
            }

            var next = start + size + (size & 1);
            if (size == 0xFFFFFFFF || next <= start - 8 || next > stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        return null;
    }

    private static bool HasWaveTag(Stream stream)
    {
        stream.Position = 8;
        var tag = new byte[4];
        var ok = stream.Read(tag, 0, 4) == 4 && Encoding.ASCII.GetString(tag) == "WAVE";
        stream.Position = 12;
        return ok;
    }

    private static Layout? ReadWave64(Stream stream)
    {
        var reader = new BinaryReader(stream);
        stream.Position = 24;
        var wave = reader.ReadBytes(16);
        if (Encoding.ASCII.GetString(wave, 0, 4) != "wave")
        {
            return null;
        }

        Format? format = null;
        while (stream.Position + 24 <= stream.Length)
        {
            var guid = reader.ReadBytes(16);
            var size = (long)reader.ReadUInt64();
            var start = stream.Position;
            if (!guid.AsSpan(4).SequenceEqual(Wave64ChunkSuffix) || size < 24)
            {
                return null;
            }

            switch (Encoding.ASCII.GetString(guid, 0, 4))
            {
                case "fmt ":
                    format = ReadWaveFormat(reader, (int)(size - 24), bigEndian: false);
                    break;
                case "data":
                    return format?.Layout(start, size - 24, bigEndian: false);
            }

            var next = start - 24 + ((size + 7) & ~7L);
            if (next <= start - 24 || next > stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        return null;
    }

    /// <summary>A WAV "fmt " chunk, as far as it matters here.</summary>
    private sealed record Format(int Tag, int Channels, int SampleRate, int BlockAlign, int Bits)
    {
        public Layout? Layout(long start, long? length, bool bigEndian)
        {
            if (Channels is < 1 or > 32 || SampleRate is < 1 or > 1_536_000 || BlockAlign < Channels || BlockAlign % Channels != 0)
            {
                return null;
            }

            var bytes = BlockAlign / Channels;
            Kind? kind = Tag switch
            {
                1 when bytes == 1 => Kind.Unsigned,
                1 when bytes is 2 or 3 or 4 => Kind.Signed,
                3 when bytes is 4 or 8 => Kind.Float,
                6 when bytes == 1 => Kind.ALaw,
                7 when bytes == 1 => Kind.MuLaw,
                _ => null,
            };
            return kind is null ? null : new Layout(SampleRate, Channels, bytes, kind.Value, bigEndian, start, length);
        }
    }

    private static Format? ReadWaveFormat(BinaryReader reader, int size, bool bigEndian)
    {
        if (size < 16)
        {
            return null;
        }

        ushort U16() => bigEndian ? BinaryPrimitives.ReverseEndianness(reader.ReadUInt16()) : reader.ReadUInt16();
        uint U32() => bigEndian ? BinaryPrimitives.ReverseEndianness(reader.ReadUInt32()) : reader.ReadUInt32();
        int tag = U16();
        int channels = U16();
        var rate = (int)U32();
        U32();
        int blockAlign = U16();
        int bits = U16();
        if (tag == 0xFFFE && size >= 40)
        {
            // WAVE_FORMAT_EXTENSIBLE: the real format is the first two bytes of its sub-format GUID.
            U16();
            U16();
            U32();
            tag = U16();
        }

        return new Format(tag, channels, rate, blockAlign, bits);
    }

    private static Layout? ReadAiff(Stream stream)
    {
        var reader = new BinaryReader(stream);
        stream.Position = 8;
        var type = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (type is not ("AIFF" or "AIFC"))
        {
            return null;
        }

        int channels = 0, bits = 0;
        long frames = 0;
        double rate = 0;
        var compression = "NONE";
        while (stream.Position + 8 <= stream.Length)
        {
            var chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = (long)BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
            var start = stream.Position;
            switch (chunk)
            {
                case "COMM":
                    channels = BinaryPrimitives.ReverseEndianness(reader.ReadInt16());
                    frames = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    bits = BinaryPrimitives.ReverseEndianness(reader.ReadInt16());
                    rate = Extended(reader.ReadBytes(10));
                    if (type == "AIFC" && size >= 22)
                    {
                        compression = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    }

                    break;
                case "SSND":
                    var offset = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    reader.ReadUInt32();
                    return AiffLayout(compression, channels, bits, rate, frames, start + 8 + offset, size - 8 - offset);
            }

            var next = start + size + (size & 1);
            if (next <= start || next > stream.Length)
            {
                break;
            }

            stream.Position = next;
        }

        return null;
    }

    private static Layout? AiffLayout(string compression, int channels, int bits, double rate, long frames, long start, long length)
    {
        if (channels is < 1 or > 32 || rate is < 1 or > 1_536_000)
        {
            return null;
        }

        var wholeBytes = Math.Clamp((bits + 7) / 8, 1, 4);
        (Kind Kind, int Bytes, bool BigEndian)? layout = compression switch
        {
            "NONE" or "twos" => (Kind.Signed, wholeBytes, true),
            "sowt" => (Kind.Signed, wholeBytes, false),
            "raw " => (Kind.Unsigned, 1, true),
            "in24" => (Kind.Signed, 3, true),
            "23ni" => (Kind.Signed, 3, false),
            "in32" => (Kind.Signed, 4, true),
            "42ni" => (Kind.Signed, 4, false),
            "fl32" or "FL32" => (Kind.Float, 4, true),
            "fl64" or "FL64" => (Kind.Float, 8, true),
            "ulaw" or "ULAW" => (Kind.MuLaw, 1, true),
            "alaw" or "ALAW" => (Kind.ALaw, 1, true),
            _ => null,
        };
        return layout is { } found
            ? new Layout((int)Math.Round(rate), channels, found.Bytes, found.Kind, found.BigEndian, start, Math.Max(0, length), frames)
            : null;
    }

    /// <summary>An 80-bit IEEE extended number, big-endian, as AIFF stores its sample rate.</summary>
    private static double Extended(byte[] b)
    {
        var exponent = ((b[0] & 0x7F) << 8) | b[1];
        var mantissa = BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(2));
        if (exponent == 0 && mantissa == 0)
        {
            return 0;
        }

        var value = mantissa * Math.Pow(2, exponent - 16383 - 63);
        return (b[0] & 0x80) != 0 ? -value : value;
    }

    private static Layout? ReadCaf(Stream stream)
    {
        var reader = new BinaryReader(stream);
        stream.Position = 8;
        (double Rate, string Id, uint Flags, int Channels, int Bits, uint BytesPerPacket)? desc = null;
        while (stream.Position + 12 <= stream.Length)
        {
            var chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var size = BinaryPrimitives.ReverseEndianness(reader.ReadInt64());
            var start = stream.Position;
            switch (chunk)
            {
                case "desc":
                    var rate = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReverseEndianness(reader.ReadInt64()));
                    var formatId = Encoding.ASCII.GetString(reader.ReadBytes(4));
                    var flags = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    var bytesPerPacket = BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    reader.ReadUInt32();
                    var channels = (int)BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    var bits = (int)BinaryPrimitives.ReverseEndianness(reader.ReadUInt32());
                    desc = (rate, formatId, flags, channels, bits, bytesPerPacket);
                    break;
                case "data":
                    if (desc is not { } d || d.Channels is < 1 or > 32 || d.Rate is < 1 or > 1_536_000)
                    {
                        return null;
                    }

                    // The first four bytes count edits; a size of -1 means the sound runs to the end of the file.
                    long? length = size < 0 ? null : size - 4;
                    var isFloat = (d.Flags & 1) != 0;
                    var bigEndian = (d.Flags & 2) == 0;
                    var bytes = d.BytesPerPacket > 0 && d.BytesPerPacket % d.Channels == 0 ? (int)(d.BytesPerPacket / d.Channels) : (d.Bits + 7) / 8;
                    Kind? kind = d.Id switch
                    {
                        "lpcm" when isFloat && bytes is 4 or 8 => Kind.Float,
                        "lpcm" when !isFloat && bytes is 1 or 2 or 3 or 4 => Kind.Signed,
                        "ulaw" => Kind.MuLaw,
                        "alaw" => Kind.ALaw,
                        _ => null,
                    };
                    if (kind is Kind.MuLaw or Kind.ALaw)
                    {
                        bytes = 1;
                    }

                    return kind is null ? null : new Layout((int)Math.Round(d.Rate), d.Channels, bytes, kind.Value, bigEndian, start + 4, length);
            }

            if (size < 0 || start + size > stream.Length)
            {
                break;
            }

            stream.Position = start + size;
        }

        return null;
    }
}
