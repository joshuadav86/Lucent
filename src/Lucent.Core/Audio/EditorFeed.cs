using NAudio.Wave;

namespace Lucent.Audio;

/// <summary>
/// Passes the editor's audio through unchanged while handing it to the visuals, meter, and analyzer, the way
/// <see cref="CapturingSampleProvider"/> does the player's. While one is open the editor's sound is what they show
/// (see <see cref="AudioCapture.BeginEditor"/>). Dispose when the editor stops, to go back.
/// </summary>
public sealed class EditorFeed : ISampleProvider, IDisposable
{
    private readonly ISampleProvider _source;
    private readonly AudioCapture _capture;
    private readonly object _lock = new();
    private bool _disposed;
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    /// <param name="latencyMs">How far ahead of the speakers the editor's output reads its audio.</param>
    public EditorFeed(ISampleProvider source, AudioCapture capture, int latencyMs)
    {
        _source = source;
        _capture = capture;
        capture.BeginEditor(source.WaveFormat.SampleRate, latencyMs);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        lock (_lock)
        {
            if (!_disposed)
            {
                var frames = CapturingSampleProvider.Split(buffer, offset, read, WaveFormat.Channels, ref _mono, ref _left, ref _right);
                _capture.WriteEditor(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
            }
        }

        return read;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _capture.EndEditor();
        }
    }
}
