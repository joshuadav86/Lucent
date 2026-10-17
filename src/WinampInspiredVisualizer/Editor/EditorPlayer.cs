using NAudio.Wave;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// Plays part of the audio in the editor through its own output, separate from the main player,
/// and tells the waveform where the playhead is.
/// </summary>
public sealed class EditorPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private RangeSampleProvider? _provider;
    private int _startFrame;
    private int _channels = 1;

    /// <summary>Raised (on the audio thread's context, usually the UI thread) when playback stops by itself or fails.</summary>
    public event EventHandler<Exception?>? Stopped;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>The frame being heard right now, allowing for the sound card's delay.</summary>
    public int PositionFrame
    {
        get
        {
            if (_output is null)
            {
                return _startFrame;
            }

            // GetPosition counts the bytes the device has actually played.
            var played = _output.GetPosition() / (sizeof(float) * _channels);
            return (int)Math.Min(_startFrame + played, _provider?.EndFrame ?? int.MaxValue);
        }
    }

    /// <summary>
    /// Plays frames <paramref name="from"/> up to <paramref name="to"/>, replacing anything already playing.
    /// <paramref name="through"/>, if given, wraps the audio in something that changes it (a plugin preview),
    /// and is passed the volume to apply after its own processing.
    /// </summary>
    public void Play(AudioBuffer audio, int from, int to, float volume, Func<ISampleProvider, float, ISampleProvider>? through = null)
    {
        Stop();
        from = Math.Clamp(from, 0, audio.Frames);
        to = Math.Clamp(to, from, audio.Frames);
        _startFrame = from;
        _channels = audio.ChannelCount;
        _provider = new RangeSampleProvider(audio, from, to) { Volume = through is null ? volume : 1f };
        _output = new WaveOutEvent { DesiredLatency = 120 };
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(through is null ? _provider : through(_provider, volume));
        _output.Play();
    }

    public void Stop()
    {
        if (_output is null)
        {
            return;
        }

        _output.PlaybackStopped -= OnPlaybackStopped;
        _output.Stop();
        _output.Dispose();
        _output = null;
        _provider = null;
    }

    public void Dispose() => Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Stop();
        Stopped?.Invoke(this, e.Exception);
    }

    /// <summary>Reads a stretch of an <see cref="AudioBuffer"/> as interleaved samples.</summary>
    private sealed class RangeSampleProvider : ISampleProvider
    {
        private readonly AudioBuffer _audio;
        private int _position;

        public RangeSampleProvider(AudioBuffer audio, int from, int to)
        {
            _audio = audio;
            _position = from;
            EndFrame = to;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.ChannelCount);
        }

        public WaveFormat WaveFormat { get; }

        public int EndFrame { get; }

        public float Volume { get; init; } = 1f;

        public int Read(float[] buffer, int offset, int count)
        {
            var channels = _audio.ChannelCount;
            var frames = Math.Min(count / channels, EndFrame - _position);
            for (var i = 0; i < frames; i++)
            {
                for (var c = 0; c < channels; c++)
                {
                    buffer[offset + i * channels + c] = _audio.Channels[c][_position + i] * Volume;
                }
            }

            _position += frames;
            return frames * channels;
        }
    }
}
