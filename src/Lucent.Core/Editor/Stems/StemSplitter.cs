using Microsoft.ML.OnnxRuntime;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Editor.Stems;

/// <summary>
/// Splits music into its stems (drums, bass, vocals and everything else) with Demucs' Hybrid Transformer
/// model (HTDemucs, by Meta AI, MIT licence), run by ONNX Runtime on the graphics card where it can.
/// </summary>
/// <remarks>
/// <para>
/// The model works on 7.8-second pieces of 44.1 kHz stereo. Longer audio is cut into pieces that overlap by
/// a quarter and are crossfaded back together, exactly as Demucs' own <c>apply_model</c> does, and other
/// sample rates and mono are converted on the way in and back on the way out.
/// </para>
/// <para>
/// The ONNX model has no STFT in it (ONNX can't hold the complex numbers), so this class does the STFT
/// going in and the inverse STFT coming out, the same sums as HTDemucs' <c>_spec</c> and <c>_ispec</c>.
/// tools/StemModel/export_htdemucs.py makes the model, and checks a NumPy copy of this class against
/// Demucs itself.
/// </para>
/// </remarks>
public sealed class StemSplitter : IDisposable
{
    private const int Nfft = 4096;
    private const int Hop = Nfft / 4;
    private const int Bins = Nfft / 2;
    private const double Overlap = 0.25;

    private static readonly double[] Window = Enumerable.Range(0, Nfft).Select(n => 0.5 - 0.5 * Math.Cos(2 * Math.PI * n / Nfft)).ToArray();

    private readonly string _modelPath;
    private InferenceSession _session;
    private readonly int _segment;
    private readonly int _frames;
    private readonly Fft _fft = new(Nfft);

    private StemSplitter(string modelPath, InferenceSession session, string device)
    {
        _modelPath = modelPath;
        _session = session;
        Device = device;
        var meta = session.ModelMetadata.CustomMetadataMap;
        Sources = meta["lucent.sources"].Split(',');
        SampleRate = int.Parse(meta["lucent.sample_rate"], System.Globalization.CultureInfo.InvariantCulture);
        _segment = int.Parse(meta["lucent.segment_length"], System.Globalization.CultureInfo.InvariantCulture);
        _frames = (_segment + Hop - 1) / Hop;
    }

    /// <summary>The stems' names in the order the model gives them, such as "drums", "bass", "other", "vocals".</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The sample rate the model works at (44.1 kHz).</summary>
    public int SampleRate { get; }

    /// <summary>What it runs on, for the status line: "graphics card" or "processor".</summary>
    public string Device { get; private set; }

    public bool OnGraphicsCard => Device != ProcessorName;

    private const string ProcessorName = "processor";

    /// <summary>Why the graphics card wasn't used, when it was tried and couldn't run the model.</summary>
    public string? GraphicsCardProblem { get; private set; }

    /// <summary>How far the graphics card's stems were from the processor's on a test segment, in dB of its level.</summary>
    public double? GraphicsCardDifference { get; private set; }

    /// <summary>
    /// Loads the model, on the graphics card if one can run it (DirectML on Windows, Core ML on a Mac),
    /// otherwise on the processor. Takes a few seconds.
    /// </summary>
    /// <remarks>
    /// Some graphics drivers run the model without complaint and get the sums wrong, which comes out as loud
    /// noise. So the graphics card splits a test segment first, and is only used if its stems match the
    /// processor's. If they don't, it's tried again with fewer of ONNX Runtime's optimizations (the usual
    /// cause), and then the processor does the work.
    /// </remarks>
    public static StemSplitter Open(string modelPath, bool useGraphicsCard = true)
    {
        string? problem = null;
        double? difference = null;
        StemSplitter? processor = null;
        if (useGraphicsCard)
        {
            foreach (var (name, add) in GraphicsProviders())
            {
                foreach (var level in new[] { GraphOptimizationLevel.ORT_ENABLE_ALL, GraphOptimizationLevel.ORT_ENABLE_BASIC })
                {
                    SessionOptions? options = null;
                    StemSplitter? graphics = null;
                    try
                    {
                        options = BaseOptions(level);
                        add(options);
                        graphics = new StemSplitter(modelPath, new InferenceSession(modelPath, options), name);
                        processor ??= OpenOnProcessor(modelPath);
                        difference = graphics.DifferenceFrom(processor);
                        if (graphics.OnGraphicsCard && difference <= MaximumDifference)
                        {
                            processor.Dispose();
                            graphics.GraphicsCardDifference = difference;
                            return graphics;
                        }

                        problem ??= graphics.GraphicsCardProblem ?? $"its stems came out {difference:0} dB off the processor's";
                    }
                    catch (Exception ex) when (ex is OnnxRuntimeException or EntryPointNotFoundException or DllNotFoundException or NotSupportedException)
                    {
                        // This build of ONNX Runtime or this computer can't do it; try the next, then the processor.
                        problem ??= $"{ex.GetType().Name}: {ex.Message}";
                        if (graphics is null)
                        {
                            // It didn't load at all, so fewer optimizations won't help.
                            break;
                        }
                    }
                    finally
                    {
                        options?.Dispose();
                    }

                    graphics?.Dispose();
                }
            }
        }

        processor ??= OpenOnProcessor(modelPath);
        processor.GraphicsCardProblem = problem;
        processor.GraphicsCardDifference = difference;
        return processor;
    }

    /// <summary>The most the graphics card's stems may differ from the processor's, in dB of the music's level.</summary>
    private const double MaximumDifference = -40;

    private static StemSplitter OpenOnProcessor(string modelPath)
    {
        using var cpu = BaseOptions();
        return new StemSplitter(modelPath, new InferenceSession(modelPath, cpu), ProcessorName);
    }

    /// <summary>Splits one segment of made-up music on both, and gives how far apart the stems are (dB of the music's level).</summary>
    private double DifferenceFrom(StemSplitter other)
    {
        var mix = TestSegment(_segment);
        var mine = RunSegment(mix, CancellationToken.None);
        var theirs = other.RunSegment(mix, CancellationToken.None);
        double error = 0;
        double level = 0;
        long count = 0;
        for (var s = 0; s < mine.Length; s++)
        {
            for (var c = 0; c < 2; c++)
            {
                for (var i = 0; i < _segment; i++)
                {
                    var d = mine[s][c][i] - theirs[s][c][i];
                    error += float.IsFinite(d) ? d * d : double.PositiveInfinity;
                    count++;
                }
            }
        }

        foreach (var value in mix)
        {
            level += value * value;
        }

        var rms = Math.Sqrt(error / Math.Max(1, count));
        return double.IsFinite(rms) ? 20 * Math.Log10(Math.Max(rms, 1e-12) / Math.Sqrt(level / mix.Length)) : double.PositiveInfinity;
    }

    /// <summary>A bass line, a wobbling lead, a kick drum and a little noise, at the level the model takes ([channel][sample], flattened).</summary>
    private static float[] TestSegment(int length)
    {
        var random = new Random(1);
        var result = new float[2 * length];
        for (var i = 0; i < length; i++)
        {
            var t = i / 44100.0;
            var value = 0.9 * Math.Sin(2 * Math.PI * 110 * t) * (Math.Sin(2 * Math.PI * 0.5 * t) > 0 ? 1 : 0)
                        + 0.6 * Math.Sin(2 * Math.PI * 440 * t + 3 * Math.Sin(2 * Math.PI * 5 * t))
                        + 1.5 * Math.Exp(-30 * (t % 0.5)) * Math.Sin(2 * Math.PI * 60 * t);
            result[i] = (float)(value + 0.15 * (random.NextDouble() * 2 - 1));
            result[length + i] = (float)(0.8 * value + 0.15 * (random.NextDouble() * 2 - 1));
        }

        return result;
    }

    private static SessionOptions BaseOptions(GraphOptimizationLevel level = GraphOptimizationLevel.ORT_ENABLE_ALL) => new()
    {
        GraphOptimizationLevel = level,
        // DirectML needs these two; they cost nothing elsewhere for a model of fixed size.
        EnableMemoryPattern = false,
        ExecutionMode = ExecutionMode.ORT_SEQUENTIAL,
    };

    private static IEnumerable<(string Name, Action<SessionOptions> Add)> GraphicsProviders()
    {
        if (OperatingSystem.IsWindows())
        {
            // Any DirectX 12 graphics card: NVIDIA, AMD or Intel.
            yield return ("graphics card", options => options.AppendExecutionProvider_DML(0));
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return ("graphics card", options => options.AppendExecutionProvider("CoreML", new Dictionary<string, string>
            {
                ["ModelFormat"] = "MLProgram",
                ["MLComputeUnits"] = "CPUAndGPU",
            }));
        }
    }

    /// <summary>
    /// Splits the audio into stems, each the same length, sample rate and number of channels as the input.
    /// Reports progress from 0 to 1, and throws <see cref="OperationCanceledException"/> if cancelled.
    /// </summary>
    public StemSet Split(AudioBuffer input, Action<double>? progress = null, CancellationToken cancellation = default)
    {
        // The model wants 44.1 kHz stereo.
        var mix = input.ChannelCount switch
        {
            2 => input,
            1 => new AudioBuffer(new[] { input.Channels[0], input.Channels[0] }, input.SampleRate),
            _ => new AudioBuffer(new[] { input.Channels[0], input.Channels[1] }, input.SampleRate),
        };

        if (mix.SampleRate != SampleRate)
        {
            mix = Resampler.Resample(mix, mix.SampleRate / (double)SampleRate, SampleRate, cancellation: cancellation);
        }

        var stems = Separate(mix.Channels, progress, cancellation);

        var result = new AudioBuffer[stems.Length];
        for (var s = 0; s < stems.Length; s++)
        {
            var stem = new AudioBuffer(stems[s], SampleRate);
            if (input.SampleRate != SampleRate)
            {
                stem = Resampler.Resample(stem, SampleRate / (double)input.SampleRate, input.SampleRate, input.Frames,
                    cancellation: cancellation);
            }

            result[s] = MatchChannels(stem, input.ChannelCount);
        }

        return new StemSet(Sources, result);
    }

    private static AudioBuffer MatchChannels(AudioBuffer stereo, int channels)
    {
        if (channels == 2)
        {
            return stereo;
        }

        var left = stereo.Channels[0];
        var right = stereo.Channels[1];
        var mono = new float[left.Length];
        for (var i = 0; i < mono.Length; i++)
        {
            mono[i] = 0.5f * (left[i] + right[i]);
        }

        // More than two channels: the first two get the split, any others stay silent.
        var result = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            result[c] = channels == 1 ? mono : c < 2 ? stereo.Channels[c] : new float[left.Length];
        }

        return new AudioBuffer(result, stereo.SampleRate);
    }

    /// <summary>Demucs' apply_model with no shifts and a quarter overlap: two channels in, [stem][channel] out.</summary>
    private float[][][] Separate(float[][] mix, Action<double>? progress, CancellationToken cancellation)
    {
        var length = mix[0].Length;

        // Like Demucs' separate.py, the music goes in at a standard level and comes out at its own again.
        double sum = 0;
        for (var i = 0; i < length; i++)
        {
            sum += 0.5 * (mix[0][i] + mix[1][i]);
        }

        var mean = length > 0 ? sum / length : 0;
        double squares = 0;
        for (var i = 0; i < length; i++)
        {
            var d = 0.5 * (mix[0][i] + mix[1][i]) - mean;
            squares += d * d;
        }

        var std = length > 1 ? Math.Sqrt(squares / (length - 1)) : 0;
        if (std < 1e-8)
        {
            // Silence splits into silence.
            return Sources.Select(_ => new[] { new float[length], new float[length] }).ToArray();
        }

        var stride = (int)((1 - Overlap) * _segment);
        var weight = new double[_segment];
        var half = _segment / 2;
        for (var i = 0; i < _segment; i++)
        {
            weight[i] = (i < half ? i + 1 : _segment - i) / (double)half;
        }

        var output = Sources.Select(_ => new[] { new double[length], new double[length] }).ToArray();
        var totals = new double[length];
        var chunks = (length + stride - 1) / stride;
        var padded = new float[2 * _segment];
        for (var n = 0; n < chunks; n++)
        {
            cancellation.ThrowIfCancellationRequested();
            var offset = n * stride;
            var chunk = Math.Min(_segment, length - offset);
            var start = offset - (_segment - chunk) / 2;
            Array.Clear(padded);
            for (var c = 0; c < 2; c++)
            {
                for (var i = Math.Max(0, start); i < Math.Min(length, start + _segment); i++)
                {
                    padded[c * _segment + i - start] = (float)((mix[c][i] - mean) / std);
                }
            }

            var stems = RunSegment(padded, cancellation);
            var trim = (_segment - chunk) / 2;
            for (var s = 0; s < stems.Length; s++)
            {
                for (var c = 0; c < 2; c++)
                {
                    var from = stems[s][c];
                    var to = output[s][c];
                    for (var i = 0; i < chunk; i++)
                    {
                        to[offset + i] += weight[i] * from[trim + i];
                    }
                }
            }

            for (var i = 0; i < chunk; i++)
            {
                totals[offset + i] += weight[i];
            }

            progress?.Invoke((n + 1.0) / chunks);
        }

        return output.Select(stem => stem.Select(channel =>
        {
            var samples = new float[length];
            for (var i = 0; i < length; i++)
            {
                samples[i] = (float)(channel[i] / totals[i] * std + mean);
            }

            return samples;
        }).ToArray()).ToArray();
    }

    /// <summary>
    /// Runs the model. Core ML can load the model and still fail when it runs (seen on Intel Macs), so if
    /// the graphics card fails, the splitter moves to the processor for good and runs it again there.
    /// </summary>
    private IDisposableReadOnlyCollection<OrtValue> Run(RunOptions options, OrtValue mix, OrtValue spec, CancellationToken cancellation)
    {
        string[] inputs = { "mix", "spec" };
        string[] outputs = { "spec_out", "wave_out" };
        try
        {
            return _session.Run(options, inputs, new[] { mix, spec }, outputs);
        }
        catch (OnnxRuntimeException ex) when (OnGraphicsCard && !cancellation.IsCancellationRequested)
        {
            GraphicsCardProblem = $"{ex.GetType().Name}: {ex.Message}";
            using var cpu = BaseOptions();
            var processor = new InferenceSession(_modelPath, cpu);
            _session.Dispose();
            _session = processor;
            Device = ProcessorName;
            return _session.Run(options, inputs, new[] { mix, spec }, outputs);
        }
    }

    /// <summary>Runs the model on one segment of normalized stereo ([channel][sample], flattened): [stem][channel][sample] out.</summary>
    private float[][][] RunSegment(float[] mix, CancellationToken cancellation)
    {
        var spec = new float[4 * Bins * _frames];
        Parallel.For(0, 2, c => Spectrogram(mix.AsSpan(c * _segment, _segment), spec.AsSpan(c * 2 * Bins * _frames, 2 * Bins * _frames)));
        cancellation.ThrowIfCancellationRequested();

        using var mixValue = OrtValue.CreateTensorValueFromMemory(mix, new long[] { 1, 2, _segment });
        using var specValue = OrtValue.CreateTensorValueFromMemory(spec, new long[] { 1, 4, Bins, _frames });
        using var options = new RunOptions();
        using var registration = cancellation.Register(() => options.Terminate = true);
        using var outputs = Run(options, mixValue, specValue, cancellation);
        cancellation.ThrowIfCancellationRequested();

        var specOut = outputs[0].GetTensorDataAsSpan<float>().ToArray();
        var waveOut = outputs[1].GetTensorDataAsSpan<float>();
        var stems = new float[Sources.Count][][];
        for (var s = 0; s < stems.Length; s++)
        {
            stems[s] = new float[2][];
            for (var c = 0; c < 2; c++)
            {
                stems[s][c] = waveOut.Slice((s * 2 + c) * _segment, _segment).ToArray();
            }
        }

        Parallel.For(0, stems.Length * 2, sc =>
        {
            var (s, c) = (sc / 2, sc % 2);
            var bins = new ReadOnlySpan<float>(specOut, (s * 4 + c * 2) * Bins * _frames, 2 * Bins * _frames);
            InverseSpectrogramAdd(bins, stems[s][c]);
        });
        return stems;
    }

    /// <summary>
    /// HTDemucs' _spec for one channel: the real and imaginary parts of its STFT, [2][2048 bins][frames], where
    /// each frame lines up with a hop of the audio (the audio is padded by reflection so the edges have frames too).
    /// </summary>
    private void Spectrogram(ReadOnlySpan<float> samples, Span<float> output)
    {
        // HTDemucs pads by reflection to a whole number of hops plus 1.5 hops each side, then torch.stft
        // pads half a window more each side, also by reflection.
        var length = samples.Length;
        const int pad = Hop / 2 * 3;
        var inner = new float[_frames * Hop + 2 * pad];
        for (var i = 0; i < inner.Length; i++)
        {
            inner[i] = samples[Reflect(i - pad, length)];
        }

        var padded = new float[inner.Length + Nfft];
        for (var i = 0; i < padded.Length; i++)
        {
            padded[i] = inner[Reflect(i - Nfft / 2, inner.Length)];
        }

        var re = new double[Nfft];
        var im = new double[Nfft];
        var scale = 1 / Math.Sqrt(Nfft);
        for (var f = 0; f < _frames; f++)
        {
            // Frames 2 and on: HTDemucs drops the first two and the last two.
            var at = (f + 2) * Hop;
            for (var i = 0; i < Nfft; i++)
            {
                re[i] = padded[at + i] * Window[i];
                im[i] = 0;
            }

            _fft.Transform(re, im, inverse: false);
            for (var k = 0; k < Bins; k++)
            {
                output[k * _frames + f] = (float)(re[k] * scale);
                output[(Bins + k) * _frames + f] = (float)(im[k] * scale);
            }
        }
    }

    /// <summary>HTDemucs' _ispec for one channel: turns [2][2048 bins][frames] back into audio, added to <paramref name="output"/>.</summary>
    private void InverseSpectrogramAdd(ReadOnlySpan<float> bins, float[] output)
    {
        const int pad = Hop / 2 * 3;
        var frames = _frames + 4;
        var total = Nfft + Hop * (frames - 1);
        var sum = new double[total];
        var envelope = new double[total];
        var re = new double[Nfft];
        var im = new double[Nfft];
        var scale = Math.Sqrt(Nfft) / Nfft;
        for (var f = 0; f < frames; f++)
        {
            var at = f * Hop;
            for (var i = 0; i < Nfft; i++)
            {
                envelope[at + i] += Window[i] * Window[i];
            }

            // The two frames padded on at each end are silent.
            var source = f - 2;
            if (source < 0 || source >= _frames)
            {
                continue;
            }

            Array.Clear(re);
            Array.Clear(im);
            for (var k = 0; k < Bins; k++)
            {
                re[k] = bins[k * _frames + source];
                im[k] = bins[(Bins + k) * _frames + source];
            }

            // The other half of the spectrum mirrors this one (the Nyquist bin, 2048, is zero).
            for (var k = 1; k < Bins; k++)
            {
                re[Nfft - k] = re[k];
                im[Nfft - k] = -im[k];
            }

            _fft.Transform(re, im, inverse: true);
            for (var i = 0; i < Nfft; i++)
            {
                sum[at + i] += re[i] * scale * Window[i];
            }
        }

        // torch.istft's centring drops half a window from the start, then _ispec drops its own padding.
        var from = Nfft / 2 + pad;
        for (var i = 0; i < output.Length; i++)
        {
            var j = from + i;
            output[i] += (float)(sum[j] / envelope[j]);
        }
    }

    /// <summary>Reflects an index outside 0..length-1 back inside, without repeating the edge sample (NumPy's and PyTorch's "reflect").</summary>
    private static int Reflect(int i, int length)
    {
        if (length == 1)
        {
            return 0;
        }

        var period = 2 * (length - 1);
        i %= period;
        if (i < 0)
        {
            i += period;
        }

        return i < length ? i : period - i;
    }

    public void Dispose() => _session.Dispose();
}

/// <summary>The stems one piece of audio was split into.</summary>
public sealed class StemSet
{
    public StemSet(IReadOnlyList<string> sources, IReadOnlyList<AudioBuffer> stems)
    {
        Sources = sources;
        Stems = stems;
    }

    /// <summary>The stems' names, as the model calls them ("drums", "bass", "other", "vocals").</summary>
    public IReadOnlyList<string> Sources { get; }

    public IReadOnlyList<AudioBuffer> Stems { get; }

    /// <summary>The stem with this name, or null.</summary>
    public AudioBuffer? this[string source]
    {
        get
        {
            var index = Sources.ToList().IndexOf(source);
            return index >= 0 ? Stems[index] : null;
        }
    }

    /// <summary>The chosen stems added together.</summary>
    public AudioBuffer Mix(IEnumerable<string> sources)
    {
        var first = Stems[0];
        var result = AudioBuffer.Silent(first.ChannelCount, first.Frames, first.SampleRate);
        foreach (var source in sources)
        {
            if (this[source] is not { } stem)
            {
                continue;
            }

            for (var c = 0; c < result.ChannelCount; c++)
            {
                var to = result.Channels[c];
                var from = stem.Channels[c];
                for (var i = 0; i < to.Length; i++)
                {
                    to[i] += from[i];
                }
            }
        }

        return result;
    }
}
