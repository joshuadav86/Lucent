using System.Buffers.Binary;
using System.Reflection;
using NAudio.Wave;

namespace WinampInspiredVisualizer.Audio;

/// <summary>
/// Turns the player's floating-point audio into the whole-number samples a sound card takes in
/// exclusive mode: 16-bit, 24-bit (packed, or padded to 32) or 32-bit.
/// </summary>
/// <remarks>
/// No dither and no rounding tricks: a 16- or 24-bit sample survives the trip through floating point
/// exactly, so at full volume with the equalizer off, what reaches the sound card is exactly what's in
/// the file.
/// </remarks>
public sealed class PcmOutput : IWaveProvider
{
    private readonly ISampleProvider _source;
    private readonly int _bytesPerSample;
    private readonly int _validBits;
    private float[] _samples = Array.Empty<float>();

    public PcmOutput(ISampleProvider source, WaveFormat format, int validBits)
    {
        _source = source;
        WaveFormat = format;
        _bytesPerSample = format.BitsPerSample / 8;
        _validBits = validBits;
    }

    public WaveFormat WaveFormat { get; }

    public int Read(byte[] buffer, int offset, int count)
    {
        var wanted = count / _bytesPerSample;
        if (_samples.Length < wanted)
        {
            _samples = new float[wanted];
        }

        var read = _source.Read(_samples, 0, wanted);
        var at = offset;
        for (var i = 0; i < read; i++)
        {
            var sample = Math.Clamp(_samples[i], -1f, 1f);
            switch (_bytesPerSample)
            {
                case 2:
                    BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(at), (short)ToInt(sample, 16 - 1));
                    break;
                case 3:
                    var s24 = ToInt(sample, 24 - 1);
                    buffer[at] = (byte)s24;
                    buffer[at + 1] = (byte)(s24 >> 8);
                    buffer[at + 2] = (byte)(s24 >> 16);
                    break;
                default:
                    // 24 bits padded to 32 sit in the top three bytes, as do the top bits of a full 32-bit sample.
                    var s32 = (long)ToInt(sample, _validBits - 1) << (32 - _validBits);
                    BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(at), (int)Math.Clamp(s32, int.MinValue, int.MaxValue));
                    break;
            }

            at += _bytesPerSample;
        }

        return read * _bytesPerSample;
    }

    // Scales -1..1 to a whole number with this many bits after the sign, keeping +1 just inside the top.
    private static int ToInt(float sample, int bits)
    {
        var scale = (double)(1L << bits);
        return (int)Math.Clamp(Math.Round(sample * scale), -scale, scale - 1);
    }

    /// <summary>
    /// The format of whole-number samples with <paramref name="validBits"/> of music in containers of
    /// <paramref name="containerBits"/> (24 in 32 is what most USB DACs want).
    /// </summary>
    public static WaveFormat Format(int sampleRate, int channels, int containerBits, int validBits)
    {
        if (containerBits == 16 && channels <= 2)
        {
            return new WaveFormat(sampleRate, 16, channels);
        }

        var format = new WaveFormatExtensible(sampleRate, containerBits, channels);
        if (validBits != containerBits)
        {
            // NAudio has no way to say "24 bits in a 32-bit container", so set the field it keeps for it.
            typeof(WaveFormatExtensible).GetField("wValidBitsPerSample", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(format, (short)validBits);
        }

        return format;
    }
}
