using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using WinampInspiredVisualizer.Audio;

namespace Lucent.Mac.Audio;

/// <summary>
/// Plays one track at a time, like the Windows app's AudioPlayer: decoded by Lucent.Core's
/// AudioReaderFactory, through the shared equalizer and capture (so the visuals hear it), out of
/// the Mac's speakers with Core Audio.
/// </summary>
public sealed class MacPlayer : IDisposable
{
    private AudioQueueOutput? _output;
    private WaveStream? _reader;
    private VolumeSampleProvider? _volumeProvider;
    private float _volume = 0.8f;
    private bool _stopRequested;

    /// <summary>Raised (on a background thread) when a track plays through to the end.</summary>
    public event EventHandler? TrackEnded;

    /// <summary>The audio being played, for the visuals to analyze.</summary>
    public AudioCapture Capture { get; } = new();

    public Equalizer Equalizer { get; } = new();

    public string? CurrentPath { get; private set; }

    public bool IsPlaying => _output?.PlaybackState == PlaybackState.Playing;

    public TimeSpan Duration => _reader?.TotalTime ?? TimeSpan.Zero;

    public TimeSpan Position
    {
        get => _reader?.CurrentTime ?? TimeSpan.Zero;
        set
        {
            if (_reader is not null)
            {
                _reader.CurrentTime = value < TimeSpan.Zero ? TimeSpan.Zero : value > Duration ? Duration : value;
            }
        }
    }

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

    /// <summary>Opens a file, ready to play. Throws if it can't be decoded.</summary>
    public void Load(string path)
    {
        Close();
        var reader = AudioReaderFactory.Open(path);
        try
        {
            // The same order as Windows: equalize, then capture for the visuals, then the volume.
            var equalized = Equalizer.Attach(reader.ToSampleProvider());
            var captured = new CapturingSampleProvider(equalized, Capture);
            _volumeProvider = new VolumeSampleProvider(captured) { Volume = _volume };
            _output = new AudioQueueOutput(AudioCapture.OutputLatencyMs);
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
        _stopRequested = false;
        _output?.Play();
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
        CurrentPath = null;
        if (_output is not null)
        {
            _output.PlaybackStopped -= OnPlaybackStopped;
            _output.Dispose();
            _output = null;
        }

        _reader?.Dispose();
        _reader = null;
        _volumeProvider = null;
        Equalizer.Detach();
    }

    public void Dispose() => Close();

    private void OnPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        if (!_stopRequested)
        {
            TrackEnded?.Invoke(this, EventArgs.Empty);
        }
    }
}
