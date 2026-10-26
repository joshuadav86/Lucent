namespace Lucent.Audio;

/// <summary>
/// Where an input's sound goes (each app's Audio/LiveInputs.cs): a <see cref="Lucent.Recording.LiveRecorder"/>
/// while recording, or an <see cref="InputFeed"/> while the visuals follow an input.
/// </summary>
public interface ILiveInputSink
{
    /// <summary>The rate it wants the input to run at.</summary>
    int SampleRate { get; }

    /// <summary>How many of the input's channels it takes.</summary>
    int Channels { get; }

    /// <summary>
    /// Takes sound on the input's thread: <paramref name="frames"/> frames of interleaved audio with
    /// <paramref name="inputChannels"/> channels, of which its own start at <paramref name="firstChannel"/>.
    /// </summary>
    void Write(ReadOnlySpan<float> audio, int frames, int inputChannels, int firstChannel, int inputRate);
}

/// <summary>
/// Hands an input's sound to the visuals, meter, equalizer's spectrum and analyzer, the way other apps' sound
/// is (see each app's Audio/SystemAudio.cs). It's how a DAW playing through its own driver (Ableton on ASIO)
/// is seen: the interface's mixer loops the DAW's output back to an input (TotalMix's Loopback), and
/// Lucent listens to that input. Two channels are taken from the first one chosen. Dispose to go back.
/// </summary>
public sealed class InputFeed : ILiveInputSink, IDisposable
{
    private readonly AudioCapture _capture;
    private readonly object _lock = new();
    private int _rate;
    private bool _disposed;
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();

    /// <param name="sampleRate">The rate the input runs at now, so it isn't changed for the visuals.</param>
    public InputFeed(AudioCapture capture, int sampleRate)
    {
        _capture = capture;
        _rate = sampleRate;
        SampleRate = sampleRate;
        capture.BeginExternal(sampleRate);
    }

    public int SampleRate { get; }

    public int Channels => 2;

    public void Write(ReadOnlySpan<float> audio, int frames, int inputChannels, int firstChannel, int inputRate)
    {
        lock (_lock)
        {
            if (_disposed || frames <= 0 || inputChannels <= 0)
            {
                return;
            }

            if (inputRate > 0 && inputRate != _rate)
            {
                _rate = inputRate;
                _capture.BeginExternal(inputRate);
            }

            if (_mono.Length < frames)
            {
                _mono = new float[frames];
                _left = new float[frames];
                _right = new float[frames];
            }

            var leftChannel = Math.Clamp(firstChannel, 0, inputChannels - 1);
            var rightChannel = Math.Min(leftChannel + 1, inputChannels - 1);
            frames = Math.Min(frames, audio.Length / inputChannels);
            for (var frame = 0; frame < frames; frame++)
            {
                var left = audio[frame * inputChannels + leftChannel];
                var right = audio[frame * inputChannels + rightChannel];
                _left[frame] = left;
                _right[frame] = right;
                _mono[frame] = (left + right) * 0.5f;
            }

            _capture.WriteExternal(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
        }
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
            _capture.EndExternal();
        }
    }
}
