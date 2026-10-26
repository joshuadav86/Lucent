using NAudio.Dsp;
using Lucent.Audio;

namespace Lucent.Visualizer;

/// <summary>
/// Turns the captured audio into the numbers visuals react to: a smoothed
/// spectrum, the waveform, bass/mid/treble levels and a beat pulse.
/// Runs once per frame on the render thread. When recording a video, the audio is handed over
/// frame by frame instead, from the decoded file (see <see cref="Update(float, ReadOnlySpan{float}, int)"/>).
/// </summary>
public sealed class AudioAnalyzer
{
    /// <summary>Width of the audio texture: row 0 is the spectrum, row 1 the waveform.</summary>
    public const int TextureWidth = 256;

    private const int FftExponent = 11;
    private const int FftSize = 1 << FftExponent;
    private const int BandCount = 64;

    /// <summary>How many of the newest samples each frame looks at (about 46 ms at 44.1 kHz).</summary>
    public const int WindowSamples = FftSize;
    private const float LowestFrequency = 30f;
    private const float HighestFrequency = 16000f;

    // The bands, from the bottom, that PunchNow covers (the first 8 are the bass).
    private const int PunchBands = 44;

    private readonly AudioCapture? _capture;
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
        : this()
    {
        _capture = capture;
    }

    /// <summary>An analyzer with no capture to read: it's handed each frame's audio instead.</summary>
    public AudioAnalyzer()
    {
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

    /// <summary>The bass this frame, unsmoothed, 0 to 1 (a 60 dB range): what <see cref="LiveBeatTracker"/> listens to.</summary>
    public float BassNow { get; private set; }

    /// <summary>
    /// The level from just above the deep bass to the upper mids (about 65 Hz to 2 kHz) this frame, unsmoothed, 0 to 1:
    /// where drum hits jump across many bands at once, while a held bass note or hi-hats barely move it.
    /// </summary>
    public float PunchNow { get; private set; }

    /// <summary>The overall loudness this frame, unsmoothed, 0 to 1 across 60 dB.</summary>
    public float LoudnessNow { get; private set; }

    /// <summary>
    /// True while the playing song's beats are known (see <see cref="SongFollower"/>): the beat then follows them,
    /// through <see cref="MarkBeat"/>, instead of being guessed from jumps in the bass.
    /// </summary>
    public bool FollowsSong { get; set; }

    /// <summary>A beat of the song's own, <paramref name="strength"/> hard (0..1), this frame.</summary>
    public void MarkBeat(float strength)
    {
        Beat = strength;
        BeatCount++;
        _sinceBeat = 0;
    }

    public void Update(float deltaTime)
    {
        if (_capture is null || _capture.IsSilent)
        {
            Array.Clear(_samples);
        }
        else
        {
            var delay = _capture.DelaySamples;
            _capture.ReadLatest(_samples, delay);
        }

        Analyze(deltaTime, _capture?.SampleRate ?? 44100);
    }

    /// <summary>
    /// The same as <see cref="Update(float)"/>, but for audio handed over rather than captured:
    /// <paramref name="latest"/> ends at the moment being drawn (a mono mix, at <paramref name="sampleRate"/>).
    /// Only its newest samples are used; a shorter span is treated as silence before it.
    /// </summary>
    public void Update(float deltaTime, ReadOnlySpan<float> latest, int sampleRate)
    {
        var count = Math.Min(latest.Length, FftSize);
        Array.Clear(_samples, 0, FftSize - count);
        latest[^count..].CopyTo(_samples.AsSpan(FftSize - count));
        Analyze(deltaTime, sampleRate);
    }

    private void Analyze(float deltaTime, int sampleRate)
    {
        EnsureBands(sampleRate);

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
        var punchNow = 0f;

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
            else if (band < PunchBands)
            {
                punchNow += target / (PunchBands - 8);
            }
        }

        Bass = Average(0, 10);
        Mid = Average(10, 36);
        Treble = Average(36, BandCount);
        var rms = MathF.Sqrt(sumSquares / FftSize);
        BassNow = bassNow;
        PunchNow = punchNow;
        LoudnessNow = Math.Clamp((20f * MathF.Log10(rms + 1e-9f) + 60f) / 60f, 0f, 1f);
        Level += (Math.Clamp(rms * 3f, 0f, 1f) - Level) * release;

        DetectBeat(bassNow, deltaTime);
        FillTexture();
    }

    /// <summary>
    /// Fills <paramref name="left"/> and <paramref name="right"/> with the newest stereo samples, timed
    /// to the speakers like the rest of the analysis (for MilkDrop presets, which draw both channels).
    /// </summary>
    public void ReadStereoWaveform(float[] left, float[] right)
    {
        if (_capture is null)
        {
            // Drawing off screen (recording a video): only the mono mix handed to Update is known, so both
            // channels get the newest of it.
            var count = Math.Min(left.Length, _samples.Length);
            Array.Clear(left);
            Array.Clear(right);
            _samples.AsSpan(_samples.Length - count).CopyTo(left);
            _samples.AsSpan(_samples.Length - count).CopyTo(right);
            return;
        }

        if (_capture.IsSilent)
        {
            Array.Clear(left);
            Array.Clear(right);
            return;
        }

        var delay = _capture.DelaySamples;
        _capture.ReadLatestStereo(left, right, delay);
    }

    private void DetectBeat(float bassNow, float deltaTime)
    {
        _sinceBeat += deltaTime;
        if (!FollowsSong && bassNow > _bassAverage * 1.3f + 0.05f && _sinceBeat > 0.28)
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
