using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace WinampInspiredVisualizer.Audio;

/// <summary>Plays one track at a time and reports when it finishes.</summary>
public sealed class AudioPlayer : IDisposable
{
    /// <summary>How far ahead of the speakers the audio is decoded, in milliseconds.</summary>
    public const int OutputLatencyMs = 150;

    private WaveOutEvent? _output;
    private WaveStream? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private float _volume = 0.8f;
    private bool _stopRequested;

    /// <summary>Raised on the UI thread when a track plays through to the end.</summary>
    public event EventHandler? TrackEnded;

    /// <summary>Raised on the UI thread when the audio device reports an error.</summary>
    public event EventHandler<Exception>? PlaybackFailed;

    /// <summary>The audio being played, for the visualizer to analyze.</summary>
    public AudioCapture Capture { get; } = new();

    public bool HasTrack => _reader is not null;

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

        var reader = AudioReaderFactory.Open(path);
        try
        {
            // Capture before the volume control so visuals don't shrink when the music is turned down.
            var captured = new CapturingSampleProvider(reader.ToSampleProvider(), Capture);
            _volumeProvider = new VolumeSampleProvider(captured) { Volume = _volume };
            _output = new WaveOutEvent { DesiredLatency = OutputLatencyMs };
            _output.PlaybackStopped += OnPlaybackStopped;
            _output.Init(_volumeProvider);
            _reader = reader;
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
    }

    public void Dispose() => Close();

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
