using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinampInspiredVisualizer.Audio;

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
/// <see cref="Read"/> runs on the audio thread; everything else on the UI thread. The two only ever meet
/// at the switch from one track to the next, which happens under a lock.
/// </remarks>
public sealed class TrackChain : ISampleProvider
{
    private readonly object _gate = new();
    private volatile ChainedTrack _current;
    private ChainedTrack? _next;

    public TrackChain(ChainedTrack first, WaveFormat format)
    {
        _current = first;
        WaveFormat = format;
    }

    /// <summary>
    /// Raised on the audio thread when the chain moves on to the track that was waiting, with the track
    /// that has just finished. Nothing reads from that track again, so it can be closed.
    /// </summary>
    public event Action<ChainedTrack>? Advanced;

    public WaveFormat WaveFormat { get; }

    /// <summary>The track playing now.</summary>
    public ChainedTrack Current => _current;

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

    public int Read(float[] buffer, int offset, int count)
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

            Advanced?.Invoke(finished);
        }

        return total;
    }
}
