using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Lucent.Video;

namespace Lucent.Audio;

/// <summary>What the player is sending to the speakers, and how.</summary>
/// <param name="Device">The output's name, or null for Windows' default output.</param>
/// <param name="Exclusive">True when Lucent has the output to itself, bypassing Windows' mixer.</param>
/// <param name="Bits">Bits per sample reaching the output (32 for floating point in shared mode).</param>
/// <param name="Resampled">True when the music is converted to another sample rate on its way out.</param>
/// <param name="SourceBits">Bits per sample in the file, or 0 when it has none of its own (MP3, AAC and other lossy formats).</param>
/// <param name="Problem">Why exclusive mode couldn't be used, when it was asked for and isn't in use.</param>
public sealed record OutputInfo(string? Device, bool Exclusive, int SampleRate, int Bits, int Channels, bool Resampled, int SourceBits, string? Problem);

/// <summary>
/// Plays one track at a time, carrying on into the next without a gap when it's been told what that is,
/// and reports when a track finishes or makes way for the next.
/// </summary>
/// <remarks>
/// Music normally goes through Windows' mixer, like every other app's sound. In exclusive mode Lucent
/// takes the output for itself and sends each track at its own sample rate and bit depth, so a DAC
/// receives exactly what's in the file: bit-perfect, as long as the volume is at 100% and the equalizer
/// is off.
/// </remarks>
public sealed class AudioPlayer : IDisposable
{
    /// <summary>How far ahead of the speakers the audio is decoded, in milliseconds.</summary>
    public const int OutputLatencyMs = AudioCapture.OutputLatencyMs;

    private IWavePlayer? _output;
    private TrackChain? _chain;
    private VolumeSampleProvider? _volumeProvider;
    private float _volume = 0.8f;
    private double _crossfadeSeconds;
    private bool _stopRequested;
    private bool _disposed;
    private VideoStream? _shownVideo;

    // Counts every Load and Close, so a file reopened after being released doesn't replace one the user chose meanwhile.
    private int _generation;

    /// <summary>Raised on the UI thread when a track plays through to the end with nothing lined up after it.</summary>
    public event EventHandler? TrackEnded;

    /// <summary>
    /// Raised on the UI thread when a track has run straight on into the one lined up with
    /// <see cref="QueueNext"/>, which is now <see cref="CurrentPath"/>.
    /// </summary>
    public event EventHandler? TrackAdvanced;

    /// <summary>Raised on the UI thread when the audio device reports an error.</summary>
    public event EventHandler<Exception>? PlaybackFailed;

    /// <summary>The audio being played, for the visualizer to analyze.</summary>
    public AudioCapture Capture { get; } = new();

    /// <summary>The equalizer every track plays through. Its settings carry over from track to track.</summary>
    public Equalizer Equalizer { get; } = new();

    /// <summary>Whether a track lined up with <see cref="QueueNext"/> follows without a gap. Takes effect from the next track.</summary>
    public bool Gapless { get; set; } = true;

    /// <summary>
    /// How many seconds a track lined up with <see cref="QueueNext"/> overlaps the end of the one before,
    /// fading in as it fades out. Zero for none.
    /// </summary>
    public double CrossfadeSeconds
    {
        get => _crossfadeSeconds;
        set
        {
            _crossfadeSeconds = Math.Max(0, value);
            if (_chain is not null)
            {
                _chain.CrossfadeSeconds = _crossfadeSeconds;
            }
        }
    }

    /// <summary>The output to play through (an id from <see cref="OutputDevices"/>), or null for Windows' default. Takes effect from the next track.</summary>
    public string? OutputDeviceId { get; set; }

    /// <summary>Whether to take the output for Lucent alone, for bit-perfect playback. Takes effect from the next track.</summary>
    public bool Exclusive { get; set; }

    /// <summary>How the current track is reaching the speakers, or null when nothing is loaded.</summary>
    public OutputInfo? Output { get; private set; }

    /// <summary>
    /// True when the samples in the file reach the output untouched: exclusive mode at the file's own
    /// sample rate, full volume and the equalizer off.
    /// </summary>
    public bool IsBitPerfect => Output is { Exclusive: true, Resampled: false } output
        && output.SourceBits > 0 && output.SourceBits <= output.Bits
        && _volume >= 1f && Equalizer.Design.IsBypass;

    public bool HasTrack => _chain is not null;

    /// <summary>The video playing, or null when what's playing isn't one.</summary>
    public VideoStream? Video => _chain?.Current.Reader as VideoStream;

    /// <summary>Raised on the UI thread when a video starts or stops being the track, so its pictures can be shown.</summary>
    public event EventHandler? VideoChanged;

    // The track's own clock, for a stream that keeps time by itself (a video).
    private IPlayerControlled? Controlled => _chain?.Current.Reader as IPlayerControlled;

    /// <summary>The file that's playing, or null when nothing is.</summary>
    public string? CurrentPath => _chain?.Current.Path;

    /// <summary>The file lined up to follow without a gap, or null when there isn't one.</summary>
    public string? QueuedPath => _chain?.Next?.Path;

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration => _chain?.Current.Reader.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _chain?.Current.Reader.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_chain is null)
            {
                return;
            }

            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration ? Duration : value;
            _chain.EndCrossfade();
            _chain.Current.Reader.CurrentTime = clamped;
        }
    }

    /// <summary>Volume from 0 (silent) to 1 (full).</summary>
    public float Volume
    {
        get => _volume;
        set
        {
            _volume = Math.Clamp(value, 0f, 1f);
            if (_volumeProvider is not null)
            {
                _volumeProvider.Volume = _volume;
            }
        }
    }

    /// <summary>Opens a file, ready to play. Throws if the file can't be decoded.</summary>
    public void Load(string path)
    {
        CloseTrack();
        _generation++;

        try
        {
            var reader = AudioReaderFactory.OpenForPlaying(path);
            try
            {
                Open(path, reader);
            }
            catch
            {
                reader.Dispose();
                Close();
                throw;
            }
        }
        finally
        {
            NotifyVideo();
        }
    }

    /// <summary>
    /// Plays audio that isn't a file: an online radio station, say. <paramref name="name"/> stands in for the file's path
    /// (the station's address). The player takes the stream over and closes it with the track.
    /// </summary>
    public void Load(string name, WaveStream stream)
    {
        CloseTrack();
        _generation++;
        try
        {
            Open(name, stream);
        }
        catch
        {
            stream.Dispose();
            Close();
            throw;
        }
        finally
        {
            NotifyVideo();
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> and lines it up to follow the current track without a gap, or with
    /// null forgets the one lined up. Returns false when it can't follow seamlessly (gapless playback is
    /// off, the file won't open, or it has a different number of channels, or in exclusive mode a
    /// different sample rate); it then plays the usual way once the current track has ended.
    /// </summary>
    public bool QueueNext(string? path)
    {
        if (_chain is null)
        {
            return false;
        }

        // A video plays to its own clock, so nothing runs on into one or out of one without a gap.
        if (path is null || (!Gapless && _crossfadeSeconds <= 0) || Video is not null || VideoFiles.IsVideo(path))
        {
            _chain.SetNext(null)?.Dispose();
            return false;
        }

        WaveStream reader;
        try
        {
            // Only decoders that start at once: a file only libVLC reads is decoded as it plays, after a gap.
            reader = AudioReaderFactory.OpenQuickly(path);
        }
        catch (Exception)
        {
            // Left to fail when it's played, which says what's wrong with it.
            _chain.SetNext(null)?.Dispose();
            return false;
        }

        // Exclusive output at the file's own rate stays bit-perfect only if the next file has that rate too.
        var allowResampling = Output is not { Exclusive: true, Resampled: false };
        var next = ChainedTrack.Create(path, reader, _chain.WaveFormat, allowResampling);
        if (next is null)
        {
            reader.Dispose();
        }

        _chain.SetNext(next)?.Dispose();
        return next is not null;
    }

    public void Play()
    {
        if (_output is null)
        {
            return;
        }

        _stopRequested = false;
        _output.Play();
        Controlled?.Playing();
    }

    public void Pause()
    {
        _output?.Pause();
        Controlled?.Paused();
    }

    public void Stop()
    {
        if (_output is null || _chain is null)
        {
            return;
        }

        _stopRequested = true;
        _output.Stop();
        _chain.EndCrossfade();
        if (Controlled is { } controlled)
        {
            controlled.Stopped();
        }
        else
        {
            _chain.Current.Reader.Position = 0;
        }
    }

    public void Close()
    {
        CloseTrack();
        NotifyVideo();
    }

    private void CloseTrack()
    {
        _generation++;
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        if (_chain is not null)
        {
            _chain.Advanced -= OnAdvanced;
            _chain.Finished -= OnFinished;
            _chain.SetNext(null)?.Dispose();
            _chain.Fading?.Dispose();
            _chain.Current.Dispose();
            _chain = null;
        }

        _volumeProvider = null;
        Output = null;
        Equalizer.Detach();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    /// <summary>True when <paramref name="path"/> is the file that's loaded.</summary>
    public bool IsCurrentFile(string path) => SameFile(CurrentPath, path);

    /// <summary>
    /// Runs <paramref name="work"/> with <paramref name="path"/> closed, if it's the file being played, then opens
    /// it again at the same place and carries on playing (or stays paused). Windows doesn't let a file be
    /// replaced while it's open, so this is how its tags get saved; the music only stops for a moment.
    /// For any other file the work just runs. Call it from the UI thread.
    /// </summary>
    public async Task RunWithFileReleasedAsync(string path, Func<Task> work)
    {
        // The track lined up next has its file open too; it's lined up again on the next tick.
        if (SameFile(QueuedPath, path))
        {
            QueueNext(null);
        }

        if (!IsCurrentFile(path))
        {
            await work();
            return;
        }

        var reopenPath = CurrentPath!;
        var position = Position;
        var wasPlaying = IsPlaying;
        Close();
        var released = _generation;
        try
        {
            await work();
        }
        finally
        {
            // If another track was loaded (or the queue cleared) in the meantime, leave that be.
            if (!_disposed && _generation == released)
            {
                Reopen(reopenPath, position, wasPlaying);
            }
        }
    }

    // Says when the video playing has changed: one started, ended or made way for a song.
    private void NotifyVideo()
    {
        if (!ReferenceEquals(_shownVideo, Video))
        {
            _shownVideo = Video;
            VideoChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    // A station's address is never a file.
    private static bool SameFile(string? a, string? b) =>
        a is not null && b is not null && !a.Contains("://") && !b.Contains("://") && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // Sets up the output for a newly opened file: exclusive if asked for and possible, else shared.
    private void Open(string path, WaveStream reader)
    {
        var source = reader.ToSampleProvider().WaveFormat;
        string? problem = null;
        if (Exclusive)
        {
            try
            {
                if (TryOpenExclusive(path, reader, source))
                {
                    return;
                }

                problem = "Your output doesn't accept this track's format on its own, so it's playing through Windows' mixer.";
            }
            catch (Exception ex)
            {
                // Most often another app already has the output to itself.
                ErrorLog.Write("Exclusive output", ex);
                problem = "Couldn't take the output for Lucent alone (another app may have it), so it's playing through Windows' mixer.";
                _output?.Dispose();
                _output = null;
            }
        }

        var first = ChainedTrack.Create(path, reader, source, allowResampling: false)!;
        var format = WaveFormat.CreateIeeeFloatWaveFormat(source.SampleRate, source.Channels);
        var chain = new TrackChain(first, format);
        var device = OutputDeviceId is null ? null : OutputDevices.Open(OutputDeviceId);
        IWavePlayer output = device is null
            ? new WaveOutEvent { DesiredLatency = OutputLatencyMs }
            : new WasapiOut(device, AudioClientShareMode.Shared, false, OutputLatencyMs);
        Start(chain, output, BuildChain(chain), new OutputInfo(device?.FriendlyName, false, source.SampleRate, 32, source.Channels, false, SourceBits(path), problem));
    }

    private bool TryOpenExclusive(string path, WaveStream reader, WaveFormat source)
    {
        var device = OutputDevices.Open(OutputDeviceId);
        if (device is null)
        {
            return false;
        }

        var sourceBits = SourceBits(path);
        var (pcm, validBits) = ExclusiveFormat(device, source.SampleRate, source.Channels, sourceBits);
        var resampled = false;
        if (pcm is null)
        {
            // The output can't take the file's sample rate: use the rate it runs at in Windows instead.
            using var client = device.AudioClient;
            var mixRate = client.MixFormat.SampleRate;
            (pcm, validBits) = ExclusiveFormat(device, mixRate, source.Channels, sourceBits);
            resampled = true;
        }

        if (pcm is null)
        {
            device.Dispose();
            return false;
        }

        var first = ChainedTrack.Create(path, reader, WaveFormat.CreateIeeeFloatWaveFormat(pcm.SampleRate, source.Channels), allowResampling: true)!;
        var chain = new TrackChain(first, WaveFormat.CreateIeeeFloatWaveFormat(pcm.SampleRate, source.Channels));
        var output = new WasapiOut(device, AudioClientShareMode.Exclusive, false, OutputLatencyMs);
        Start(chain, output, new PcmOutput(BuildChain(chain), pcm, validBits),
            new OutputInfo(device.FriendlyName, true, pcm.SampleRate, validBits, pcm.Channels, resampled, sourceBits, null));
        return true;
    }

    // The best whole-number format the output takes at this rate in exclusive mode: the file's own bit
    // depth if it can, otherwise the deepest. Null when it takes none.
    private static (WaveFormat? Format, int ValidBits) ExclusiveFormat(MMDevice device, int sampleRate, int channels, int sourceBits)
    {
        (int Container, int Valid)[] deepestFirst = { (32, 24), (24, 24), (32, 32), (16, 16) };
        var order = sourceBits == 16
            ? deepestFirst.OrderBy(choice => choice.Valid == 16 ? 0 : 1).ToArray()
            : deepestFirst;
        using var client = device.AudioClient;
        foreach (var (container, valid) in order)
        {
            var format = PcmOutput.Format(sampleRate, channels, container, valid);
            if (client.IsFormatSupported(AudioClientShareMode.Exclusive, format))
            {
                return (format, valid);
            }
        }

        return (null, 0);
    }

    // Bits per sample in the file itself (0 for lossy formats, which have none).
    private static int SourceBits(string path)
    {
        if (path.Contains("://"))
        {
            // A radio station: MP3, so lossy.
            return 0;
        }

        try
        {
            using var file = TagLib.File.Create(path);
            return file.Properties?.BitsPerSample ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    // Equalize before capturing, so the visuals respond to what you hear, and capture before
    // the volume control so visuals don't shrink when the music is turned down.
    private ISampleProvider BuildChain(TrackChain chain)
    {
        var equalized = Equalizer.Attach(chain);
        var captured = new CapturingSampleProvider(equalized, Capture);
        _volumeProvider = new VolumeSampleProvider(captured) { Volume = _volume };
        return _volumeProvider;
    }

    private void Start(TrackChain chain, IWavePlayer output, ISampleProvider audio, OutputInfo info) =>
        Start(chain, output, audio.ToWaveProvider(), info);

    private void Start(TrackChain chain, IWavePlayer output, IWaveProvider audio, OutputInfo info)
    {
        _output = output;
        _output.PlaybackStopped += OnPlaybackStopped;
        _output.Init(audio);
        _chain = chain;
        _chain.Advanced += OnAdvanced;
        _chain.Finished += OnFinished;
        _chain.CrossfadeSeconds = _crossfadeSeconds;
        Output = info;
    }

    // On the audio thread: the chain has just moved on to the track that was lined up.
    private void OnAdvanced()
    {
        var chain = _chain;
        Post(() =>
        {
            if (!_disposed && _chain is { } current && ReferenceEquals(chain, current))
            {
                Output = Output is null ? null : Output with { SourceBits = SourceBits(current.Current.Path) };
                TrackAdvanced?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    private static void Post(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher)
        {
            dispatcher.BeginInvoke(action);
        }
    }

    // On the audio thread: nothing reads from this track again (it ended, or finished fading out).
    private static void OnFinished(ChainedTrack finished) => Post(finished.Dispose);

    private void Reopen(string path, TimeSpan position, bool play)
    {
        try
        {
            Load(path);
            Position = position;
            if (play)
            {
                Play();
            }
        }
        catch (Exception ex)
        {
            PlaybackFailed?.Invoke(this, ex);
        }
    }

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
        {
            PlaybackFailed?.Invoke(this, e.Exception);
            return;
        }

        // Stop() and a quick Stop-then-Play both land here; only a track that
        // ran out on its own should advance the playlist.
        if (!_stopRequested && _output?.PlaybackState == PlaybackState.Stopped)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
