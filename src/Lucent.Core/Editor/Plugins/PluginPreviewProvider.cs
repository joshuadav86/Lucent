using NAudio.Wave;

namespace Lucent.Editor.Plugins;

/// <summary>One plugin a preview plays through.</summary>
/// <param name="Generation">From the <see cref="PluginHost.Start"/> call that set up this preview.</param>
/// <param name="IsBypassed">Asked for every block, so switching a plugin off while it plays is heard at once.</param>
public sealed record PreviewStage(PluginHost Host, int Generation, Func<bool>? IsBypassed = null);

/// <summary>
/// Plays audio through a plugin, or a chain of them, as it's heard, so turning a knob in a plugin's window
/// changes the sound straight away. After the audio ends it keeps going for a few seconds, so a reverb's
/// tail can be heard.
/// </summary>
public sealed class PluginPreviewProvider : ISampleProvider
{
    private static readonly TimeSpan TailTime = TimeSpan.FromSeconds(3);

    private readonly ISampleProvider _source;
    private readonly IReadOnlyList<PreviewStage> _stages;
    private readonly float _volume;
    private readonly float[][] _planes;
    private int _tailLeft;
    private bool _sourceEnded;

    /// <param name="source">The dry audio, in the plugin's format.</param>
    /// <param name="generation">From the <see cref="PluginHost.Start"/> call that set up this preview.</param>
    /// <param name="volume">Applied after the plugin, so it doesn't change what the plugin hears.</param>
    public PluginPreviewProvider(ISampleProvider source, PluginHost host, int generation, float volume)
        : this(source, new[] { new PreviewStage(host, generation) }, volume)
    {
    }

    /// <param name="stages">The plugins in the order the audio goes through them.</param>
    public PluginPreviewProvider(ISampleProvider source, IReadOnlyList<PreviewStage> stages, float volume)
    {
        _source = source;
        _stages = stages;
        _volume = volume;
        WaveFormat = source.WaveFormat;
        _planes = new float[WaveFormat.Channels][];
        _tailLeft = (int)(TailTime.TotalSeconds * WaveFormat.SampleRate);
    }

    public WaveFormat WaveFormat { get; }

    /// <summary>Why the preview stopped early, if the plugin failed.</summary>
    public Exception? Error { get; private set; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _planes.Length;
        var frames = count / channels;
        var read = _sourceEnded ? 0 : _source.Read(buffer, offset, frames * channels) / channels;
        if (read < frames)
        {
            _sourceEnded = true;
            var extra = Math.Min(frames - read, _tailLeft);
            Array.Clear(buffer, offset + read * channels, extra * channels);
            _tailLeft -= extra;
            read += extra;
        }

        if (read == 0 || Error is not null)
        {
            return 0;
        }

        for (var c = 0; c < channels; c++)
        {
            if (_planes[c] is null || _planes[c].Length < read)
            {
                _planes[c] = new float[read];
            }

            for (var i = 0; i < read; i++)
            {
                _planes[c][i] = buffer[offset + i * channels + c];
            }
        }

        try
        {
            foreach (var stage in _stages)
            {
                if (stage.IsBypassed?.Invoke() == true)
                {
                    continue;
                }

                // An old preview still finishing after the plugin was restarted for something else is silenced.
                if (!stage.Host.Process(_planes, 0, read, stage.Generation))
                {
                    return 0;
                }
            }
        }
        catch (Exception ex)
        {
            Error = ex;
            return 0;
        }

        for (var c = 0; c < channels; c++)
        {
            for (var i = 0; i < read; i++)
            {
                buffer[offset + i * channels + c] = _planes[c][i] * _volume;
            }
        }

        return read * channels;
    }
}
