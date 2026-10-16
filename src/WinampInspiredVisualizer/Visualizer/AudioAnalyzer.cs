using NAudio.Dsp;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer.Visualizer;

/// <summary>
/// Turns the captured audio into the numbers visuals react to: a smoothed
/// spectrum, the waveform, bass/mid/treble levels and a beat pulse.
/// Runs once per frame on the render thread.
/// </summary>
public sealed class AudioAnalyzer
{
    /// <summary>Width of the audio texture: row 0 is the spectrum, row 1 the waveform.</summary>
    public const int TextureWidth = 256;

    private const int FftExponent = 11;
    private const int FftSize = 1 << FftExponent;
    private const int BandCount = 64;
    private const float LowestFrequency = 30f;
    private const float HighestFrequency = 16000f;

    private readonly AudioCapture _capture;
    private readonly float[] _samples = new float[FftSize];
    private readonly Complex[] _fft = new Complex[FftSize];
    private readonly float[] _window = new float[FftSize];
    private readonly float[] _bands = new float[BandCount];
    private readonly int[] _bandStart = new int[BandCount];
    private readonly int[] _bandEnd = new int[BandCount];
    private readonly float[] _bandTilt = new float[BandCount];
    private int _bandSampleRate;
    private float _bassAverage;
    private double _sinceBeat = 10;

    public AudioAnalyzer(AudioCapture capture)
    {
        _capture = capture;
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = (float)FastFourierTransform.HannWindow(i, FftSize);
        }
    }

    /// <summary>Spectrum (row 0) and waveform (row 1), uploaded to the GPU each frame.</summary>
    public float[] TextureData { get; } = new float[TextureWidth * 2];

    public float Bass { get; private set; }

    public float Mid { get; private set; }

    public float Treble { get; private set; }

    /// <summary>Jumps to 1 on each detected beat, then fades back to 0.</summary>
    public float Beat { get; private set; }

    /// <summary>Overall loudness, roughly 0 to 1.</summary>
    public float Level { get; private set; }

    public int BeatCount { get; private set; }

    public void Update(float deltaTime)
    {
        var silent = _capture.IsSilent;
        if (silent)
        {
            Array.Clear(_samples);
        }
        else
        {
            var delay = _capture.SampleRate * AudioPlayer.OutputLatencyMs / 1000 * 2 / 3;
            _capture.ReadLatest(_samples, delay);
        }

        EnsureBands(_capture.SampleRate);

        var sumSquares = 0f;
        for (var i = 0; i < FftSize; i++)
        {
            sumSquares += _samples[i] * _samples[i];
            _fft[i].X = _samples[i] * _window[i];
            _fft[i].Y = 0;
        }

        FastFourierTransform.FFT(true, FftExponent, _fft);

        // Fast attack, slower release, independent of frame rate.
        var attack = 1f - MathF.Exp(-deltaTime * 35f);
        var release = 1f - MathF.Exp(-deltaTime * 7f);
        var bassNow = 0f;

        for (var band = 0; band < BandCount; band++)
        {
            var peak = 0f;
            for (var bin = _bandStart[band]; bin < _bandEnd[band]; bin++)
            {
                var magnitude = MathF.Sqrt(_fft[bin].X * _fft[bin].X + _fft[bin].Y * _fft[bin].Y);
                peak = MathF.Max(peak, magnitude);
            }

            var decibels = 20f * MathF.Log10(peak + 1e-9f) + _bandTilt[band];
            var target = Math.Clamp((decibels + 72f) / 60f, 0f, 1f);
            var current = _bands[band];
            _bands[band] = current + (target - current) * (target > current ? attack : release);

            if (band < 8)
            {
                bassNow += target / 8f;
            }
        }

        Bass = Average(0, 10);
        Mid = Average(10, 36);
        Treble = Average(36, BandCount);
        var rms = MathF.Sqrt(sumSquares / FftSize);
        Level += (Math.Clamp(rms * 3f, 0f, 1f) - Level) * release;

        DetectBeat(bassNow, deltaTime);
        FillTexture();
    }

    private void DetectBeat(float bassNow, float deltaTime)
    {
        _sinceBeat += deltaTime;
        if (bassNow > _bassAverage * 1.3f + 0.05f && _sinceBeat > 0.28)
        {
            Beat = 1f;
            BeatCount++;
            _sinceBeat = 0;
        }

        _bassAverage += (bassNow - _bassAverage) * (1f - MathF.Exp(-deltaTime * 1.5f));
        Beat *= MathF.Exp(-deltaTime * 5f);
    }

    private void FillTexture()
    {
        // Row 0: the 64 bands stretched across 256 texels.
        for (var x = 0; x < TextureWidth; x++)
        {
            var position = (float)x / (TextureWidth - 1) * (BandCount - 1);
            var low = (int)position;
            var high = Math.Min(low + 1, BandCount - 1);
            var t = position - low;
            TextureData[x] = _bands[low] + (_bands[high] - _bands[low]) * t;
        }

        // Row 1: the waveform, from the most recent ~46 ms of audio.
        const int step = FftSize / TextureWidth;
        for (var x = 0; x < TextureWidth; x++)
        {
            TextureData[TextureWidth + x] = Math.Clamp(_samples[x * step], -1f, 1f);
        }
    }

    private float Average(int from, int to)
    {
        var sum = 0f;
        for (var i = from; i < to; i++)
        {
            sum += _bands[i];
        }

        return sum / (to - from);
    }

    private void EnsureBands(int sampleRate)
    {
        if (sampleRate == _bandSampleRate || sampleRate <= 0)
        {
            return;
        }

        _bandSampleRate = sampleRate;
        var binHz = (float)sampleRate / FftSize;
        var ratio = HighestFrequency / LowestFrequency;
        for (var band = 0; band < BandCount; band++)
        {
            var lowHz = LowestFrequency * MathF.Pow(ratio, (float)band / BandCount);
            var highHz = LowestFrequency * MathF.Pow(ratio, (float)(band + 1) / BandCount);
            var start = Math.Clamp((int)(lowHz / binHz), 1, FftSize / 2 - 1);
            var end = Math.Clamp((int)(highHz / binHz), start + 1, FftSize / 2);
            _bandStart[band] = start;
            _bandEnd[band] = end;

            // Music has much less energy up high; tilt +3 dB per octave so treble shows up.
            var centreHz = MathF.Sqrt(lowHz * highHz);
            _bandTilt[band] = 3f * MathF.Log2(centreHz / 1000f);
        }
    }
}
