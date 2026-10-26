using System.Buffers.Binary;
using System.IO;
using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>
/// Copies the FLAC frames in an Ogg file (FLAC in Ogg, .oga) into a plain .flac file, unchanged, for the system's FLAC
/// decoder. Neither system decoder reads FLAC in Ogg, and libVLC (3.0) stops after its first packet.
/// </summary>
public static class OggFlac
{
    /// <summary>The most bytes skipped looking for the first page.</summary>
    private const int MaxJunk = 64 * 1024;

    /// <summary>True when the file's first Ogg stream holds FLAC.</summary>
    public static bool IsOggFlac(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            var head = new byte[Math.Min(MaxJunk, file.Length)];
            var got = file.Read(head, 0, head.Length);
            for (var at = 0; at + 27 <= got; at++)
            {
                if (head[at] != 'O' || head[at + 1] != 'g' || head[at + 2] != 'g' || head[at + 3] != 'S')
                {
                    continue;
                }

                var segments = head[at + 26];
                var body = at + 27 + segments;
                return body + 5 <= got && head[body] == 0x7F
                    && head[body + 1] == 'F' && head[body + 2] == 'L' && head[body + 3] == 'A' && head[body + 4] == 'C';
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return false;
    }

    /// <summary>
    /// Opens a FLAC in Ogg file with <paramref name="openFlac"/>, by way of a temporary .flac file that's deleted when
    /// the stream is closed.
    /// </summary>
    public static WaveStream Open(string path, Func<string, WaveStream> openFlac)
    {
        var temp = Path.Combine(Path.GetTempPath(), $"Lucent-oggflac-{Guid.NewGuid():N}.flac");
        try
        {
            Remux(path, temp);
            return new TempFileStream(openFlac(temp), temp);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    /// <summary>
    /// Writes <paramref name="source"/>'s FLAC stream to a .flac file at <paramref name="destination"/>. Throws
    /// <see cref="InvalidDataException"/> when it holds no FLAC in Ogg.
    /// </summary>
    public static void Remux(string source, string destination)
    {
        using var input = new BufferedStream(File.OpenRead(source), 1 << 16);
        using var output = new FileStream(destination, FileMode.Create, FileAccess.ReadWrite);
        uint? serial = null;
        long lastGranule = -1;
        var packet = new MemoryStream();
        var packets = 0;
        long streamInfoAt = -1;
        var header = new byte[27];
        var table = new byte[255];
        var body = new byte[255 * 255];
        var skipped = 0;
        while (true)
        {
            if (!ReadPage(input, header, ref skipped))
            {
                break;
            }

            int segments = header[26];
            ReadExactly(input, table, segments);
            var length = 0;
            for (var i = 0; i < segments; i++)
            {
                length += table[i];
            }

            ReadExactly(input, body, length);
            var pageSerial = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(14));
            serial ??= pageSerial;
            if (pageSerial != serial)
            {
                // Another stream multiplexed alongside (rare): only the first is the FLAC.
                continue;
            }

            if ((header[5] & 1) == 0)
            {
                // A page that doesn't continue a packet starts a new one; anything left over was cut off.
                packet.SetLength(0);
            }

            var at = 0;
            for (var i = 0; i < segments; i++)
            {
                packet.Write(body, at, table[i]);
                at += table[i];
                if (table[i] < 255)
                {
                    WritePacket(packet.GetBuffer().AsSpan(0, (int)packet.Length), packets++, output, ref streamInfoAt);
                    packet.SetLength(0);
                }
            }

            var granule = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(6));
            if (granule > 0)
            {
                lastGranule = granule;
            }

            if ((header[5] & 4) != 0)
            {
                break;
            }
        }

        if (streamInfoAt < 0)
        {
            throw new InvalidDataException("This Ogg file doesn't hold FLAC.");
        }

        // The Ogg muxer can leave the total sample count at 0 (it isn't known while streaming); the last page's
        // granule position is the true count, which the decoder needs for the length and seeking.
        if (lastGranule > 0)
        {
            var info = new byte[8];
            output.Position = streamInfoAt + 13;
            ReadExactly(output, info, 5);
            var total = ((long)(info[0] & 0x0F) << 32) | BinaryPrimitives.ReadUInt32BigEndian(info.AsSpan(1));
            if (total == 0 && lastGranule < 1L << 36)
            {
                info[0] = (byte)((info[0] & 0xF0) | (int)(lastGranule >> 32));
                BinaryPrimitives.WriteUInt32BigEndian(info.AsSpan(1), (uint)lastGranule);
                output.Position = streamInfoAt + 13;
                output.Write(info, 0, 5);
            }
        }
    }

    private static void WritePacket(ReadOnlySpan<byte> packet, int index, Stream output, ref long streamInfoAt)
    {
        if (index == 0)
        {
            // 0x7F "FLAC", the mapping's version (2 bytes), how many header packets follow (2), then "fLaC" and the
            // STREAMINFO block, which is written alone, marked last (the tags are read from the Ogg file itself).
            if (packet.Length < 13 + 4 + 34 || packet[0] != 0x7F || packet[1] != 'F' || packet[9] != 'f' || packet[12] != 'C'
                || (packet[13] & 0x7F) != 0)
            {
                throw new InvalidDataException("This Ogg file doesn't hold FLAC.");
            }

            output.Write(packet.Slice(9, 4));
            var block = packet.Slice(13, 4 + 34).ToArray();
            block[0] |= 0x80;
            streamInfoAt = output.Position + 4;
            output.Write(block);
            return;
        }

        // The other header packets are metadata blocks; after them, each packet is one FLAC frame (sync code 0xFFF8
        // or 0xFFF9). The count in the first packet can be 0 (unknown), so frames are told apart by their sync code.
        if (packet.Length >= 2 && packet[0] == 0xFF && (packet[1] & 0xFE) == 0xF8)
        {
            output.Write(packet);
        }
    }

    private static bool ReadPage(Stream input, byte[] header, ref int skipped)
    {
        var got = ReadUpTo(input, header, 0, 27);
        if (got < 27)
        {
            return false;
        }

        // Find the capture pattern, in case of junk before or between pages.
        while (header[0] != 'O' || header[1] != 'g' || header[2] != 'g' || header[3] != 'S')
        {
            if (++skipped > MaxJunk)
            {
                return false;
            }

            Buffer.BlockCopy(header, 1, header, 0, 26);
            if (ReadUpTo(input, header, 26, 1) < 1)
            {
                return false;
            }
        }

        return true;
    }

    private static int ReadUpTo(Stream input, byte[] buffer, int offset, int count)
    {
        var got = 0;
        while (got < count)
        {
            var read = input.Read(buffer, offset + got, count - got);
            if (read <= 0)
            {
                break;
            }

            got += read;
        }

        return got;
    }

    private static void ReadExactly(Stream input, byte[] buffer, int count)
    {
        if (ReadUpTo(input, buffer, 0, count) < count)
        {
            throw new EndOfStreamException();
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Left in the temp folder, which the system clears.
        }
    }

    /// <summary>A stream read from a temporary file, which is deleted once the stream is closed.</summary>
    private sealed class TempFileStream : WaveStream
    {
        private readonly WaveStream _inner;
        private readonly string _path;

        public TempFileStream(WaveStream inner, string path)
        {
            _inner = inner;
            _path = path;
        }

        public override WaveFormat WaveFormat => _inner.WaveFormat;

        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
                DeleteQuietly(_path);
            }

            base.Dispose(disposing);
        }
    }
}
