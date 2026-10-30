using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Lucent.Audio;

/// <summary>
/// Lucent's own WavPack (.wv) decoder, the same on Windows and the Mac, since neither system nor the bundled libVLC
/// reads WavPack. It decodes WavPack 4 and 5 files: lossless and hybrid (lossy, without its .wvc correction file),
/// 8 to 32-bit whole numbers and 32-bit floats, mono, stereo and multichannel. DSD WavPack files aren't read.
/// </summary>
/// <remarks>
/// A port of the decoding parts of David Bryant's reference library (github.com/dbry/WavPack, BSD licence, see
/// assets/licenses/LICENSE-WavPack.txt), simplified to decode one block at a time.
/// </remarks>
public sealed class WavPackReader : BlockAudioReader
{
    private const int HeaderSize = 32;

    // WavPack's encoders keep a block to 131,072 samples or fewer, so a file whose blocks claim far more is damaged, or
    // made to ask for gigabytes of memory. A block's samples times its channels is what each decode allocates.
    private const int MaxBlockSamples = 1 << 20;
    private const int MaxChannels = 64;
    private const long MaxBlockValues = 1 << 24;

    private const uint BytesStored = 3;
    private const uint MonoFlag = 4;
    private const uint HybridFlag = 8;
    private const uint JointStereo = 0x10;
    private const uint FloatData = 0x80;
    private const uint Int32Data = 0x100;
    private const uint HybridBitrate = 0x200;
    private const uint HybridBalance = 0x400;
    private const uint InitialBlock = 0x800;
    private const uint FinalBlock = 0x1000;
    private const int ShiftLsb = 13;
    private const uint ShiftMask = 0x1fu << ShiftLsb;
    private const int MagLsb = 18;
    private const uint MagMask = 0x1fu << MagLsb;
    private const int SrateLsb = 23;
    private const uint SrateMask = 0xfu << SrateLsb;
    private const uint FalseStereo = 0x40000000;
    private const uint DsdFlag = 0x80000000;
    private const uint MonoData = MonoFlag | FalseStereo;

    private static readonly int[] SampleRates =
        { 6000, 8000, 9600, 11025, 12000, 16000, 22050, 24000, 32000, 44100, 48000, 64000, 88200, 96000, 192000 };

    /// <summary>One frame's worth of blocks (one block per one or two channels) in the file.</summary>
    private readonly record struct Group(long Start, long Offset, int Length, int Samples);

    private readonly List<Group> _groups;
    private readonly int _channels;

    private WavPackReader(Stream stream, int sampleRate, int channels, long frames, List<Group> groups, int bits, bool isFloat)
        : base(stream, sampleRate, channels, frames)
    {
        _groups = groups;
        _channels = channels;
        SourceBits = bits;
        IsSourceFloat = isFloat;
    }

    /// <summary>
    /// Opens a WavPack file, or returns null when <paramref name="path"/> isn't one. Throws
    /// <see cref="NotSupportedException"/> for WavPack files this can't decode (DSD, or WavPack 3 and older).
    /// </summary>
    public static WavPackReader? TryOpen(string path)
    {
        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        try
        {
            var reader = Open(stream);
            if (reader is null)
            {
                stream.Dispose();
            }

            return reader;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static WavPackReader? Open(Stream stream)
    {
        var groups = new List<Group>();
        var header = new byte[HeaderSize];
        long at = 0, groupOffset = -1, firstIndex = -1;
        var groupSamples = 0;
        long groupIndex = 0;
        int sampleRate = 0, channels = 0, groupChannels = 0, bits = 0;
        var isFloat = false;
        var first = true;
        while (at + HeaderSize <= stream.Length)
        {
            stream.Position = at;
            if (stream.Read(header, 0, HeaderSize) < HeaderSize)
            {
                break;
            }

            if (header[0] != 'w' || header[1] != 'v' || header[2] != 'p' || header[3] != 'k')
            {
                if (first && at < 1 << 20)
                {
                    // Some files start with a tag (ID3v2): look for the first block.
                    at++;
                    continue;
                }

                // A tag at the end (APEv2 or ID3v1), or damage: the audio has ended.
                break;
            }

            var size = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            var version = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
            var blockIndex = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16)) + ((long)header[10] << 32);
            var blockSamples = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24));
            if (size < HeaderSize - 8 || size > 1 << 25 || version < 0x402 || version > 0x410)
            {
                if (first)
                {
                    throw new NotSupportedException("Only WavPack 4 and 5 files can be decoded.");
                }

                break;
            }

            var length = (int)size + 8;
            if (blockSamples > MaxBlockSamples)
            {
                throw new InvalidDataException("The WavPack file's blocks are bigger than WavPack makes them.");
            }

            if (blockSamples > 0)
            {
                if ((flags & DsdFlag) != 0)
                {
                    throw new NotSupportedException("DSD WavPack files can't be decoded.");
                }

                if (first)
                {
                    first = false;
                    firstIndex = blockIndex;
                    var block = ReadAt(stream, at, length);
                    var info = FirstBlockInfo(block, flags);
                    sampleRate = info.SampleRate;
                    channels = info.Channels;
                    bits = (int)((flags & BytesStored) + 1) * 8 - (int)((flags & ShiftMask) >> ShiftLsb);
                    isFloat = (flags & FloatData) != 0;
                    if ((flags & Int32Data) != 0 || isFloat)
                    {
                        bits = 32;
                    }
                }

                if ((flags & InitialBlock) != 0)
                {
                    groupOffset = at;
                    groupIndex = blockIndex;
                    groupSamples = blockSamples;
                    groupChannels = 0;
                }

                groupChannels += (flags & MonoFlag) != 0 ? 1 : 2;
                if ((flags & FinalBlock) != 0 && groupOffset >= 0)
                {
                    groups.Add(new Group(groupIndex - firstIndex, groupOffset, (int)(at + length - groupOffset), groupSamples));
                    if (channels == 0)
                    {
                        channels = groupChannels;
                    }

                    groupOffset = -1;
                }
            }

            at += length;
        }

        if (first)
        {
            return null;
        }

        if (groups.Count == 0 || channels == 0)
        {
            throw new InvalidDataException("The WavPack file holds no complete blocks.");
        }

        if (channels > MaxChannels || sampleRate <= 0 || groups.Any(group => (long)group.Samples * channels > MaxBlockValues))
        {
            throw new InvalidDataException("The WavPack file's header doesn't make sense.");
        }

        var last = groups[^1];
        return new WavPackReader(stream, sampleRate, channels, last.Start + last.Samples, groups, bits, isFloat);
    }

    /// <summary>The sample rate and channel count, which only the first block says.</summary>
    private static (int SampleRate, int Channels) FirstBlockInfo(byte[] block, uint flags)
    {
        var rate = (flags & SrateMask) == SrateMask ? 44100 : SampleRates[(flags & SrateMask) >> SrateLsb];
        var channels = 0;
        foreach (var (id, start, length) in Metadata(block))
        {
            if (id == IdChannelInfo && length > 0)
            {
                channels = length >= 6 ? (block[start] | ((block[start + 2] & 0xf) << 8)) + 1 : block[start];
            }
            else if (id == IdSampleRate && length is 3 or 4)
            {
                rate = block[start] | (block[start + 1] << 8) | (block[start + 2] << 16) | (length == 4 ? (block[start + 3] & 0x7f) << 24 : 0);
            }
        }

        return (rate, channels);
    }

    protected override int BlockCount => _groups.Count;

    protected override long BlockStart(int block) => _groups[block].Start;

    protected override int DecodeBlock(int block, Func<long, int, byte[]> read, ref float[] output)
    {
        var group = _groups[block];
        var bytes = read(group.Offset, group.Length);
        var frames = group.Samples;
        if (output.Length < frames * _channels)
        {
            output = new float[frames * _channels];
        }

        var at = 0;
        var channel = 0;
        var samples = new int[frames * 2];
        while (at + HeaderSize <= group.Length && channel < _channels)
        {
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 4)) + 8;
            var flags = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 24));
            var blockSamples = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(at + 20));
            if (blockSamples <= 0 || length < HeaderSize || at + length > group.Length)
            {
                at += Math.Max(length, HeaderSize);
                continue;
            }

            var count = Math.Min(blockSamples, frames);
            var stereo = (flags & MonoFlag) == 0;
            var good = new BlockDecoder(bytes, at, length).Unpack(samples, count);
            var scale = (flags & FloatData) != 0 ? 1f : 1f / (1u << (int)((flags & BytesStored) + 1) * 8 - 1);
            var isFloat = (flags & FloatData) != 0;
            var width = stereo && channel + 1 < _channels ? 2 : 1;
            for (var c = 0; c < width; c++)
            {
                for (var i = 0; i < count; i++)
                {
                    var value = samples[stereo ? i * 2 + c : i];
                    output[i * _channels + channel + c] = !good ? 0 : isFloat ? BitConverter.Int32BitsToSingle(value) : value * scale;
                }
            }

            channel += width;
            at += length;
        }

        return frames;
    }

    private const int IdChannelInfo = 0xd;
    private const int IdSampleRate = 0x27;

    /// <summary>A block's metadata items: their id (without the size flags), and where their data is.</summary>
    private static IEnumerable<(int Id, int Start, int Length)> Metadata(byte[] block, int blockStart = 0, int blockLength = -1)
    {
        var end = blockStart + (blockLength < 0 ? (int)BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(blockStart + 4)) + 8 : blockLength);
        end = Math.Min(end, block.Length);
        var at = blockStart + HeaderSize;
        while (end - at >= 2)
        {
            int id = block[at++];
            var length = block[at++] << 1;
            if ((id & 0x80) != 0)
            {
                if (end - at < 2)
                {
                    yield break;
                }

                length += block[at++] << 9;
                length += block[at++] << 17;
                id &= ~0x80;
            }

            if ((id & 0x40) != 0)
            {
                if (length == 0)
                {
                    yield break;
                }

                id &= ~0x40;
                length--;
            }

            if (end - at < length + (length & 1))
            {
                yield break;
            }

            yield return (id, at, length);
            at += length + (length & 1);
        }
    }

    /// <summary>The decoding of one block, which holds one or two channels.</summary>
    private sealed class BlockDecoder
    {
        private const int MaxTerm = 8;
        private const int MaxTerms = 16;
        private const int LimitOnes = 16;
        private const int Sls = 8;
        private const uint Slo = 1 << (Sls - 1);
        private const int WordEof = int.MinValue;

        private const uint FloatShiftOnes = 1;
        private const uint FloatShiftSame = 2;
        private const uint FloatShiftSent = 4;
        private const uint FloatZerosSent = 8;
        private const uint FloatNegZeros = 0x10;

        private readonly byte[] _block;
        private readonly int _start;
        private readonly int _length;
        private readonly uint _flags;
        private readonly uint _crc;
        private readonly int _version;

        private readonly Pass[] _passes = new Pass[MaxTerms];
        private int _terms;
        private readonly Entropy[] _c = { new(), new() };
        private readonly uint[] _bitrateDelta = new uint[2];
        private readonly uint[] _bitrateAcc = new uint[2];
        private uint _holdingOne;
        private uint _zerosAcc;
        private int _holdingZero;

        private Bits? _wv;
        private Bits? _wvx;
        private uint _crcWvx;
        private int _int32SentBits, _int32Zeros, _int32Ones, _int32Dups, _int32MaxWidth;
        private uint _floatFlags;
        private int _floatShift, _floatMaxExp, _floatNormExp = 127, _floatMinShiftedZeros, _floatMaxShiftedOnes;

        public BlockDecoder(byte[] block, int start, int length)
        {
            _block = block;
            _start = start;
            _length = length;
            _version = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(start + 8));
            _flags = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(start + 24));
            _crc = BinaryPrimitives.ReadUInt32LittleEndian(block.AsSpan(start + 28));
            for (var i = 0; i < _passes.Length; i++)
            {
                _passes[i] = new Pass();
            }
        }

        private bool Mono => (_flags & MonoData) != 0;

        /// <summary>
        /// Decodes <paramref name="count"/> samples into <paramref name="buffer"/> (interleaved when stereo, as whole
        /// numbers, or float bits for float files). Returns false when the block is damaged, as its checksum says.
        /// </summary>
        public bool Unpack(int[] buffer, int count)
        {
            foreach (var (id, start, length) in Metadata(_block, _start, _length))
            {
                if (!ReadMetadata(id, start, length))
                {
                    return false;
                }
            }

            if (_wv is null || (_flags & MonoData) == MonoData)
            {
                return false;
            }

            var crc = 0xffffffffu;
            var muteLimit = (1L << (int)((_flags & MagMask) >> MagLsb)) + 2;
            if ((_flags & HybridFlag) != 0)
            {
                muteLimit = muteLimit * 3 + 128;
            }

            if (Mono)
            {
                if ((_flags & HybridFlag) != 0)
                {
                    for (var i = 0; i < count; i++)
                    {
                        if ((buffer[i] = GetWord(0)) == WordEof)
                        {
                            return false;
                        }
                    }
                }
                else if (GetWordsLossless(buffer, count) != count)
                {
                    return false;
                }

                for (var t = 0; t < _terms; t++)
                {
                    DecorrMono(_passes[t], buffer, count);
                }

                for (var i = 0; i < count; i++)
                {
                    if (Math.Abs((long)buffer[i]) > muteLimit)
                    {
                        return false;
                    }

                    crc = unchecked(crc * 3 + (uint)buffer[i]);
                }
            }
            else
            {
                if ((_flags & HybridFlag) != 0)
                {
                    for (var i = 0; i < count * 2; i += 2)
                    {
                        if ((buffer[i] = GetWord(0)) == WordEof || (buffer[i + 1] = GetWord(1)) == WordEof)
                        {
                            return false;
                        }
                    }
                }
                else if (GetWordsLossless(buffer, count) != count)
                {
                    return false;
                }

                for (var t = 0; t < _terms; t++)
                {
                    DecorrStereo(_passes[t], buffer, count);
                }

                var joint = (_flags & JointStereo) != 0;
                for (var i = 0; i < count * 2; i += 2)
                {
                    if (joint)
                    {
                        buffer[i + 1] -= buffer[i] >> 1;
                        buffer[i] += buffer[i + 1];
                    }

                    crc = unchecked(crc + (crc << 3) + ((uint)buffer[i] << 1) + (uint)buffer[i] + (uint)buffer[i + 1]);
                    if (Math.Abs((long)buffer[i]) > muteLimit || Math.Abs((long)buffer[i + 1]) > muteLimit)
                    {
                        return false;
                    }
                }
            }

            if (crc != _crc)
            {
                return false;
            }

            if (!Fixup(buffer, count))
            {
                return false;
            }

            if ((_flags & FalseStereo) != 0)
            {
                for (var i = count - 1; i >= 0; i--)
                {
                    buffer[i * 2] = buffer[i * 2 + 1] = buffer[i];
                }
            }

            return true;
        }

        private bool ReadMetadata(int id, int start, int length)
        {
            var b = _block;
            switch (id)
            {
                case 0x2:
                    // Decorrelation terms, stored the other way round.
                    if (length > MaxTerms)
                    {
                        return false;
                    }

                    _terms = length;
                    for (var i = 0; i < length; i++)
                    {
                        var pass = _passes[length - 1 - i];
                        pass.Term = (b[start + i] & 0x1f) - 5;
                        pass.Delta = (b[start + i] >> 5) & 0x7;
                        if (pass.Term == 0 || pass.Term < -3 || (pass.Term > MaxTerm && pass.Term < 17) || pass.Term > 18
                            || (Mono && pass.Term < 0))
                        {
                            return false;
                        }
                    }

                    return true;

                case 0x3:
                {
                    // Decorrelation weights, from the last term.
                    var count = Mono ? length : length / 2;
                    if (count > _terms)
                    {
                        return false;
                    }

                    for (var i = 0; i < _terms; i++)
                    {
                        _passes[i].WeightA = _passes[i].WeightB = 0;
                    }

                    var at = start;
                    for (var t = _terms - 1; t >= 0 && count-- > 0; t--)
                    {
                        _passes[t].WeightA = RestoreWeight((sbyte)b[at++]);
                        if (!Mono)
                        {
                            _passes[t].WeightB = RestoreWeight((sbyte)b[at++]);
                        }
                    }

                    return true;
                }

                case 0x4:
                    return ReadDecorrSamples(start, length);

                case 0x5:
                    // Entropy medians.
                    if (length != (Mono ? 6 : 12))
                    {
                        return false;
                    }

                    for (var c = 0; c < (Mono ? 1 : 2); c++)
                    {
                        for (var m = 0; m < 3; m++)
                        {
                            _c[c].Median[m] = (uint)Exp2s(b[start + c * 6 + m * 2] | (b[start + c * 6 + m * 2 + 1] << 8));
                        }
                    }

                    return true;

                case 0x6:
                    return ReadHybridProfile(start, length);

                case 0x8:
                    if (length != 4)
                    {
                        return false;
                    }

                    _floatFlags = b[start];
                    _floatShift = (sbyte)b[start + 1];
                    _floatMaxExp = (sbyte)b[start + 2];
                    _floatNormExp = (sbyte)b[start + 3];
                    return true;

                case 0x9:
                    if (length != 4)
                    {
                        return false;
                    }

                    _int32SentBits = (sbyte)b[start];
                    _int32Zeros = (sbyte)b[start + 1];
                    _int32Ones = (sbyte)b[start + 2];
                    _int32Dups = (sbyte)b[start + 3];
                    return true;

                case 0xa:
                    if (length == 0 || (length & 1) != 0)
                    {
                        return false;
                    }

                    _wv = new Bits(b, start, start + length);
                    return true;

                case 0xc:
                case 0x2c:
                    // The extra bits of 32-bit floats and whole numbers over 24 bits, for lossless files.
                    if (length <= 4 || (length & 1) != 0)
                    {
                        return false;
                    }

                    _crcWvx = BinaryPrimitives.ReadUInt32LittleEndian(b.AsSpan(start));
                    _wvx = new Bits(b, start + 4, start + length);
                    if (id == 0x2c)
                    {
                        if ((_flags & FloatData) != 0)
                        {
                            _floatMinShiftedZeros = (int)(_wvx.Read(5) & 0x1f);
                            _floatMaxShiftedOnes = (int)(_wvx.Read(5) & 0x1f);
                        }
                        else
                        {
                            _int32MaxWidth = (int)(_wvx.Read(5) & 0x1f);
                        }
                    }

                    return true;

                case 0xb:
                    // A correction stream, which is only in .wvc files: the lossy stream is decoded without it.
                    return true;

                case 0xe:
                    return false;

                default:
                    // Shaping weights only matter with the correction file; the rest (channel layout, wrapper, MD5)
                    // don't change the sound.
                    return true;
            }
        }

        private bool ReadDecorrSamples(int start, int length)
        {
            var b = _block;
            var at = start;
            var end = start + length;
            for (var t = 0; t < _terms; t++)
            {
                Array.Clear(_passes[t].SamplesA);
                Array.Clear(_passes[t].SamplesB);
            }

            int Next()
            {
                var value = Exp2s((short)(b[at] | (b[at + 1] << 8)));
                at += 2;
                return value;
            }

            if (_version == 0x402 && (_flags & HybridFlag) != 0)
            {
                // The noise shaping's error, which only the correction file uses.
                if (at + (Mono ? 2 : 4) > end)
                {
                    return false;
                }

                at += Mono ? 2 : 4;
            }

            for (var t = _terms - 1; t >= 0 && at < end; t--)
            {
                var pass = _passes[t];
                if (pass.Term > MaxTerm)
                {
                    if (at + (Mono ? 4 : 8) > end)
                    {
                        return false;
                    }

                    pass.SamplesA[0] = Next();
                    pass.SamplesA[1] = Next();
                    if (!Mono)
                    {
                        pass.SamplesB[0] = Next();
                        pass.SamplesB[1] = Next();
                    }
                }
                else if (pass.Term < 0)
                {
                    if (at + 4 > end)
                    {
                        return false;
                    }

                    pass.SamplesA[0] = Next();
                    pass.SamplesB[0] = Next();
                }
                else
                {
                    for (var m = 0; m < pass.Term; m++)
                    {
                        if (at + (Mono ? 2 : 4) > end)
                        {
                            return false;
                        }

                        pass.SamplesA[m] = Next();
                        if (!Mono)
                        {
                            pass.SamplesB[m] = Next();
                        }
                    }
                }
            }

            return at == end;
        }

        private bool ReadHybridProfile(int start, int length)
        {
            var b = _block;
            var at = start;
            var end = start + length;
            var step = Mono ? 2 : 4;
            if ((_flags & HybridBitrate) != 0)
            {
                if (at + step > end)
                {
                    return false;
                }

                _c[0].SlowLevel = (uint)Exp2s(b[at] | (b[at + 1] << 8));
                if (!Mono)
                {
                    _c[1].SlowLevel = (uint)Exp2s(b[at + 2] | (b[at + 3] << 8));
                }

                at += step;
            }

            if (at + step > end)
            {
                return false;
            }

            _bitrateAcc[0] = (uint)(b[at] | (b[at + 1] << 8)) << 16;
            if (!Mono)
            {
                _bitrateAcc[1] = (uint)(b[at + 2] | (b[at + 3] << 8)) << 16;
            }

            at += step;
            if (at < end)
            {
                if (at + step > end)
                {
                    return false;
                }

                _bitrateDelta[0] = (uint)Exp2s((short)(b[at] | (b[at + 1] << 8)));
                if (!Mono)
                {
                    _bitrateDelta[1] = (uint)Exp2s((short)(b[at + 2] | (b[at + 3] << 8)));
                }

                at += step;
                if (at < end)
                {
                    return false;
                }
            }
            else
            {
                _bitrateDelta[0] = _bitrateDelta[1] = 0;
            }

            return true;
        }

        private void UpdateErrorLimit()
        {
            var bitrate0 = (int)((_bitrateAcc[0] += _bitrateDelta[0]) >> 16);
            if (Mono)
            {
                if ((_flags & HybridBitrate) != 0)
                {
                    var slowLog0 = (int)((_c[0].SlowLevel + Slo) >> Sls);
                    _c[0].ErrorLimit = slowLog0 - bitrate0 > -0x100 ? (uint)Exp2s(slowLog0 - bitrate0 + 0x100) : 0;
                }
                else
                {
                    _c[0].ErrorLimit = (uint)Exp2s(bitrate0);
                }

                return;
            }

            var bitrate1 = (int)((_bitrateAcc[1] += _bitrateDelta[1]) >> 16);
            if ((_flags & HybridBitrate) != 0)
            {
                var slowLog0 = (int)((_c[0].SlowLevel + Slo) >> Sls);
                var slowLog1 = (int)((_c[1].SlowLevel + Slo) >> Sls);
                if ((_flags & HybridBalance) != 0)
                {
                    var balance = (slowLog1 - slowLog0 + bitrate1 + 1) >> 1;
                    if (balance > bitrate0)
                    {
                        bitrate1 = bitrate0 * 2;
                        bitrate0 = 0;
                    }
                    else if (-balance > bitrate0)
                    {
                        bitrate0 *= 2;
                        bitrate1 = 0;
                    }
                    else
                    {
                        bitrate1 = bitrate0 + balance;
                        bitrate0 -= balance;
                    }
                }

                _c[0].ErrorLimit = slowLog0 - bitrate0 > -0x100 ? (uint)Exp2s(slowLog0 - bitrate0 + 0x100) : 0;
                _c[1].ErrorLimit = slowLog1 - bitrate1 > -0x100 ? (uint)Exp2s(slowLog1 - bitrate1 + 0x100) : 0;
            }
            else
            {
                _c[0].ErrorLimit = (uint)Exp2s(bitrate0);
                _c[1].ErrorLimit = (uint)Exp2s(bitrate1);
            }
        }

        /// <summary>Counts a run of 1 bits, with the escape for long runs. Returns -1 at the end of the data.</summary>
        private static int OnesCount(Bits bits)
        {
            var ones = bits.CountOnes(LimitOnes + 1);
            if (ones < LimitOnes)
            {
                return ones;
            }

            if (ones == LimitOnes + 1)
            {
                return -1;
            }

            var cbits = bits.CountOnes(33);
            if (cbits == 33)
            {
                return -1;
            }

            if (cbits < 2)
            {
                ones = cbits;
            }
            else
            {
                ones = (int)(bits.Read(cbits - 1) | (1u << (cbits - 1)));
            }

            return ones + LimitOnes;
        }

        /// <summary>A run of zeros' length, from its own escape code. Returns -1 at the end of the data.</summary>
        private static long ZeroRun(Bits bits)
        {
            var cbits = bits.CountOnes(33);
            if (cbits == 33)
            {
                return -1;
            }

            return cbits < 2 ? cbits : bits.Read(cbits - 1) | (1u << (cbits - 1));
        }

        private int GetWordsLossless(int[] buffer, int count)
        {
            var bits = _wv!;
            var total = Mono ? count : count * 2;
            var c = _c[0];
            int i;
            for (i = 0; i < total; i++)
            {
                if (!Mono)
                {
                    c = _c[i & 1];
                }

                if (_holdingZero != 0)
                {
                    _holdingZero = 0;
                    var low0 = ReadCode(bits, c.Med(0) - 1);
                    c.DecMed0();
                    buffer[i] = bits.Bit() != 0 ? (int)~low0 : (int)low0;
                    if (++i == total)
                    {
                        break;
                    }

                    if (!Mono)
                    {
                        c = _c[i & 1];
                    }
                }

                if (_c[0].Median[0] < 2 && _holdingOne == 0 && _c[1].Median[0] < 2)
                {
                    if (_zerosAcc != 0)
                    {
                        if (--_zerosAcc != 0)
                        {
                            buffer[i] = 0;
                            continue;
                        }
                    }
                    else
                    {
                        var run = ZeroRun(bits);
                        if (run < 0)
                        {
                            break;
                        }

                        _zerosAcc = (uint)run;
                        if (_zerosAcc != 0)
                        {
                            _c[0].ClearMedians();
                            _c[1].ClearMedians();
                            buffer[i] = 0;
                            continue;
                        }
                    }
                }

                var ones = OnesCount(bits);
                if (ones < 0)
                {
                    break;
                }

                var held = _holdingOne;
                _holdingOne = (uint)ones & 1;
                _holdingZero = ~ones & 1;
                var onesCount = (uint)(ones >> 1) + held;
                uint low, high;
                if (onesCount == 0)
                {
                    low = 0;
                    high = c.Med(0) - 1;
                    c.DecMed0();
                }
                else
                {
                    low = c.Med(0);
                    c.IncMed0();
                    if (onesCount == 1)
                    {
                        high = low + c.Med(1) - 1;
                        c.DecMed1();
                    }
                    else
                    {
                        low += c.Med(1);
                        c.IncMed1();
                        if (onesCount == 2)
                        {
                            high = low + c.Med(2) - 1;
                            c.DecMed2();
                        }
                        else
                        {
                            low += (onesCount - 2) * c.Med(2);
                            high = low + c.Med(2) - 1;
                            c.IncMed2();
                        }
                    }
                }

                low += ReadCode(bits, high - low);
                buffer[i] = bits.Bit() != 0 ? (int)~low : (int)low;
            }

            return Mono ? i : i / 2;
        }

        private int GetWord(int channel)
        {
            var bits = _wv!;
            var c = _c[channel];
            if ((_c[0].Median[0] & ~1u) == 0 && _holdingZero == 0 && _holdingOne == 0 && (_c[1].Median[0] & ~1u) == 0)
            {
                if (_zerosAcc != 0)
                {
                    if (--_zerosAcc != 0)
                    {
                        c.SlowLevel -= (c.SlowLevel + Slo) >> Sls;
                        return 0;
                    }
                }
                else
                {
                    var run = ZeroRun(bits);
                    if (run < 0)
                    {
                        return WordEof;
                    }

                    _zerosAcc = (uint)run;
                    if (_zerosAcc != 0)
                    {
                        c.SlowLevel -= (c.SlowLevel + Slo) >> Sls;
                        _c[0].ClearMedians();
                        _c[1].ClearMedians();
                        return 0;
                    }
                }
            }

            uint onesCount;
            if (_holdingZero != 0)
            {
                onesCount = 0;
                _holdingZero = 0;
            }
            else
            {
                var ones = OnesCount(bits);
                if (ones < 0)
                {
                    return WordEof;
                }

                if (_holdingOne != 0)
                {
                    _holdingOne = (uint)ones & 1;
                    onesCount = (uint)(ones >> 1) + 1;
                }
                else
                {
                    _holdingOne = (uint)ones & 1;
                    onesCount = (uint)(ones >> 1);
                }

                _holdingZero = (int)(~_holdingOne & 1);
            }

            if ((_flags & HybridFlag) != 0 && channel == 0)
            {
                UpdateErrorLimit();
            }

            uint low, high;
            if (onesCount == 0)
            {
                low = 0;
                high = c.Med(0) - 1;
                c.DecMed0();
            }
            else
            {
                low = c.Med(0);
                c.IncMed0();
                if (onesCount == 1)
                {
                    high = low + c.Med(1) - 1;
                    c.DecMed1();
                }
                else
                {
                    low += c.Med(1);
                    c.IncMed1();
                    if (onesCount == 2)
                    {
                        high = low + c.Med(2) - 1;
                        c.DecMed2();
                    }
                    else
                    {
                        low += (onesCount - 2) * c.Med(2);
                        high = low + c.Med(2) - 1;
                        c.IncMed2();
                    }
                }
            }

            low &= 0x7fffffff;
            high &= 0x7fffffff;
            if (low > high)
            {
                high = low;
            }

            var mid = (high + low + 1) >> 1;
            if (c.ErrorLimit == 0)
            {
                mid = ReadCode(bits, high - low) + low;
            }
            else
            {
                while (high - low > c.ErrorLimit)
                {
                    if (bits.Bit() != 0)
                    {
                        low = mid;
                        mid = (high + low + 1) >> 1;
                    }
                    else
                    {
                        high = mid - 1;
                        mid = (high + low + 1) >> 1;
                    }
                }
            }

            var sign = bits.Bit();
            if ((_flags & HybridBitrate) != 0)
            {
                c.SlowLevel -= (c.SlowLevel + Slo) >> Sls;
                c.SlowLevel += (uint)Log2(mid);
            }

            return sign != 0 ? (int)~mid : (int)mid;
        }

        /// <summary>A value from 0 to <paramref name="maxCode"/>, in the fewest bits.</summary>
        private static uint ReadCode(Bits bits, uint maxCode)
        {
            if (maxCode < 2)
            {
                return maxCode != 0 ? (uint)bits.Bit() : 0;
            }

            var bitCount = 32 - BitOperations.LeadingZeroCount(maxCode);
            var extras = (uint)((1L << bitCount) - maxCode - 1);
            var code = bits.Read(bitCount - 1);
            if (code >= extras)
            {
                code = (code << 1) - extras + (uint)bits.Bit();
            }

            return code;
        }

        private static int ApplyWeight(int weight, int sample) => unchecked(sample != (short)sample
            ? ((((sample & 0xffff) * weight) >> 9) + (((sample & ~0xffff) >> 9) * weight) + 1) >> 1
            : (weight * sample + 512) >> 10);

        private static void UpdateWeight(ref int weight, int delta, int source, int result)
        {
            if (source != 0 && result != 0)
            {
                var s = (source ^ result) >> 31;
                weight = (delta ^ s) + (weight - s);
            }
        }

        private static void UpdateWeightClip(ref int weight, int delta, int source, int result)
        {
            if (source != 0 && result != 0)
            {
                var s = (source ^ result) >> 31;
                if ((weight = (weight ^ s) + (delta - s)) > 1024)
                {
                    weight = 1024;
                }

                weight = (weight ^ s) - s;
            }
        }

        private static void DecorrMono(Pass p, int[] buffer, int count)
        {
            var delta = p.Delta;
            var weight = p.WeightA;
            var a = p.SamplesA;
            switch (p.Term)
            {
                case 17:
                    for (var i = 0; i < count; i++)
                    {
                        var sam = unchecked(2 * a[0] - a[1]);
                        a[1] = a[0];
                        a[0] = unchecked(ApplyWeight(weight, sam) + buffer[i]);
                        UpdateWeight(ref weight, delta, sam, buffer[i]);
                        buffer[i] = a[0];
                    }

                    break;

                case 18:
                    for (var i = 0; i < count; i++)
                    {
                        var sam = unchecked((3 * a[0] - a[1]) >> 1);
                        a[1] = a[0];
                        a[0] = unchecked(ApplyWeight(weight, sam) + buffer[i]);
                        UpdateWeight(ref weight, delta, sam, buffer[i]);
                        buffer[i] = a[0];
                    }

                    break;

                default:
                {
                    int m = 0, k = p.Term & (MaxTerm - 1);
                    for (var i = 0; i < count; i++)
                    {
                        var sam = a[m];
                        a[k] = unchecked(ApplyWeight(weight, sam) + buffer[i]);
                        UpdateWeight(ref weight, delta, sam, buffer[i]);
                        buffer[i] = a[k];
                        m = (m + 1) & (MaxTerm - 1);
                        k = (k + 1) & (MaxTerm - 1);
                    }

                    break;
                }
            }

            p.WeightA = weight;
        }

        private static void DecorrStereo(Pass p, int[] buffer, int count)
        {
            var a = p.SamplesA;
            var b = p.SamplesB;
            var n = count * 2;
            switch (p.Term)
            {
                case 17:
                    for (var i = 0; i < n; i += 2)
                    {
                        var sam = unchecked(2 * a[0] - a[1]);
                        a[1] = a[0];
                        var tmp = buffer[i];
                        buffer[i] = a[0] = unchecked(ApplyWeight(p.WeightA, sam) + tmp);
                        UpdateWeight(ref p.WeightA, p.Delta, sam, tmp);

                        sam = unchecked(2 * b[0] - b[1]);
                        b[1] = b[0];
                        tmp = buffer[i + 1];
                        buffer[i + 1] = b[0] = unchecked(ApplyWeight(p.WeightB, sam) + tmp);
                        UpdateWeight(ref p.WeightB, p.Delta, sam, tmp);
                    }

                    break;

                case 18:
                    for (var i = 0; i < n; i += 2)
                    {
                        var sam = unchecked(a[0] + ((a[0] - a[1]) >> 1));
                        a[1] = a[0];
                        var tmp = buffer[i];
                        buffer[i] = a[0] = unchecked(ApplyWeight(p.WeightA, sam) + tmp);
                        UpdateWeight(ref p.WeightA, p.Delta, sam, tmp);

                        sam = unchecked(b[0] + ((b[0] - b[1]) >> 1));
                        b[1] = b[0];
                        tmp = buffer[i + 1];
                        buffer[i + 1] = b[0] = unchecked(ApplyWeight(p.WeightB, sam) + tmp);
                        UpdateWeight(ref p.WeightB, p.Delta, sam, tmp);
                    }

                    break;

                case -1:
                    for (var i = 0; i < n; i += 2)
                    {
                        var sam = unchecked(buffer[i] + ApplyWeight(p.WeightA, a[0]));
                        UpdateWeightClip(ref p.WeightA, p.Delta, a[0], buffer[i]);
                        buffer[i] = sam;
                        a[0] = unchecked(buffer[i + 1] + ApplyWeight(p.WeightB, sam));
                        UpdateWeightClip(ref p.WeightB, p.Delta, sam, buffer[i + 1]);
                        buffer[i + 1] = a[0];
                    }

                    break;

                case -2:
                    for (var i = 0; i < n; i += 2)
                    {
                        var sam = unchecked(buffer[i + 1] + ApplyWeight(p.WeightB, b[0]));
                        UpdateWeightClip(ref p.WeightB, p.Delta, b[0], buffer[i + 1]);
                        buffer[i + 1] = sam;
                        b[0] = unchecked(buffer[i] + ApplyWeight(p.WeightA, sam));
                        UpdateWeightClip(ref p.WeightA, p.Delta, sam, buffer[i]);
                        buffer[i] = b[0];
                    }

                    break;

                case -3:
                    for (var i = 0; i < n; i += 2)
                    {
                        var samA = unchecked(buffer[i] + ApplyWeight(p.WeightA, a[0]));
                        UpdateWeightClip(ref p.WeightA, p.Delta, a[0], buffer[i]);
                        var samB = unchecked(buffer[i + 1] + ApplyWeight(p.WeightB, b[0]));
                        UpdateWeightClip(ref p.WeightB, p.Delta, b[0], buffer[i + 1]);
                        buffer[i] = b[0] = samA;
                        buffer[i + 1] = a[0] = samB;
                    }

                    break;

                default:
                {
                    int m = 0, k = p.Term & (MaxTerm - 1);
                    for (var i = 0; i < n; i += 2)
                    {
                        var sam = a[m];
                        a[k] = unchecked(ApplyWeight(p.WeightA, sam) + buffer[i]);
                        UpdateWeight(ref p.WeightA, p.Delta, sam, buffer[i]);
                        buffer[i] = a[k];

                        sam = b[m];
                        b[k] = unchecked(ApplyWeight(p.WeightB, sam) + buffer[i + 1]);
                        UpdateWeight(ref p.WeightB, p.Delta, sam, buffer[i + 1]);
                        buffer[i + 1] = b[k];
                        m = (m + 1) & (MaxTerm - 1);
                        k = (k + 1) & (MaxTerm - 1);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// The last steps: floats are put back together, whole numbers over 24 bits get their extra bits, lossy
        /// samples are clipped, and everything is shifted back up. False when the extra bits' checksum is wrong.
        /// </summary>
        private bool Fixup(int[] buffer, int count)
        {
            var lossy = (_flags & HybridFlag) != 0;
            var shift = (int)((_flags & ShiftMask) >> ShiftLsb);
            var values = Mono ? count : count * 2;
            if ((_flags & FloatData) != 0)
            {
                return FloatValues(buffer, values);
            }

            if ((_flags & Int32Data) != 0)
            {
                int sentBits = _int32SentBits & 0x1f, zeros = _int32Zeros & 0x1f, ones = _int32Ones & 0x1f, dups = _int32Dups & 0x1f;
                var mask = (1u << sentBits) - 1;
                if (_wvx is { } wvx)
                {
                    var crc = 0xffffffffu;
                    for (var i = 0; i < values; i++)
                    {
                        var v = buffer[i];
                        if (sentBits != 0)
                        {
                            if (_int32MaxWidth != 0)
                            {
                                var positive = v < 0 ? ~v : v;
                                var width = (32 - BitOperations.LeadingZeroCount((uint)positive)) + sentBits;
                                var toRead = sentBits;
                                if (width <= _int32MaxWidth || (toRead -= width - _int32MaxWidth) > 0)
                                {
                                    var data = wvx.Read(toRead) & ((1u << toRead) - 1);
                                    v = (int)((((uint)v << toRead) | data) << (sentBits - toRead));
                                }
                                else
                                {
                                    v = (int)((uint)v << sentBits);
                                }
                            }
                            else
                            {
                                v = (int)(((uint)v << sentBits) | (wvx.Read(sentBits) & mask));
                            }
                        }

                        v = ExtendInt32(v, zeros, ones, dups);
                        crc = unchecked(crc * 9 + (uint)(v & 0xffff) * 3 + (uint)((v >> 16) & 0xffff));
                        buffer[i] = v;
                    }

                    if (crc != _crcWvx)
                    {
                        return false;
                    }
                }
                else if (sentBits == 0 && zeros + ones + dups != 0)
                {
                    while (lossy && (_flags & BytesStored) == 3 && shift < 8)
                    {
                        if (zeros != 0)
                        {
                            zeros--;
                        }
                        else if (ones != 0)
                        {
                            ones--;
                        }
                        else if (dups != 0)
                        {
                            dups--;
                        }
                        else
                        {
                            break;
                        }

                        shift++;
                    }

                    for (var i = 0; i < values; i++)
                    {
                        buffer[i] = ExtendInt32(buffer[i], zeros, ones, dups);
                    }
                }
                else
                {
                    shift += zeros + sentBits + ones + dups;
                }
            }

            shift &= 0x1f;
            if (lossy)
            {
                var (min, max) = (_flags & BytesStored) switch
                {
                    0 => (-128 >> shift, 127 >> shift),
                    1 => (-32768 >> shift, 32767 >> shift),
                    2 => (-8388608 >> shift, 8388607 >> shift),
                    _ => (int.MinValue >> shift, int.MaxValue >> shift),
                };
                var minShifted = (int)((uint)min << shift);
                var maxShifted = max << shift;
                for (var i = 0; i < values; i++)
                {
                    buffer[i] = buffer[i] < min ? minShifted : buffer[i] > max ? maxShifted : (int)((uint)buffer[i] << shift);
                }
            }
            else if (shift != 0)
            {
                for (var i = 0; i < values; i++)
                {
                    buffer[i] = (int)((uint)buffer[i] << shift);
                }
            }

            return true;
        }

        private static int ExtendInt32(int v, int zeros, int ones, int dups) => unchecked(
            zeros != 0 ? (int)((uint)v << zeros)
            : ones != 0 ? (int)((uint)(v + 1) << ones) - 1
            : dups != 0 ? (int)((uint)(v + (v & 1)) << dups) - (v & 1)
            : v);

        private bool FloatValues(int[] values, int count)
        {
            var crc = 0xffffffffu;
            var wvx = _wvx;
            for (var i = 0; i < count; i++)
            {
                int shiftCount = 0, exp = _floatMaxExp;
                uint mantissa = 0, exponent = 0, sign = 0;
                var v = values[i];
                if (wvx is null)
                {
                    if (v != 0)
                    {
                        v = (int)((uint)v << (_floatShift & 0x1f));
                        if (v < 0)
                        {
                            v = -v;
                            sign = 1;
                        }

                        if (v >= 0x1000000)
                        {
                            while ((v & 0xf000000) != 0)
                            {
                                v >>= 1;
                                ++exp;
                            }
                        }
                        else if (exp != 0)
                        {
                            while ((v & 0x800000) == 0 && --exp != 0)
                            {
                                shiftCount++;
                                v = (int)((uint)v << 1);
                            }

                            if ((shiftCount &= 0x1f) != 0 && (_floatFlags & FloatShiftOnes) != 0)
                            {
                                v |= (int)((1u << shiftCount) - 1);
                            }
                        }

                        mantissa = (uint)v;
                        exponent = (uint)exp;
                    }
                }
                else if (v == 0)
                {
                    if ((_floatFlags & FloatZerosSent) != 0)
                    {
                        if (wvx.Bit() != 0)
                        {
                            mantissa = wvx.Read(23);
                            if (exp >= 25)
                            {
                                exponent = wvx.Read(8);
                            }

                            sign = (uint)wvx.Bit();
                        }
                        else if ((_floatFlags & FloatNegZeros) != 0)
                        {
                            sign = (uint)wvx.Bit();
                        }
                    }
                }
                else
                {
                    v = (int)((uint)v << (_floatShift & 0x1f));
                    if (v < 0)
                    {
                        v = -v;
                        sign = 1;
                    }

                    if (v == 0x1000000)
                    {
                        if (wvx.Bit() != 0)
                        {
                            mantissa = wvx.Read(23);
                        }

                        exponent = 255;
                    }
                    else
                    {
                        if (exp != 0)
                        {
                            while ((v & 0x800000) == 0 && --exp != 0)
                            {
                                shiftCount++;
                                v = (int)((uint)v << 1);
                            }
                        }

                        if ((shiftCount &= 0x1f) != 0)
                        {
                            if ((_floatFlags & FloatShiftOnes) != 0 || ((_floatFlags & FloatShiftSame) != 0 && wvx.Bit() != 0))
                            {
                                v |= (int)((1u << shiftCount) - 1);
                            }
                            else if ((_floatFlags & FloatShiftSent) != 0)
                            {
                                var mask = (int)((1u << shiftCount) - 1);
                                var numZeros = 0;
                                if (_floatMaxShiftedOnes != 0 && shiftCount > _floatMaxShiftedOnes)
                                {
                                    numZeros = shiftCount - _floatMaxShiftedOnes;
                                }

                                if (_floatMinShiftedZeros > numZeros)
                                {
                                    numZeros = _floatMinShiftedZeros > shiftCount ? shiftCount : _floatMinShiftedZeros;
                                }

                                if ((shiftCount -= numZeros) > 0)
                                {
                                    v |= (int)(wvx.Read(shiftCount) << numZeros) & mask;
                                }
                            }
                        }

                        mantissa = (uint)v;
                        exponent = (uint)exp;
                    }
                }

                mantissa &= 0x7fffff;
                exponent &= 0xff;
                crc = unchecked(crc * 27 + mantissa * 9 + exponent * 3 + sign);
                var bits = (sign << 31) | (exponent << 23) | mantissa;
                var value = BitConverter.UInt32BitsToSingle(bits);
                if (_floatNormExp != 127)
                {
                    // Floats stored at another scale (not ±1) are brought back to it.
                    value = MathF.ScaleB(value, 127 - _floatNormExp);
                }

                values[i] = BitConverter.SingleToInt32Bits(value);
            }

            return wvx is null || crc == _crcWvx;
        }

        private static int RestoreWeight(sbyte weight)
        {
            var result = weight * 8;
            if (result > 0)
            {
                result += (result + 64) >> 7;
            }

            return result;
        }

        private static readonly byte[] Log2Table =
        {
            0x00, 0x01, 0x03, 0x04, 0x06, 0x07, 0x09, 0x0a, 0x0b, 0x0d, 0x0e, 0x10, 0x11, 0x12, 0x14, 0x15,
            0x16, 0x18, 0x19, 0x1a, 0x1c, 0x1d, 0x1e, 0x20, 0x21, 0x22, 0x24, 0x25, 0x26, 0x28, 0x29, 0x2a,
            0x2c, 0x2d, 0x2e, 0x2f, 0x31, 0x32, 0x33, 0x34, 0x36, 0x37, 0x38, 0x39, 0x3b, 0x3c, 0x3d, 0x3e,
            0x3f, 0x41, 0x42, 0x43, 0x44, 0x45, 0x47, 0x48, 0x49, 0x4a, 0x4b, 0x4d, 0x4e, 0x4f, 0x50, 0x51,
            0x52, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a, 0x5c, 0x5d, 0x5e, 0x5f, 0x60, 0x61, 0x62, 0x63,
            0x64, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x74, 0x75,
            0x76, 0x77, 0x78, 0x79, 0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85,
            0x86, 0x87, 0x88, 0x89, 0x8a, 0x8b, 0x8c, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x92, 0x93, 0x94, 0x95,
            0x96, 0x97, 0x98, 0x99, 0x9a, 0x9b, 0x9b, 0x9c, 0x9d, 0x9e, 0x9f, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4,
            0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xa9, 0xaa, 0xab, 0xac, 0xad, 0xae, 0xaf, 0xb0, 0xb1, 0xb2, 0xb2,
            0xb3, 0xb4, 0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xb9, 0xba, 0xbb, 0xbc, 0xbd, 0xbe, 0xbf, 0xc0, 0xc0,
            0xc1, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xcb, 0xcb, 0xcc, 0xcd, 0xce,
            0xcf, 0xd0, 0xd0, 0xd1, 0xd2, 0xd3, 0xd4, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd8, 0xd9, 0xda, 0xdb,
            0xdc, 0xdc, 0xdd, 0xde, 0xdf, 0xe0, 0xe0, 0xe1, 0xe2, 0xe3, 0xe4, 0xe4, 0xe5, 0xe6, 0xe7, 0xe7,
            0xe8, 0xe9, 0xea, 0xea, 0xeb, 0xec, 0xed, 0xee, 0xee, 0xef, 0xf0, 0xf1, 0xf1, 0xf2, 0xf3, 0xf4,
            0xf4, 0xf5, 0xf6, 0xf7, 0xf7, 0xf8, 0xf9, 0xf9, 0xfa, 0xfb, 0xfc, 0xfc, 0xfd, 0xfe, 0xff, 0xff,
        };

        private static readonly byte[] Exp2Table =
        {
            0x00, 0x01, 0x01, 0x02, 0x03, 0x03, 0x04, 0x05, 0x06, 0x06, 0x07, 0x08, 0x08, 0x09, 0x0a, 0x0b,
            0x0b, 0x0c, 0x0d, 0x0e, 0x0e, 0x0f, 0x10, 0x10, 0x11, 0x12, 0x13, 0x13, 0x14, 0x15, 0x16, 0x16,
            0x17, 0x18, 0x19, 0x19, 0x1a, 0x1b, 0x1c, 0x1d, 0x1d, 0x1e, 0x1f, 0x20, 0x20, 0x21, 0x22, 0x23,
            0x24, 0x24, 0x25, 0x26, 0x27, 0x28, 0x28, 0x29, 0x2a, 0x2b, 0x2c, 0x2c, 0x2d, 0x2e, 0x2f, 0x30,
            0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x3a, 0x3b, 0x3c, 0x3d,
            0x3e, 0x3f, 0x40, 0x41, 0x41, 0x42, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x48, 0x49, 0x4a, 0x4b,
            0x4c, 0x4d, 0x4e, 0x4f, 0x50, 0x51, 0x51, 0x52, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59, 0x5a,
            0x5b, 0x5c, 0x5d, 0x5e, 0x5e, 0x5f, 0x60, 0x61, 0x62, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69,
            0x6a, 0x6b, 0x6c, 0x6d, 0x6e, 0x6f, 0x70, 0x71, 0x72, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79,
            0x7a, 0x7b, 0x7c, 0x7d, 0x7e, 0x7f, 0x80, 0x81, 0x82, 0x83, 0x84, 0x85, 0x87, 0x88, 0x89, 0x8a,
            0x8b, 0x8c, 0x8d, 0x8e, 0x8f, 0x90, 0x91, 0x92, 0x93, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0x9b,
            0x9c, 0x9d, 0x9f, 0xa0, 0xa1, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa8, 0xa9, 0xaa, 0xab, 0xac, 0xad,
            0xaf, 0xb0, 0xb1, 0xb2, 0xb3, 0xb4, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xbc, 0xbd, 0xbe, 0xbf, 0xc0,
            0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc8, 0xc9, 0xca, 0xcb, 0xcd, 0xce, 0xcf, 0xd0, 0xd2, 0xd3, 0xd4,
            0xd6, 0xd7, 0xd8, 0xd9, 0xdb, 0xdc, 0xdd, 0xde, 0xe0, 0xe1, 0xe2, 0xe4, 0xe5, 0xe6, 0xe8, 0xe9,
            0xea, 0xec, 0xed, 0xee, 0xf0, 0xf1, 0xf2, 0xf4, 0xf5, 0xf6, 0xf8, 0xf9, 0xfa, 0xfc, 0xfd, 0xff,
        };

        private static int Log2(uint value)
        {
            if ((value += value >> 9) < 1 << 8)
            {
                var dbits = value == 0 ? 0 : 32 - BitOperations.LeadingZeroCount(value);
                return (dbits << 8) + Log2Table[(value << (9 - dbits)) & 0xff];
            }
            else
            {
                var dbits = 32 - BitOperations.LeadingZeroCount(value);
                return (dbits << 8) + Log2Table[(value >> (dbits - 9)) & 0xff];
            }
        }

        private static int Exp2s(int log)
        {
            if (log < 0)
            {
                return unchecked((int)~((uint)Exp2s(-log) - 1));
            }

            var value = Exp2Table[log & 0xff] | 0x100u;
            return (log >>= 8) <= 9 ? (int)(value >> (9 - log)) : unchecked((int)(value << ((log - 9) & 0x1f)));
        }

        private sealed class Pass
        {
            public int Term;
            public int Delta;
            public int WeightA;
            public int WeightB;
            public readonly int[] SamplesA = new int[MaxTerm];
            public readonly int[] SamplesB = new int[MaxTerm];
        }

        private sealed class Entropy
        {
            public readonly uint[] Median = new uint[3];
            public uint SlowLevel;
            public uint ErrorLimit;

            public uint Med(int i) => (Median[i] >> 4) + 1;

            public void IncMed0() => Median[0] += (Median[0] + 128) / 128 * 5;

            public void DecMed0() => Median[0] -= (Median[0] + 126) / 128 * 2;

            public void IncMed1() => Median[1] += (Median[1] + 64) / 64 * 5;

            public void DecMed1() => Median[1] -= (Median[1] + 62) / 64 * 2;

            public void IncMed2() => Median[2] += (Median[2] + 32) / 32 * 5;

            public void DecMed2() => Median[2] -= (Median[2] + 30) / 32 * 2;

            public void ClearMedians() => Array.Clear(Median);
        }
    }

    /// <summary>Reads a block's bits, lowest bit of each byte first. Past the end, it reads zeros.</summary>
    private sealed class Bits
    {
        private readonly byte[] _data;
        private readonly int _end;
        private int _at;
        private ulong _bits;
        private int _count;

        public Bits(byte[] data, int start, int end)
        {
            _data = data;
            _at = start;
            _end = end;
        }

        private void Fill()
        {
            while (_count <= 56)
            {
                ulong next = _at < _end ? _data[_at] : 0ul;
                _at++;
                _bits |= next << _count;
                _count += 8;
            }
        }

        public int Bit()
        {
            if (_count == 0)
            {
                Fill();
            }

            var bit = (int)(_bits & 1);
            _bits >>= 1;
            _count--;
            return bit;
        }

        /// <summary>The next <paramref name="count"/> bits (up to 32), the first in the lowest place.</summary>
        public uint Read(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            if (_count < count)
            {
                Fill();
            }

            var value = (uint)(_bits & ((1ul << count) - 1));
            _bits >>= count;
            _count -= count;
            return value;
        }

        /// <summary>Counts and reads 1 bits up to <paramref name="limit"/>, and the 0 that ends them if it comes first.</summary>
        public int CountOnes(int limit)
        {
            var ones = 0;
            while (true)
            {
                if (_count < 32)
                {
                    Fill();
                }

                var run = BitOperations.TrailingZeroCount(~_bits);
                if (ones + run >= limit)
                {
                    var take = limit - ones;
                    _bits >>= take;
                    _count -= take;
                    return limit;
                }

                if (run < _count)
                {
                    _bits >>= run + 1;
                    _count -= run + 1;
                    return ones + run;
                }

                ones += _count;
                _bits = 0;
                _count = 0;
            }
        }
    }
}
