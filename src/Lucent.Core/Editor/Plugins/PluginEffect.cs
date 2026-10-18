using WinampInspiredVisualizer.Editor.Effects;

namespace WinampInspiredVisualizer.Editor.Plugins;

/// <summary>
/// Runs the selection through a VST3 effect, or a chain of them one after another, with the settings they have now. Reverbs and delays ring on
/// after the selection ends: that tail is mixed over the audio that follows (which is why the input
/// reaches up to <see cref="MaxTail"/> past the selection), or, at the end of the song, makes it longer.
/// </summary>
public sealed class PluginEffect : IAudioEffect
{
    /// <summary>The longest tail kept after the selection.</summary>
    public static readonly TimeSpan MaxTail = TimeSpan.FromSeconds(30);

    /// <summary>Quieter than this (−80 dB) counts as silence when deciding where a tail ends.</summary>
    private const float Quiet = 1e-4f;

    private const int BlockFrames = 16384;

    private readonly IReadOnlyList<PluginHost> _hosts;
    private readonly int _selectionFrames;

    /// <param name="host">The effect, already set up the way the user wants.</param>
    /// <param name="selectionFrames">
    /// How much of the input is the selection; the rest is the audio after it, for the tail to mix into.
    /// </param>
    public PluginEffect(PluginHost host, int selectionFrames)
        : this(new[] { host }, selectionFrames)
    {
    }

    /// <param name="hosts">The effects in the order the audio goes through them (at least one).</param>
    public PluginEffect(IReadOnlyList<PluginHost> hosts, int selectionFrames)
    {
        if (hosts.Count == 0)
        {
            throw new ArgumentException("There are no plugins to run.", nameof(hosts));
        }

        _hosts = hosts;
        _selectionFrames = selectionFrames;
    }

    public string Description => _hosts.Count == 1 ? _hosts[0].Name : $"Plugin chain ({_hosts.Count} plugins)";

    public string? Details => _hosts.Count == 1 ? null : string.Join(" → ", _hosts.Select(h => h.Name));

    public AudioBuffer Process(AudioBuffer input, EffectContext context)
    {
        var rate = input.SampleRate;
        var channels = input.ChannelCount;
        var selection = Math.Min(_selectionFrames, input.Frames);
        // In a chain the delays add up, and so do the tails: a reverb's tail rings on through what comes after it.
        var latency = 0;
        long reportedTail = 0;
        foreach (var host in _hosts)
        {
            var (delay, tail, _) = host.Start(offline: true);
            latency += Math.Max(0, delay);
            reportedTail = reportedTail < 0 || tail < 0 ? -1 : reportedTail + tail;
        }

        // How long to let it ring on. A plugin that says how long its tail is gets exactly that (up to
        // the limit); one that says it has none, or an endless one, is listened to until it falls silent.
        var maxTail = (int)(MaxTail.TotalSeconds * rate);
        var tailLimit = reportedTail switch
        {
            < 0 => maxTail,
            0 => Math.Min(maxTail, 10 * rate),
            _ => (int)Math.Min(maxTail, reportedTail),
        };
        var listenForSilence = reportedTail <= 0;
        var silenceNeeded = rate;

        var wet = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            wet[c] = new float[selection + tailLimit];
        }

        var block = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            block[c] = new float[BlockFrames];
        }

        // Frames fed in so far; the output runs `latency` frames behind, and those first frames are dropped.
        long fed = 0;
        var produced = 0;
        var quietRun = 0;
        var expected = (double)selection + (listenForSilence ? silenceNeeded : tailLimit) + latency;
        while (true)
        {
            var tail = produced - selection;
            if (tail >= tailLimit || (listenForSilence && tail >= 0 && quietRun >= silenceNeeded))
            {
                break;
            }

            for (var c = 0; c < channels; c++)
            {
                var source = input.Channels[c];
                for (var i = 0; i < BlockFrames; i++)
                {
                    var at = fed + i;
                    block[c][i] = at < selection ? source[at] : 0f;
                }
            }

            foreach (var host in _hosts)
            {
                host.Process(block, 0, BlockFrames);
            }

            for (var i = 0; i < BlockFrames; i++)
            {
                var at = fed + i - latency;
                if (at < 0 || at >= wet[0].Length)
                {
                    continue;
                }

                var loudest = 0f;
                for (var c = 0; c < channels; c++)
                {
                    var value = block[c][i];
                    wet[c][at] = value;
                    loudest = Math.Max(loudest, Math.Abs(value));
                }

                if (at >= selection)
                {
                    quietRun = loudest < Quiet ? quietRun + 1 : 0;
                }
            }

            fed += BlockFrames;
            produced = (int)Math.Clamp(fed - latency, 0, wet[0].Length);
            context.Report(Math.Min(0.99, fed / expected));
        }

        // The tail ends where it last made a sound.
        var tailFrames = Math.Clamp(produced - selection, 0, tailLimit);
        while (tailFrames > 0 && Loudest(wet, selection + tailFrames - 1) < Quiet)
        {
            tailFrames--;
        }

        // The selection becomes the processed audio; the tail is added on top of what follows it.
        var length = Math.Max(input.Frames, selection + tailFrames);
        var output = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            var result = length == input.Frames ? input.Channels[c] : new float[length];
            if (!ReferenceEquals(result, input.Channels[c]))
            {
                Array.Copy(input.Channels[c], result, input.Frames);
            }

            Array.Copy(wet[c], result, selection);
            for (var i = selection; i < selection + tailFrames; i++)
            {
                result[i] += wet[c][i];
            }

            output[c] = result;
        }

        // A few frames of ringing past the end isn't worth mentioning.
        if (length - input.Frames >= rate / 10)
        {
            context.Note = $"{Description}: done. Its tail made the sound {(length - input.Frames) / (double)rate:0.0} seconds longer. Ctrl+Z undoes it.";
        }

        context.Report(1);
        return new AudioBuffer(output, rate);
    }

    private static float Loudest(float[][] audio, int frame)
    {
        var loudest = 0f;
        foreach (var channel in audio)
        {
            loudest = Math.Max(loudest, Math.Abs(channel[frame]));
        }

        return loudest;
    }
}
