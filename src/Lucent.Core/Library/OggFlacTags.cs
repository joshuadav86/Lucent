using System.Runtime.CompilerServices;
using TagLib;
using TagLib.Ogg;

namespace Lucent.Library;

/// <summary>
/// Teaches TagLib FLAC in Ogg (.oga), which it doesn't know: the tags are the same Vorbis comments as in Ogg Vorbis,
/// kept in a FLAC metadata block, and the length and format come from the FLAC header.
/// </summary>
public sealed class OggFlacTags : Codec, IAudioCodec, ILosslessAudioCodec
{
    private const int VorbisCommentBlock = 4;

    private ByteVector? _comment;
    private int _sampleRate;
    private int _channels;
    private int _bits;
    private long _totalSamples;

    // Registered as Lucent.Core loads, since TagLib is opened in many places and each must know FLAC in Ogg.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Register() => AddCodecProvider(packet => IsFirstPacket(packet) ? new OggFlacTags() : null);

    public override string Description => "FLAC Audio";

    public override MediaTypes MediaTypes => MediaTypes.Audio;

    public override ByteVector CommentData => _comment ?? new XiphComment().Render(false);

    public int AudioBitrate => 0;

    public int AudioSampleRate => _sampleRate;

    public int AudioChannels => _channels;

    public int BitsPerSample => _bits;

    // A packet is either the first (0x7F "FLAC", the mapping's version, how many header packets follow, "fLaC" and
    // the STREAMINFO block), a metadata block, or (after them) a FLAC frame, which starts with its sync code.
    public override bool ReadPacket(ByteVector packet, int index)
    {
        if (index == 0)
        {
            if (!IsFirstPacket(packet) || packet.Count < 13 + 4 + 34)
            {
                throw new CorruptFileException("Stream does not begin with a FLAC header.");
            }

            var info = packet.Mid(17, 18).Data;
            _sampleRate = (info[10] << 12) | (info[11] << 4) | (info[12] >> 4);
            _channels = ((info[12] >> 1) & 7) + 1;
            _bits = (((info[12] & 1) << 4) | (info[13] >> 4)) + 1;
            _totalSamples = ((long)(info[13] & 0x0F) << 32) | ((long)info[14] << 24) | ((long)info[15] << 16) | ((long)info[16] << 8) | info[17];
            return (packet[13] & 0x80) != 0;
        }

        if (IsFrame(packet))
        {
            return true;
        }

        if ((packet[0] & 0x7F) == VorbisCommentBlock && _comment is null)
        {
            _comment = packet.Mid(4);
        }

        return _comment is not null || (packet[0] & 0x80) != 0;
    }

    public override TimeSpan GetDuration(long firstGranularPosition, long lastGranularPosition)
    {
        var samples = _totalSamples > 0 ? _totalSamples : lastGranularPosition - firstGranularPosition;
        return _sampleRate > 0 && samples > 0 ? TimeSpan.FromSeconds(samples / (double)_sampleRate) : TimeSpan.Zero;
    }

    public override void SetCommentPacket(ByteVectorCollection packets, XiphComment comment)
    {
        var body = comment.Render(false);
        for (var i = 1; i < packets.Count && !IsFrame(packets[i]); i++)
        {
            if ((packets[i][0] & 0x7F) == VorbisCommentBlock)
            {
                packets[i] = Block(packets[i][0] & 0x80, body);
                return;
            }
        }

        // No comments yet: a block is added after STREAMINFO, and the header count (if it's given) goes up by one.
        var first = new ByteVector(packets[0]);
        var last = packets.Count == 1 || IsFrame(packets[1]) ? (first[13] & 0x80) : 0;
        first[13] = (byte)(first[13] & 0x7F);
        var count = (first[7] << 8) | first[8];
        if (count > 0)
        {
            first[7] = (byte)((count + 1) >> 8);
            first[8] = (byte)(count + 1);
        }

        packets[0] = first;
        packets.Insert(1, Block(last, body));
    }

    private static ByteVector Block(int last, ByteVector body)
    {
        var block = new ByteVector((byte)(last | VorbisCommentBlock), (byte)(body.Count >> 16), (byte)(body.Count >> 8), (byte)body.Count);
        block.Add(body);
        return block;
    }

    private static bool IsFirstPacket(ByteVector packet) =>
        packet.Count >= 13 && packet[0] == 0x7F && packet[1] == 'F' && packet[2] == 'L' && packet[3] == 'A' && packet[4] == 'C';

    private static bool IsFrame(ByteVector packet) => packet.Count >= 2 && packet[0] == 0xFF && (packet[1] & 0xFE) == 0xF8;
}
