using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Lucent.Audio;

/// <summary>
/// One track, opened and ready to play, as a <see cref="TrackChain"/> holds it: the file's reader and
/// its audio in the chain's format.
/// </summary>
public sealed class ChainedTrack : IDisposable
{
    private readonly LevelledSamples _levelled;

    private ChainedTrack(string path, WaveStream reader, ISampleProvider audio)
    {
        Path = path;
        Reader = reader;
        _levelled = new LevelledSamples(audio);
        Audio = _levelled;
    }

    public string Path { get; }

    /// <summary>The file's own reader: its position, length and format.</summary>
    public WaveStream Reader { get; }

    /// <summary>The track's audio, resampled to the chain's rate if it needed to be, and levelled (<see cref="Gain"/>).</summary>
    internal ISampleProvider Audio { get; }

    /// <summary>
    /// How much the track is turned up or down to play about as loud as the others (volume leveling), as a factor:
    /// 1 leaves it as it is. A change once it's playing glides there over a fraction of a second rather than jumping.
    /// </summary>
    public float Gain
    {
        get => _levelled.Target;
        set => _levelled.Target = value;
    }

    /// <summary>The gain the track's sound has now: <see cref="Gain"/>, or on its way there.</summary>
    internal float AppliedGain => _levelled.Applied;

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

    /// <summary>
    /// The volume leveling gain of the track playing now, as it is at the moment (see <see cref="ChainedTrack.Gain"/>), so
    /// what the visuals and analyzer hear can be put back to the song's own level.
    /// </summary>
    public float Gain => _current.AppliedGain;

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

/// <summary>
/// A track's sound turned up or down by its volume leveling gain. A new gain set in its first moments (before the
/// output has played any of it, or just after) is taken at once; one set later glides there over about half a second,
/// so nothing clicks or jumps.
/// </summary>
internal sealed class LevelledSamples : ISampleProvider
{
    private readonly ISampleProvider _source;
    private volatile float _target = 1f;
    private float _applied = 1f;
    private long _read;
    private readonly long _firstMoments;
    private readonly float _step;

    public LevelledSamples(ISampleProvider source)
    {
        _source = source;
        // Most of the way to a new gain in about a fifth of a second, and there in half a second, at any rate and channels.
        var samplesPerSecond = (float)Math.Max(8000, source.WaveFormat.SampleRate) * Math.Max(1, source.WaveFormat.Channels);
        _step = 1f - MathF.Exp(-1f / (0.06f * samplesPerSecond));
        // The output reads a little ahead before it starts, so the first 0.3 s is still "before it plays".
        _firstMoments = (long)(0.3f * samplesPerSecond);
    }

    public WaveFormat WaveFormat => _source.WaveFormat;

    public float Target
    {
        get => _target;
        set
        {
            _target = float.IsFinite(value) && value > 0 ? value : 1f;
            if (Interlocked.Read(ref _read) < _firstMoments)
            {
                _applied = _target;
            }
        }
    }

    public float Applied => _applied;

    public int Read(float[] buffer, int offset, int count)
    {
        var read = _source.Read(buffer, offset, count);
        Interlocked.Add(ref _read, read);
        var target = _target;
        var applied = _applied;
        if (applied == target)
        {
            if (applied != 1f)
            {
                for (var i = 0; i < read; i++)
                {
                    buffer[offset + i] *= applied;
                }
            }

            return read;
        }

        for (var i = 0; i < read; i++)
        {
            applied += (target - applied) * _step;
            buffer[offset + i] *= applied;
        }

        // Settled: from here it's the gain itself, so the quicker loop above takes over.
        _applied = Math.Abs(target - applied) < 0.0005f * target ? target : applied;
        return read;
    }
}
