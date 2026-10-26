using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lucent.Audio;

/// <summary>
/// One track, opened and ready to play, as a <see cref="TrackChain"/> holds it: the file's reader and
/// its audio in the chain's format.
/// </summary>
public sealed class ChainedTrack : IDisposable
{
    private ChainedTrack(string path, WaveStream reader, ISampleProvider audio)
    {
        Path = path;
        Reader = reader;
        Audio = audio;
    }

    public string Path { get; }

    /// <summary>The file's own reader: its position, length and format.</summary>
    public WaveStream Reader { get; }

    /// <summary>The track's audio, resampled to the chain's rate if it needed to be.</summary>
    internal ISampleProvider Audio { get; }

    /// <summary>
    /// Wraps an opened file for a chain playing <paramref name="format"/>, or returns null when it can't
    /// join it seamlessly: a different number of channels, or a different sample rate when
    /// <paramref name="allowResampling"/> is off (bit-perfect output has to keep every sample as it is).
    /// </summary>
    public static ChainedTrack? Create(string path, WaveStream reader, WaveFormat format, bool allowResampling)
    {
        ISampleProvider audio = reader.ToSampleProvider();
        if (audio.WaveFormat.Channels != format.Channels)
        {
            return null;
        }

        if (audio.WaveFormat.SampleRate != format.SampleRate)
        {
            if (!allowResampling)
            {
                return null;
            }

            audio = new WdlResamplingSampleProvider(audio, format.SampleRate);
        }

        return new ChainedTrack(path, reader, audio);
    }

    public void Dispose() => Reader.Dispose();
}

/// <summary>
/// Plays one track after another without a gap. While a track plays, the next one can be opened and
/// handed over with <see cref="SetNext"/>; the moment the first runs out, the chain carries straight on
/// into the second within the same buffer, so the sound card never sees silence between them. That
/// matters for live albums, DJ mixes and anything else whose tracks run into each other.
/// </summary>
/// <remarks>
/// With <see cref="CrossfadeSeconds"/> set, the next track starts that long before the current one ends
/// instead, and the two are mixed: the old one fading out as the new one fades in.
/// <see cref="Read"/> runs on the audio thread; everything else on the UI thread. The two only ever meet
/// at the switch from one track to the next, which happens under a lock.
/// </remarks>
public sealed class TrackChain : ISampleProvider
{
    private readonly object _gate = new();
    private volatile ChainedTrack _current;
    private ChainedTrack? _next;

    // The track fading out under the current one during a crossfade, and how far through the fade it is.
    private ChainedTrack? _fading;
    private long _fadeSamples;
    private long _fadePosition;
    private float[] _fadeBuffer = Array.Empty<float>();
    private volatile bool _dropFade;

    public TrackChain(ChainedTrack first, WaveFormat format)
    {
        _current = first;
        WaveFormat = format;
    }

    /// <summary>
    /// Raised on the audio thread when the chain moves on to the track that was waiting, which is now
    /// <see cref="Current"/>. In a crossfade that's as the new track starts fading in.
    /// </summary>
    public event Action? Advanced;

    /// <summary>
    /// Raised on the audio thread with a track nothing reads from again (it ran out, or finished fading
    /// out), so it can be closed.
    /// </summary>
    public event Action<ChainedTrack>? Finished;

    public WaveFormat WaveFormat { get; }

    /// <summary>
    /// How long the next track overlaps the end of this one, fading in as this one fades out. Zero runs
    /// one straight into the next. Takes effect from the next change of track.
    /// </summary>
    public double CrossfadeSeconds { get; set; }

    /// <summary>The track playing now.</summary>
    public ChainedTrack Current => _current;

    /// <summary>The track still fading out under <see cref="Current"/>, during a crossfade.</summary>
    public ChainedTrack? Fading => _fading;

    /// <summary>The track that will follow without a gap, if one has been handed over.</summary>
    public ChainedTrack? Next
    {
        get
        {
            lock (_gate)
            {
                return _next;
            }
        }
    }

    /// <summary>
    /// Sets the track to carry on into (or none), and returns the one it replaces, if any, for the
    /// caller to close. It must have been made for this chain's format.
    /// </summary>
    public ChainedTrack? SetNext(ChainedTrack? next)
    {
        lock (_gate)
        {
            var replaced = _next;
            _next = next;
            return replaced;
        }
    }

    /// <summary>Silences the track fading out, if there is one: the music was stopped or moved.</summary>
    public void EndCrossfade() => _dropFade = true;

    public int Read(float[] buffer, int offset, int count)
    {
        StartCrossfadeIfDue();
        var total = ReadOn(buffer, offset, count);
        return _fading is null ? total : MixFading(buffer, offset, count, total);
    }

    // Plays the current track, running straight on into the next one if it ends.
    private int ReadOn(float[] buffer, int offset, int count)
    {
        var total = 0;
        while (total < count)
        {
            var read = _current.Audio.Read(buffer, offset + total, count - total);
            if (read > 0)
            {
                total += read;
                continue;
            }

            ChainedTrack finished;
            lock (_gate)
            {
                if (_next is null)
                {
                    break;
                }

                finished = _current;
                _current = _next;
                _next = null;
            }

            Advanced?.Invoke();
            Finished?.Invoke(finished);
        }

        return total;
    }

    // Starts the next track fading in once the current one is within the crossfade of its end.
    private void StartCrossfadeIfDue()
    {
        var seconds = CrossfadeSeconds;
        if (seconds <= 0 || _fading is not null)
        {
            return;
        }

        var reader = _current.Reader;
        var length = reader.TotalTime;
        if (length <= TimeSpan.Zero)
        {
            return;
        }

        var left = (length - reader.CurrentTime).TotalSeconds;
        if (left > seconds)
        {
            return;
        }

        lock (_gate)
        {
            if (_next is null)
            {
                return;
            }

            _fading = _current;
            _current = _next;
            _next = null;
        }

        // As long as what's left of the old track, so it fades all the way out just as it ends.
        var channels = WaveFormat.Channels;
        _fadeSamples = Math.Max(1, (long)(Math.Max(left, 0.05) * WaveFormat.SampleRate)) * channels;
        _fadePosition = 0;
        _dropFade = false;
        Advanced?.Invoke();
    }

    // Mixes the track fading out into the one fading in, with equal-power curves so the loudness holds steady.
    private int MixFading(float[] buffer, int offset, int count, int total)
    {
        var fading = _fading!;
        if (_dropFade)
        {
            _fading = null;
            Finished?.Invoke(fading);
            return total;
        }

        var channels = WaveFormat.Channels;
        var wanted = (int)Math.Min(count, _fadeSamples - _fadePosition);
        if (_fadeBuffer.Length < wanted)
        {
            _fadeBuffer = new float[wanted];
        }

        var tail = fading.Audio.Read(_fadeBuffer, 0, wanted);
        Array.Clear(_fadeBuffer, tail, wanted - tail);
        if (tail > total)
        {
            // The new track gave out first (a very short one): the old one's tail plays over silence.
            Array.Clear(buffer, offset + total, tail - total);
            total = tail;
        }

        var span = Math.Min(wanted, total);
        for (var i = 0; i < span; i++)
        {
            var t = (double)((_fadePosition + i) / channels) / (_fadeSamples / channels);
            var angle = t * Math.PI / 2;
            buffer[offset + i] = (float)(buffer[offset + i] * Math.Sin(angle) + _fadeBuffer[i] * Math.Cos(angle));
        }

        _fadePosition += wanted;
        if (_fadePosition >= _fadeSamples)
        {
            _fading = null;
            Finished?.Invoke(fading);
        }

        return total;
    }
}
