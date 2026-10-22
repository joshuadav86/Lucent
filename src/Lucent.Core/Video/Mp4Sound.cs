using System.Buffers.Binary;
using System.IO;
using System.Text;
using SharpJaad.AAC;
using Decoder = SharpJaad.AAC.Decoder;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// Decodes the sound of an MP4, M4V or MOV with Lucent's own decoders, straight from the file's sample table, so a
/// video's sound reads the same on Windows and the Mac whatever the system's own decoders make of the file: AAC with
/// SharpJaad (as the radio uses), and uncompressed sound (the "ipcm" and "fpcm" OBS and cameras write, and QuickTime's
/// own kinds) as it is. Each packet is placed where the file says it plays, after the track's edit list (its start,
/// which skips the encoder's priming, and any delay before it), so the sound lines up with the pictures.
/// </summary>
public static class Mp4Sound
{
    /// <summary>
    /// The first sound track, decoded: one array per channel (surround mixed down to stereo), its sample rate, how
    /// many channels it had, and its bits per sample (16 for AAC). Null when it's in a format Lucent doesn't decode
    /// itself, or the decoder can't take it, so the caller can try the system's decoders instead.
    /// </summary>
    public static (float[][] Channels, int SampleRate, int SourceChannels, int Bits, int BitrateKbps)? Decode(Mp4Movie movie,
        TimeSpan longest, IProgress<double>? progress, CancellationToken cancellation)
    {
        var track = movie.Tracks.FirstOrDefault(each => each.IsSound && each.Samples.Length > 0);
        if (track?.SampleEntry is not { } entry)
        {
            return null;
        }

        IPacketDecoder? decoder = null;
        if (track.Format == "mp4a")
        {
            if (AudioSpecificConfig(entry) is { } config)
            {
                try
                {
                    decoder = new AacDecoder(new Decoder(config));
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }
        else if (PcmLayout(entry, track.Timescale) is { } layout)
        {
            decoder = new PcmDecoder(layout);
        }

        return decoder is null ? null : Decode(movie, track, decoder, longest, progress, cancellation);
    }

    private static (float[][] Channels, int SampleRate, int SourceChannels, int Bits, int BitrateKbps)? Decode(Mp4Movie movie, Mp4Track track,
        IPacketDecoder decoder, TimeSpan longest, IProgress<double>? progress, CancellationToken cancellation)
    {
        using var file = new FileStream(movie.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16);
        float[][]? output = null;
        var rate = 0;
        var channels = 0;
        var outputChannels = 0;
        long skip = 0;
        long lead = 0;
        var packet = Array.Empty<byte>();
        var chunk = Array.Empty<float>();
        long bytes = 0;
        var failed = 0;
        for (var i = 0; i < track.Samples.Length; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var sample = track.Samples[i];
            if (sample.Size <= 0 || sample.Size > 1 << 20)
            {
                continue;
            }

            // Uncompressed sound is often stored a frame to a sample: runs that follow on in the file and in time are
            // read as one packet.
            while (decoder.Joins && i + 1 < track.Samples.Length && sample.Size < 1 << 18)
            {
                var next = track.Samples[i + 1];
                if (next.Offset != sample.Offset + sample.Size || next.Dts != sample.Dts + sample.Duration || next.Size <= 0)
                {
                    break;
                }

                sample = sample with { Size = sample.Size + next.Size, Duration = sample.Duration + next.Duration };
                i++;
            }

            if (packet.Length < sample.Size)
            {
                packet = new byte[sample.Size];
            }

            file.Position = sample.Offset;
            file.ReadExactly(packet, 0, (int)sample.Size);
            bytes += sample.Size;
            int count, rateNow, channelsNow;
            try
            {
                (count, rateNow, channelsNow) = decoder.Decode(packet, (int)sample.Size, ref chunk);
            }
            catch (Exception)
            {
                // A damaged packet stays silent; the next one decodes as usual. A sound that won't decode at all
                // goes to the system's decoders.
                if (++failed > 8 && output is null)
                {
                    return null;
                }

                continue;
            }

            if (output is null)
            {
                rate = rateNow;
                channels = channelsNow;
                if (rate <= 0 || channels is <= 0 or > 8)
                {
                    return null;
                }

                if (track.End > longest.TotalSeconds)
                {
                    throw new InvalidDataException("This video's sound is longer than the editor can hold in memory.");
                }

                outputChannels = Math.Min(2, channels);
                // HE-AAC decodes at twice the rate the track counts in, so the track's ticks are scaled to the sound's.
                skip = (long)Math.Round(track.Shift * (double)rate / track.Timescale);
                lead = (long)Math.Round(track.Delay * rate);
                var frames = (int)Math.Max(1, Math.Round(track.End * rate));
                output = Enumerable.Range(0, outputChannels).Select(_ => new float[frames]).ToArray();
            }

            if (rateNow != rate || channelsNow != channels || count <= 0)
            {
                continue;
            }

            // Where this packet plays: its place in the track, less the edit list's start, after any delay.
            var at = (long)Math.Round(sample.Dts * (double)rate / track.Timescale) - skip + lead;
            var from = (int)Math.Clamp(-at, 0, count);
            var length = (int)Math.Clamp(output[0].Length - at, 0, count);
            if (length > from)
            {
                var place = new float[outputChannels][];
                // The packet's frames, de-interleaved (and surround folded down) into a scratch space, then copied in.
                for (var c = 0; c < outputChannels; c++)
                {
                    place[c] = new float[count];
                }

                Editor.AudioFileLoader.CopyFrames(chunk, count, channels, place, 0);
                for (var c = 0; c < outputChannels; c++)
                {
                    Array.Copy(place[c], from, output[c], (int)(at + from), length - from);
                }
            }

            if ((i & 255) == 0)
            {
                progress?.Report((double)i / track.Samples.Length);
            }
        }

        if (output is null)
        {
            return null;
        }

        var seconds = Math.Max(0.001, track.End - track.Delay);
        return (output, rate, channels, decoder.Bits, (int)Math.Round(bytes * 8 / seconds / 1000));
    }

    // ---- Decoders ----

    private interface IPacketDecoder
    {
        /// <summary>Bits per sample of the source, for the file's details.</summary>
        int Bits { get; }

        /// <summary>Whether packets that follow on can be read together (uncompressed sound).</summary>
        bool Joins { get; }

        /// <summary>Decodes a packet into <paramref name="samples"/> (interleaved, grown as needed): frames, rate, channels.</summary>
        (int Frames, int Rate, int Channels) Decode(byte[] packet, int size, ref float[] samples);
    }

    private sealed class AacDecoder : IPacketDecoder
    {
        private readonly Decoder _decoder;
        private readonly SampleBuffer _decoded = new();

        public AacDecoder(Decoder decoder)
        {
            _decoder = decoder;
            _decoded.SetBigEndian(false);
        }

        public int Bits => 16;

        public bool Joins => false;

        public (int Frames, int Rate, int Channels) Decode(byte[] packet, int size, ref float[] samples)
        {
            _decoder.DecodeFrame(size == packet.Length ? packet : packet[..size], _decoded);
            var channels = _decoded.Channels;
            if (channels <= 0)
            {
                return (0, _decoded.SampleRate, channels);
            }

            var count = _decoded.Data.Length / 2 / channels;
            if (samples.Length < count * channels)
            {
                samples = new float[count * channels];
            }

            for (var s = 0; s < count * channels; s++)
            {
                samples[s] = BinaryPrimitives.ReadInt16LittleEndian(_decoded.Data.AsSpan(s * 2)) / 32768f;
            }

            return (count, _decoded.SampleRate, channels);
        }
    }

    /// <summary>How uncompressed sound is laid out: rate, channels, bytes per sample, and whether it's floating point or little-endian.</summary>
    internal readonly record struct PcmFormat(int Rate, int Channels, int Bytes, bool Float, bool LittleEndian, bool Unsigned = false);

    private sealed class PcmDecoder : IPacketDecoder
    {
        private readonly PcmFormat _format;

        public PcmDecoder(PcmFormat format) => _format = format;

        public int Bits => _format.Bytes * 8;

        public bool Joins => true;

        public (int Frames, int Rate, int Channels) Decode(byte[] packet, int size, ref float[] samples)
        {
            var (rate, channels, width, isFloat, little, unsigned) = _format;
            var count = size / (width * channels);
            if (samples.Length < count * channels)
            {
                samples = new float[count * channels];
            }

            var data = packet.AsSpan(0, count * channels * width);
            for (var s = 0; s < count * channels; s++)
            {
                var b = data.Slice(s * width, width);
                samples[s] = (width, isFloat, little) switch
                {
                    (4, true, true) => BinaryPrimitives.ReadSingleLittleEndian(b),
                    (4, true, false) => BinaryPrimitives.ReadSingleBigEndian(b),
                    (8, true, true) => (float)BinaryPrimitives.ReadDoubleLittleEndian(b),
                    (8, true, false) => (float)BinaryPrimitives.ReadDoubleBigEndian(b),
                    (1, _, _) => (unsigned ? b[0] - 128 : (sbyte)b[0]) / 128f,
                    (2, _, true) => BinaryPrimitives.ReadInt16LittleEndian(b) / 32768f,
                    (2, _, false) => BinaryPrimitives.ReadInt16BigEndian(b) / 32768f,
                    (3, _, true) => ((b[2] << 24) | (b[1] << 16) | (b[0] << 8)) / 2147483648f,
                    (3, _, false) => ((b[0] << 24) | (b[1] << 16) | (b[2] << 8)) / 2147483648f,
                    (4, _, true) => BinaryPrimitives.ReadInt32LittleEndian(b) / 2147483648f,
                    _ => BinaryPrimitives.ReadInt32BigEndian(b) / 2147483648f,
                };
            }

            return (count, rate, channels);
        }
    }

    /// <summary>
    /// The layout of uncompressed sound from its sample description, or null when it isn't uncompressed. "ipcm" and
    /// "fpcm" (MP4's own, which OBS writes) say it in a "pcmC" box. QuickTime's "sowt", "twos", "in24", "in32", "fl32",
    /// "fl64" and "raw " say it in their name (big-endian unless an "enda" box says otherwise), and "lpcm" in its
    /// version 2 description's flags, which any of them may also have.
    /// </summary>
    internal static PcmFormat? PcmLayout(byte[] entry, uint timescale = 0)
    {
        if (entry.Length < 36)
        {
            return null;
        }

        var type = Encoding.ASCII.GetString(entry, 4, 4);
        var version = BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(16));
        var channels = (int)BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(24));
        var bits = (int)BinaryPrimitives.ReadUInt16BigEndian(entry.AsSpan(26));
        var rate = (int)(BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(32)) >> 16);
        // QuickTime's version 2 holds the rate, channels and bits further on, with flags saying the kind.
        uint? flags = null;
        if (version == 2)
        {
            if (entry.Length < 72)
            {
                return null;
            }

            rate = (int)Math.Round(BinaryPrimitives.ReadDoubleBigEndian(entry.AsSpan(40)));
            channels = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(48));
            bits = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(56));
            flags = BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(60));
        }
        else if (Find(entry, "srat") is { } srat && srat + 16 <= entry.Length)
        {
            // A rate too high for 16 bits.
            rate = (int)BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(srat + 12));
        }
        else if (rate == 0 && timescale <= 768_000)
        {
            // FFmpeg leaves a rate over 65535 out (no srat); the track's timescale is the rate.
            rate = (int)timescale;
        }

        var little = Find(entry, "enda") is { } enda && enda + 10 <= entry.Length && entry[enda + 9] != 0;
        PcmFormat format;
        switch (type)
        {
            case "ipcm" or "fpcm":
                if (Find(entry, "pcmC") is not { } pcmC || pcmC + 14 > entry.Length)
                {
                    return null;
                }

                format = new PcmFormat(rate, channels, entry[pcmC + 13] / 8, type == "fpcm", (entry[pcmC + 12] & 1) != 0);
                break;
            case "lpcm":
                // Core Audio's flags: 1 floating point, 2 big-endian, 4 signed.
                if (flags is not { } kind)
                {
                    return null;
                }

                format = new PcmFormat(rate, channels, bits / 8, (kind & 1) != 0, (kind & 2) == 0, (kind & 5) == 0);
                break;
            case "sowt":
                format = new PcmFormat(rate, channels, bits / 8, false, true);
                break;
            case "twos":
                format = new PcmFormat(rate, channels, bits / 8, false, false);
                break;
            case "raw ":
                format = new PcmFormat(rate, channels, 1, false, false, Unsigned: true);
                break;
            case "in24":
                format = new PcmFormat(rate, channels, 3, false, little);
                break;
            case "in32":
                format = new PcmFormat(rate, channels, 4, false, little);
                break;
            case "fl32":
                format = new PcmFormat(rate, channels, 4, true, little);
                break;
            case "fl64":
                format = new PcmFormat(rate, channels, 8, true, little);
                break;
            default:
                return null;
        }

        var valid = format.Rate is > 0 and <= 768_000 && format.Channels is > 0 and <= 8
            && (format.Float ? format.Bytes is 4 or 8 : format.Bytes is >= 1 and <= 4);
        return valid ? format : null;
    }

    // Where a box of this type starts inside a sample description (after its own fields), or null.
    private static int? Find(byte[] entry, string type)
    {
        var found = entry.AsSpan(36).IndexOf(Encoding.ASCII.GetBytes(type));
        return found < 0 ? null : 36 + found - 4;
    }

    /// <summary>
    /// The AAC decoder's setup (the "AudioSpecificConfig") from an "mp4a" sample description: inside its "esds" box
    /// (straight in an MP4, or in a "wave" box in a QuickTime file), or null when it isn't AAC.
    /// </summary>
    internal static byte[]? AudioSpecificConfig(byte[] entry)
    {
        var esds = Encoding.ASCII.GetBytes("esds");
        var found = entry.AsSpan(8).IndexOf(esds);
        if (found < 0)
        {
            return null;
        }

        var start = 8 + found - 4;
        var size = BinaryPrimitives.ReadUInt32BigEndian(entry.AsSpan(start));
        var end = (int)Math.Min(entry.Length, start + (long)size);
        // After the box's header and its version and flags: the elementary stream's descriptors.
        var at = start + 12;
        if (ReadDescriptor(entry, ref at, end) is not (0x03, var esEnd))
        {
            return null;
        }

        at += 2;
        var flags = entry[at++];
        if ((flags & 0x80) != 0)
        {
            at += 2;
        }

        if ((flags & 0x40) != 0 && at < esEnd)
        {
            at += 1 + entry[at];
        }

        if ((flags & 0x20) != 0)
        {
            at += 2;
        }

        if (ReadDescriptor(entry, ref at, esEnd) is not (0x04, var configEnd) || at + 13 > configEnd)
        {
            return null;
        }

        // MPEG-4 audio, or one of MPEG-2's AAC profiles.
        var kind = entry[at];
        if (kind is not (0x40 or 0x66 or 0x67 or 0x68))
        {
            return null;
        }

        at += 13;
        if (ReadDescriptor(entry, ref at, configEnd) is not (0x05, var infoEnd) || infoEnd <= at)
        {
            return null;
        }

        return entry[at..infoEnd];
    }

    // A descriptor's tag, and where it ends; at moves to its contents.
    private static (int Tag, int End)? ReadDescriptor(byte[] data, ref int at, int limit)
    {
        if (at >= limit)
        {
            return null;
        }

        var tag = data[at++];
        var length = 0;
        for (var i = 0; i < 4 && at < limit; i++)
        {
            var b = data[at++];
            length = (length << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        var end = at + length;
        return end <= limit ? (tag, end) : null;
    }
}
