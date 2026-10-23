using System.Diagnostics;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Keeps the most recent audio that went to the speakers, as a mono mix and as its left and
/// right channels, so the visualizer and analyzer can look at it. Written by the audio thread, read by the render thread.
/// </summary>
/// <remarks>
/// It can also listen to every app's sound instead (see each app's Audio/SystemAudio.cs). While it does,
/// what the player writes is left out, since the player's own sound is part of what's heard anyway.
/// A live input being recorded (see Recording/LiveRecorder.cs) comes before both while it's open.
/// While the editor plays (see <see cref="EditorFeed"/>), its sound is shown instead of the player's or other apps'.
/// </remarks>
/// <remarks>
/// The audio thread writes in blocks (one sound card buffer, about 75 ms, at a time), so reading
/// "the newest samples" would only move on when a block arrives: about 13 times a second, which
/// makes the meters look choppy. Instead the read position glides through the newest block at
/// the music's own speed, so every screen frame sees audio a little further on.
/// </remarks>
public sealed class AudioCapture
{
    private const int Capacity = 1 << 15;

    private readonly float[] _ring = new float[Capacity];
    private readonly float[] _leftRing = new float[Capacity];
    private readonly float[] _rightRing = new float[Capacity];
    private readonly object _lock = new();
    private readonly Stopwatch _sinceLastWrite = Stopwatch.StartNew();
    private readonly Stopwatch _sinceSound = Stopwatch.StartNew();
    private long _written;
    private int _lastBlock;
    private bool _external;
    private bool _live;
    private int _editors;
    private int _editorRate = 44100;
    private int _editorLatencyMs;
    private int _playerRate = 44100;
    private int _externalRate = 44100;

    /// <summary>How far ahead of the speakers the audio is decoded, in milliseconds.</summary>
    public const int OutputLatencyMs = 150;

    public int SampleRate { get; private set; } = 44100;

    /// <summary>True when no audio has arrived recently (paused, stopped, or nothing loaded).</summary>
    public bool IsSilent => _sinceLastWrite.Elapsed > TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// True when nothing above a whisper (-60 dB) has arrived for <paramref name="time"/>, written or not: another app's
    /// sound keeps arriving as silence while that app plays somewhere else (Spotify on a TV, say).
    /// </summary>
    public bool HeardNothingFor(TimeSpan time) => _sinceSound.Elapsed > time;

    /// <summary>True while it's listening to every app's sound rather than the player's.</summary>
    public bool IsExternal => _external;

    /// <summary>True while a live input (an audio interface being recorded) is what's shown.</summary>
    public bool IsLive => _live;

    /// <summary>True while the editor's playback is what's shown.</summary>
    public bool IsEditor => _editors > 0 && !_live;

    /// <summary>
    /// How many of the newest samples the speakers haven't played yet, for <see cref="ReadLatest"/>. The
    /// player and the editor decode ahead of the speakers; other apps' sound is caught as it's played, so there's no wait.
    /// </summary>
    public int DelaySamples => _live ? 0
        : _editors > 0 ? SampleRate * _editorLatencyMs / 1000 * 2 / 3
        : _external ? 0
        : SampleRate * OutputLatencyMs / 1000 * 2 / 3;

    public void Reset(int sampleRate)
    {
        lock (_lock)
        {
            // Kept for later while other apps are being listened to.
            _playerRate = sampleRate;
            if (!_external && !_live && _editors == 0)
            {
                Clear(sampleRate);
            }
        }
    }

    /// <summary>Starts taking other apps' sound, at <paramref name="sampleRate"/>, through <see cref="WriteExternal"/>.</summary>
    public void BeginExternal(int sampleRate)
    {
        lock (_lock)
        {
            _external = true;
            _externalRate = sampleRate;
            if (!_live && _editors == 0)
            {
                Clear(sampleRate);
            }
        }
    }

    /// <summary>Goes back to the player's own sound.</summary>
    public void EndExternal()
    {
        lock (_lock)
        {
            _external = false;
            if (!_live && _editors == 0)
            {
                Clear(_playerRate);
            }
        }
    }

    /// <summary>Starts taking a live input's sound, at <paramref name="sampleRate"/>, through <see cref="WriteLive"/>.</summary>
    public void BeginLive(int sampleRate)
    {
        lock (_lock)
        {
            _live = true;
            Clear(sampleRate);
        }
    }

    /// <summary>Goes back to other apps' sound, if that was being listened to, or the player's.</summary>
    public void EndLive()
    {
        lock (_lock)
        {
            _live = false;
            Clear(_editors > 0 ? _editorRate : _external ? _externalRate : _playerRate);
        }
    }

    /// <summary>
    /// Starts taking the editor's sound, at <paramref name="sampleRate"/>, through <see cref="WriteEditor"/>.
    /// <paramref name="latencyMs"/> is how far ahead of its speakers the editor's audio is read.
    /// </summary>
    public void BeginEditor(int sampleRate, int latencyMs)
    {
        lock (_lock)
        {
            _editors++;
            _editorRate = sampleRate;
            _editorLatencyMs = latencyMs;
            if (!_live)
            {
                Clear(sampleRate);
            }
        }
    }

    /// <summary>Goes back to what was shown before the editor played.</summary>
    public void EndEditor()
    {
        lock (_lock)
        {
            _editors = Math.Max(0, _editors - 1);
            if (!_live && _editors == 0)
            {
                Clear(_external ? _externalRate : _playerRate);
            }
        }
    }

    /// <summary>Adds mono audio; the left and right channels both get the same.</summary>
    public void Write(ReadOnlySpan<float> samples) => Write(samples, samples, samples);

    /// <summary>Adds the player's audio as its mono mix and its left and right channels, all the same length.</summary>
    public void Write(ReadOnlySpan<float> samples, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        lock (_lock)
        {
            if (!_external && !_live && _editors == 0)
            {
                Append(samples, left, right);
            }
        }
    }

    /// <summary>Adds other apps' sound, as <see cref="Write(ReadOnlySpan{float}, ReadOnlySpan{float}, ReadOnlySpan{float})"/> does the player's.</summary>
    public void WriteExternal(ReadOnlySpan<float> samples, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        lock (_lock)
        {
            if (_external && !_live && _editors == 0)
            {
                Append(samples, left, right);
            }
        }
    }

    /// <summary>Adds the editor's sound, as <see cref="Write(ReadOnlySpan{float}, ReadOnlySpan{float}, ReadOnlySpan{float})"/> does the player's.</summary>
    public void WriteEditor(ReadOnlySpan<float> samples, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        lock (_lock)
        {
            if (_editors > 0 && !_live)
            {
                Append(samples, left, right);
            }
        }
    }

    /// <summary>Adds a live input's sound, as <see cref="Write(ReadOnlySpan{float}, ReadOnlySpan{float}, ReadOnlySpan{float})"/> does the player's.</summary>
    public void WriteLive(ReadOnlySpan<float> samples, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        lock (_lock)
        {
            if (_live)
            {
                Append(samples, left, right);
            }
        }
    }

    // Call while holding the lock.
    private void Clear(int sampleRate)
    {
        SampleRate = sampleRate;
        Array.Clear(_ring);
        Array.Clear(_leftRing);
        Array.Clear(_rightRing);
        _written = 0;
        _lastBlock = 0;
    }

    // Call while holding the lock.
    private void Append(ReadOnlySpan<float> samples, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        var loudest = 0f;
        for (var i = 0; i < samples.Length; i++)
        {
            loudest = Math.Max(loudest, Math.Abs(samples[i]));
            var at = _written % Capacity;
            _ring[at] = samples[i];
            _leftRing[at] = left[i];
            _rightRing[at] = right[i];
            _written++;
        }

        _lastBlock = Math.Min(samples.Length, Capacity / 4);
        _sinceLastWrite.Restart();
        if (loudest > 0.001f)
        {
            _sinceSound.Restart();
        }
    }

    /// <summary>
    /// Copies the newest samples into <paramref name="destination"/>, skipping the
    /// last <paramref name="delaySamples"/> because the sound card hasn't played them yet.
    /// </summary>
    public void ReadLatest(float[] destination, int delaySamples)
    {
        lock (_lock)
        {
            var start = StartOfLatest(destination.Length, delaySamples);
            for (var i = 0; i < destination.Length; i++)
            {
                var index = start + i;
                destination[i] = index < 0 ? 0f : _ring[index % Capacity];
            }
        }
    }

    /// <summary>The same as <see cref="ReadLatest"/>, but the left and right channels separately.</summary>
    public void ReadLatestStereo(float[] left, float[] right, int delaySamples)
    {
        lock (_lock)
        {
            var start = StartOfLatest(left.Length, delaySamples);
            for (var i = 0; i < left.Length; i++)
            {
                var index = start + i;
                left[i] = index < 0 ? 0f : _leftRing[index % Capacity];
                right[i] = index < 0 ? 0f : _rightRing[index % Capacity];
            }
        }
    }

    // Where a read of the newest <paramref name="length"/> samples starts. Call while holding the lock.
    private long StartOfLatest(int length, int delaySamples)
    {
        // How far into the newest block the music has got since it arrived.
        var played = (long)Math.Min(_lastBlock, _sinceLastWrite.Elapsed.TotalSeconds * SampleRate);
        var end = Math.Max(0, _written - _lastBlock + played - delaySamples);
        return end - length;
    }
}
