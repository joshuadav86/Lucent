using System.Buffers.Binary;
using System.IO;
using System.Text;
using WinampInspiredVisualizer.Export;

namespace WinampInspiredVisualizer.Recording;

/// <summary>A place in a recording where a new song starts.</summary>
/// <param name="Frame">How far in, in sample frames.</param>
/// <param name="Title">What the track is called ("Track 3" until it's named).</param>
/// <param name="Automatic">Lucent heard the song change, rather than the button being pressed.</param>
public sealed record TrackMarker(long Frame, string Title, bool Automatic = false);

/// <summary>
/// A WAV file written as the sound arrives, for a live recording that may run for hours. The header is
/// brought up to date every few seconds, so if Lucent or the computer stops part way, what was recorded
/// up to then still opens. A file that grows past 4 GB (about 3 hours of 96 kHz 24-bit stereo) becomes an
/// RF64 file, the broadcast standard's way of going past WAV's limit, which DAWs and editors read.
/// </summary>
/// <remarks>
/// When it's finished, the track markers go in as cue points with labels (the way Sound Forge, Audition,
/// REAPER and Wavelab mark regions, and Lucent's own player skips between them), and the title, date and
/// "Lucent" as INFO tags. Not thread-safe: it's written from the recorder's own thread.
/// </remarks>
public sealed class WavRecording : IDisposable
{
    // Room for the ds64 chunk an RF64 file needs, kept as a JUNK chunk until then.
    private const int Ds64Size = 28;
    private const long RiffLimit = uint.MaxValue - 1024 * 1024;

    private readonly FileStream _file;
    private readonly PcmQuantizer? _quantizer;
    private readonly int _bytesPerSample;
    private readonly long _dataStart;
    private readonly long _factAt;
    private byte[] _scratch = Array.Empty<byte>();
    private int[] _whole = Array.Empty<int>();
    private bool _rf64;
    private bool _finished;

    /// <param name="bitDepth">16 or 24 for whole numbers (16 with dither), 32 for floating point.</param>
    public WavRecording(string path, int sampleRate, int channels, int bitDepth)
    {
        if (bitDepth is not (16 or 24 or 32))
        {
            throw new ArgumentOutOfRangeException(nameof(bitDepth), "Recordings are 16-bit, 24-bit or 32-bit float.");
        }

        Path = path;
        SampleRate = sampleRate;
        Channels = channels;
        BitDepth = bitDepth;
        _bytesPerSample = bitDepth / 8;
        _quantizer = bitDepth == 32 ? null : new PcmQuantizer(bitDepth);
        _file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 1 << 16);

        var isFloat = bitDepth == 32;
        var header = new MemoryStream();
        using (var writer = new BinaryWriter(header, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write("RIFF"u8);
            writer.Write(0u);
            writer.Write("WAVE"u8);
            writer.Write("JUNK"u8);
            writer.Write(Ds64Size);
            writer.Write(new byte[Ds64Size]);
            writer.Write("fmt "u8);
            writer.Write(isFloat ? 18 : 16);
            writer.Write((short)(isFloat ? 3 : 1));
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * _bytesPerSample);
            writer.Write((short)(channels * _bytesPerSample));
            writer.Write((short)bitDepth);
            if (isFloat)
            {
                writer.Write((short)0);
                // Floating-point WAV files are meant to say how many frames they hold.
                writer.Write("fact"u8);
                writer.Write(4);
                _factAt = header.Position;
                writer.Write(0u);
            }
            else
            {
                _factAt = -1;
            }

            writer.Write("data"u8);
            writer.Write(0u);
        }

        _file.Write(header.ToArray());
        _dataStart = _file.Position;
    }

    public string Path { get; }

    public int SampleRate { get; }

    public int Channels { get; }

    public int BitDepth { get; }

    /// <summary>How many sample frames have been written.</summary>
    public long Frames { get; private set; }

    /// <summary>How big the file is so far, in bytes.</summary>
    public long Bytes => _dataStart + DataBytes;

    public TimeSpan Duration => TimeSpan.FromSeconds((double)Frames / SampleRate);

    private long DataBytes => Frames * Channels * _bytesPerSample;

    /// <summary>Adds interleaved audio, in this file's number of channels.</summary>
    public void Write(ReadOnlySpan<float> samples)
    {
        var count = samples.Length - samples.Length % Channels;
        var bytes = count * _bytesPerSample;
        if (_scratch.Length < bytes)
        {
            _scratch = new byte[bytes];
            _whole = new int[count];
        }

        var output = _scratch.AsSpan(0, bytes);
        if (_quantizer is null)
        {
            for (var i = 0; i < count; i++)
            {
                BinaryPrimitives.WriteSingleLittleEndian(output[(i * 4)..], samples[i]);
            }
        }
        else
        {
            _quantizer.Quantize(samples[..count], _whole);
            for (var i = 0; i < count; i++)
            {
                var value = _whole[i];
                var at = i * _bytesPerSample;
                output[at] = (byte)value;
                output[at + 1] = (byte)(value >> 8);
                if (_bytesPerSample == 3)
                {
                    output[at + 2] = (byte)(value >> 16);
                }
            }
        }

        _file.Write(output);
        Frames += count / Channels;
        if (!_rf64 && Bytes > RiffLimit)
        {
            _rf64 = true;
        }
    }

    /// <summary>Brings the header up to date and pushes what's written to the disk, so the file opens even if Lucent stops now.</summary>
    public void Flush()
    {
        WriteSizes(Bytes - 8, DataBytes);
        _file.Flush(flushToDisk: true);
    }

    /// <summary>Adds the markers and tags, writes the final header and closes the file.</summary>
    /// <param name="title">The recording's name, for its tags.</param>
    public void Finish(IReadOnlyList<TrackMarker> markers, string? title)
    {
        if (_finished)
        {
            return;
        }

        _finished = true;
        _file.Seek(0, SeekOrigin.End);
        if (DataBytes % 2 == 1)
        {
            // Chunks start on even bytes.
            _file.WriteByte(0);
        }

        var tail = new MemoryStream();
        using (var writer = new BinaryWriter(tail, Encoding.UTF8, leaveOpen: true))
        {
            var cues = markers.Where(m => m.Frame >= 0 && m.Frame <= Frames).OrderBy(m => m.Frame).ToList();
            if (cues.Count > 0)
            {
                writer.Write("cue "u8);
                writer.Write(4 + cues.Count * 24);
                writer.Write(cues.Count);
                for (var i = 0; i < cues.Count; i++)
                {
                    var frame = (uint)Math.Min(cues[i].Frame, uint.MaxValue);
                    writer.Write(i + 1);
                    writer.Write(frame);
                    writer.Write("data"u8);
                    writer.Write(0);
                    writer.Write(0);
                    writer.Write(frame);
                }

                var labels = new MemoryStream();
                using (var adtl = new BinaryWriter(labels, Encoding.UTF8, leaveOpen: true))
                {
                    adtl.Write("adtl"u8);
                    for (var i = 0; i < cues.Count; i++)
                    {
                        var text = Encoding.UTF8.GetBytes(cues[i].Title + "\0");
                        adtl.Write("labl"u8);
                        adtl.Write(4 + text.Length);
                        adtl.Write(i + 1);
                        adtl.Write(text);
                        if ((4 + text.Length) % 2 == 1)
                        {
                            adtl.Write((byte)0);
                        }
                    }
                }

                WriteList(writer, labels.ToArray());
            }

            var info = new MemoryStream();
            using (var tags = new BinaryWriter(info, Encoding.UTF8, leaveOpen: true))
            {
                tags.Write("INFO"u8);
                if (!string.IsNullOrWhiteSpace(title))
                {
                    WriteText(tags, "INAM"u8, title);
                }

                WriteText(tags, "ICRD"u8, DateTime.Now.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
                WriteText(tags, "ISFT"u8, "Lucent");
            }

            WriteList(writer, info.ToArray());
        }

        _file.Write(tail.ToArray());
        if (Bytes + tail.Length + 1 > RiffLimit)
        {
            _rf64 = true;
        }

        WriteSizes(_file.Length - 8, DataBytes);
        _file.Flush(flushToDisk: true);
        _file.Dispose();
    }

    public void Dispose()
    {
        if (!_finished)
        {
            Finish(Array.Empty<TrackMarker>(), null);
        }
    }

    private static void WriteList(BinaryWriter writer, byte[] body)
    {
        writer.Write("LIST"u8);
        writer.Write(body.Length);
        writer.Write(body);
        if (body.Length % 2 == 1)
        {
            writer.Write((byte)0);
        }
    }

    private static void WriteText(BinaryWriter writer, ReadOnlySpan<byte> id, string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text + "\0");
        writer.Write(id);
        writer.Write(bytes.Length);
        writer.Write(bytes);
        if (bytes.Length % 2 == 1)
        {
            writer.Write((byte)0);
        }
    }

    // Writes the RIFF and data sizes (and the frame count of a float file), as RF64 once it's too big for WAV.
    private void WriteSizes(long riffSize, long dataSize)
    {
        var end = _file.Position;
        Span<byte> four = stackalloc byte[4];
        if (_rf64)
        {
            _file.Seek(0, SeekOrigin.Begin);
            _file.Write("RF64"u8);
            BinaryPrimitives.WriteUInt32LittleEndian(four, uint.MaxValue);
            _file.Write(four);
            _file.Seek(12, SeekOrigin.Begin);
            Span<byte> ds64 = stackalloc byte[8 + Ds64Size];
            "ds64"u8.CopyTo(ds64);
            BinaryPrimitives.WriteInt32LittleEndian(ds64[4..], Ds64Size);
            BinaryPrimitives.WriteInt64LittleEndian(ds64[8..], riffSize);
            BinaryPrimitives.WriteInt64LittleEndian(ds64[16..], dataSize);
            BinaryPrimitives.WriteInt64LittleEndian(ds64[24..], Frames);
            BinaryPrimitives.WriteInt32LittleEndian(ds64[32..], 0);
            _file.Write(ds64);
            _file.Seek(_dataStart - 4, SeekOrigin.Begin);
            _file.Write(four);
            if (_factAt >= 0)
            {
                _file.Seek(_factAt, SeekOrigin.Begin);
                _file.Write(four);
            }
        }
        else
        {
            _file.Seek(4, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt32LittleEndian(four, (uint)riffSize);
            _file.Write(four);
            _file.Seek(_dataStart - 4, SeekOrigin.Begin);
            BinaryPrimitives.WriteUInt32LittleEndian(four, (uint)dataSize);
            _file.Write(four);
            if (_factAt >= 0)
            {
                _file.Seek(_factAt, SeekOrigin.Begin);
                BinaryPrimitives.WriteUInt32LittleEndian(four, (uint)Frames);
                _file.Write(four);
            }
        }

        _file.Seek(end, SeekOrigin.Begin);
    }
}
