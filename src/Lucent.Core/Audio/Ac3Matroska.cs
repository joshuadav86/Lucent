using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Lucent.Audio;

/// <summary>
/// Copies a raw Dolby Digital (.ac3) or Dolby Digital Plus (.eac3, .ec3) file's frames into a Matroska audio file,
/// unchanged, for <see cref="LibVlcAudioDecoder"/>. Windows' libVLC (3.0.24) stops after the first two frames of a raw
/// AC-3 or E-AC-3 file, but reads the same frames from Matroska whole, as the Mac's does either way.
/// </summary>
public static class Ac3Matroska
{
    /// <summary>The extensions of raw AC-3 and E-AC-3 files.</summary>
    public static bool IsRawAc3(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".ac3" or ".eac3" or ".ec3";

    // Frame sizes in 16-bit words for each frmsizecod (two rows each), at 48, 44.1 and 32 kHz.
    private static readonly int[,] Ac3FrameWords =
    {
        { 64, 69, 96 }, { 64, 70, 96 }, { 80, 87, 120 }, { 80, 88, 120 }, { 96, 104, 144 }, { 96, 105, 144 },
        { 112, 121, 168 }, { 112, 122, 168 }, { 128, 139, 192 }, { 128, 140, 192 }, { 160, 174, 240 }, { 160, 175, 240 },
        { 192, 208, 288 }, { 192, 209, 288 }, { 224, 243, 336 }, { 224, 244, 336 }, { 256, 278, 384 }, { 256, 279, 384 },
        { 320, 348, 480 }, { 320, 349, 480 }, { 384, 417, 576 }, { 384, 418, 576 }, { 448, 487, 672 }, { 448, 488, 672 },
        { 512, 557, 768 }, { 512, 558, 768 }, { 640, 696, 960 }, { 640, 697, 960 }, { 768, 835, 1152 }, { 768, 836, 1152 },
        { 896, 975, 1344 }, { 896, 976, 1344 }, { 1024, 1114, 1536 }, { 1024, 1115, 1536 }, { 1152, 1253, 1728 },
        { 1152, 1254, 1728 }, { 1280, 1393, 1920 }, { 1280, 1394, 1920 },
    };

    private static readonly int[] Rates = { 48000, 44100, 32000 };

    /// <summary>One frame: where it is in the file, how long it is, and how many samples it holds.</summary>
    private readonly record struct Frame(long Offset, int Length, int Samples);

    /// <summary>
    /// Writes <paramref name="source"/>'s frames to a Matroska file at <paramref name="destination"/>. Throws
    /// <see cref="InvalidDataException"/> when it holds no AC-3 or E-AC-3 frames.
    /// </summary>
    public static void Wrap(string source, string destination)
    {
        var data = File.ReadAllBytes(source);
        var frames = new List<Frame>();
        int rate = 0, channels = 2;
        var enhanced = false;
        var at = 0;
        while (at + 6 <= data.Length)
        {
            if (data[at] != 0x0B || data[at + 1] != 0x77)
            {
                // Lost the beat (a damaged frame, or a tag): look for the next frame's start.
                at++;
                continue;
            }

            var bsid = data[at + 5] >> 3;
            int length, samples, frameRate;
            if (bsid <= 10)
            {
                var fscod = data[at + 4] >> 6;
                var frmsizecod = data[at + 4] & 0x3F;
                if (fscod == 3 || frmsizecod >= 38)
                {
                    at++;
                    continue;
                }

                length = Ac3FrameWords[frmsizecod, fscod] * 2;
                samples = 1536;
                frameRate = Rates[fscod];
            }
            else if (bsid <= 16)
            {
                enhanced = true;
                length = ((((data[at + 2] & 0x07) << 8) | data[at + 3]) + 1) * 2;
                var fscod = data[at + 4] >> 6;
                var numblkscod = (data[at + 4] >> 4) & 0x03;
                frameRate = fscod == 3 ? Rates[(data[at + 4] >> 4) & 0x03] / 2 : Rates[fscod];
                samples = fscod == 3 ? 1536 : new[] { 256, 512, 768, 1536 }[numblkscod];
                // Dependent substreams (extra channels for 7.1) go in the same block as the frame they belong to.
                if (((data[at + 2] >> 6) & 0x03) == 1 && frames.Count > 0)
                {
                    samples = 0;
                }
            }
            else
            {
                at++;
                continue;
            }

            if (length < 6 || at + length > data.Length)
            {
                break;
            }

            if (rate == 0)
            {
                rate = frameRate;
                channels = Channels(data, at, bsid);
            }

            if (samples == 0)
            {
                var last = frames[^1];
                frames[^1] = last with { Length = (int)(at + length - last.Offset) };
            }
            else
            {
                frames.Add(new Frame(at, length, samples));
            }

            at += length;
        }

        if (frames.Count == 0)
        {
            throw new InvalidDataException("No Dolby Digital frames were found in the file.");
        }

        using var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16);
        Write(output, Element(0x1A45DFA3,
            Element(0x4286, Uint(1)), Element(0x42F7, Uint(1)), Element(0x42F2, Uint(4)), Element(0x42F3, Uint(8)),
            Element(0x4282, Encoding.ASCII.GetBytes("matroska")), Element(0x4287, Uint(4)), Element(0x4285, Uint(2))));
        // The segment's size is left unknown, so it runs to the end of the file.
        Write(output, Id(0x18538067));
        Write(output, new byte[] { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
        Write(output, Element(0x1549A966, Element(0x2AD7B1, Uint(1_000_000)), Element(0x4D80, Encoding.ASCII.GetBytes("Lucent")),
            Element(0x5741, Encoding.ASCII.GetBytes("Lucent"))));
        Write(output, Element(0x1654AE6B, Element(0xAE,
            Element(0xD7, Uint(1)), Element(0x73C5, Uint(1)), Element(0x83, Uint(2)), Element(0x9C, Uint(0)),
            Element(0x86, Encoding.ASCII.GetBytes(enhanced ? "A_EAC3" : "A_AC3")),
            Element(0xE1, Element(0xB5, Float(rate)), Element(0x9F, Uint((ulong)channels))))));

        // Clusters of up to 20 seconds, so each block's time fits the 16 bits it's given.
        var sample = 0L;
        var index = 0;
        while (index < frames.Count)
        {
            var clusterMs = sample * 1000 / rate;
            var blocks = new List<byte[]> { Element(0xE7, Uint((ulong)clusterMs)) };
            while (index < frames.Count && sample * 1000 / rate - clusterMs < 20_000)
            {
                var frame = frames[index++];
                var relative = (short)(sample * 1000 / rate - clusterMs);
                var block = new byte[4 + frame.Length];
                block[0] = 0x81;
                BinaryPrimitives.WriteInt16BigEndian(block.AsSpan(1), relative);
                block[3] = 0x80;
                Array.Copy(data, frame.Offset, block, 4, frame.Length);
                blocks.Add(Element(0xA3, block));
                sample += frame.Samples;
            }

            Write(output, Element(0x1F43B675, blocks.ToArray()));
        }
    }

    /// <summary>How many main channels a frame's header says (its acmod), for the track's header; libVLC reads the frames' own.</summary>
    private static int Channels(byte[] data, int at, int bsid)
    {
        // AC-3: acmod is the top three bits of the byte after bsid/bsmod. E-AC-3: after fscod, numblkscod.
        var acmod = bsid <= 10 ? data[at + 6] >> 5 : (data[at + 4] >> 1) & 0x07;
        var main = new[] { 2, 1, 2, 3, 3, 4, 4, 5 }[acmod];
        return main;
    }

    private static void Write(Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);

    private static byte[] Element(uint id, params byte[][] children)
    {
        var body = children.SelectMany(child => child).ToArray();
        return Element(id, body);
    }

    private static byte[] Element(uint id, byte[] body)
    {
        var head = Id(id);
        var size = Size(body.LongLength);
        var all = new byte[head.Length + size.Length + body.Length];
        head.CopyTo(all, 0);
        size.CopyTo(all, head.Length);
        body.CopyTo(all, head.Length + size.Length);
        return all;
    }

    private static byte[] Id(uint id)
    {
        var bytes = id > 0xFFFFFF ? 4 : id > 0xFFFF ? 3 : id > 0xFF ? 2 : 1;
        var result = new byte[bytes];
        for (var i = 0; i < bytes; i++)
        {
            result[i] = (byte)(id >> (8 * (bytes - 1 - i)));
        }

        return result;
    }

    /// <summary>An element's size, as EBML writes it: always in eight bytes, which every reader takes.</summary>
    private static byte[] Size(long size)
    {
        var result = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(result, size);
        result[0] = 0x01;
        return result;
    }

    private static byte[] Uint(ulong value)
    {
        var result = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(result, value);
        return result;
    }

    private static byte[] Float(double value)
    {
        var result = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(result, value);
        return result;
    }
}
