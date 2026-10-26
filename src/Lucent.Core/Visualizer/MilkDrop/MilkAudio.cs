namespace Lucent.Visualizer.MilkDrop;

/// <summary>
/// The sound as MilkDrop presets expect it, made from Lucent's own analysis. MilkDrop's bass, mid
/// and treb are relative: about 1 for the song's usual level, above 1 when louder (a kick drum
/// might reach 2), below when quieter. The _att forms are the same, smoothed. Lucent's analyzer
/// gives levels on a decibel-like 0..1 scale, so they're turned back into energy and divided by
/// a slow running average, the way MilkDrop does it. Waveforms are 576 stereo samples (Winamp's
/// size) and the spectrum is Lucent's, stretched to 512 bins.
/// </summary>
public sealed class MilkAudio
{
    public const int WaveformSamples = 576;
    public const int SpectrumSamples = 512;

    private readonly double[] _average = { 1, 1, 1 };
    private readonly double[] _longAverage = { 1, 1, 1 };
    private readonly double[] _immediate = new double[3];
    private int _updates;

    public float[] WaveLeft { get; } = new float[WaveformSamples];

    public float[] WaveRight { get; } = new float[WaveformSamples];

    public float[] Spectrum { get; } = new float[SpectrumSamples];

    public double Bass { get; private set; } = 1;

    public double Mid { get; private set; } = 1;

    public double Treb { get; private set; } = 1;

    public double BassAtt { get; private set; } = 1;

    public double MidAtt { get; private set; } = 1;

    public double TrebAtt { get; private set; } = 1;

    public double Vol => (Bass + Mid + Treb) / 3;

    public double VolAtt => (BassAtt + MidAtt + TrebAtt) / 3;

    /// <summary>Takes this frame's sound from the analyzer, which has already been updated this frame.</summary>
    public void Update(AudioAnalyzer analyzer, double deltaTime)
    {
        analyzer.ReadStereoWaveform(WaveLeft, WaveRight);

        // Row 0 of the analyzer's texture is its spectrum, low to high, 0..1.
        var bands = analyzer.TextureData;
        var width = AudioAnalyzer.TextureWidth;
        for (var i = 0; i < SpectrumSamples; i++)
        {
            var position = (float)i / (SpectrumSamples - 1) * (width - 1);
            var low = (int)position;
            var high = Math.Min(low + 1, width - 1);
            Spectrum[i] = bands[low] + (bands[high] - bands[low]) * (position - low);
        }

        Update(analyzer.Bass, analyzer.Mid, analyzer.Treble, deltaTime);
    }

    /// <summary>Works out bass, mid and treb (and their _att forms) from 0..1 levels.</summary>
    public void Update(float bass, float mid, float treble, double deltaTime)
    {
        // MilkDrop's rates are per frame at 30 fps; scale them to this frame's length.
        var frames = Math.Clamp(deltaTime * 30.0, 0.01, 10.0);
        _immediate[0] = Energy(bass);
        _immediate[1] = Energy(mid);
        _immediate[2] = Energy(treble);
        for (var band = 0; band < 3; band++)
        {
            var immediate = _immediate[band];
            var rate = Math.Pow(immediate > _average[band] ? 0.2 : 0.5, frames);
            _average[band] = _average[band] * rate + immediate * (1 - rate);

            // The long average settles quickly at first, then follows the song slowly.
            var longRate = Math.Pow(_updates < 50 ? 0.9 : 0.992, frames);
            _longAverage[band] = _longAverage[band] * longRate + immediate * (1 - longRate);
        }

        _updates++;
        Bass = Relative(_immediate[0], 0);
        Mid = Relative(_immediate[1], 1);
        Treb = Relative(_immediate[2], 2);
        BassAtt = Relative(_average[0], 0);
        MidAtt = Relative(_average[1], 1);
        TrebAtt = Relative(_average[2], 2);
    }

    // The analyzer's levels span 60 dB; this is the matching amplitude, from 0.001 to 1.
    private static double Energy(float level) => Math.Pow(10.0, (Math.Clamp(level, 0f, 1f) - 1.0) * 3.0);

    private double Relative(double value, int band) =>
        _longAverage[band] < 0.0001 ? 1.0 : Math.Clamp(value / _longAverage[band], 0.0, 10.0);
}
