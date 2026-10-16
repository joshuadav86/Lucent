using System.Diagnostics;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Keeps the most recent mono audio that went to the speakers, so the
/// visualizer can analyze it. Written by the audio thread, read by the render thread.
/// </summary>
public sealed class AudioCapture
{
    private const int Capacity = 1 << 15;

    private readonly float[] _ring = new float[Capacity];
    private readonly object _lock = new();
    private readonly Stopwatch _sinceLastWrite = Stopwatch.StartNew();
    private long _written;

    public int SampleRate { get; private set; } = 44100;

    /// <summary>True when no audio has arrived recently (paused, stopped, or nothing loaded).</summary>
    public bool IsSilent => _sinceLastWrite.Elapsed > TimeSpan.FromMilliseconds(250);

    public void Reset(int sampleRate)
    {
        lock (_lock)
        {
            SampleRate = sampleRate;
            Array.Clear(_ring);
            _written = 0;
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (_lock)
        {
            foreach (var sample in samples)
            {
                _ring[_written % Capacity] = sample;
                _written++;
            }

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
            var end = Math.Max(0, _written - delaySamples);
            var start = end - destination.Length;
            for (var i = 0; i < destination.Length; i++)
            {
                var index = start + i;
                destination[i] = index < 0 ? 0f : _ring[index % Capacity];
            }
        }
    }
}
