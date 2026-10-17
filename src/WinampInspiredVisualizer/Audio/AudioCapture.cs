using System.Diagnostics;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Keeps the most recent audio that went to the speakers, as a mono mix and as its left and
/// right channels, so the visualizer and analyzer can look at it. Written by the audio thread, read by the render thread.
/// </summary>
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
    private long _written;
    private int _lastBlock;

    public int SampleRate { get; private set; } = 44100;

    /// <summary>True when no audio has arrived recently (paused, stopped, or nothing loaded).</summary>
    public bool IsSilent => _sinceLastWrite.Elapsed > TimeSpan.FromMilliseconds(250);

    public void Reset(int sampleRate)
    {
        lock (_lock)
        {
            SampleRate = sampleRate;
            Array.Clear(_ring);
            Array.Clear(_leftRing);
            Array.Clear(_rightRing);
            _written = 0;
            _lastBlock = 0;
        }
    }

    /// <summary>Adds mono audio; the left and right channels both get the same.</summary>
    public void Write(ReadOnlySpan<float> samples) => Write(samples, samples, samples);

    /// <summary>Adds audio as its mono mix and its left and right channels, all the same length.</summary>
    public void Write(ReadOnlySpan<float> samples, ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        lock (_lock)
        {
            for (var i = 0; i < samples.Length; i++)
            {
                var at = _written % Capacity;
                _ring[at] = samples[i];
                _leftRing[at] = left[i];
                _rightRing[at] = right[i];
                _written++;
            }

            _lastBlock = Math.Min(samples.Length, Capacity / 4);
            _sinceLastWrite.Restart();
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
