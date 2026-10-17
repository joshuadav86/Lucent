namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// The lowest and highest sample in every block of audio, at several block sizes, so the
/// waveform can be drawn at any zoom by looking at a handful of numbers per pixel instead
/// of every sample. Built once per edit; read-only afterwards, so it is safe to share between threads.
/// </summary>
public sealed class PeakCache
{
    /// <summary>Frames per block at the finest level; each further level groups four blocks.</summary>
    public const int BaseBlock = 16;
    private const int Fan = 4;

    // [channel][level] -> interleaved min/max pairs.
    private readonly float[][][] _levels;

    private PeakCache(AudioBuffer audio, float[][][] levels)
    {
        Audio = audio;
        _levels = levels;
    }

    /// <summary>The audio these peaks describe.</summary>
    public AudioBuffer Audio { get; }

    public static PeakCache Build(AudioBuffer audio)
    {
        var levels = new float[audio.ChannelCount][][];
        Parallel.For(0, audio.ChannelCount, c =>
        {
            var samples = audio.Channels[c];
            var list = new List<float[]>();
            var blocks = (samples.Length + BaseBlock - 1) / BaseBlock;
            var level = new float[Math.Max(1, blocks) * 2];
            for (var b = 0; b < blocks; b++)
            {
                var start = b * BaseBlock;
                var end = Math.Min(samples.Length, start + BaseBlock);
                float min = samples[start], max = min;
                for (var i = start + 1; i < end; i++)
                {
                    var v = samples[i];
                    if (v < min)
                    {
                        min = v;
                    }
                    else if (v > max)
                    {
                        max = v;
                    }
                }

                level[2 * b] = min;
                level[2 * b + 1] = max;
            }

            list.Add(level);
            while (level.Length / 2 > 1)
            {
                var count = (level.Length / 2 + Fan - 1) / Fan;
                var next = new float[count * 2];
                for (var b = 0; b < count; b++)
                {
                    float min = float.MaxValue, max = float.MinValue;
                    var end = Math.Min(level.Length / 2, (b + 1) * Fan);
                    for (var i = b * Fan; i < end; i++)
                    {
                        min = Math.Min(min, level[2 * i]);
                        max = Math.Max(max, level[2 * i + 1]);
                    }

                    next[2 * b] = min;
                    next[2 * b + 1] = max;
                }

                list.Add(next);
                level = next;
            }

            levels[c] = list.ToArray();
        });

        return new PeakCache(audio, levels);
    }

    /// <summary>The lowest and highest sample of one channel between two frame positions.</summary>
    public (float Min, float Max) Range(int channel, double fromFrame, double toFrame)
    {
        var samples = Audio.Channels[channel];
        var from = (long)Math.Floor(Math.Max(0, fromFrame));
        var to = (long)Math.Ceiling(Math.Min(samples.Length, toFrame));
        if (to <= from)
        {
            if (from >= samples.Length || samples.Length == 0)
            {
                return (0, 0);
            }

            to = from + 1;
        }

        var span = to - from;
        if (span < BaseBlock * 2)
        {
            float min = samples[from], max = min;
            for (var i = from + 1; i < to; i++)
            {
                var v = samples[i];
                min = Math.Min(min, v);
                max = Math.Max(max, v);
            }

            return (min, max);
        }

        // The coarsest level whose blocks still fit in the span, so a pixel reads only a few blocks.
        var levels = _levels[channel];
        var levelIndex = 0;
        long block = BaseBlock;
        while (levelIndex + 1 < levels.Length && block * Fan <= span / 2)
        {
            block *= Fan;
            levelIndex++;
        }

        var level = levels[levelIndex];
        var first = from / block;
        var last = Math.Min(level.Length / 2 - 1, (to - 1) / block);
        float lo = float.MaxValue, hi = float.MinValue;
        for (var b = first; b <= last; b++)
        {
            lo = Math.Min(lo, level[2 * b]);
            hi = Math.Max(hi, level[2 * b + 1]);
        }

        return (lo, hi);
    }
}
