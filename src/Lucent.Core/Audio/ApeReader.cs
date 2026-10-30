using System.Buffers.Binary;
using System.IO;
using System.Numerics;

namespace Lucent.Audio;

/// <summary>
/// Lucent's own Monkey's Audio (.ape) decoder, the same on Windows and the Mac, since neither system nor the bundled
/// libVLC reads it. It decodes files from Monkey's Audio 3.95 (2002) on, at every compression level (fast to insane),
/// 8, 16, 24 and 32-bit, mono, stereo and multichannel. Older files say so rather than playing as noise.
/// </summary>
/// <remarks>
/// A port of the decoding parts of Matthew T. Ashland's Monkey's Audio SDK (monkeysaudio.com, BSD licence, see
/// assets/licenses/LICENSE-MonkeysAudio.txt). Each frame (about 6.7 seconds) decodes on its own, from the file's seek
/// table, and is checked against its CRC.
/// </remarks>
public sealed class ApeReader : BlockAudioReader
{
    private const int FlagCreateWavHeader = 1 << 5;
    private const int Flag8Bit = 1 << 0;
    private const int Flag24Bit = 1 << 3;
    private const int FlagHasPeakLevel = 1 << 2;
    private const int FlagHasSeekElements = 1 << 4;
    private const int MaxBlocksPerFrame = 1 << 21;

    private readonly long[] _seek;
    private readonly int _version;
    private readonly int _level;
    private readonly int _bits;
    private readonly int _channels;
    private readonly int _blocksPerFrame;
    private readonly int _finalFrameBlocks;
    private readonly long _audioEnd;

    private ApeReader(Stream stream, int sampleRate, int channels, long frames, long[] seek, int version, int level, int bits,
        int blocksPerFrame, int finalFrameBlocks, long audioEnd)
        : base(stream, sampleRate, channels, frames)
    {
        _seek = seek;
        _version = version;
        _level = level;
        _bits = bits;
        _channels = channels;
        _blocksPerFrame = blocksPerFrame;
        _finalFrameBlocks = finalFrameBlocks;
        _audioEnd = audioEnd;
        SourceBits = bits;
    }

    /// <summary>
    /// Opens a Monkey's Audio file, or returns null when <paramref name="path"/> isn't one. Throws
    /// <see cref="NotSupportedException"/> for files from before Monkey's Audio 3.95.
    /// </summary>
    public static ApeReader? TryOpen(string path)
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

    private static ApeReader? Open(Stream stream)
    {
        var start = FindDescriptor(stream);
        if (start < 0)
        {
            return null;
        }

        var common = ReadAt(stream, start, 6);
        int version = BinaryPrimitives.ReadUInt16LittleEndian(common.AsSpan(4));
        if (version < 3950)
        {
            throw new NotSupportedException($"Monkey's Audio files from before version 3.95 can't be decoded (this one is {version / 1000.0:0.00}).");
        }

        int level, flags, channels, sampleRate, bits, blocksPerFrame, finalFrameBlocks, totalFrames, seekElements;
        uint terminatingBytes;
        long seekTableAt;
        if (version >= 3980)
        {
            var descriptor = ReadAt(stream, start, 52);
            var descriptorBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(8));
            var headerBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(12));
            var seekTableBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(16));
            terminatingBytes = BinaryPrimitives.ReadUInt32LittleEndian(descriptor.AsSpan(32));
            var header = ReadAt(stream, start + descriptorBytes, 24);
            level = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(0));
            flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
            blocksPerFrame = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            finalFrameBlocks = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
            totalFrames = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
            bits = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(16));
            channels = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(18));
            sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
            seekElements = (int)(seekTableBytes / 4);
            seekTableAt = start + descriptorBytes + headerBytes;
        }
        else
        {
            var header = ReadAt(stream, start, 32);
            level = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(6));
            flags = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(8));
            channels = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(10));
            sampleRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12));
            var wavHeaderBytes = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
            terminatingBytes = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
            totalFrames = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(24));
            finalFrameBlocks = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(28));
            var at = start + 32;
            if ((flags & FlagHasPeakLevel) != 0)
            {
                at += 4;
            }

            if ((flags & FlagHasSeekElements) != 0)
            {
                seekElements = (int)BinaryPrimitives.ReadUInt32LittleEndian(ReadAt(stream, at, 4));
                at += 4;
            }
            else
            {
                seekElements = totalFrames;
            }

            if ((flags & FlagCreateWavHeader) == 0)
            {
                at += wavHeaderBytes;
            }

            blocksPerFrame = 73728 * 4;
            bits = (flags & Flag8Bit) != 0 ? 8 : (flags & Flag24Bit) != 0 ? 24 : 16;
            seekTableAt = at;
        }

        // Real files hold at most about a million blocks a frame; a header asking for far more is damaged, or made to ask
        // for gigabytes of memory.
        if (totalFrames <= 0 || totalFrames > int.MaxValue / 4 || channels is < 1 or > 32 || sampleRate <= 0
            || bits is not (8 or 16 or 24 or 32) || blocksPerFrame <= 0 || blocksPerFrame > MaxBlocksPerFrame
            || finalFrameBlocks < 0 || finalFrameBlocks > blocksPerFrame
            || level is not (1000 or 2000 or 3000 or 4000 or 5000) || seekElements < totalFrames
            || (long)seekElements * 4 > stream.Length)
        {
            throw new InvalidDataException("The Monkey's Audio file's header doesn't make sense.");
        }

        var table = ReadAt(stream, seekTableAt, totalFrames * 4);
        var seek = new long[totalFrames];
        long add = 0;
        for (var i = 0; i < totalFrames; i++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(i * 4));
            if (i > 0 && value < (uint)(seek[i - 1] - add - start))
            {
                // Over 4 GB, the 32-bit table wraps round.
                add += 1L << 32;
            }

            seek[i] = add + value + start;
        }

        var audioEnd = stream.Length - TagBytes(stream) - terminatingBytes;
        var frames = (long)(totalFrames - 1) * blocksPerFrame + finalFrameBlocks;
        return new ApeReader(stream, sampleRate, channels, frames, seek, version, level, bits, blocksPerFrame, finalFrameBlocks, audioEnd);
    }

    /// <summary>Where the "MAC " header starts (after any ID3v2 tag), or -1.</summary>
    private static long FindDescriptor(Stream stream)
    {
        if (stream.Length < 32)
        {
            return -1;
        }

        long at = 0;
        var head = ReadAt(stream, 0, 10);
        if (head[0] == 'I' && head[1] == 'D' && head[2] == '3')
        {
            var size = ((head[6] & 127) << 21) | ((head[7] & 127) << 14) | ((head[8] & 127) << 7) | (head[9] & 127);
            at = size + ((head[5] & 16) != 0 ? 20 : 10);
        }

        var scan = ReadAt(stream, at, (int)Math.Min(1 << 20, stream.Length - at));
        for (var i = 0; i + 4 <= scan.Length; i++)
        {
            if (scan[i] == 'M' && scan[i + 1] == 'A' && scan[i + 2] == 'C' && scan[i + 3] == ' ')
            {
                return at + i;
            }
        }

        return -1;
    }

    /// <summary>The bytes of tags at the end of the file (APEv2, ID3v1), which aren't audio.</summary>
    private static long TagBytes(Stream stream)
    {
        long bytes = 0;
        if (stream.Length >= 128)
        {
            var id3 = ReadAt(stream, stream.Length - 128, 3);
            if (id3[0] == 'T' && id3[1] == 'A' && id3[2] == 'G')
            {
                bytes += 128;
            }
        }

        if (stream.Length - bytes >= 32)
        {
            var footer = ReadAt(stream, stream.Length - bytes - 32, 32);
            if (System.Text.Encoding.ASCII.GetString(footer, 0, 8) == "APETAGEX")
            {
                var size = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(12));
                var tagFlags = BinaryPrimitives.ReadUInt32LittleEndian(footer.AsSpan(20));
                bytes += size + ((tagFlags & 0x80000000) != 0 ? 32 : 0);
            }
        }

        return Math.Min(bytes, stream.Length);
    }

    protected override int BlockCount => _seek.Length;

    protected override long BlockStart(int block) => (long)block * _blocksPerFrame;

    protected override int DecodeBlock(int block, Func<long, int, byte[]> read, ref float[] output)
    {
        var blocks = block == _seek.Length - 1 ? _finalFrameBlocks : _blocksPerFrame;
        var remainder = (int)((_seek[block] - _seek[0]) % 4);
        var from = _seek[block] - remainder;
        // The range decoder reads a few bytes past its frame, into the next one (or zeros at the end).
        var end = block + 1 < _seek.Length ? Math.Min(_seek[block + 1] + 64, _audioEnd) : _audioEnd;
        // A frame is never much bigger than its sound uncompressed; a seek table saying otherwise is damaged.
        var length = (int)Math.Clamp(end - from, 0, (long)blocks * _channels * 8 + 4096);
        var bytes = new byte[(length + 3) / 4 * 4 + 64];
        Array.Copy(read(from, length), bytes, length);

        var pcm = new byte[blocks * _channels * (_bits / 8)];
        var frame = new Frame(this, bytes, remainder);
        if (!frame.Decode(blocks, pcm, legacy: false) && !new Frame(this, bytes, remainder).Decode(blocks, pcm, legacy: true))
        {
            // A damaged frame plays as silence, as Monkey's Audio itself does.
            Array.Clear(pcm);
        }

        if (output.Length < blocks * _channels)
        {
            output = new float[blocks * _channels];
        }

        var count = blocks * _channels;
        switch (_bits)
        {
            case 8:
                for (var i = 0; i < count; i++)
                {
                    output[i] = (pcm[i] - 128) / 128f;
                }

                break;
            case 16:
                for (var i = 0; i < count; i++)
                {
                    output[i] = BinaryPrimitives.ReadInt16LittleEndian(pcm.AsSpan(i * 2)) / 32768f;
                }

                break;
            case 24:
                for (var i = 0; i < count; i++)
                {
                    output[i] = ((pcm[i * 3] << 8) | (pcm[i * 3 + 1] << 16) | (pcm[i * 3 + 2] << 24)) / 2147483648f;
                }

                break;
            default:
                for (var i = 0; i < count; i++)
                {
                    output[i] = BinaryPrimitives.ReadInt32LittleEndian(pcm.AsSpan(i * 4)) / 2147483648f;
                }

                break;
        }

        return blocks;
    }

    /// <summary>One frame's decoding: its range decoder, predictors and output.</summary>
    private sealed class Frame
    {
        private const int SpecialMonoSilence = 1;
        private const int SpecialLeftSilence = 1;
        private const int SpecialRightSilence = 2;
        private const int SpecialPseudoStereo = 4;

        private readonly ApeReader _file;
        private readonly RangeDecoder _range;

        public Frame(ApeReader file, byte[] bytes, int remainder)
        {
            _file = file;
            _range = new RangeDecoder(bytes, remainder, file._version);
        }

        /// <summary>Decodes the frame into <paramref name="pcm"/>, as the WAV it came from. False when its CRC is wrong.</summary>
        public bool Decode(int blocks, byte[] pcm, bool legacy)
        {
            var channels = _file._channels;
            var bits = _file._bits;
            var storedCrc = _range.ReadUInt32();
            var special = 0;
            if ((storedCrc & 0x80000000) != 0)
            {
                special = (int)_range.ReadUInt32();
            }

            storedCrc &= 0x7fffffff;
            var predictors = new Predictor[channels];
            var states = new RangeDecoder.State[channels];
            for (var c = 0; c < channels; c++)
            {
                predictors[c] = new Predictor(_file._level, _file._version, bits, legacy);
                states[c] = RangeDecoder.State.Fresh;
            }

            _range.Start();
            var values = new int[channels];
            var writer = new PcmWriter(pcm, channels, bits);
            try
            {
                if (channels > 2)
                {
                    for (var i = 0; i < blocks; i++)
                    {
                        for (var c = 0; c < channels; c++)
                        {
                            values[c] = predictors[c].Decompress(_range.DecodeValue(ref states[c]), 0);
                        }

                        writer.Write(values);
                    }
                }
                else if (channels == 2)
                {
                    if ((special & SpecialLeftSilence) != 0 && (special & SpecialRightSilence) != 0)
                    {
                        for (var i = 0; i < blocks; i++)
                        {
                            values[0] = values[1] = 0;
                            writer.Write(values);
                        }
                    }
                    else if ((special & SpecialPseudoStereo) != 0)
                    {
                        for (var i = 0; i < blocks; i++)
                        {
                            values[0] = predictors[0].Decompress(_range.DecodeValue(ref states[0]), 0);
                            values[1] = 0;
                            writer.Write(values);
                        }
                    }
                    else
                    {
                        var lastX = 0;
                        for (var i = 0; i < blocks; i++)
                        {
                            var y = _range.DecodeValue(ref states[1]);
                            var x = _range.DecodeValue(ref states[0]);
                            var yValue = predictors[1].Decompress(y, lastX);
                            var xValue = predictors[0].Decompress(x, yValue);
                            lastX = xValue;
                            values[0] = xValue;
                            values[1] = yValue;
                            writer.Write(values);
                        }
                    }
                }
                else
                {
                    for (var i = 0; i < blocks; i++)
                    {
                        values[0] = (special & SpecialMonoSilence) != 0 ? 0 : predictors[0].Decompress(_range.DecodeValue(ref states[0]), 0);
                        writer.Write(values);
                    }
                }
            }
            catch (InvalidDataException)
            {
                return false;
            }

            var crc = Crc32(pcm) ^ 0xffffffff;
            return crc >> 1 == storedCrc;
        }
    }

    /// <summary>Writes decoded values as the original WAV's bytes (which the CRC is of), pairing channels back up.</summary>
    private sealed class PcmWriter
    {
        private readonly byte[] _pcm;
        private readonly int _channels;
        private readonly int _bits;
        private int _at;

        public PcmWriter(byte[] pcm, int channels, int bits)
        {
            _pcm = pcm;
            _channels = channels;
            _bits = bits;
        }

        public void Write(int[] v)
        {
            if (_bits == 32)
            {
                if (_channels == 2)
                {
                    var first = v[0] - v[1] / 2;
                    Put(first);
                    Put(first + v[1]);
                }
                else
                {
                    for (var c = 0; c < _channels; c++)
                    {
                        Put(v[c]);
                    }
                }
            }
            else if (_channels > 2)
            {
                if (_bits == 8)
                {
                    for (var c = 0; c < _channels; c++)
                    {
                        _pcm[_at++] = (byte)(v[c] + 128);
                    }
                }
                else if (_channels == 4)
                {
                    Pair(v[0], v[1]);
                    Pair(v[2], v[3]);
                }
                else if (_channels >= 6)
                {
                    Pair(v[0], v[1]);
                    Put(v[2], check: true);
                    Put(v[3], check: true);
                    Pair(v[4], v[5]);
                    if (_channels >= 8)
                    {
                        Pair(v[6], v[7]);
                    }

                    for (var c = _channels == 7 ? 6 : 8; c < _channels; c++)
                    {
                        Put(v[c]);
                    }
                }
                else
                {
                    for (var c = 0; c < _channels; c++)
                    {
                        Put(v[c]);
                    }
                }
            }
            else if (_channels == 2)
            {
                if (_bits == 8)
                {
                    var first = (byte)(v[0] - v[1] / 2 + 128);
                    _pcm[_at++] = first;
                    _pcm[_at++] = (byte)(first + v[1]);
                }
                else
                {
                    Pair(v[0], v[1]);
                }
            }
            else if (_bits == 8)
            {
                _pcm[_at++] = (byte)(v[0] + 128);
            }
            else
            {
                Put(v[0]);
            }
        }

        private void Pair(int a, int b)
        {
            var first = a - b / 2;
            Put(first, check: true);
            Put(first + b, check: true);
        }

        private void Put(int value, bool check = false)
        {
            switch (_bits)
            {
                case 16:
                    if (check && (value < short.MinValue || value > short.MaxValue))
                    {
                        throw new InvalidDataException();
                    }

                    _pcm[_at++] = (byte)value;
                    _pcm[_at++] = (byte)(value >> 8);
                    break;
                case 24:
                    _pcm[_at++] = (byte)value;
                    _pcm[_at++] = (byte)(value >> 8);
                    _pcm[_at++] = (byte)(value >> 16);
                    break;
                default:
                    BinaryPrimitives.WriteInt32LittleEndian(_pcm.AsSpan(_at), value);
                    _at += 4;
                    break;
            }
        }
    }

    /// <summary>
    /// Monkey's Audio's range decoder. Its bits are stored in little-endian 32-bit words read from the top bit down, so
    /// byte k of the stream is the file's byte (k with its last two bits flipped).
    /// </summary>
    private sealed class RangeDecoder
    {
        private const uint BottomValue = 1u << 23;
        private const int ExtraBits = 7;
        private const int ModelElements = 64;

        private static readonly uint[] KSumMinBoundary =
        {
            0, 32, 64, 128, 256, 512, 1024, 2048, 4096, 8192, 16384, 32768, 65536, 131072, 262144, 524288, 1048576,
            2097152, 4194304, 8388608, 16777216, 33554432, 67108864, 134217728, 268435456, 536870912, 1073741824,
            2147483648, 0, 0, 0, 0,
        };

        private static readonly uint[] RangeTotal1 =
        {
            0, 14824, 28224, 39348, 47855, 53994, 58171, 60926, 62682, 63786, 64463, 64878, 65126, 65276, 65365, 65419,
            65450, 65469, 65480, 65487, 65491, 65493, 65494, 65495, 65496, 65497, 65498, 65499, 65500, 65501, 65502,
            65503, 65504, 65505, 65506, 65507, 65508, 65509, 65510, 65511, 65512, 65513, 65514, 65515, 65516, 65517,
            65518, 65519, 65520, 65521, 65522, 65523, 65524, 65525, 65526, 65527, 65528, 65529, 65530, 65531, 65532,
            65533, 65534, 65535, 65536,
        };

        private static readonly uint[] RangeWidth1 =
        {
            14824, 13400, 11124, 8507, 6139, 4177, 2755, 1756, 1104, 677, 415, 248, 150, 89, 54, 31, 19, 11, 7, 4, 2,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1,
        };

        private static readonly uint[] RangeTotal2 =
        {
            0, 19578, 36160, 48417, 56323, 60899, 63265, 64435, 64971, 65232, 65351, 65416, 65447, 65466, 65476, 65482,
            65485, 65488, 65490, 65491, 65492, 65493, 65494, 65495, 65496, 65497, 65498, 65499, 65500, 65501, 65502,
            65503, 65504, 65505, 65506, 65507, 65508, 65509, 65510, 65511, 65512, 65513, 65514, 65515, 65516, 65517,
            65518, 65519, 65520, 65521, 65522, 65523, 65524, 65525, 65526, 65527, 65528, 65529, 65530, 65531, 65532,
            65533, 65534, 65535, 65536,
        };

        private static readonly uint[] RangeWidth2 =
        {
            19578, 16582, 12257, 7906, 4576, 2366, 1170, 536, 261, 119, 65, 31, 19, 10, 6, 3, 3, 2, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1,
            1, 1, 1, 1,
        };

        private readonly byte[] _data;
        private readonly int _version;
        private int _byte;
        private uint _low;
        private uint _range;
        private uint _buffer;

        public RangeDecoder(byte[] data, int startByte, int version)
        {
            _data = data;
            _byte = startByte;
            _version = version;
        }

        public struct State
        {
            public uint K;
            public uint KSum;

            public static State Fresh => new() { K = 10, KSum = (1u << 10) * 16 };
        }

        private uint NextByte()
        {
            var k = _byte++;
            var at = (k & ~3) + (3 - (k & 3));
            return at < _data.Length ? _data[at] : 0u;
        }

        public uint ReadUInt32()
        {
            uint value = 0;
            for (var i = 0; i < 4; i++)
            {
                value = (value << 8) | NextByte();
            }

            return value;
        }

        /// <summary>Starts the range coder: skips a dummy byte and takes the first.</summary>
        public void Start()
        {
            NextByte();
            _buffer = NextByte();
            _low = _buffer >> (8 - ExtraBits);
            _range = 1u << ExtraBits;
        }

        private void Normalize()
        {
            while (_range <= BottomValue)
            {
                _buffer = (_buffer << 8) | NextByte();
                _low = (_low << 8) | ((_buffer >> 1) & 0xff);
                _range <<= 8;
                if (_range == 0)
                {
                    throw new InvalidDataException();
                }
            }
        }

        private uint DecodeFast(int shift)
        {
            Normalize();
            _range >>= shift;
            return _low / _range;
        }

        private uint DecodeFastWithUpdate(int shift)
        {
            Normalize();
            _range >>= shift;
            if (_range == 0)
            {
                throw new InvalidDataException();
            }

            var result = _low / _range;
            _low %= _range;
            return result;
        }

        private uint DecodeDivided(uint divisor)
        {
            Normalize();
            _range /= divisor;
            if (_range == 0)
            {
                throw new InvalidDataException();
            }

            var result = _low / _range;
            _low %= _range;
            return result;
        }

        public long DecodeValue(ref State state)
        {
            long value;
            if (_version >= 3990)
            {
                var pivot = Math.Max(state.KSum / 32, 1u);
                var total = DecodeFast(16);
                if (total >= 65536)
                {
                    throw new InvalidDataException();
                }

                uint overflow = 0;
                while (total >= RangeTotal2[overflow + 1])
                {
                    overflow++;
                }

                _low -= _range * RangeTotal2[overflow];
                _range *= RangeWidth2[overflow];
                if (overflow == ModelElements - 1)
                {
                    overflow = DecodeFastWithUpdate(16) << 16;
                    overflow |= DecodeFastWithUpdate(16);
                }

                uint baseValue;
                if (pivot >= 1 << 16)
                {
                    var pivotBits = 32 - BitOperations.LeadingZeroCount(pivot);
                    var split = 1u << (pivotBits - 16);
                    var a = DecodeDivided(pivot / split + 1);
                    var b = DecodeDivided(split);
                    baseValue = a * split + b;
                }
                else
                {
                    baseValue = DecodeDivided(pivot);
                }

                value = baseValue + (long)overflow * pivot;
            }
            else
            {
                var total = DecodeFast(16);
                if (total >= 65536)
                {
                    throw new InvalidDataException();
                }

                uint overflow = 0;
                while (total >= RangeTotal1[overflow + 1])
                {
                    overflow++;
                }

                _low -= _range * RangeTotal1[overflow];
                _range *= RangeWidth1[overflow];
                uint tempK;
                if (overflow == ModelElements - 1)
                {
                    tempK = DecodeFastWithUpdate(5);
                    overflow = 0;
                }
                else
                {
                    tempK = state.K < 1 ? 0 : state.K - 1;
                }

                if (tempK <= 16 || _version < 3910)
                {
                    value = DecodeFastWithUpdate((int)tempK);
                }
                else
                {
                    var x1 = DecodeFastWithUpdate(16);
                    var x2 = DecodeFastWithUpdate((int)(tempK - 16));
                    value = x1 | (x2 << 16);
                }

                value += (uint)(overflow << (int)tempK);
            }

            state.KSum = unchecked(state.KSum + (uint)((value + 1) / 2 - ((state.KSum + 16) >> 5)));
            if (state.KSum < KSumMinBoundary[state.K])
            {
                state.K--;
            }
            else if (KSumMinBoundary[state.K + 1] != 0 && state.KSum >= KSumMinBoundary[state.K + 1])
            {
                state.K++;
            }

            return (value & 1) != 0 ? (value >> 1) + 1 : -(value >> 1);
        }
    }

    /// <summary>
    /// The predictor for one channel: neural-network filters (more at higher compression levels), then a fixed
    /// order-4 filter on this channel and order-5 on the other. Files up to 24 bits work in 32-bit numbers, 32-bit files
    /// in 64-bit ones, as Monkey's Audio itself does.
    /// </summary>
    private sealed class Predictor
    {
        private const int Window = 4096;
        private const int History = 8;

        private readonly bool _wide;
        private readonly bool _legacy;
        private readonly int _bits;
        private readonly NnFilter? _filter0;
        private readonly NnFilter? _filter1;
        private readonly NnFilter? _filter2;
        private readonly long[] _ma = new long[8];
        private readonly long[] _mb = new long[8];
        private readonly long[] _predA = new long[Window + History];
        private readonly long[] _predB = new long[Window + History];
        private readonly long[] _adaptA = new long[Window + History];
        private readonly long[] _adaptB = new long[Window + History];
        private int _at = History;
        private int _index;
        private long _lastA;
        private int _stage1A;
        private int _stage1B;

        public Predictor(int level, int version, int bits, bool legacy)
        {
            _bits = bits;
            _wide = bits >= 32;
            _legacy = legacy;
            switch (level)
            {
                case 2000:
                    _filter0 = new NnFilter(16, 11, version, _wide, legacy);
                    break;
                case 3000:
                    _filter0 = new NnFilter(64, 11, version, _wide, legacy);
                    break;
                case 4000:
                    _filter0 = new NnFilter(256, 13, version, _wide, legacy);
                    _filter1 = new NnFilter(32, 10, version, _wide, legacy);
                    break;
                case 5000:
                    _filter0 = new NnFilter(1024 + 256, 15, version, _wide, legacy);
                    _filter1 = new NnFilter(256, 13, version, _wide, legacy);
                    _filter2 = new NnFilter(16, 11, version, _wide, legacy);
                    break;
            }

            _ma[0] = 360;
            _ma[1] = 317;
            _ma[2] = -109;
            _ma[3] = 98;
        }

        /// <summary>Wraps a value the way the reference's number type would: to 32 bits, unless it's a 32-bit file.</summary>
        private long W(long value) => _wide ? value : (int)value;

        public int Decompress(long inputA, long inputB)
        {
            if (_index == Window)
            {
                Roll(_predA);
                Roll(_predB);
                Roll(_adaptA);
                Roll(_adaptB);
                _at = History;
                _index = 0;
            }

            var a = W(inputA);
            var b = W(inputB);
            if (_filter2 is not null)
            {
                a = _filter2.Decompress(a);
            }

            if (_filter1 is not null)
            {
                a = _filter1.Decompress(a);
            }

            if (_filter0 is not null)
            {
                a = _filter0.Decompress(a);
            }

            var i = _at;
            var pa = _predA;
            var pb = _predB;
            pa[i] = _lastA;
            pa[i - 1] = W(pa[i] - pa[i - 1]);

            // Stage 1's first-order filter on the other channel, at 31/32.
            var bInput = (int)b;
            pb[i] = W(bInput - W((long)_stage1B * 31 >> 5));
            _stage1B = bInput;
            pb[i - 1] = W(pb[i] - pb[i - 1]);

            long current;
            if (_bits <= 16 || _wide)
            {
                var predictionA = W(pa[i] * _ma[0] + pa[i - 1] * _ma[1] + pa[i - 2] * _ma[2] + pa[i - 3] * _ma[3]);
                var predictionB = W(pb[i] * _mb[0] + pb[i - 1] * _mb[1] + pb[i - 2] * _mb[2] + pb[i - 3] * _mb[3] + pb[i - 4] * _mb[4]);
                current = W(a + (W(predictionA + (predictionB >> 1)) >> 10));
            }
            else
            {
                var predictionA = pa[i] * _ma[0] + pa[i - 1] * _ma[1] + pa[i - 2] * _ma[2] + pa[i - 3] * _ma[3];
                var predictionB = pb[i] * _mb[0] + pb[i - 1] * _mb[1] + pb[i - 2] * _mb[2] + pb[i - 3] * _mb[3] + pb[i - 4] * _mb[4];
                current = _legacy
                    ? W(a + (((int)predictionA + ((int)predictionB >> 1)) >> 10))
                    : W(a + W((predictionA + (predictionB >> 1)) >> 10));
            }

            _adaptA[i] = pa[i] != 0 ? ((pa[i] >> 30) & 2) - 1 : 0;
            _adaptA[i - 1] = pa[i - 1] != 0 ? ((pa[i - 1] >> 30) & 2) - 1 : 0;
            _adaptB[i] = pb[i] != 0 ? ((pb[i] >> 30) & 2) - 1 : 0;
            _adaptB[i - 1] = pb[i - 1] != 0 ? ((pb[i - 1] >> 30) & 2) - 1 : 0;
            if (a > 0)
            {
                for (var k = 0; k < 4; k++)
                {
                    _ma[k] = W(_ma[k] - _adaptA[i - k]);
                }

                for (var k = 0; k < 5; k++)
                {
                    _mb[k] = W(_mb[k] - _adaptB[i - k]);
                }
            }
            else if (a < 0)
            {
                for (var k = 0; k < 4; k++)
                {
                    _ma[k] = W(_ma[k] + _adaptA[i - k]);
                }

                for (var k = 0; k < 5; k++)
                {
                    _mb[k] = W(_mb[k] + _adaptB[i - k]);
                }
            }

            // Stage 1's first-order filter on this channel, undone.
            _stage1A = (int)(current + ((long)_stage1A * 31 >> 5));
            _lastA = current;
            _at++;
            _index++;
            return _stage1A;
        }

        private void Roll(long[] buffer) => Array.Copy(buffer, _at - History, buffer, 0, History);
    }

    /// <summary>One of Monkey's Audio's adaptive ("neural net") filters: a sign-LMS filter over the last samples.</summary>
    private sealed class NnFilter
    {
        private const int Window = 4096;

        private readonly int _order;
        private readonly int _shift;
        private readonly int _round;
        private readonly int _version;
        private readonly bool _wide;
        private readonly bool _legacy;
        private readonly short[] _m;
        private readonly short[] _input;
        private readonly short[] _delta;
        private readonly int[]? _m32;
        private readonly int[]? _delta32;
        private int _at;
        private long _average;

        public NnFilter(int order, int shift, int version, bool wide, bool legacy)
        {
            _order = order;
            _shift = shift;
            _round = 1 << (shift - 1);
            _version = version;
            _wide = wide;
            _legacy = legacy;
            _m = new short[order];
            _input = new short[Window + order];
            _delta = new short[Window + order];
            if (wide)
            {
                _m32 = new int[order];
                _delta32 = new int[Window + order];
            }

            _at = order;
        }

        private static short Saturate(long value) => (short)value == value ? (short)value : (short)((value >> 63) ^ 0x7fff);

        public long Decompress(long input)
        {
            var start = _at - _order;
            long output;
            if (!_wide)
            {
                var dot = Dot(_input.AsSpan(start, _order), _m);
                Adapt(_m, _delta.AsSpan(start, _order), input);
                output = (int)(input + (((long)dot + _round) >> _shift));
                _input[_at] = Saturate((int)output);
                if (_version >= 3980)
                {
                    var abs = Math.Abs((long)(int)output);
                    var o = (int)output;
                    if (abs > _average * 3)
                    {
                        _delta[_at] = (short)(((o >> 25) & 64) - 32);
                    }
                    else if (abs > _average * 4 / 3)
                    {
                        _delta[_at] = (short)(((o >> 26) & 32) - 16);
                    }
                    else if (abs > 0)
                    {
                        _delta[_at] = (short)(((o >> 27) & 16) - 8);
                    }
                    else
                    {
                        _delta[_at] = 0;
                    }

                    _average = (int)(_average + (abs - _average) / 16);
                    _delta[_at - 1] >>= 1;
                    _delta[_at - 2] >>= 1;
                    _delta[_at - 8] >>= 1;
                }
                else
                {
                    _delta[_at] = (short)(output == 0 ? 0 : (((int)output >> 28) & 8) - 4);
                    _delta[_at - 4] >>= 1;
                    _delta[_at - 8] >>= 1;
                }
            }
            else
            {
                // 32-bit files: the same, with the weights in 32 bits.
                var m = _m32!;
                var delta = _delta32!;
                long dot = 0;
                for (var k = 0; k < _order; k++)
                {
                    dot += unchecked(_input[start + k] * m[k]);
                }

                if (input < 0)
                {
                    for (var k = 0; k < _order; k++)
                    {
                        m[k] = unchecked(m[k] + delta[start + k]);
                    }
                }
                else if (input > 0)
                {
                    for (var k = 0; k < _order; k++)
                    {
                        m[k] = unchecked(m[k] - delta[start + k]);
                    }
                }

                output = input + ((dot + _round) >> _shift);
                _input[_at] = Saturate(output);
                if (_version >= 3980)
                {
                    var abs = Math.Abs(output);
                    if (abs > _average * 3)
                    {
                        delta[_at] = (int)(((output >> 25) & 64) - 32);
                    }
                    else if (abs > _average * 4 / 3)
                    {
                        delta[_at] = (int)(((output >> 26) & 32) - 16);
                    }
                    else if (abs > 0)
                    {
                        delta[_at] = (int)(((output >> 27) & 16) - 8);
                    }
                    else
                    {
                        delta[_at] = 0;
                    }

                    _average += (abs - _average) / 16;
                    delta[_at - 1] >>= 1;
                    delta[_at - 2] >>= 1;
                    delta[_at - 8] >>= 1;
                }
                else
                {
                    delta[_at] = output == 0 ? 0 : (int)(((output >> 28) & 8) - 4);
                    delta[_at - 4] >>= 1;
                    delta[_at - 8] >>= 1;
                }
            }

            _at++;
            if (_at == Window + _order)
            {
                // Roll: the last samples move to the front.
                Array.Copy(_input, _at - _order, _input, 0, _order);
                Array.Copy(_delta, _at - _order, _delta, 0, _order);
                if (_delta32 is not null)
                {
                    Array.Copy(_delta32, _at - _order, _delta32, 0, _order);
                }

                _at = _order;
            }

            return output;
        }

        /// <summary>The dot product of 16-bit samples and weights, in 32 bits as the reference's SIMD code works it out.</summary>
        private static int Dot(ReadOnlySpan<short> a, ReadOnlySpan<short> b)
        {
            var sum = Vector<int>.Zero;
            var i = 0;
            var lanes = Vector<short>.Count;
            for (; i + lanes <= a.Length; i += lanes)
            {
                Vector.Widen(new Vector<short>(a.Slice(i)), out var a0, out var a1);
                Vector.Widen(new Vector<short>(b.Slice(i)), out var b0, out var b1);
                sum += a0 * b0 + a1 * b1;
            }

            var total = Vector.Sum(sum);
            for (; i < a.Length; i++)
            {
                total = unchecked(total + a[i] * b[i]);
            }

            return total;
        }

        private static void Adapt(short[] m, ReadOnlySpan<short> delta, long direction)
        {
            if (direction == 0)
            {
                return;
            }

            var weights = m.AsSpan();
            var lanes = Vector<short>.Count;
            var i = 0;
            for (; i + lanes <= weights.Length; i += lanes)
            {
                var w = new Vector<short>(weights.Slice(i));
                var d = new Vector<short>(delta.Slice(i));
                (direction < 0 ? w + d : w - d).CopyTo(weights.Slice(i));
            }

            for (; i < weights.Length; i++)
            {
                weights[i] = (short)(direction < 0 ? weights[i] + delta[i] : weights[i] - delta[i]);
            }
        }
    }

    private static readonly uint[] CrcTable = MakeCrcTable();

    private static uint[] MakeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var crc = i;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }

            table[i] = crc;
        }

        return table;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xffffffffu;
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xff] ^ (crc >> 8);
        }

        return crc;
    }
}
