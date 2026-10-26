namespace Lucent.Export;

/// <summary>
/// Turns float samples into whole numbers for 16- or 24-bit files. 16-bit gets a touch of
/// triangular (TPDF) dither, which swaps the faint gritty distortion of rounding quiet sounds
/// for an even, inaudible hiss; 24-bit is precise enough to round directly.
/// Not thread-safe: use one per file being written.
/// </summary>
public sealed class PcmQuantizer
{
    private readonly int _bits;
    private readonly double _scale;
    private readonly long _max;
    private readonly long _min;
    private readonly bool _dither;
    private uint _random = 0x9E3779B9;

    public PcmQuantizer(int bitsPerSample)
    {
        _bits = bitsPerSample;
        _max = (1L << (bitsPerSample - 1)) - 1;
        _min = -(1L << (bitsPerSample - 1));
        _scale = 1L << (bitsPerSample - 1);
        _dither = bitsPerSample <= 16;
    }

    public int BitsPerSample => _bits;

    public int Quantize(float sample)
    {
        var value = sample * _scale;
        // Samples already on the grid (audio that came from a file of this bit depth and wasn't
        // changed) are left alone, so converting 16-bit audio to 16-bit FLAC stays bit-exact.
        if (_dither && Math.Abs(value - Math.Round(value)) > 1e-6)
        {
            // The difference of two uniform random numbers, one step wide each: triangular dither.
            value += NextUnit() - NextUnit();
        }

        var rounded = (long)Math.Round(value);
        return (int)Math.Clamp(rounded, _min, _max);
    }

    public void Quantize(ReadOnlySpan<float> samples, Span<int> output)
    {
        for (var i = 0; i < samples.Length; i++)
        {
            output[i] = Quantize(samples[i]);
        }
    }

    private double NextUnit()
    {
        // xorshift32: fast, and plenty random for dither.
        _random ^= _random << 13;
        _random ^= _random >> 17;
        _random ^= _random << 5;
        return _random / 4294967296.0;
    }
}
