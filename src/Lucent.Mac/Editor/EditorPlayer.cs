using System.Diagnostics;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Dsp;

namespace Lucent.Mac.Editor;

/// <summary>
/// Plays part of the audio in the editor through its own output (Core Audio, like the main player), separate
/// from the main player, and tells the waveform where the playhead is. What's played is metered on its way out.
/// </summary>
public sealed class EditorPlayer : IDisposable
{
    /// <summary>How much audio is queued ahead of the speakers, as on Windows (WaveOutEvent's DesiredLatency).</summary>
    private const int LatencyMs = 120;

    /// <summary>The number of buffers <see cref="AudioQueueOutput"/> splits that between.</summary>
    private const int Buffers = 3;

    private AudioQueueOutput? _output;
    private RangeSampleProvider? _provider;
    private PlayedCounter? _counter;
    private int _startFrame;

    /// <summary>Raised on the UI thread when playback stops by itself or fails.</summary>
    public event EventHandler<Exception?>? Stopped;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    /// <summary>Levels and loudness of what's playing, measured before the listening volume; null when stopped.</summary>
    public LiveMeter? Meter { get; private set; }

    /// <summary>The frame being heard right now, allowing for the sound card's delay.</summary>
    public int PositionFrame
    {
        get
        {
            if (_output is null || _provider is not { } provider || _counter is not { } counter)
            {
                return _startFrame;
            }

            var played = counter.FramesPlayed;
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
    public void Play(AudioBuffer audio, int from, int to, float volume, Func<ISampleProvider, ISampleProvider>? through = null,
        bool loop = false)
    {
        Stop();
        from = Math.Clamp(from, 0, audio.Frames);
        to = Math.Clamp(to, from, audio.Frames);
        _startFrame = from;
        _provider = new RangeSampleProvider(audio, from, to) { Loop = loop && to - from > audio.SampleRate / 20 };
        Meter = new LiveMeter(audio.ChannelCount, audio.SampleRate);
        var processed = through is null ? _provider : through(_provider);
        _counter = new PlayedCounter(new MeteredOutput(processed, Meter, volume), LatencyFrames(audio.SampleRate, audio.ChannelCount));
        var output = new AudioQueueOutput(LatencyMs);
        _output = output;
        output.PlaybackStopped += OnPlaybackStopped;
        try
        {
            output.Init(new SampleToWaveProvider(_counter));
            output.Play();
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void Stop()
    {
        if (_output is not { } output)
        {
            return;
        }

        output.PlaybackStopped -= OnPlaybackStopped;
        _output = null;
        output.Stop();
        output.Dispose();
        _provider = null;
        _counter = null;
        Meter = null;
    }

    public void Dispose() => Stop();

    /// <summary>
    /// How many frames Core Audio holds ahead of the speakers: all of <see cref="AudioQueueOutput"/>'s buffers,
    /// sized the way it sizes them.
    /// </summary>
    private static long LatencyFrames(int sampleRate, int channels)
    {
        var blockAlign = channels * sizeof(float);
        var bytesPerBuffer = sampleRate * blockAlign * LatencyMs / 1000 / Buffers;
        bytesPerBuffer -= bytesPerBuffer % blockAlign;
        return (long)Buffers * bytesPerBuffer / blockAlign;
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // Core Audio reports the end on one of its own threads; the editor hears about it on the UI thread,
        // as WaveOutEvent's event arrives on Windows.
        Dispatcher.UIThread.Post(() =>
        {
            if (!ReferenceEquals(sender, _output))
            {
                return;
            }

            Stop();
            Stopped?.Invoke(this, e.Exception);
        });
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
        private readonly float _volume;

        public MeteredOutput(ISampleProvider source, LiveMeter meter, float volume)
        {
            _source = source;
            _meter = meter;
            _volume = volume;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            _meter.Feed(buffer, offset, read);
            for (var i = 0; i < read; i++)
            {
                buffer[offset + i] *= _volume;
            }

            return read;
        }
    }

    /// <summary>
    /// Counts what Core Audio takes, to tell how much has been heard: WaveOutEvent.GetPosition on Windows.
    /// Core Audio asks for more each time a buffer has played, so the frames taken minus the buffers queued
    /// ahead have been heard; between refills the clock carries it on smoothly.
    /// </summary>
    private sealed class PlayedCounter : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly long _latencyFrames;
        private readonly int _channels;
        private readonly int _sampleRate;
        private long _framesRead;
        private long _readAt = Stopwatch.GetTimestamp();

        public PlayedCounter(ISampleProvider source, long latencyFrames)
        {
            _source = source;
            _latencyFrames = latencyFrames;
            _channels = source.WaveFormat.Channels;
            _sampleRate = source.WaveFormat.SampleRate;
        }

        public WaveFormat WaveFormat => _source.WaveFormat;

        public long FramesPlayed
        {
            get
            {
                var read = Interlocked.Read(ref _framesRead);
                var since = Stopwatch.GetElapsedTime(Interlocked.Read(ref _readAt)).TotalSeconds * _sampleRate;
                return Math.Max(0, read - _latencyFrames + (long)Math.Min(since, _latencyFrames));
            }
        }

        public int Read(float[] buffer, int offset, int count)
        {
            var read = _source.Read(buffer, offset, count);
            if (read > 0)
            {
                // At the end nothing more is read, and the clock runs on through what's still queued.
                Interlocked.Exchange(ref _readAt, Stopwatch.GetTimestamp());
                Interlocked.Add(ref _framesRead, read / _channels);
            }

            return read;
        }
    }
}
