using System.IO;
using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// The playing, seeking and buffering shared by Lucent's own decoders for formats stored as blocks that each decode on
/// their own (WavPack, Monkey's Audio): a seek decodes the block the new place is in, and reading carries on from there.
/// Everything comes out as 32-bit float.
/// </summary>
public abstract class BlockAudioReader : WaveStream
{
    private readonly object _lock = new();
    private readonly Stream _stream;
    private readonly long _frames;
    private long _frame;
    private int _cachedBlock = -1;
    private int _cachedFrames;
    private float[] _cached = Array.Empty<float>();
    private byte[] _raw = Array.Empty<byte>();

    protected BlockAudioReader(Stream stream, int sampleRate, int channels, long frames)
    {
        _stream = stream;
        _frames = frames;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
    }

    /// <summary>The file's own bits per sample (24 for a 24-bit file), before being turned into floats.</summary>
    public int SourceBits { get; protected init; }

    /// <summary>True when the file holds floating-point samples.</summary>
    public bool IsSourceFloat { get; protected init; }

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

    /// <summary>How many blocks the file has.</summary>
    protected abstract int BlockCount { get; }

    /// <summary>The first frame of block <paramref name="block"/>.</summary>
    protected abstract long BlockStart(int block);

    /// <summary>
    /// Decodes block <paramref name="block"/> into <paramref name="output"/>, interleaved, scaled to ±1, and returns how
    /// many frames it holds. <paramref name="read"/> reads the file's bytes at a place.
    /// </summary>
    protected abstract int DecodeBlock(int block, Func<long, int, byte[]> read, ref float[] output);

    public override int Read(byte[] buffer, int offset, int count)
    {
        var channels = WaveFormat.Channels;
        lock (_lock)
        {
            var wanted = (int)Math.Min(count / WaveFormat.BlockAlign, _frames - _frame);
            var output = System.Runtime.InteropServices.MemoryMarshal.Cast<byte, float>(buffer.AsSpan(offset, Math.Max(0, wanted) * channels * 4));
            var done = 0;
            while (done < wanted)
            {
                var block = FindBlock(_frame);
                var frames = Decoded(block);
                var start = BlockStart(block);
                var into = (int)(_frame - start);
                var take = Math.Min(wanted - done, frames - into);
                if (take <= 0)
                {
                    // A block shorter than the next one's start says: the gap reads as silence.
                    var next = block + 1 < BlockCount ? BlockStart(block + 1) : _frames;
                    take = (int)Math.Min(wanted - done, Math.Max(1, next - _frame));
                    output.Slice(done * channels, take * channels).Clear();
                }
                else
                {
                    _cached.AsSpan(into * channels, take * channels).CopyTo(output.Slice(done * channels));
                }

                done += take;
                _frame += take;
            }

            return done * WaveFormat.BlockAlign;
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

    private int FindBlock(long frame)
    {
        int low = 0, high = BlockCount - 1;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (BlockStart(middle) <= frame)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low;
    }

    private int Decoded(int block)
    {
        if (block != _cachedBlock)
        {
            try
            {
                _cachedFrames = DecodeBlock(block, ReadBytes, ref _cached);
            }
            catch (Exception ex) when (ex is InvalidDataException or IndexOutOfRangeException or EndOfStreamException or ArgumentException)
            {
                // A damaged block plays as silence, like the reference decoders do, rather than stopping the song.
                _cachedFrames = 0;
            }

            _cachedBlock = block;
        }

        return _cachedFrames;
    }

    private byte[] ReadBytes(long position, int count)
    {
        if (_raw.Length < count)
        {
            _raw = new byte[count];
        }

        _stream.Position = position;
        var got = 0;
        while (got < count)
        {
            var read = _stream.Read(_raw, got, count - got);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            got += read;
        }

        return _raw;
    }

    /// <summary>Reads <paramref name="count"/> bytes at <paramref name="position"/> while a reader is being opened.</summary>
    protected static byte[] ReadAt(Stream stream, long position, int count)
    {
        var bytes = new byte[count];
        stream.Position = position;
        var got = 0;
        while (got < count)
        {
            var read = stream.Read(bytes, got, count - got);
            if (read <= 0)
            {
                throw new EndOfStreamException();
            }

            got += read;
        }

        return bytes;
    }
}
