using System.IO;
using System.Security.Cryptography;

namespace WinampInspiredVisualizer.Export;

/// <summary>
/// A lossless FLAC encoder written in C#, so no native library has to ship with the app.
/// It writes standard "subset" FLAC (4096-frame blocks, LPC order up to 12, Rice partition
/// order up to 8) that every FLAC player can decode, with the MD5 signature filled in.
/// </summary>
/// <remarks>
/// Each block is coded with the smallest of: a constant, the fixed polynomial predictors,
/// linear prediction (Levinson-Durbin on a Tukey-windowed block) or plain samples. Stereo
/// blocks also try left/side, right/side and mid/side. Blocks are encoded on several cores
/// in batches and written in order.
/// </remarks>
public sealed class FlacEncoder : IDisposable
{
    private const int BlockSize = 4096;
    private const int BatchBlocks = 32;

    private readonly Stream _output;
    private readonly int _sampleRate;
    private readonly int _channels;
    private readonly int _bits;
    private readonly Settings _settings;
    private readonly IncrementalHash _md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
    private readonly long _streamInfoPosition;
    private readonly int[] _pending;
    private readonly List<int[]> _batch = new();
    private int _pendingFrames;
    private long _totalFrames;
    private long _frameNumber;
    private int _minFrameBytes = int.MaxValue;
    private int _maxFrameBytes;
    private bool _finished;

    public FlacEncoder(Stream output, int sampleRate, int channels, int bitsPerSample, FlacCompression compression)
    {
        if (channels is < 1 or > 8)
        {
            throw new ArgumentOutOfRangeException(nameof(channels), "FLAC holds 1 to 8 channels.");
        }

        if (bitsPerSample is not (16 or 24))
        {
            throw new ArgumentOutOfRangeException(nameof(bitsPerSample), "Only 16- and 24-bit FLAC is written.");
        }

        _output = output;
        _sampleRate = sampleRate;
        _channels = channels;
        _bits = bitsPerSample;
        _settings = compression switch
        {
            FlacCompression.Fast => new Settings(MaxLpcOrder: 0, MaxPartitionOrder: 4, ExhaustiveOrder: false),
            FlacCompression.Normal => new Settings(MaxLpcOrder: 8, MaxPartitionOrder: 6, ExhaustiveOrder: false),
            _ => new Settings(MaxLpcOrder: 12, MaxPartitionOrder: 8, ExhaustiveOrder: true),
        };
        _pending = new int[BlockSize * channels];

        _output.Write("fLaC"u8);
        _streamInfoPosition = _output.Position;
        WriteStreamInfo();
        // Room for tags to be added later without rewriting the whole file.
        WriteMetadataHeader(last: true, type: 1, length: 4096);
        _output.Write(new byte[4096]);
    }

    /// <summary>Adds interleaved samples, already whole numbers in the file's bit depth.</summary>
    public void Write(ReadOnlySpan<int> interleaved)
    {
        if (_finished)
        {
            throw new InvalidOperationException("The FLAC stream is already finished.");
        }

        var frames = interleaved.Length / _channels;
        var offset = 0;
        while (offset < frames)
        {
            var take = Math.Min(BlockSize - _pendingFrames, frames - offset);
            interleaved.Slice(offset * _channels, take * _channels).CopyTo(_pending.AsSpan(_pendingFrames * _channels));
            _pendingFrames += take;
            offset += take;
            if (_pendingFrames == BlockSize)
            {
                QueueBlock(_pending.AsSpan(0, BlockSize * _channels).ToArray());
                _pendingFrames = 0;
            }
        }
    }

    /// <summary>Writes the last partial block and fills in the stream's length and MD5 signature.</summary>
    public void Finish()
    {
        if (_finished)
        {
            return;
        }

        if (_pendingFrames > 0)
        {
            QueueBlock(_pending.AsSpan(0, _pendingFrames * _channels).ToArray());
            _pendingFrames = 0;
        }

        FlushBatch();
        _finished = true;
        var end = _output.Position;
        _output.Position = _streamInfoPosition;
        WriteStreamInfo();
        _output.Position = end;
        _output.Flush();
    }

    public void Dispose() => _md5.Dispose();

    private void QueueBlock(int[] interleaved)
    {
        _batch.Add(interleaved);
        if (_batch.Count == BatchBlocks)
        {
            FlushBatch();
        }
    }

    private void FlushBatch()
    {
        if (_batch.Count == 0)
        {
            return;
        }

        var blocks = _batch.ToArray();
        _batch.Clear();
        var encoded = new byte[blocks.Length][];
        var firstNumber = _frameNumber;
        Parallel.For(0, blocks.Length, i => encoded[i] = EncodeFrame(blocks[i], firstNumber + i));

        var bytesPerSample = _bits / 8;
        foreach (var (block, frame) in blocks.Zip(encoded))
        {
            // The MD5 signature covers the original samples as little-endian bytes.
            var raw = new byte[block.Length * bytesPerSample];
            for (var i = 0; i < block.Length; i++)
            {
                var v = block[i];
                for (var b = 0; b < bytesPerSample; b++)
                {
                    raw[i * bytesPerSample + b] = (byte)(v >> (8 * b));
                }
            }

            _md5.AppendData(raw);
            _output.Write(frame);
            _totalFrames += block.Length / _channels;
            _minFrameBytes = Math.Min(_minFrameBytes, frame.Length);
            _maxFrameBytes = Math.Max(_maxFrameBytes, frame.Length);
        }

        _frameNumber += blocks.Length;
    }

    private void WriteMetadataHeader(bool last, int type, int length)
    {
        _output.WriteByte((byte)((last ? 0x80 : 0) | type));
        _output.WriteByte((byte)(length >> 16));
        _output.WriteByte((byte)(length >> 8));
        _output.WriteByte((byte)length);
    }

    private void WriteStreamInfo()
    {
        WriteMetadataHeader(last: false, type: 0, length: 34);
        var bits = new BitWriter(34);
        bits.Write(BlockSize, 16);
        bits.Write(BlockSize, 16);
        bits.Write(_maxFrameBytes == 0 ? 0 : _minFrameBytes, 24);
        bits.Write(_maxFrameBytes, 24);
        bits.Write(_sampleRate, 20);
        bits.Write(_channels - 1, 3);
        bits.Write(_bits - 1, 5);
        bits.Write(_totalFrames >> 32, 4);
        bits.Write(_totalFrames & 0xFFFFFFFF, 32);
        var md5 = _finished ? _md5.GetCurrentHash() : new byte[16];
        foreach (var b in md5)
        {
            bits.Write(b, 8);
        }

        _output.Write(bits.ToArray());
    }

    // ---- Frames ----

    private byte[] EncodeFrame(int[] interleaved, long frameNumber)
    {
        var frames = interleaved.Length / _channels;
        var channelData = new long[_channels][];
        for (var c = 0; c < _channels; c++)
        {
            channelData[c] = new long[frames];
            for (var i = 0; i < frames; i++)
            {
                channelData[c][i] = interleaved[i * _channels + c];
            }
        }

        int assignment;
        Subframe[] subframes;
        if (_channels == 2)
        {
            var left = channelData[0];
            var right = channelData[1];
            var mid = new long[frames];
            var side = new long[frames];
            for (var i = 0; i < frames; i++)
            {
                mid[i] = (left[i] + right[i]) >> 1;
                side[i] = left[i] - right[i];
            }

            // Plan all four signals, then keep the cheapest pair.
            var candidates = new[]
            {
                PlanSubframe(left, _bits), PlanSubframe(right, _bits),
                PlanSubframe(mid, _bits), PlanSubframe(side, _bits + 1),
            };
            var options = new (int Assignment, int A, int B)[]
            {
                (1, 0, 1), // independent
                (8, 0, 3), // left/side
                (9, 3, 1), // side/right
                (10, 2, 3), // mid/side
            };
            var best = options.MinBy(o => candidates[o.A].Bits + candidates[o.B].Bits);
            assignment = best.Assignment;
            subframes = new[] { candidates[best.A], candidates[best.B] };
        }
        else
        {
            assignment = _channels - 1;
            subframes = channelData.Select(data => PlanSubframe(data, _bits)).ToArray();
        }

        var frame = new BitWriter(frames * _channels * 3 + 64);
        frame.Write(0b11111111111110, 14);
        frame.Write(0, 1); // reserved
        frame.Write(0, 1); // fixed block size
        var sizeCode = frames == BlockSize ? 12 : 7;
        frame.Write(sizeCode, 4);
        frame.Write(SampleRateCode(_sampleRate), 4);
        frame.Write(assignment, 4);
        frame.Write(_bits == 16 ? 0b100 : 0b110, 3);
        frame.Write(0, 1); // reserved
        WriteUtf8Number(frame, frameNumber);
        if (sizeCode == 7)
        {
            frame.Write(frames - 1, 16);
        }

        frame.Write(Crc8(frame.ToArray()), 8);
        foreach (var subframe in subframes)
        {
            WriteSubframe(frame, subframe);
        }

        frame.AlignToByte();
        var bytes = frame.ToArray();
        var crc = Crc16(bytes);
        var result = new byte[bytes.Length + 2];
        bytes.CopyTo(result, 0);
        result[^2] = (byte)(crc >> 8);
        result[^1] = (byte)crc;
        return result;
    }

    private static int SampleRateCode(int rate) => rate switch
    {
        88200 => 1,
        176400 => 2,
        192000 => 3,
        8000 => 4,
        16000 => 5,
        22050 => 6,
        24000 => 7,
        32000 => 8,
        44100 => 9,
        48000 => 10,
        96000 => 11,
        _ => 0, // taken from STREAMINFO
    };

    private static void WriteUtf8Number(BitWriter writer, long value)
    {
        if (value < 0x80)
        {
            writer.Write(value, 8);
            return;
        }

        // Like UTF-8: a lead byte saying how many continuation bytes follow, each carrying 6 bits.
        var continuation = value < 0x800 ? 1 : value < 0x10000 ? 2 : value < 0x200000 ? 3 : value < 0x4000000 ? 4 : 5;
        var leadBits = 6 - continuation;
        var lead = (0xFF << (7 - continuation)) & 0xFF;
        writer.Write(lead | ((int)(value >> (6 * continuation)) & ((1 << leadBits) - 1)), 8);
        for (var i = continuation - 1; i >= 0; i--)
        {
            writer.Write(0x80 | (int)((value >> (6 * i)) & 0x3F), 8);
        }
    }

    // ---- Subframes ----

    private enum SubframeKind
    {
        Constant,
        Verbatim,
        Fixed,
        Lpc,
    }

    /// <summary>How one channel of one block will be coded, and roughly how many bits that takes.</summary>
    private sealed record Subframe(SubframeKind Kind, long[] Samples, int SampleBits, long Bits)
    {
        public int Order { get; init; }
        public int Precision { get; init; }
        public int Shift { get; init; }
        public int[] Coefficients { get; init; } = Array.Empty<int>();
        public ResidualPlan? Residual { get; init; }
    }

    private sealed record ResidualPlan(ulong[] Folded, int PartitionOrder, int[] Parameters, long Bits);

    private Subframe PlanSubframe(long[] samples, int bits)
    {
        var n = samples.Length;
        if (samples.All(s => s == samples[0]))
        {
            return new Subframe(SubframeKind.Constant, samples, bits, 8 + bits);
        }

        var best = new Subframe(SubframeKind.Verbatim, samples, bits, 8 + (long)n * bits);

        // Fixed predictors: pick the order with the smallest total residual.
        var fixedOrder = 0;
        var fixedResidual = FixedResidual(samples, 0);
        var fixedCost = AbsSum(fixedResidual, 0);
        for (var order = 1; order <= Math.Min(4, n - 1); order++)
        {
            var residual = FixedResidual(samples, order);
            var cost = AbsSum(residual, order);
            if (cost < fixedCost)
            {
                fixedOrder = order;
                fixedResidual = residual;
                fixedCost = cost;
            }
        }

        if (Fits(fixedResidual, fixedOrder))
        {
            var plan = PlanResidual(fixedResidual, fixedOrder, n);
            var total = 8 + (long)fixedOrder * bits + plan.Bits;
            if (total < best.Bits)
            {
                best = new Subframe(SubframeKind.Fixed, samples, bits, total) { Order = fixedOrder, Residual = plan };
            }
        }

        if (_settings.MaxLpcOrder > 0 && n > 32)
        {
            foreach (var (order, precision, shift, coefficients) in LpcCandidates(samples, bits))
            {
                var residual = LpcResidual(samples, coefficients, shift, order);
                if (!Fits(residual, order))
                {
                    continue;
                }

                var plan = PlanResidual(residual, order, n);
                var total = 8 + (long)order * bits + 4 + 5 + (long)order * precision + plan.Bits;
                if (total < best.Bits)
                {
                    best = new Subframe(SubframeKind.Lpc, samples, bits, total)
                    {
                        Order = order,
                        Precision = precision,
                        Shift = shift,
                        Coefficients = coefficients,
                        Residual = plan,
                    };
                }
            }
        }

        return best;
    }

    private static void WriteSubframe(BitWriter writer, Subframe subframe)
    {
        var samples = subframe.Samples;
        var bits = subframe.SampleBits;
        switch (subframe.Kind)
        {
            case SubframeKind.Constant:
                writer.Write(0, 8); // padding bit, type 000000, no wasted bits
                writer.WriteSigned(samples[0], bits);
                return;
            case SubframeKind.Verbatim:
                writer.Write(0b00000010, 8); // padding bit, type 000001, no wasted bits
                foreach (var s in samples)
                {
                    writer.WriteSigned(s, bits);
                }

                return;
            case SubframeKind.Fixed:
                writer.Write(0b001000 | subframe.Order, 8 - 1);
                writer.Write(0, 1);
                break;
            default:
                writer.Write(0b100000 | (subframe.Order - 1), 8 - 1);
                writer.Write(0, 1);
                break;
        }

        for (var i = 0; i < subframe.Order; i++)
        {
            writer.WriteSigned(samples[i], bits);
        }

        if (subframe.Kind == SubframeKind.Lpc)
        {
            writer.Write(subframe.Precision - 1, 4);
            writer.WriteSigned(subframe.Shift, 5);
            foreach (var q in subframe.Coefficients)
            {
                writer.WriteSigned(q, subframe.Precision);
            }
        }

        WriteResidual(writer, subframe.Residual!, subframe.Order, samples.Length);
    }

    /// <summary>Residuals must fit in a signed 32-bit number for decoders.</summary>
    private static bool Fits(long[] residual, int order)
    {
        for (var i = order; i < residual.Length; i++)
        {
            if (residual[i] is > int.MaxValue or <= int.MinValue)
            {
                return false;
            }
        }

        return true;
    }

    private static long AbsSum(long[] values, int from)
    {
        long sum = 0;
        for (var i = from; i < values.Length; i++)
        {
            sum += Math.Abs(values[i]);
        }

        return sum;
    }

    private static long[] FixedResidual(long[] x, int order)
    {
        var r = new long[x.Length];
        for (var i = order; i < x.Length; i++)
        {
            r[i] = order switch
            {
                0 => x[i],
                1 => x[i] - x[i - 1],
                2 => x[i] - 2 * x[i - 1] + x[i - 2],
                3 => x[i] - 3 * x[i - 1] + 3 * x[i - 2] - x[i - 3],
                _ => x[i] - 4 * x[i - 1] + 6 * x[i - 2] - 4 * x[i - 3] + x[i - 4],
            };
        }

        return r;
    }

    /// <summary>Linear-prediction orders worth trying, with their quantized coefficients.</summary>
    private IEnumerable<(int Order, int Precision, int Shift, int[] Coefficients)> LpcCandidates(long[] samples, int bits)
    {
        var n = samples.Length;
        var maxOrder = Math.Min(_settings.MaxLpcOrder, n - 1);

        // Tukey(0.5) window, then autocorrelation.
        var windowed = new double[n];
        var taper = n / 4;
        for (var i = 0; i < n; i++)
        {
            var w = 1.0;
            if (i < taper)
            {
                w = 0.5 - 0.5 * Math.Cos(Math.PI * i / taper);
            }
            else if (i >= n - taper)
            {
                w = 0.5 - 0.5 * Math.Cos(Math.PI * (n - 1 - i) / taper);
            }

            windowed[i] = samples[i] * w;
        }

        var autocorrelation = new double[maxOrder + 1];
        for (var lag = 0; lag <= maxOrder; lag++)
        {
            double sum = 0;
            for (var i = lag; i < n; i++)
            {
                sum += windowed[i] * windowed[i - lag];
            }

            autocorrelation[lag] = sum;
        }

        if (autocorrelation[0] <= 0)
        {
            yield break;
        }

        // Levinson-Durbin: predictor coefficients for every order, and each order's prediction error.
        var coefficients = new double[maxOrder + 1][];
        var errors = new double[maxOrder + 1];
        var current = new double[maxOrder + 1];
        var error = autocorrelation[0] * (1 + 1e-9);
        var reached = 0;
        for (var order = 1; order <= maxOrder; order++)
        {
            var acc = autocorrelation[order];
            for (var j = 1; j < order; j++)
            {
                acc -= current[j] * autocorrelation[order - j];
            }

            var reflection = acc / error;
            var next = (double[])current.Clone();
            next[order] = reflection;
            for (var j = 1; j < order; j++)
            {
                next[j] = current[j] - reflection * current[order - j];
            }

            current = next;
            error *= 1 - reflection * reflection;
            coefficients[order] = current[1..(order + 1)];
            errors[order] = Math.Max(error, 1e-12);
            reached = order;
            if (error <= 0)
            {
                break;
            }
        }

        // Coefficient precision: 12 bits like libFLAC uses for this block size, a little more for 24-bit.
        var precision = bits > 17 ? 14 : 12;

        IEnumerable<int> orders;
        if (_settings.ExhaustiveOrder)
        {
            orders = Enumerable.Range(1, reached);
        }
        else
        {
            // Estimate each order's size from its prediction error, plus the cost of its coefficients.
            var bestOrder = 1;
            var bestEstimate = double.MaxValue;
            for (var order = 1; order <= reached; order++)
            {
                var perSample = Math.Max(0, 0.5 * Math.Log2(errors[order] / n) + 1);
                var estimate = perSample * (n - order) + order * (precision + bits);
                if (estimate < bestEstimate)
                {
                    bestEstimate = estimate;
                    bestOrder = order;
                }
            }

            orders = reached > 0 ? new[] { bestOrder } : Array.Empty<int>();
        }

        foreach (var order in orders)
        {
            if (Quantize(coefficients[order], precision, out var shift) is { } quantized)
            {
                yield return (order, precision, shift, quantized);
            }
        }
    }

    /// <summary>Rounds coefficients to <paramref name="precision"/>-bit integers with a shared shift, carrying the rounding error forward.</summary>
    private static int[]? Quantize(double[] coefficients, int precision, out int shift)
    {
        var max = coefficients.Max(Math.Abs);
        shift = 0;
        if (max <= 0 || double.IsNaN(max) || double.IsInfinity(max))
        {
            return null;
        }

        var exponent = (int)Math.Floor(Math.Log2(max)) + 1;
        shift = Math.Clamp(precision - 1 - exponent, 0, 15);
        var limit = (1 << (precision - 1)) - 1;
        var result = new int[coefficients.Length];
        var carried = 0.0;
        for (var i = 0; i < coefficients.Length; i++)
        {
            carried += coefficients[i] * (1 << shift);
            var q = (int)Math.Clamp(Math.Round(carried), -limit - 1, limit);
            result[i] = q;
            carried -= q;
        }

        return result;
    }

    private static long[] LpcResidual(long[] x, int[] q, int shift, int order)
    {
        var r = new long[x.Length];
        for (var i = order; i < x.Length; i++)
        {
            long sum = 0;
            for (var j = 0; j < order; j++)
            {
                sum += q[j] * x[i - j - 1];
            }

            r[i] = x[i] - (sum >> shift);
        }

        return r;
    }

    /// <summary>
    /// Chooses how to split the residual into partitions and each partition's Rice parameter, from
    /// sums worked out once at the finest split (the estimate libFLAC also uses).
    /// </summary>
    private ResidualPlan PlanResidual(long[] residual, int predictorOrder, int n)
    {
        var folded = new ulong[n];
        for (var i = predictorOrder; i < n; i++)
        {
            var r = residual[i];
            folded[i] = (ulong)((r << 1) ^ (r >> 63));
        }

        var maxOrder = 0;
        while (maxOrder < _settings.MaxPartitionOrder && n % (1 << (maxOrder + 1)) == 0 && (n >> (maxOrder + 1)) > predictorOrder)
        {
            maxOrder++;
        }

        var finest = 1 << maxOrder;
        var sums = new ulong[finest];
        var size = n >> maxOrder;
        for (var p = 0; p < finest; p++)
        {
            ulong sum = 0;
            for (var i = Math.Max(p * size, predictorOrder); i < (p + 1) * size; i++)
            {
                sum += folded[i];
            }

            sums[p] = sum;
        }

        ResidualPlan? best = null;
        for (var order = maxOrder; order >= 0; order--)
        {
            var partitions = 1 << order;
            var parameters = new int[partitions];
            long bits = 2 + 4;
            for (var p = 0; p < partitions; p++)
            {
                var count = (n >> order) - (p == 0 ? predictorOrder : 0);
                var (k, cost) = BestParameter(sums[p], count);
                parameters[p] = k;
                bits += cost;
            }

            var wide = parameters.Any(k => k > 14);
            bits += partitions * (wide ? 5 : 4);
            if (best is null || bits < best.Bits)
            {
                best = new ResidualPlan(folded, order, parameters, bits);
            }

            // Merge neighbouring partitions' sums for the next coarser split.
            if (order > 0)
            {
                for (var p = 0; p < partitions / 2; p++)
                {
                    sums[p] = sums[2 * p] + sums[2 * p + 1];
                }
            }
        }

        return best!;
    }

    private static (int K, long Bits) BestParameter(ulong sum, int count)
    {
        if (count <= 0)
        {
            return (0, 0);
        }

        var bestK = 0;
        var bestBits = long.MaxValue;
        var mean = (double)sum / count;
        var guess = mean < 1 ? 0 : (int)Math.Log2(mean);
        for (var k = Math.Max(0, guess - 1); k <= Math.Min(30, guess + 1); k++)
        {
            // Unary quotients add up to about sum / 2^k, plus the stop bit and k low bits per value.
            var bits = (long)count * (k + 1) + (long)(sum >> k);
            if (bits < bestBits)
            {
                bestBits = bits;
                bestK = k;
            }
        }

        return (bestK, bestBits);
    }

    private static void WriteResidual(BitWriter writer, ResidualPlan plan, int predictorOrder, int n)
    {
        var wide = plan.Parameters.Any(k => k > 14);
        writer.Write(wide ? 1 : 0, 2);
        writer.Write(plan.PartitionOrder, 4);
        var size = n >> plan.PartitionOrder;
        for (var p = 0; p < plan.Parameters.Length; p++)
        {
            var k = plan.Parameters[p];
            writer.Write(k, wide ? 5 : 4);
            var from = p == 0 ? predictorOrder : p * size;
            var to = (p + 1) * size;
            for (var i = from; i < to; i++)
            {
                writer.WriteRice(plan.Folded[i], k);
            }
        }
    }

    // ---- Checksums ----

    private static byte Crc8(byte[] data)
    {
        var crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1;
            }

            crc &= 0xFF;
        }

        return (byte)crc;
    }

    private static ushort Crc16(byte[] data)
    {
        var crc = 0;
        foreach (var b in data)
        {
            crc ^= b << 8;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1;
            }

            crc &= 0xFFFF;
        }

        return (ushort)crc;
    }

    private sealed record Settings(int MaxLpcOrder, int MaxPartitionOrder, bool ExhaustiveOrder);

    /// <summary>Writes bits most significant first, through a 64-bit accumulator.</summary>
    private sealed class BitWriter
    {
        private byte[] _buffer;
        private int _bytes;
        private ulong _accumulator;
        private int _pendingBits;

        public BitWriter(int capacityBytes) => _buffer = new byte[Math.Max(16, capacityBytes)];

        /// <summary>Writes the low <paramref name="bits"/> bits of <paramref name="value"/> (up to 32 at a time).</summary>
        public void Write(long value, int bits)
        {
            if (bits > 32)
            {
                Write(value >> 32, bits - 32);
                bits = 32;
            }

            if (bits == 0)
            {
                return;
            }

            _accumulator = (_accumulator << bits) | ((ulong)value & ((1UL << bits) - 1));
            _pendingBits += bits;
            while (_pendingBits >= 8)
            {
                _pendingBits -= 8;
                Emit((byte)(_accumulator >> _pendingBits));
            }
        }

        public void WriteSigned(long value, int bits) => Write(value & ((1L << bits) - 1), bits);

        /// <summary>A Rice code: the quotient in unary (zeros ended by a one), then the low k bits.</summary>
        public void WriteRice(ulong value, int k)
        {
            var quotient = value >> k;
            while (quotient >= 32)
            {
                Write(0, 32);
                quotient -= 32;
            }

            Write(1, (int)quotient + 1);
            if (k > 0)
            {
                Write((long)(value & ((1UL << k) - 1)), k);
            }
        }

        public void AlignToByte()
        {
            if (_pendingBits > 0)
            {
                Write(0, 8 - _pendingBits);
            }
        }

        /// <summary>Everything written so far, with any final partial byte padded with zeros.</summary>
        public byte[] ToArray()
        {
            var result = new byte[_bytes + (_pendingBits > 0 ? 1 : 0)];
            Array.Copy(_buffer, result, _bytes);
            if (_pendingBits > 0)
            {
                result[^1] = (byte)(_accumulator << (8 - _pendingBits));
            }

            return result;
        }

        private void Emit(byte value)
        {
            if (_bytes == _buffer.Length)
            {
                Array.Resize(ref _buffer, _buffer.Length * 2);
            }

            _buffer[_bytes++] = value;
        }
    }
}
