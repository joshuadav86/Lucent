using System.Buffers.Binary;
using System.IO;
using System.Text;
using SharpJaad.AAC;
using Decoder = SharpJaad.AAC.Decoder;

namespace WinampInspiredVisualizer.Video;

/// <summary>
/// Decodes the AAC sound of an MP4, M4V or MOV with Lucent's own decoder (SharpJaad, as the radio uses), straight
/// from the file's sample table, so a video's sound reads the same on Windows and the Mac whatever the system's own
/// decoders make of the file. Each packet is placed where the file says it plays, after the track's edit list (its
/// start, which skips the encoder's priming, and any delay before it), so the sound lines up with the pictures.
/// </summary>
public static class Mp4Sound
{
    /// <summary>
    /// The first sound track, decoded: one array per channel (surround mixed down to stereo), its sample rate, and
    /// how many channels it had. Null when it isn't AAC, or the decoder can't take it, so the caller can try the
    /// system's decoders instead.
    /// </summary>
    public static (float[][] Channels, int SampleRate, int SourceChannels, int BitrateKbps)? DecodeAac(Mp4Movie movie,
        TimeSpan longest, IProgress<double>? progress, CancellationToken cancellation)
    {
        var track = movie.Tracks.FirstOrDefault(each => each.IsSound && each.Samples.Length > 0);
        if (track?.SampleEntry is not { } entry || track.Format != "mp4a" || AudioSpecificConfig(entry) is not { } config)
        {
            return null;
        }

        Decoder decoder;
        try
        {
            decoder = new Decoder(config);
        }
        catch (Exception)
        {
            return null;
        }

        var decoded = new SampleBuffer();
        decoded.SetBigEndian(false);
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

            if (packet.Length < sample.Size)
            {
                packet = new byte[sample.Size];
            }

            file.Position = sample.Offset;
            file.ReadExactly(packet, 0, (int)sample.Size);
            bytes += sample.Size;
            try
            {
                decoder.DecodeFrame(sample.Size == packet.Length ? packet : packet[..(int)sample.Size], decoded);
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
                rate = decoded.SampleRate;
                channels = decoded.Channels;
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

            if (decoded.SampleRate != rate || decoded.Channels != channels)
            {
                continue;
            }

            var count = decoded.Data.Length / 2 / channels;
            if (chunk.Length < count * channels)
            {
                chunk = new float[count * channels];
            }

            for (var s = 0; s < count * channels; s++)
            {
                chunk[s] = BinaryPrimitives.ReadInt16LittleEndian(decoded.Data.AsSpan(s * 2)) / 32768f;
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
        return (output, rate, channels, (int)Math.Round(bytes * 8 / seconds / 1000));
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
