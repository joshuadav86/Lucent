using Avalonia.Threading;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WinampInspiredVisualizer.Audio;

namespace Lucent.Mac.Audio;

/// <summary>
/// Plays one track at a time and reports when it finishes: the Windows app's AudioPlayer, with Core Audio
/// (<see cref="AudioQueueOutput"/>) in place of WaveOut.
/// </summary>
public sealed class AudioPlayer : IDisposable
{
    /// <summary>How far ahead of the speakers the audio is decoded, in milliseconds.</summary>
    public const int OutputLatencyMs = AudioCapture.OutputLatencyMs;

    private AudioQueueOutput? _output;
    private WaveStream? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private float _volume = 0.8f;
    private bool _stopRequested;
    private bool _disposed;

    // Counts every Load and Close, so a file reopened after being released doesn't replace one the user chose meanwhile.
    private int _generation;

    /// <summary>Raised on the UI thread when a track plays through to the end.</summary>
    public event EventHandler? TrackEnded;

    /// <summary>Raised on the UI thread when the audio device reports an error.</summary>
    public event EventHandler<Exception>? PlaybackFailed;

    /// <summary>The audio being played, for the visualizer to analyze.</summary>
    public AudioCapture Capture { get; } = new();

    /// <summary>The equalizer every track plays through. Its settings carry over from track to track.</summary>
    public Equalizer Equalizer { get; } = new();

    public bool HasTrack => _reader is not null;

    /// <summary>The file that's loaded, or null when nothing is.</summary>
    public string? CurrentPath { get; private set; }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader is null)
            {
                return;
            }

            var clamped = value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration ? Duration : value;
            _reader.CurrentTime = clamped;
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
        Close();
        _generation++;

        var reader = AudioReaderFactory.Open(path);
        try
        {
            // Equalize before capturing, so the visuals respond to what you hear, and capture before
            // the volume control so visuals don't shrink when the music is turned down.
            var equalized = Equalizer.Attach(reader.ToSampleProvider());
            var captured = new CapturingSampleProvider(equalized, Capture);
            _volumeProvider = new VolumeSampleProvider(captured) { Volume = _volume };
            _output = new AudioQueueOutput(OutputLatencyMs);
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(new SampleToWaveProvider(_volumeProvider));
            _reader = reader;
            CurrentPath = path;
        }
        catch
        {
            reader.Dispose();
            Close();
            throw;
        }
    }

    public void Play()
    {
        if (_output is null)
        {
            return;
        }

        _stopRequested = false;
        _output.Play();
    }

    public void Pause() => _output?.Pause();

    public void Stop()
    {
        if (_output is null || _reader is null)
        {
            return;
        }

        _stopRequested = true;
        _output.Stop();
        _reader.Position = 0;
    }

    public void Close()
    {
        _generation++;
        CurrentPath = null;
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Stop();
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
        _volumeProvider = null;
        Equalizer.Detach();
    }

    public void Dispose()
    {
        _disposed = true;
        Close();
    }

    /// <summary>True when <paramref name="path"/> is the file that's loaded.</summary>
    public bool IsCurrentFile(string path) =>
        CurrentPath is not null && string.Equals(Path.GetFullPath(CurrentPath), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Runs <paramref name="work"/> with <paramref name="path"/> closed, if it's the file being played, then opens
    /// it again at the same place and carries on playing (or stays paused). The Mac lets a file be replaced
    /// while it's open, but the reader would go on reading the old copy, so tags are saved the same way as on Windows.
    /// For any other file the work just runs. Call it from the UI thread.
    /// </summary>
    public async Task RunWithFileReleasedAsync(string path, Func<Task> work)
    {
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

    // Core Audio reports from its own thread; the player's listeners expect the UI thread, as on Windows.
    private void OnPlaybackStopped(object? sender, StoppedEventArgs e) => Dispatcher.UIThread.Post(() => OnStoppedOnUiThread(sender, e));

    private void OnStoppedOnUiThread(object? sender, StoppedEventArgs e)
    {
        if (!ReferenceEquals(sender, _output))
        {
            return;
        }

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
