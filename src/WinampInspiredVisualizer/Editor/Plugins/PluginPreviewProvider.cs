using NAudio.Wave;

namespace WinampInspiredVisualizer.Editor.Plugins;

/// <summary>
/// Plays audio through a plugin as it's heard, so turning a knob in the plugin's window changes the sound
/// straight away. After the audio ends it keeps going for a few seconds, so a reverb's tail can be heard.
/// </summary>
public sealed class PluginPreviewProvider : ISampleProvider
{
    private static readonly TimeSpan TailTime = TimeSpan.FromSeconds(3);

    private readonly ISampleProvider _source;
    private readonly PluginHost _host;
    private readonly int _generation;
    private readonly float _volume;
    private readonly float[][] _planes;
    private int _tailLeft;
    private bool _sourceEnded;

    /// <param name="source">The dry audio, in the plugin's format.</param>
    /// <param name="generation">From the <see cref="PluginHost.Start"/> call that set up this preview.</param>
    /// <param name="volume">Applied after the plugin, so it doesn't change what the plugin hears.</param>
    public PluginPreviewProvider(ISampleProvider source, PluginHost host, int generation, float volume)
    {
        _source = source;
        _host = host;
        _generation = generation;
        _volume = volume;
        WaveFormat = source.WaveFormat;
        _planes = new float[host.Channels][];
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
            // An old preview still finishing after the plugin was restarted for something else is silenced.
            if (!_host.Process(_planes, 0, read, _generation))
            {
                return 0;
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
