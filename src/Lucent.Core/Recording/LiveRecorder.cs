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
        _detector = DetectSongChanges ? new SongChangeDetector(SampleRate) : null;
        _lastFlush = DateTime.UtcNow;
        Interlocked.Exchange(ref _recordedFrames, 0);
        lock (_markerLock)
        {
            _markers.Clear();
            _markers.Add(new TrackMarker(0, "Track 1"));
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

        var tracks = Tracks;
        file.Finish(tracks, target.Title);
        dryFile?.Finish(tracks, target.Title);
        string? sheet = null;
        if (target.CueSheet && tracks.Count > 1)
        {
            sheet = Path.ChangeExtension(target.Path, ".cue");
            try
            {
                CueSheet.Write(sheet, target.Path, tracks.Select(t => new TrackMark(TimeSpan.FromSeconds((double)t.Frame / SampleRate), t.Title)).ToList(), target.Title);
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
            }

            added = new TrackMarker(frame, string.Empty, automatic);
            _markers.Add(added);
            Renumber();
            added = _markers.First(m => m.Frame == frame);
        }

        if (automatic)
        {
            SongChanged?.Invoke(this, added);
        }
    }

    // Call while holding _markerLock: puts the marks in order and names them Track 1, Track 2…
    private void Renumber()
    {
        _markers.Sort((a, b) => a.Frame.CompareTo(b.Frame));
        for (var i = 0; i < _markers.Count; i++)
        {
            _markers[i] = _markers[i] with { Title = $"Track {i + 1}" };
        }
    }
}
