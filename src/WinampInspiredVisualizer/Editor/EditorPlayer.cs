using NAudio.Wave;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// Plays part of the audio in the editor through its own output, separate from the main player,
/// and tells the waveform where the playhead is. What's played is metered on its way out.
/// </summary>
public sealed class EditorPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private RangeSampleProvider? _provider;
    private int _startFrame;
    private int _channels = 1;

    /// <summary>Raised (on the audio thread's context, usually the UI thread) when playback stops by itself or fails.</summary>
    public event EventHandler<Exception?>? Stopped;

    private volatile float _volume = 1;

    /// <summary>
    /// The listening volume, from 0 to 1. It only changes what's heard (after the meters), never the audio itself,
    /// and a change is heard straight away, even while playing.
    /// </summary>
    public float Volume
    {
        get => _volume;
        set => _volume = Math.Clamp(value, 0f, 1f);
    }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Levels and loudness of what's playing, measured before the listening volume; null when stopped.</summary>
    public LiveMeter? Meter { get; private set; }

    /// <summary>The frame being heard right now, allowing for the sound card's delay.</summary>
    public int PositionFrame
    {
        get
        {
            if (_output is null || _provider is not { } provider)
            {
                return _startFrame;
            }

            // GetPosition counts the bytes the device has actually played.
            var played = _output.GetPosition() / (sizeof(float) * _channels);
            var length = provider.EndFrame - _startFrame;
            if (provider.Loop && length > 0)
            {
                return (int)(_startFrame + played % length);
            }

            return (int)Math.Min(_startFrame + played, provider.EndFrame);
        }
    }

    /// <summary>
    /// Plays frames <paramref name="from"/> up to <paramref name="to"/>, replacing anything already playing, and
    /// over again from <paramref name="from"/> when <paramref name="loop"/> is set. <paramref name="through"/>,
    /// if given, wraps the audio in something that changes it (a plugin or tool preview).
    /// </summary>
    public void Play(AudioBuffer audio, int from, int to, Func<ISampleProvider, ISampleProvider>? through = null,
        bool loop = false)
    {
        Stop();
        from = Math.Clamp(from, 0, audio.Frames);
        to = Math.Clamp(to, from, audio.Frames);
        _startFrame = from;
        _channels = audio.ChannelCount;
        _provider = new RangeSampleProvider(audio, from, to) { Loop = loop && to - from > audio.SampleRate / 20 };
        Meter = new LiveMeter(audio.ChannelCount, audio.SampleRate);
        var processed = through is null ? _provider : through(_provider);
        _output = new WaveOutEvent { DesiredLatency = 120 };
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(new MeteredOutput(processed, Meter, this));
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
        Meter = null;
    }

    public void Dispose() => Stop();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        Stop();
        Stopped?.Invoke(this, e.Exception);
    }

    /// <summary>Reads a stretch of an <see cref="AudioBuffer"/> as interleaved samples, round and round if looping.</summary>
    private sealed class RangeSampleProvider : ISampleProvider
    {
        private readonly AudioBuffer _audio;
        private readonly int _from;
        private int _position;

        public RangeSampleProvider(AudioBuffer audio, int from, int to)
        {
            _audio = audio;
            _from = from;
            _position = from;
            EndFrame = to;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(audio.SampleRate, audio.ChannelCount);
        }

        public WaveFormat WaveFormat { get; }

        public int EndFrame { get; }

        public bool Loop { get; init; }

        public int Read(float[] buffer, int offset, int count)
        {
            var channels = _audio.ChannelCount;
            var wanted = count / channels;
            var written = 0;
            while (written < wanted)
            {
                if (_position >= EndFrame)
                {
                    if (!Loop)
                    {
                        break;
                    }

                    _position = _from;
                }

                var frames = Math.Min(wanted - written, EndFrame - _position);
                for (var i = 0; i < frames; i++)
                {
                    for (var c = 0; c < channels; c++)
                    {
                        buffer[offset + (written + i) * channels + c] = _audio.Channels[c][_position + i];
                    }
                }

                _position += frames;
                written += frames;
            }

            return written * channels;
        }
    }

    /// <summary>Meters the audio, then applies the listening volume, so the meters show the music's own level.</summary>
    private sealed class MeteredOutput : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly LiveMeter _meter;
        private readonly EditorPlayer _player;
        private float _volume;

        public MeteredOutput(ISampleProvider source, LiveMeter meter, EditorPlayer player)
        {
            _source = source;
            _meter = meter;
            _player = player;
            _volume = player.Volume;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            _meter.Feed(buffer, offset, read);
            // A change of volume slides across the block, so moving the fader doesn't click.
            var from = _volume;
            var to = _player.Volume;
            var step = read > 0 ? (to - from) / read : 0;
            for (var i = 0; i < read; i++)
            {
                buffer[offset + i] *= from + step * i;
            }

            _volume = to;

            return read;
        }
    }
}
