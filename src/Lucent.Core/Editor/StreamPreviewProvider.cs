using NAudio.Wave;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// Plays audio through one of the editor's own tools (the equalizer, compressor or limiter) as it's heard,
/// so moving a control changes the sound straight away.
/// </summary>
public sealed class StreamPreviewProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly IStreamProcessor _processor;
    private readonly float[][] _planes;

    public StreamPreviewProvider(ISampleProvider source, IStreamProcessor processor)
    {
        _source = source;
        _processor = processor;
        WaveFormat = source.WaveFormat;
        _planes = new float[source.WaveFormat.Channels][];
        processor.Reset();
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var channels = _planes.Length;
        var read = _source.Read(buffer, offset, count);
        var frames = read / channels;
        if (frames == 0)
        {
            return 0;
        }

        for (var c = 0; c < channels; c++)
        {
            if (_planes[c] is null || _planes[c].Length < frames)
            {
                _planes[c] = new float[frames];
            }

            for (var i = 0; i < frames; i++)
            {
                _planes[c][i] = buffer[offset + i * channels + c];
            }
        }

        _processor.Process(_planes, 0, frames);
        for (var c = 0; c < channels; c++)
        {
            for (var i = 0; i < frames; i++)
            {
                buffer[offset + i * channels + c] = _planes[c][i];
            }
        }

        return frames * channels;
    }
}
