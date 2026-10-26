using System.Runtime.CompilerServices;

namespace Lucent.Visualizer.Avs;

/// <summary>
/// The sound as AVS presets expect it (AVS's visdata[2][2][576]): for each of the two channels, 576
/// spectrum values (0..255, low to high frequency) and 576 waveform samples (-128..127), Winamp's
/// sizes, plus AVS's own beat detection. Made from Lucent's analysis each frame.
/// </summary>
public sealed class AvsAudio
{
    public const int Samples = 576;

    // Lucent's spectrum is 64 bands from 30 Hz to 16 kHz, on a log scale; Winamp's ran evenly in frequency.
    private const double LowestFrequency = 30.0;
    private const double HighestFrequency = 16000.0;
    private const double WinampTopFrequency = 11025.0;

    private readonly byte[] _spectrum = new byte[2 * Samples];
    private readonly sbyte[] _waveform = new sbyte[2 * Samples];
    private readonly float[] _left = new float[Samples];
    private readonly float[] _right = new float[Samples];
    private int _beatPeak1;
    private int _beatPeak2;
    private int _beatPeak1Peak;
    private int _beatCount;
    private int _lucentBeats = -1;

    /// <summary>Whether this frame is on a beat, by AVS's own detector (or Lucent's, when that's louder about it).</summary>
    public bool IsBeat { get; private set; }

    /// <summary>A spectrum value, 0..255: <paramref name="channel"/> 0 is left, 1 right; <paramref name="index"/> 0..575.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Spectrum(int channel, int index) => _spectrum[channel * Samples + index];

    /// <summary>A waveform sample, -128..127, as AVS's signed chars.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Waveform(int channel, int index) => _waveform[channel * Samples + index];

    /// <summary>
    /// visdata[type][channel][index] as AVS's C code reads it, a signed char: type 0 is the spectrum
    /// (so values over 127 read as negative, as they did in AVS), type 1 the waveform.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Signed(int type, int channel, int index) =>
        type == 0 ? (sbyte)_spectrum[channel * Samples + index] : _waveform[channel * Samples + index];

    /// <summary>visdata[type][channel][index] read as an unsigned char (0..255), as some effects do.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Unsigned(int type, int channel, int index) =>
        type == 0 ? _spectrum[channel * Samples + index] : (byte)_waveform[channel * Samples + index];

    /// <summary>Takes this frame's sound from the analyzer, which has already been updated this frame.</summary>
    public void Update(AudioAnalyzer analyzer)
    {
        analyzer.ReadStereoWaveform(_left, _right);
        var bands = analyzer.TextureData;
        Update(_left, _right, frequency =>
        {
            // Row 0 of the analyzer's texture is its 64 bands stretched over 256 texels.
            var position = Math.Log(Math.Max(frequency, LowestFrequency) / LowestFrequency) / Math.Log(HighestFrequency / LowestFrequency);
            var texel = Math.Clamp(position, 0, 1) * (AudioAnalyzer.TextureWidth - 1);
            var low = (int)texel;
            var high = Math.Min(low + 1, AudioAnalyzer.TextureWidth - 1);
            return bands[low] + (bands[high] - bands[low]) * (float)(texel - low);
        }, _lucentBeats >= 0 && analyzer.BeatCount != _lucentBeats);
        _lucentBeats = analyzer.BeatCount;
    }

    /// <summary>
    /// Takes a frame's sound directly: two channels of 576 samples (-1..1), and the spectrum's level
    /// (0..1) at a frequency in Hz. <paramref name="lucentBeat"/> is Lucent's own beat, which counts too.
    /// </summary>
    public void Update(float[] left, float[] right, Func<double, float> levelAt, bool lucentBeat = false)
    {
        for (var i = 0; i < Samples; i++)
        {
            _waveform[i] = (sbyte)Math.Clamp((int)MathF.Round(left[i] * 127f), -128, 127);
            _waveform[Samples + i] = (sbyte)Math.Clamp((int)MathF.Round(right[i] * 127f), -128, 127);

            // Winamp's 576 values run evenly from 0 Hz to half of 22 kHz.
            var level = levelAt((i + 0.5) * WinampTopFrequency / Samples);
            var value = (byte)Math.Clamp((int)(level * 255f), 0, 255);
            _spectrum[i] = value;
            _spectrum[Samples + i] = value;
        }

        IsBeat = DetectBeat() || lucentBeat;
    }

    /// <summary>AVS's beat detection (main.cpp): a jump in the waveform's loudness over its recent peak.</summary>
    private bool DetectBeat()
    {
        var loudest = 0;
        for (var channel = 0; channel < 2; channel++)
        {
            var sum = 0;
            for (var i = 0; i < Samples; i++)
            {
                sum += Math.Abs((int)_waveform[channel * Samples + i]);
            }

            loudest = Math.Max(loudest, sum);
        }

        var beat = false;
        _beatPeak1 = (_beatPeak1 * 125 + _beatPeak2 * 3) / 128;
        _beatCount++;
        if (loudest >= _beatPeak1 * 34 / 32 && loudest > Samples * 16)
        {
            if (_beatCount > 0)
            {
                _beatCount = 0;
                beat = true;
            }

            _beatPeak1 = (loudest + _beatPeak1Peak) / 2;
            _beatPeak1Peak = loudest;
        }
        else if (loudest > _beatPeak2)
        {
            _beatPeak2 = loudest;
        }
        else
        {
            _beatPeak2 = _beatPeak2 * 14 / 16;
        }

        return beat;
    }

    /// <summary>
    /// getspec and getosc (avs_eelif.cpp): the average of <paramref name="width"/> values around
    /// <paramref name="band"/> (both 0..1 across the 576), from channel 0 (both, averaged), 1 (left) or 2 (right).
    /// </summary>
    public double Get(bool waveform, double band, double width, double channel)
    {
        var bc = (int)(band * Samples);
        var bw = (int)(width * Samples);
        var ch = (int)(channel + 0.5);
        if (ch is < 0 or > 2)
        {
            return 0.0;
        }

        if (bw < 1)
        {
            bw = 1;
        }

        bc -= bw / 2;
        if (bc < 0)
        {
            bw += bc;
            bc = 0;
        }

        if (bc > Samples - 1)
        {
            bc = Samples - 1;
        }

        if (bc + bw > Samples)
        {
            bw = Samples - bc;
        }

        if (bw <= 0)
        {
            return 0.0;
        }

        double accum = 0;
        if (ch == 0)
        {
            for (var x = 0; x < bw; x++)
            {
                accum += Value(waveform, 0, bc + x) + Value(waveform, 1, bc + x);
            }

            accum /= bw * 255.0;
        }
        else
        {
            for (var x = 0; x < bw; x++)
            {
                accum += Value(waveform, ch - 1, bc + x);
            }

            accum /= bw * 127.5;
        }

        // getspec halves its result, as AVS's did.
        return waveform ? accum : accum * 0.5;
    }

    private int Value(bool waveform, int channel, int index) =>
        waveform ? _waveform[channel * Samples + index] : _spectrum[channel * Samples + index];
}
