using System.Buffers.Binary;
using System.Text;

namespace Lucent.Radio;

/// <summary>
/// The audio in an HLS station's MP4 pieces (fragmented MP4, or CMAF), as SomaFM and many newer stations send it:
/// one starting piece (the playlist's <c>#EXT-X-MAP</c>) says what the sound is, and every piece after it is a few
/// seconds of AAC (or MP3) frames with a little table saying where each one is. The frames are passed on as ADTS
/// (AAC with a 7-byte header on each frame, as Shoutcast and Icecast send it), so the decoders read them as they
/// read any station. Songs come as ID3 tags in event boxes (<c>emsg</c>) at the start of a piece.
/// </summary>
/// <remarks>
/// Boxes are read as ISO/IEC 14496-12 lays them out: a 32-bit size (1 for a 64-bit one after the name, 0 for "to the
/// end") and a four-letter name. Only the first sound track is read; pictures, if any, are left alone.
/// </remarks>
internal sealed class Mp4Pieces
{
    private static readonly int[] SampleRates = { 96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350 };

    private readonly uint _track;
    private readonly uint _defaultSize;
    private readonly (int Profile, int RateIndex, int Channels) _adts;

    private Mp4Pieces(RadioCodec codec, uint track, uint defaultSize, (int Profile, int RateIndex, int Channels) adts, int kbps)
    {
        Codec = codec;
        _track = track;
        _defaultSize = defaultSize;
        _adts = adts;
        Kbps = kbps;
    }

    /// <summary>What the frames passed on are: AAC in ADTS frames, or MP3.</summary>
    public RadioCodec Codec { get; }

    /// <summary>The average bitrate the starting piece gives, in kilobits per second, or 0 when it doesn't say.</summary>
    public int Kbps { get; }

    /// <summary>
    /// Reads the starting piece: which track is the sound, and what it is. Throws <see cref="RadioProblem"/>
    /// (unreadable, so the video player is tried) for sound Lucent's own decoders don't read, such as FLAC or Opus.
    /// </summary>
    public static Mp4Pieces Read(ReadOnlySpan<byte> init)
    {
        if (!TryFind(init, "moov", out var moov))
        {
            throw Unreadable("The station's HLS stream starts without saying what's in it.");
        }

        foreach (var range in All(moov, "trak"))
        {
            var trak = moov[range];
            if (!TryFind(trak, "mdia", out var mdia) || !TryFind(mdia, "hdlr", out var hdlr) || hdlr.Length < 12
                || Encoding.ASCII.GetString(hdlr.Slice(8, 4)) != "soun")
            {
                continue;
            }

            var track = TryFind(trak, "tkhd", out var tkhd) && tkhd.Length >= 24
                ? BinaryPrimitives.ReadUInt32BigEndian(tkhd.Slice(tkhd[0] == 1 ? 20 : 12, 4))
                : 1;
            if (!TryFind(mdia, "minf", out var minf) || !TryFind(minf, "stbl", out var stbl) || !TryFind(stbl, "stsd", out var stsd)
                || stsd.Length < 16)
            {
                break;
            }

            // The sample description: a full box's 4 bytes and an entry count, then the first entry (a box itself).
            var entry = stsd[8..];
            var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(entry), (uint)entry.Length);
            var format = Encoding.ASCII.GetString(entry.Slice(4, 4));
            if (format != "mp4a" && format != ".mp3")
            {
                throw Unreadable($"The station's HLS stream is {Describe(format)} in MP4 pieces, which Lucent's own radio player doesn't read.");
            }

            var defaultSize = DefaultSize(moov, track);
            if (format == ".mp3")
            {
                return new Mp4Pieces(RadioCodec.Mp3, track, defaultSize, default, 0);
            }

            // An audio sample entry (version 0): 8 bytes of box header and 28 of its own, then its boxes, esds among them.
            if (size <= 36 || !TryFind(entry[36..size], "esds", out var esds) || esds.Length <= 4
                || Decoder(esds[4..]) is not { } config)
            {
                throw Unreadable("The station's HLS stream doesn't say what its AAC is.");
            }

            var kbps = config.AverageBitrate > 0 ? (int)Math.Round(config.AverageBitrate / 1000.0) : 0;
            if (config.ObjectType is 0x6B or 0x69)
            {
                return new Mp4Pieces(RadioCodec.Mp3, track, defaultSize, default, kbps);
            }

            if (config.ObjectType != 0x40 || Adts(config.Specific) is not { } adts)
            {
                throw Unreadable("The station's HLS stream is a kind of AAC Lucent's own radio player doesn't read.");
            }

            return new Mp4Pieces(RadioCodec.Aac, track, defaultSize, adts, kbps);
        }

        throw Unreadable("The station's HLS stream has no sound Lucent can find.");
    }

    /// <summary>
    /// The sound in one piece, as frames the decoders read; <paramref name="title"/> is the song an ID3 tag in it
    /// names, or null.
    /// </summary>
    public byte[] Audio(byte[] piece, out string? title)
    {
        title = null;
        using var audio = new MemoryStream(piece.Length + piece.Length / 64);
        var frames = new List<(long Offset, int Size, bool InMdat)>();
        var at = 0;
        while (Next(piece, at) is { } box)
        {
            var body = piece.AsSpan(box.Body, box.End - box.Body);
            switch (box.Name)
            {
                case "emsg":
                    title = EventTitle(body) ?? title;
                    break;
                case "moof":
                    frames.Clear();
                    Fragment(body, at, piece.Length, frames);
                    break;
                case "mdat":
                    foreach (var (place, size, inMdat) in frames)
                    {
                        // A frame is in this mdat when the table says so; one that isn't (a damaged piece) is left out.
                        var offset = inMdat ? box.Body + place : place;
                        if (offset >= box.Body && offset + size <= box.End)
                        {
                            Write(audio, piece.AsSpan((int)offset, size));
                        }
                    }

                    frames.Clear();
                    break;
            }

            at = box.End;
        }

        return audio.ToArray();
    }

    // Where each of the sound track's frames is in a piece, from a moof's tables. moofStart is where the moof begins.
    private void Fragment(ReadOnlySpan<byte> moof, int moofStart, int pieceLength, List<(long Offset, int Size, bool InMdat)> frames)
    {
        foreach (var trafRange in All(moof, "traf"))
        {
            var traf = moof[trafRange];
            if (!TryFind(traf, "tfhd", out var tfhd) || tfhd.Length < 8 || BinaryPrimitives.ReadUInt32BigEndian(tfhd[4..]) != _track)
            {
                continue;
            }

            var flags = Flags(tfhd);
            var at = 8;
            long baseOffset = moofStart;
            if ((flags & 0x01) != 0 && tfhd.Length >= at + 8)
            {
                // Counted from the start of the piece, for a piece fetched on its own.
                baseOffset = (long)BinaryPrimitives.ReadUInt64BigEndian(tfhd[at..]);
                at += 8;
            }

            at += (flags & 0x02) != 0 ? 4 : 0;
            at += (flags & 0x08) != 0 ? 4 : 0;
            var defaultSize = (flags & 0x10) != 0 && tfhd.Length >= at + 4 ? BinaryPrimitives.ReadUInt32BigEndian(tfhd[at..]) : _defaultSize;

            // With no offset given, a run carries on from the last, and the first starts the mdat that follows (InMdat:
            // counted from the start of its data).
            var offset = 0L;
            var inMdat = true;
            foreach (var trunRange in All(traf, "trun"))
            {
                var trun = traf[trunRange];
                if (trun.Length < 8)
                {
                    continue;
                }

                var runFlags = Flags(trun);
                var count = BinaryPrimitives.ReadUInt32BigEndian(trun[4..]);
                var i = 8;
                if ((runFlags & 0x01) != 0 && trun.Length >= i + 4)
                {
                    offset = baseOffset + BinaryPrimitives.ReadInt32BigEndian(trun[i..]);
                    inMdat = false;
                    i += 4;
                }

                i += (runFlags & 0x04) != 0 ? 4 : 0;
                var each = ((runFlags & 0x100) != 0 ? 4 : 0) + ((runFlags & 0x200) != 0 ? 4 : 0)
                           + ((runFlags & 0x400) != 0 ? 4 : 0) + ((runFlags & 0x800) != 0 ? 4 : 0);
                var sizeAt = (runFlags & 0x100) != 0 ? 4 : 0;
                for (var n = 0; n < count && i + each <= trun.Length; n++, i += each)
                {
                    var size = (runFlags & 0x200) != 0 ? BinaryPrimitives.ReadUInt32BigEndian(trun[(i + sizeAt)..]) : defaultSize;
                    // A frame past the end of the piece can't be in it. This also stops a run that claims billions of
                    // frames, all of the default size, from filling memory.
                    if (size is 0 or > 1 << 20 || offset + size > pieceLength)
                    {
                        break;
                    }

                    frames.Add((offset, (int)size, inMdat));
                    offset += size;
                }
            }
        }
    }

    // Writes a frame out: as it is for MP3, and behind an ADTS header for AAC.
    private void Write(MemoryStream audio, ReadOnlySpan<byte> frame)
    {
        if (Codec == RadioCodec.Aac)
        {
            var length = frame.Length + 7;
            if (length >= 1 << 13)
            {
                return;
            }

            Span<byte> header = stackalloc byte[7];
            header[0] = 0xFF;
            header[1] = 0xF1; // MPEG-4, layer 0, no CRC.
            header[2] = (byte)(((_adts.Profile - 1) << 6) | (_adts.RateIndex << 2) | (_adts.Channels >> 2));
            header[3] = (byte)(((_adts.Channels & 0x03) << 6) | (length >> 11));
            header[4] = (byte)((length >> 3) & 0xFF);
            header[5] = (byte)(((length & 0x07) << 5) | 0x1F);
            header[6] = 0xFC;
            audio.Write(header);
        }

        audio.Write(frame);
    }

    // The song an event box names, when it carries an ID3 tag (as both Apple's and AOM's ID3 schemes do).
    private static string? EventTitle(ReadOnlySpan<byte> emsg)
    {
        if (emsg.Length < 4)
        {
            return null;
        }

        // Version 0 has the scheme and value (two strings) first, then four numbers; version 1 has the numbers first
        // (one of them 64-bit), then the strings.
        var at = emsg[0] == 0 ? SkipString(emsg, SkipString(emsg, 4)) : SkipString(emsg, SkipString(emsg, 24));
        if (at > 0 && emsg[0] == 0)
        {
            at += 16;
        }

        if (at <= 0 || at >= emsg.Length)
        {
            return null;
        }

        var data = emsg[at..];
        return HlsStream.Id3.Length(data) > 0 ? HlsStream.Id3.Title(data) : null;
    }

    // Past a zero-ended string starting at at, or -1 when it runs off the end.
    private static int SkipString(ReadOnlySpan<byte> data, int at)
    {
        if (at < 0 || at >= data.Length)
        {
            return -1;
        }

        var end = data[at..].IndexOf((byte)0);
        return end < 0 ? -1 : at + end + 1;
    }

    // The default frame size the movie gives a track (in mvex/trex), for pieces whose tables leave it out.
    private static uint DefaultSize(ReadOnlySpan<byte> moov, uint track)
    {
        if (TryFind(moov, "mvex", out var mvex))
        {
            foreach (var range in All(mvex, "trex"))
            {
                var trex = mvex[range];
                if (trex.Length >= 24 && BinaryPrimitives.ReadUInt32BigEndian(trex[4..]) == track)
                {
                    return BinaryPrimitives.ReadUInt32BigEndian(trex[16..]);
                }
            }
        }

        return 0;
    }

    /// <summary>
    /// What an ADTS header needs from an AAC AudioSpecificConfig: the profile, sample rate and channel layout of the
    /// core AAC. HE-AAC's extra layer (SBR, and PS for v2) is still in the frames, where the decoder finds it, as it
    /// does on Shoutcast and Icecast stations. Null for kinds ADTS can't carry.
    /// </summary>
    internal static (int Profile, int RateIndex, int Channels)? Adts(ReadOnlySpan<byte> config)
    {
        var bits = new BitReader(config);
        var objectType = ObjectType(ref bits);
        var rateIndex = RateIndex(ref bits);
        var channels = bits.Read(4);
        if (objectType is 5 or 29)
        {
            // Explicit HE-AAC: the output rate comes next, then the core's own type.
            RateIndex(ref bits);
            objectType = ObjectType(ref bits);
        }

        if (bits.Overrun || objectType is < 1 or > 4 || rateIndex is < 0 or >= 13 || channels is < 1 or > 7)
        {
            return null;
        }

        return (objectType, rateIndex, channels);
    }

    private static int ObjectType(ref BitReader bits)
    {
        var type = bits.Read(5);
        return type == 31 ? 32 + bits.Read(6) : type;
    }

    // A sample rate's index in the ADTS table; one written out in full is looked up there.
    private static int RateIndex(ref BitReader bits)
    {
        var index = bits.Read(4);
        return index == 15 ? Array.IndexOf(SampleRates, bits.Read(24)) : index;
    }

    // The decoder config in an esds box's descriptors: ES (0x03) holds DecoderConfig (0x04), which holds DecoderSpecific (0x05).
    private static (int ObjectType, uint AverageBitrate, byte[] Specific)? Decoder(ReadOnlySpan<byte> descriptors)
    {
        var es = Descriptor(descriptors, 0x03);
        if (es.Length < 3)
        {
            return null;
        }

        var flags = es[2];
        var at = 3;
        at += (flags & 0x80) != 0 ? 2 : 0;
        if ((flags & 0x40) != 0 && at < es.Length)
        {
            at += 1 + es[at];
        }

        at += (flags & 0x20) != 0 ? 2 : 0;
        var config = at < es.Length ? Descriptor(es[at..], 0x04) : ReadOnlySpan<byte>.Empty;
        if (config.Length < 13)
        {
            return null;
        }

        var specific = Descriptor(config[13..], 0x05).ToArray();
        return (config[0], BinaryPrimitives.ReadUInt32BigEndian(config[9..]), specific);
    }

    // The body of the descriptor with this tag at the start of data (its length is 1 to 4 bytes, 7 bits each).
    private static ReadOnlySpan<byte> Descriptor(ReadOnlySpan<byte> data, byte tag)
    {
        if (data.Length < 2 || data[0] != tag)
        {
            return ReadOnlySpan<byte>.Empty;
        }

        var length = 0;
        var at = 1;
        for (var i = 0; i < 4 && at < data.Length; i++)
        {
            var b = data[at++];
            length = (length << 7) | (b & 0x7F);
            if ((b & 0x80) == 0)
            {
                break;
            }
        }

        return data.Slice(at, Math.Min(length, data.Length - at));
    }

    private static string Describe(string format) => format switch
    {
        "fLaC" => "FLAC",
        "Opus" => "Opus",
        "ac-3" => "Dolby Digital",
        "ec-3" => "Dolby Digital Plus",
        "alac" => "Apple Lossless",
        _ => $"\"{format.Trim()}\"",
    };

    private static RadioProblem Unreadable(string message) => new(message) { Unreadable = true };

    // A full box's flags: the 24 bits after its version.
    private static int Flags(ReadOnlySpan<byte> box) => (box[1] << 16) | (box[2] << 8) | box[3];

    /// <summary>The body of the first box called <paramref name="name"/> in <paramref name="data"/>.</summary>
    private static bool TryFind(ReadOnlySpan<byte> data, string name, out ReadOnlySpan<byte> body)
    {
        var at = 0;
        while (Next(data, at) is { } box)
        {
            if (box.Name == name)
            {
                body = data[box.Body..box.End];
                return true;
            }

            at = box.End;
        }

        body = ReadOnlySpan<byte>.Empty;
        return false;
    }

    /// <summary>Where the body of each box called <paramref name="name"/> in <paramref name="data"/> is.</summary>
    private static List<Range> All(ReadOnlySpan<byte> data, string name)
    {
        var found = new List<Range>();
        var at = 0;
        while (Next(data, at) is { } box)
        {
            if (box.Name == name)
            {
                found.Add(box.Body..box.End);
            }

            at = box.End;
        }

        return found;
    }

    /// <summary>The box starting at <paramref name="at"/>, or null at the end (or where the data stops making sense).</summary>
    private static Box? Next(ReadOnlySpan<byte> data, int at)
    {
        if (at < 0 || at + 8 > data.Length)
        {
            return null;
        }

        long size = BinaryPrimitives.ReadUInt32BigEndian(data[at..]);
        var name = Encoding.ASCII.GetString(data.Slice(at + 4, 4));
        var header = 8;
        if (size == 1)
        {
            if (at + 16 > data.Length)
            {
                return null;
            }

            size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(at + 8)..]);
            header = 16;
        }
        else if (size == 0)
        {
            size = data.Length - at;
        }

        if (size < header || at + size > data.Length)
        {
            // Cut short: what's there of it is read, as a piece that was cut off still holds whole frames before the cut.
            return size < header ? null : new Box(name, at + header, data.Length);
        }

        return new Box(name, at + header, (int)(at + size));
    }

    private readonly record struct Box(string Name, int Body, int End);

    /// <summary>Reads bits, the highest first, as AAC's configs are written.</summary>
    private ref struct BitReader
    {
        private readonly ReadOnlySpan<byte> _data;
        private int _bit;

        public BitReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            _bit = 0;
        }

        /// <summary>True when more was read than there is (the rest read as zeros).</summary>
        public bool Overrun { get; private set; }

        public int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++, _bit++)
            {
                var index = _bit >> 3;
                if (index >= _data.Length)
                {
                    Overrun = true;
                    value <<= 1;
                    continue;
                }

                value = (value << 1) | ((_data[index] >> (7 - (_bit & 7))) & 1);
            }

            return value;
        }
    }
}
