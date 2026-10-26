using System.Diagnostics;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Lucent.Audio;
using Lucent.Editor;
using Lucent.Editor.Dsp;

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

    private readonly AudioCapture? _capture;
    private AudioQueueOutput? _output;
    private RangeSampleProvider? _provider;
    private EditorFeed? _feed;
    private PlayedCounter? _counter;
    private int _startFrame;

    /// <param name="capture">
    /// Where the visuals, meter, and analyzer look (the main player's), so they follow the editor while it plays.
    /// </param>
    public EditorPlayer(AudioCapture? capture = null) => _capture = capture;

    /// <summary>Raised on the UI thread when playback stops by itself or fails.</summary>
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

    private volatile bool[] _heard = { true, true };

    /// <summary>
    /// Which channels are heard, left then right (the lanes' solo and mute). Like the volume, it only changes what's
    /// heard (after the meters), never the audio itself, and a change is heard straight away.
    /// </summary>
    public IReadOnlyList<bool> HeardChannels
    {
        get => _heard;
        set => _heard = value.ToArray();
    }

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
    public void Play(AudioBuffer audio, int from, int to, Func<ISampleProvider, ISampleProvider>? through = null,
        bool loop = false)
    {
        Stop();
        from = Math.Clamp(from, 0, audio.Frames);
        to = Math.Clamp(to, from, audio.Frames);
        _startFrame = from;
        _provider = new RangeSampleProvider(audio, from, to) { Loop = loop && to - from > audio.SampleRate / 20 };
        Meter = new LiveMeter(audio.ChannelCount, audio.SampleRate);
        var processed = through is null ? _provider : through(_provider);
        if (_capture is not null)
        {
            // What's heard, before the listening volume, as the player's music is captured.
            processed = _feed = new EditorFeed(processed, _capture, LatencyMs);
        }

        _counter = new PlayedCounter(new MeteredOutput(processed, Meter, this), LatencyFrames(audio.SampleRate, audio.ChannelCount));
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
        _feed?.Dispose();
        _feed = null;
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

            // Channels muted or left out of a solo are silenced.
            var heard = _player._heard;
            if (heard.Contains(false))
            {
                var channels = WaveFormat.Channels;
                for (var i = 0; i < read; i++)
                {
                    var channel = i % channels;
                    if (channel < heard.Length && !heard[channel])
                    {
                        buffer[offset + i] = 0;
                    }
                }
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
