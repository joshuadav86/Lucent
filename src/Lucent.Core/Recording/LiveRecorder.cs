using System.Collections.Concurrent;
using System.IO;
using NAudio.Dsp;
using NAudio.Wave;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor.Plugins;

namespace WinampInspiredVisualizer.Recording;

/// <summary>A recording that has been saved.</summary>
/// <param name="DryPath">The copy without the plugins, when one was kept.</param>
/// <param name="CueSheetPath">The .cue sheet beside it, when one was saved.</param>
public sealed record FinishedRecording(string Path, string? DryPath, string? CueSheetPath, TimeSpan Duration, IReadOnlyList<TrackMarker> Tracks);

/// <summary>What the Record window needs to start a file.</summary>
/// <param name="Title">The recording's name, for its tags and cue sheet.</param>
/// <param name="DryPath">Where to keep a copy without the plugins, or null for none.</param>
/// <param name="CueSheet">Save a .cue sheet of the tracks beside it.</param>
public sealed record RecordingTarget(string Path, string Title, string? DryPath, bool CueSheet);

/// <summary>
/// Everything between an audio interface and the disk: the sound arrives from the input (each app's
/// Audio/LiveInputs.cs) and goes through the live plugins, into the WAV file, to the visuals, meters and
/// analyzer (as other apps' sound does), and to the speakers if it's being listened to.
/// </summary>
/// <remarks>
/// The input's own thread only copies the sound into a queue, and the rest happens on the recorder's
/// thread, so a plugin window opening or the disk being slow for a moment never makes the input drop
/// sound: the queue holds up to <see cref="QueueSeconds"/> seconds. Starting, stopping, splitting and
/// marking go through the same queue, so each lands exactly where the sound was when it was asked for,
/// and a split loses not a single sample.
/// </remarks>
public sealed class LiveRecorder : IDisposable
{
    /// <summary>How much sound can wait for the recorder's thread before the oldest is let go.</summary>
    public const int QueueSeconds = 20;

    private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(2);

    private readonly AudioCapture _capture;
    // Blocks of sound (float[]) and commands (Action) in the order they arrived, so a mark or a stop lands
    // exactly where the sound was when it was asked for.
    private readonly ConcurrentQueue<object> _work = new();
    private readonly SemaphoreSlim _ready = new(0);
    private readonly Thread _thread;
    private readonly object _meterLock = new();
    private readonly object _markerLock = new();
    private readonly List<TrackMarker> _markers = new();
    private readonly float[] _peaks = new float[2];
    private volatile IReadOnlyList<ChainSlot> _plugins = Array.Empty<ChainSlot>();
    private volatile bool _stopping;
    private int _queuedFrames;
    private long _dropped;
    private bool _clipped;
    private WdlResampler? _resampler;
    private int _resamplerRate;
    private float[][] _planes = Array.Empty<float[]>();
    private float[] _resampled = Array.Empty<float>();
    private float[] _mono = Array.Empty<float>();
    private float[] _left = Array.Empty<float>();
    private float[] _right = Array.Empty<float>();
    private float[] _stereo = Array.Empty<float>();
    private BufferedWaveProvider? _monitor;
    private WavRecording? _file;
    private WavRecording? _dryFile;
    private RecordingTarget? _target;
    private SongChangeDetector? _detector;
    private DateTime _lastFlush;
    private long _recordedFrames;
    private int _tracksVersion;
    // Song identification: stretches being gathered to send, and lookups on their way.
    private readonly List<Listening> _listening = new();
    private readonly List<Task> _lookups = new();
    private readonly CancellationTokenSource _lookupCancel = new();
    private volatile SongIdentifier? _identifier;
    private volatile string? _identifyProblem;

    /// <param name="capture">The visuals' audio: it shows this input while the recorder is open.</param>
    /// <param name="sampleRate">The rate the recording is made at. Input at another rate is converted.</param>
    /// <param name="channels">1 for mono, 2 for stereo.</param>
    public LiveRecorder(AudioCapture capture, int sampleRate, int channels)
    {
        _capture = capture;
        SampleRate = sampleRate;
        Channels = channels;
        _capture.BeginLive(sampleRate);
        _thread = new Thread(Run) { IsBackground = true, Name = "Lucent recorder", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    /// <summary>Raised on the recorder's thread when a file can't be written (the disk is full, say); the recording has stopped.</summary>
    public event EventHandler<Exception>? Failed;

    /// <summary>Raised on the recorder's thread when Lucent hears a new song start, with where.</summary>
    public event EventHandler<TrackMarker>? SongChanged;

    /// <summary>Raised (on a pool thread) when a track's song has been recognised, with the track as it is now.</summary>
    public event EventHandler<TrackMarker>? SongIdentified;

    /// <summary>
    /// Asks ACRCloud which song each track is, a little way into it, with the DJ's own key; null to not ask.
    /// Each track is asked about at most twice: <see cref="FirstLookup"/> in, and again a minute later if it wasn't known.
    /// </summary>
    public SongIdentifier? Identifier
    {
        get => _identifier;
        set
        {
            _identifier = value;
            _identifyProblem = null;
        }
    }

    /// <summary>How far into a track it's first listened to for a lookup: past the mix from the last one.</summary>
    public static readonly TimeSpan FirstLookup = TimeSpan.FromSeconds(25);

    /// <summary>When the second lookup is made for a track that wasn't recognised the first time.</summary>
    public static readonly TimeSpan SecondLookup = TimeSpan.FromSeconds(85);

    /// <summary>Why songs can't be recognised right now (a wrong key, say), or null.</summary>
    public string? IdentifyProblem => _identifyProblem;

    /// <summary>Goes up each time a track is added, taken out or named, so the window knows to show them again.</summary>
    public int TracksVersion => Volatile.Read(ref _tracksVersion);

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>A live plugin stopped working (its helper crashed); the sound goes past it now.</summary>
    public string? PluginProblem { get; private set; }

    public bool IsRecording => _file is not null;

    /// <summary>The file being recorded, if one is.</summary>
    public string? CurrentPath => _target?.Path;

    /// <summary>How long the current file is.</summary>
    public TimeSpan Elapsed => TimeSpan.FromSeconds((double)Interlocked.Read(ref _recordedFrames) / SampleRate);

    /// <summary>How big the current file is, in bytes (and its dry copy, if there is one).</summary>
    public long Bytes => (_file?.Bytes ?? 0) + (_dryFile?.Bytes ?? 0);

    /// <summary>How much of the input's sound is waiting for the recorder's thread.</summary>
    public TimeSpan Queued => TimeSpan.FromSeconds((double)Volatile.Read(ref _queuedFrames) / SampleRate);

    /// <summary>Sound the input sent that had to be let go because the recorder fell too far behind.</summary>
    public TimeSpan Dropped => TimeSpan.FromSeconds((double)Interlocked.Read(ref _dropped) / SampleRate);

    /// <summary>Lucent marks song changes it hears in the files recorded from now on.</summary>
    public bool DetectSongChanges { get; set; } = true;

    /// <summary>The tracks marked in the current file so far, the first starting at the beginning.</summary>
    public IReadOnlyList<TrackMarker> Tracks
    {
        get
        {
            lock (_markerLock)
            {
                return _markers.ToList();
            }
        }
    }

    /// <summary>
    /// The sound after the plugins, for the speakers: set up by <see cref="ListenThrough"/>. Always stereo, so
    /// any output can play it.
    /// </summary>
    public IWaveProvider? Monitor => _monitor;

    /// <summary>Starts (or stops, with false) keeping the sound for <see cref="Monitor"/>.</summary>
    public IWaveProvider? ListenThrough(bool listen)
    {
        if (!listen)
        {
            _monitor = null;
            return null;
        }

        return _monitor ??= new BufferedWaveProvider(WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2))
        {
            BufferDuration = TimeSpan.FromSeconds(1),
            DiscardOnBufferOverflow = true,
            ReadFully = true,
        };
    }

    /// <summary>
    /// The plugins the sound goes through, in order, each started with <see cref="PluginHost.Start"/> at this
    /// recorder's rate and channels. Takes effect from the next block.
    /// </summary>
    public void SetPlugins(IReadOnlyList<ChainSlot> plugins) => _plugins = plugins.ToList();

    /// <summary>
    /// The loudest each channel has been since the last call (after the plugins, as recorded), from 0 to 1,
    /// and whether anything clipped. For the meters.
    /// </summary>
    public (float Left, float Right, bool Clipped) ReadMeters()
    {
        lock (_meterLock)
        {
            var result = (_peaks[0], _peaks[Channels > 1 ? 1 : 0], _clipped);
            _peaks[0] = _peaks[1] = 0;
            _clipped = false;
            return result;
        }
    }

    /// <summary>
    /// Takes sound from the input, on the input's thread: <paramref name="frames"/> frames of interleaved
    /// audio with <paramref name="inputChannels"/> channels, from which this recorder's channels start at
    /// <paramref name="firstChannel"/>. <paramref name="inputRate"/> is the rate it actually arrives at.
    /// </summary>
    public void Write(ReadOnlySpan<float> audio, int frames, int inputChannels, int firstChannel, int inputRate)
    {
        if (_stopping || frames <= 0)
        {
            return;
        }

        if (Volatile.Read(ref _queuedFrames) > inputRate * QueueSeconds)
        {
            Interlocked.Add(ref _dropped, frames);
            return;
        }

        // One block: the rate it came at, then the chosen channels interleaved.
        var block = new float[1 + frames * Channels];
        block[0] = inputRate;
        var second = Math.Min(firstChannel + 1, inputChannels - 1);
        for (var f = 0; f < frames; f++)
        {
            var at = f * inputChannels;
            block[1 + f * Channels] = audio[at + Math.Min(firstChannel, inputChannels - 1)];
            if (Channels > 1)
            {
                block[2 + f * Channels] = audio[at + second];
            }
        }

        Interlocked.Add(ref _queuedFrames, frames);
        _work.Enqueue(block);
        _ready.Release();
    }

    /// <summary>Starts writing a file, from the next sound to arrive.</summary>
    public void Start(RecordingTarget target) => Do(() => Open(target));

    /// <summary>Finishes the file being written: its markers, tags and cue sheet go in, and it's closed.</summary>
    public Task<FinishedRecording?> StopAsync()
    {
        var done = new TaskCompletionSource<FinishedRecording?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Do(() => done.TrySetResult(Close()));
        return done.Task;
    }

    /// <summary>Finishes the file and goes straight on in a new one, without losing a sample between them.</summary>
    public Task<FinishedRecording?> SplitAsync(RecordingTarget next)
    {
        var done = new TaskCompletionSource<FinishedRecording?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Do(() =>
        {
            var finished = Close();
            Open(next);
            done.TrySetResult(finished);
        });
        return done.Task;
    }

    /// <summary>Marks a new song starting now (the Record window's New track button).</summary>
    public void MarkTrack() => Do(() => AddMarker(_file?.Frames ?? 0, automatic: false));

    /// <summary>Takes out a mark, by where it is in the file.</summary>
    public void RemoveMarker(long frame) => Do(() =>
    {
        lock (_markerLock)
        {
            if (frame > 0)
            {
                _markers.RemoveAll(m => m.Frame == frame);
                _listening.RemoveAll(l => l.Marker == frame);
                Renumber();
            }
        }
    });

    /// <summary>Stops taking sound, finishing any file first, and hands the visuals back.</summary>
    public void Dispose()
    {
        if (_stopping)
        {
            return;
        }

        Do(() => Close());
        _stopping = true;
        _lookupCancel.Cancel();
        _ready.Release();
        _thread.Join(TimeSpan.FromSeconds(10));
        _capture.EndLive();
    }

    private void Do(Action command)
    {
        _work.Enqueue(command);
        _ready.Release();
    }

    // ---- The recorder's thread ----

    private void Run()
    {
        while (true)
        {
            _ready.Wait(TimeSpan.FromMilliseconds(250));
            while (_work.TryDequeue(out var item))
            {
                if (item is float[] block)
                {
                    Guard(() => Process(block));
                }
                else if (item is Action command)
                {
                    Guard(command);
                }
            }

            if (_file is not null && DateTime.UtcNow - _lastFlush > FlushEvery)
            {
                Guard(() =>
                {
                    _file?.Flush();
                    _dryFile?.Flush();
                    _lastFlush = DateTime.UtcNow;
                });
            }

            if (_stopping && _work.IsEmpty)
            {
                return;
            }
        }
    }

    // A file that can't be written stops the recording but keeps what was saved; the input carries on.
    private void Guard(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            try
            {
                Close();
            }
            catch (Exception)
            {
                // Already reported below.
            }

            Failed?.Invoke(this, ex);
        }
    }

    private void Process(float[] block)
    {
        var inputRate = (int)block[0];
        var frames = (block.Length - 1) / Channels;
        Interlocked.Add(ref _queuedFrames, -frames);
        var audio = block.AsSpan(1);
        if (inputRate != SampleRate)
        {
            audio = Resample(audio, frames, inputRate);
            frames = audio.Length / Channels;
            if (frames == 0)
            {
                return;
            }
        }

        if (_dryFile is not null)
        {
            _dryFile.Write(audio);
        }

        RunPlugins(audio, frames);
        if (_file is not null)
        {
            _file.Write(audio);
            Interlocked.Exchange(ref _recordedFrames, _file.Frames);
        }

        Share(audio, frames);
    }

    // Converts the input's rate to the recording's, carrying on smoothly from block to block.
    private Span<float> Resample(Span<float> audio, int frames, int inputRate)
    {
        if (_resampler is null || _resamplerRate != inputRate)
        {
            _resampler = new WdlResampler();
            _resampler.SetMode(true, 2, false);
            _resampler.SetFilterParms();
            _resampler.SetFeedMode(true);
            _resampler.SetRates(inputRate, SampleRate);
            _resamplerRate = inputRate;
        }

        var most = (int)((long)frames * SampleRate / inputRate) + 64;
        if (_resampled.Length < most * Channels)
        {
            _resampled = new float[most * Channels];
        }

        // The resampler may take the block in more than one helping.
        var made = 0;
        var used = 0;
        while (used < frames && made < most)
        {
            var wanted = _resampler.ResamplePrepare(frames - used, Channels, out var input, out var inputOffset);
            wanted = Math.Min(wanted, frames - used);
            audio.Slice(used * Channels, wanted * Channels).CopyTo(input.AsSpan(inputOffset));
            made += _resampler.ResampleOut(_resampled, made * Channels, wanted, most - made, Channels);
            used += wanted;
            if (wanted == 0)
            {
                break;
            }
        }

        return _resampled.AsSpan(0, made * Channels);
    }

    private void RunPlugins(Span<float> audio, int frames)
    {
        var plugins = _plugins;
        if (plugins.Count == 0)
        {
            return;
        }

        if (_planes.Length != Channels || _planes[0].Length < frames)
        {
            _planes = Enumerable.Range(0, Channels).Select(_ => new float[Math.Max(frames, 4096)]).ToArray();
        }

        for (var c = 0; c < Channels; c++)
        {
            for (var f = 0; f < frames; f++)
            {
                _planes[c][f] = audio[f * Channels + c];
            }
        }

        var changed = false;
        foreach (var slot in plugins)
        {
            if (slot.IsBypassed || slot.Host.IsGone)
            {
                continue;
            }

            try
            {
                slot.Host.Process(_planes, 0, frames);
                changed = true;
            }
            catch (Exception ex)
            {
                // The sound goes on without it; the window says which one stopped.
                PluginProblem = $"{slot.Host.Name} stopped working, so the sound goes past it now. ({ex.Message})";
            }
        }

        if (!changed)
        {
            return;
        }

        for (var c = 0; c < Channels; c++)
        {
            for (var f = 0; f < frames; f++)
            {
                audio[f * Channels + c] = _planes[c][f];
            }
        }
    }

    // To the meters, the visuals, the speakers and the song-change detector.
    private void Share(ReadOnlySpan<float> audio, int frames)
    {
        if (_mono.Length < frames)
        {
            _mono = new float[frames];
            _left = new float[frames];
            _right = new float[frames];
            _stereo = new float[frames * 2];
        }

        var peakLeft = 0f;
        var peakRight = 0f;
        for (var f = 0; f < frames; f++)
        {
            var left = audio[f * Channels];
            var right = Channels > 1 ? audio[f * Channels + 1] : left;
            _left[f] = left;
            _right[f] = right;
            _mono[f] = (left + right) / 2;
            _stereo[f * 2] = left;
            _stereo[f * 2 + 1] = right;
            peakLeft = Math.Max(peakLeft, Math.Abs(left));
            peakRight = Math.Max(peakRight, Math.Abs(right));
        }

        // Whole-number files clip at full scale; a float file keeps what's over, but it's still worth knowing.
        var clipped = peakLeft >= 0.999f || peakRight >= 0.999f;
        lock (_meterLock)
        {
            _peaks[0] = Math.Max(_peaks[0], peakLeft);
            _peaks[1] = Math.Max(_peaks[1], peakRight);
            _clipped |= clipped;
        }

        _capture.WriteLive(_mono.AsSpan(0, frames), _left.AsSpan(0, frames), _right.AsSpan(0, frames));
        if (_monitor is { } monitor)
        {
            var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(_stereo.AsSpan(0, frames * 2)).ToArray();
            // Kept short, so what's heard stays close to what's played (the input and output clocks drift apart).
            if (monitor.BufferedDuration > TimeSpan.FromMilliseconds(250))
            {
                monitor.ClearBuffer();
            }

            monitor.AddSamples(bytes, 0, bytes.Length);
        }

        if (_detector is { } detector && _file is { } file)
        {
            foreach (var at in detector.Add(_mono.AsSpan(0, frames)))
            {
                AddMarker(Math.Min(at, file.Frames), automatic: true);
            }
        }

        if (_file is { } recording)
        {
            Listen(_mono.AsSpan(0, frames), recording.Frames - frames);
        }
    }

    // ---- Song identification ----

    // A stretch of a track being gathered to send to ACRCloud.
    private sealed class Listening
    {
        public long Marker;        // the track's mark, by where it is
        public long From;          // where in the file the stretch starts
        public int Attempt;        // 1 or 2
        public float[] Sound = Array.Empty<float>();
        public int Filled;
    }

    // Call while holding _markerLock, or on the recorder's thread before anything else can see the marks.
    private void ListenFor(long marker, int attempt)
    {
        if (_identifier is null || _identifyProblem is not null)
        {
            return;
        }

        _listening.RemoveAll(l => l.Marker == marker);
        var into = attempt == 1 ? FirstLookup : SecondLookup;
        _listening.Add(new Listening
        {
            Marker = marker,
            From = marker + (long)(into.TotalSeconds * SampleRate),
            Attempt = attempt,
            Sound = new float[(int)(SongIdentifier.SampleLength.TotalSeconds * SampleRate)],
        });
    }

    // Gathers this block (which starts at blockStart in the file) into any stretch waiting for it.
    private void Listen(ReadOnlySpan<float> mono, long blockStart)
    {
        List<Listening>? ready = null;
        lock (_markerLock)
        {
            if (_listening.Count == 0)
            {
                return;
            }

            foreach (var listening in _listening)
            {
                var end = blockStart + mono.Length;
                var at = listening.From + listening.Filled;
                if (end <= at)
                {
                    continue;
                }

                var skip = (int)Math.Max(0, at - blockStart);
                var count = Math.Min(mono.Length - skip, listening.Sound.Length - listening.Filled);
                mono.Slice(skip, count).CopyTo(listening.Sound.AsSpan(listening.Filled));
                listening.Filled += count;
                if (listening.Filled >= listening.Sound.Length)
                {
                    (ready ??= new()).Add(listening);
                }
            }

            if (ready is null)
            {
                return;
            }

            foreach (var listening in ready)
            {
                _listening.Remove(listening);
            }

            // A stretch that ran into the next track's mark would be the wrong song.
            ready.RemoveAll(l => _markers.Any(m => m.Frame > l.Marker && m.Frame < l.From + l.Sound.Length));
        }

        foreach (var listening in ready)
        {
            Ask(listening);
        }
    }

    private void Ask(Listening listening)
    {
        if (_identifier is not { } identifier)
        {
            return;
        }

        var cancel = _lookupCancel.Token;
        var task = Task.Run(async () =>
        {
            var lookup = await identifier.IdentifyAsync(listening.Sound, SampleRate, cancel).ConfigureAwait(false);
            if (lookup.KeyProblem)
            {
                _identifyProblem = lookup.Problem;
            }

            TrackMarker? named = null;
            lock (_markerLock)
            {
                var index = _markers.FindIndex(m => m.Frame == listening.Marker);
                if (index < 0)
                {
                    return;
                }

                if (lookup.Song is { } song)
                {
                    _markers[index] = _markers[index] with { Song = song };
                    Renumber();
                    named = _markers.First(m => m.Frame == listening.Marker);
                }
                else if (listening.Attempt == 1 && !lookup.KeyProblem)
                {
                    ListenFor(listening.Marker, 2);
                }
            }

            if (named is not null)
            {
                SongIdentified?.Invoke(this, named);
            }
        }, cancel);
        lock (_lookups)
        {
            _lookups.RemoveAll(t => t.IsCompleted);
            _lookups.Add(task);
        }
    }

    // A file's last tracks are often still being looked up as it's stopped; they're given a few seconds.
    private void WaitForLookups()
    {
        Task[] waiting;
        lock (_lookups)
        {
            waiting = _lookups.Where(t => !t.IsCompleted).ToArray();
            _lookups.Clear();
        }

        try
        {
            Task.WaitAll(waiting, TimeSpan.FromSeconds(6));
        }
        catch (AggregateException)
        {
            // A lookup that failed just leaves its track unnamed.
        }
    }

    private void Open(RecordingTarget target)
    {
        if (_file is not null)
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target.Path))!);
        _file = new WavRecording(target.Path, SampleRate, Channels, BitDepth);
        try
        {
            _dryFile = target.DryPath is { } dry ? new WavRecording(dry, SampleRate, Channels, BitDepth) : null;
        }
        catch
        {
            _file.Dispose();
            _file = null;
            throw;
        }

        _target = target;
        RecordingsInProgress.Add(target.Path);
        if (target.DryPath is { } dryPath)
        {
            RecordingsInProgress.Add(dryPath);
        }

        _detector = DetectSongChanges ? new SongChangeDetector(SampleRate) : null;
        _lastFlush = DateTime.UtcNow;
        Interlocked.Exchange(ref _recordedFrames, 0);
        lock (_markerLock)
        {
            _markers.Clear();
            _listening.Clear();
            _markers.Add(new TrackMarker(0, "Track 1"));
            ListenFor(0, 1);
            Interlocked.Increment(ref _tracksVersion);
        }
    }

    /// <summary>The bit depth files are written at: 16, 24 or 32 (float). Set before <see cref="Start"/>.</summary>
    public int BitDepth { get; set; } = 24;

    private FinishedRecording? Close()
    {
        var file = _file;
        var dryFile = _dryFile;
        var target = _target;
        _file = null;
        _dryFile = null;
        _target = null;
        _detector = null;
        if (file is null || target is null)
        {
            return null;
        }

        // Finishing the files rewrites them, which lets the library see them now they're whole.
        RecordingsInProgress.Remove(target.Path);
        if (target.DryPath is { } dryPath)
        {
            RecordingsInProgress.Remove(dryPath);
        }

        lock (_markerLock)
        {
            _listening.Clear();
        }

        WaitForLookups();
        var tracks = Tracks;
        file.Finish(tracks, target.Title);
        dryFile?.Finish(tracks, target.Title);
        string? sheet = null;
        if (target.CueSheet && tracks.Count > 1)
        {
            sheet = Path.ChangeExtension(target.Path, ".cue");
            try
            {
                CueSheet.Write(sheet, target.Path, tracks.Select(t => new TrackMark(TimeSpan.FromSeconds((double)t.Frame / SampleRate),
                    t.Song?.Title ?? t.Title, t.Song is { Artist.Length: > 0 } song ? song.Artist : null)).ToList(), target.Title);
            }
            catch (Exception)
            {
                // The markers are in the WAV file itself anyway.
                sheet = null;
            }
        }

        return new FinishedRecording(target.Path, dryFile?.Path, sheet, file.Duration, tracks);
    }

    private void AddMarker(long frame, bool automatic)
    {
        if (_file is null || frame <= 0)
        {
            return;
        }

        TrackMarker? added = null;
        lock (_markerLock)
        {
            // Lucent's guess gives way to a mark made by hand near it, and a second press right after the first does nothing.
            var near = _markers.FirstOrDefault(m => m.Frame > 0 && Math.Abs(m.Frame - frame) < SampleRate * (automatic ? 30L : 5L));
            if (near is not null)
            {
                if (automatic || !near.Automatic)
                {
                    return;
                }

                _markers.Remove(near);
                _listening.RemoveAll(l => l.Marker == near.Frame);
            }

            added = new TrackMarker(frame, string.Empty, automatic);
            _markers.Add(added);
            Renumber();
            ListenFor(frame, 1);
            added = _markers.First(m => m.Frame == frame);
        }

        if (automatic)
        {
            SongChanged?.Invoke(this, added);
        }
    }

    // Call while holding _markerLock: puts the marks in order and names them Track 1, Track 2…, or by their song once it's known.
    private void Renumber()
    {
        _markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        for (var i = 0; i < _markers.Count; i++)
        {
            _markers[i] = _markers[i] with { Title = _markers[i].Song?.ToString() ?? $"Track {i + 1}" };
        }

        Interlocked.Increment(ref _tracksVersion);
    }
}
