namespace Lucent.Editor.Dsp;

/// <summary>An in-place radix-2 complex FFT of one fixed size, with its tables worked out once.</summary>
public sealed class Fft
{
    private readonly int _size;
    private readonly int[] _reversed;
    // Twiddle factors for every stage, one after another: the stage with half-length h starts at h - 1.
    private readonly double[] _twiddleRe;
    private readonly double[] _twiddleIm;

    public Fft(int size)
    {
        if (size < 2 || (size & (size - 1)) != 0)
        {
            throw new ArgumentException("The FFT size must be a power of two.", nameof(size));
        }

        _size = size;
        _reversed = new int[size];
        var bits = (int)Math.Log2(size);
        for (var i = 0; i < size; i++)
        {
            var r = 0;
            for (var b = 0; b < bits; b++)
            {
                r |= ((i >> b) & 1) << (bits - 1 - b);
            }

            _reversed[i] = r;
        }

        _twiddleRe = new double[size - 1];
        _twiddleIm = new double[size - 1];
        for (var half = 1; half < size; half <<= 1)
        {
            for (var k = 0; k < half; k++)
            {
                _twiddleRe[half - 1 + k] = Math.Cos(Math.PI * k / half);
                _twiddleIm[half - 1 + k] = -Math.Sin(Math.PI * k / half);
            }
        }
    }

    public int Size => _size;

    /// <summary>Transforms in place. The inverse is unscaled: divide by <see cref="Size"/> afterwards.</summary>
    public void Transform(double[] re, double[] im, bool inverse)
    {
        var real = re.AsSpan(0, _size);
        var imag = im.AsSpan(0, _size);
        for (var i = 0; i < _size; i++)
        {
            var j = _reversed[i];
            if (j > i)
            {
                var t = real[i];
                real[i] = real[j];
                real[j] = t;
                t = imag[i];
                imag[i] = imag[j];
                imag[j] = t;
            }
        }

        // The inverse is the same transform with the twiddles conjugated.
        var sign = inverse ? -1.0 : 1.0;
        for (var half = 1; half < _size; half <<= 1)
        {
            var wRe = _twiddleRe.AsSpan(half - 1, half);
            var wIm = _twiddleIm.AsSpan(half - 1, half);
            for (var start = 0; start < _size; start += half << 1)
            {
                var aRe = real.Slice(start, half);
                var aIm = imag.Slice(start, half);
                var bRe = real.Slice(start + half, half);
                var bIm = imag.Slice(start + half, half);
                for (var k = 0; k < half; k++)
                {
                    var wr = wRe[k];
                    var wi = wIm[k] * sign;
                    var tr = bRe[k] * wr - bIm[k] * wi;
                    var ti = bRe[k] * wi + bIm[k] * wr;
                    bRe[k] = aRe[k] - tr;
                    bIm[k] = aIm[k] - ti;
                    aRe[k] += tr;
                    aIm[k] += ti;
                }
            }
        }
    }
}
