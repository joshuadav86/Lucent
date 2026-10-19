using NAudio.Wave;

namespace WinampInspiredVisualizer.Editor.Stems;

/// <summary>
/// Plays a split's stems together for the stems window, with any of them muted or soloed while it plays.
/// </summary>
public sealed class StemMixer
{
    private readonly AudioBuffer[] _stems;
    private readonly bool[] _muted;
    private readonly bool[] _soloed;

    public StemMixer(StemSet stems)
    {
        Sources = StemNames.All.Where(source => stems[source] is not null).ToArray();
        _stems = Sources.Select(source => stems[source]!).ToArray();
        _muted = new bool[Sources.Count];
        _soloed = new bool[Sources.Count];
    }

    /// <summary>The stems, in the editor's order (vocals, drums, bass, other instruments).</summary>
    public IReadOnlyList<string> Sources { get; }

    /// <summary>The stem to play the others alongside: it has the same length, rate and channels as them all.</summary>
    public AudioBuffer First => _stems[0];

    public AudioBuffer this[int index] => _stems[index];

    public bool IsMuted(int index) => _muted[index];

    public bool IsSoloed(int index) => _soloed[index];

    public void SetMuted(int index, bool muted) => Volatile.Write(ref _muted[index], muted);

    public void SetSoloed(int index, bool soloed) => Volatile.Write(ref _soloed[index], soloed);

    /// <summary>Whether a stem is heard: not muted, and soloed if any are.</summary>
    public bool IsAudible(int index)
    {
        var anySolo = false;
        for (var s = 0; s < _soloed.Length; s++)
        {
            anySolo |= Volatile.Read(ref _soloed[s]);
        }

        return anySolo ? Volatile.Read(ref _soloed[index]) : !Volatile.Read(ref _muted[index]);
    }

    /// <summary>The stems being heard.</summary>
    public IReadOnlyList<string> Audible => Sources.Where((_, index) => IsAudible(index)).ToList();

    /// <summary>
    /// For the editor's player: plays the audible stems in place of <paramref name="range"/> (which reads
    /// <see cref="First"/> from frame <paramref name="from"/>), following mutes and solos as they change.
    /// </summary>
    public ISampleProvider Through(ISampleProvider range, int from) => new MixProvider(this, range, from);

    /// <summary>The loudest sample of each of <paramref name="bins"/> stretches of the audio, from 0 to 1, for drawing it.</summary>
    public static float[] Peaks(AudioBuffer audio, int bins)
    {
        var peaks = new float[bins];
        if (audio.Frames == 0)
        {
            return peaks;
        }

        for (var b = 0; b < bins; b++)
        {
            var start = (int)((long)audio.Frames * b / bins);
            var end = Math.Max(start + 1, (int)((long)audio.Frames * (b + 1) / bins));
            var peak = 0f;
            foreach (var channel in audio.Channels)
            {
                for (var i = start; i < end && i < channel.Length; i++)
                {
                    peak = Math.Max(peak, Math.Abs(channel[i]));
                }
            }

            peaks[b] = Math.Min(1, peak);
        }

        return peaks;
    }

    private sealed class MixProvider : ISampleProvider
    {
        private readonly StemMixer _mixer;
        private readonly ISampleProvider _range;
        private readonly float[] _gains;
        private int _position;

        public MixProvider(StemMixer mixer, ISampleProvider range, int from)
        {
            _mixer = mixer;
            _range = range;
            _position = from;
            _gains = Enumerable.Range(0, mixer._stems.Length).Select(s => mixer.IsAudible(s) ? 1f : 0f).ToArray();
        }

        public WaveFormat WaveFormat => _range.WaveFormat;

        public int Read(float[] buffer, int offset, int count)
        {
            // The range keeps track of where playing ends; what it reads is replaced with the stems.
            var read = _range.Read(buffer, offset, count);
            var channels = WaveFormat.Channels;
            var frames = read / channels;
            Array.Clear(buffer, offset, read);
            for (var s = 0; s < _mixer._stems.Length; s++)
            {
                // A mute or solo fades over the buffer rather than clicking.
                var from = _gains[s];
                var to = _mixer.IsAudible(s) ? 1f : 0f;
                _gains[s] = to;
                if (from == 0 && to == 0)
                {
                    continue;
                }

                var stem = _mixer._stems[s];
                for (var i = 0; i < frames; i++)
                {
                    var gain = from + (to - from) * (i + 1) / frames;
                    var frame = _position + i;
                    if (frame >= stem.Frames)
                    {
                        break;
                    }

                    for (var c = 0; c < channels; c++)
                    {
                        buffer[offset + i * channels + c] += gain * stem.Channels[c][frame];
                    }
                }
            }

            _position += frames;
            return read;
        }
    }
}
