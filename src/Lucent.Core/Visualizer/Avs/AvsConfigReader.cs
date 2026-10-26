using System.Text;

namespace Lucent.Visualizer.Avs;

/// <summary>
/// Reads one effect's settings out of a preset, the way AVS's load_config functions do: little-endian
/// 32-bit numbers, single bytes, and strings stored as a length then the text. A setting past the end
/// of the data (an older version of the effect saved fewer) reads as the fallback given, so an effect
/// keeps its default for it, as AVS's "if (len-pos >= 4)" checks do.
/// </summary>
public sealed class AvsConfigReader
{
    private readonly byte[] _data;
    private readonly int _start;

    public AvsConfigReader(byte[] data, int start = 0, int length = -1)
    {
        _data = data;
        _start = start;
        Length = length < 0 ? data.Length - start : length;
    }

    /// <summary>How many bytes of settings there are.</summary>
    public int Length { get; }

    /// <summary>Where the next read starts, from the start of the settings (AVS's pos).</summary>
    public int Position { get; set; }

    public int Remaining => Math.Max(0, Length - Position);

    /// <summary>Whether a whole 32-bit number is left to read.</summary>
    public bool HasInt => Remaining >= 4;

    /// <summary>The byte at <paramref name="offset"/> from the start of the settings (not moving on), or 0 past the end.</summary>
    public byte this[int offset] => offset >= 0 && offset < Length ? _data[_start + offset] : (byte)0;

    /// <summary>The settings' bytes from <paramref name="offset"/> on, for effects that read their data their own way.</summary>
    public ReadOnlySpan<byte> Bytes(int offset = 0) =>
        offset >= Length ? ReadOnlySpan<byte>.Empty : _data.AsSpan(_start + offset, Length - offset);

    /// <summary>Part of the settings, from <paramref name="offset"/> for <paramref name="length"/> bytes, as settings of their own.</summary>
    public AvsConfigReader Slice(int offset, int length) =>
        new(_data, _start + offset, Math.Clamp(length, 0, Math.Max(0, Length - offset)));

    /// <summary>GET_INT(); pos += 4. Gives <paramref name="fallback"/> (and stays put) when fewer than 4 bytes are left.</summary>
    public int ReadInt(int fallback = 0)
    {
        if (!HasInt)
        {
            return fallback;
        }

        var at = _start + Position;
        Position += 4;
        return _data[at] | (_data[at + 1] << 8) | (_data[at + 2] << 16) | (_data[at + 3] << 24);
    }

    /// <summary>A 32-bit float, stored as its bits (some APEs save them so).</summary>
    public float ReadFloat(float fallback = 0f) => HasInt ? BitConverter.Int32BitsToSingle(ReadInt()) : fallback;

    /// <summary>A 64-bit float (some APEs save doubles).</summary>
    public double ReadDouble(double fallback = 0.0)
    {
        if (Remaining < 8)
        {
            return fallback;
        }

        var value = BitConverter.ToDouble(_data, _start + Position);
        Position += 8;
        return value;
    }

    /// <summary>data[pos++]. Gives <paramref name="fallback"/> past the end.</summary>
    public int ReadByte(int fallback = 0)
    {
        if (Remaining < 1)
        {
            return fallback;
        }

        return _data[_start + Position++];
    }

    /// <summary>
    /// C_RBASE::load_string: a 32-bit length, then that many bytes of text (with its closing NUL).
    /// A length that doesn't fit what's left gives an empty string, as in AVS.
    /// </summary>
    public string ReadString()
    {
        var size = ReadInt();
        if (size <= 0 || Remaining < size)
        {
            return string.Empty;
        }

        var text = Text(_start + Position, size);
        Position += size;
        return text;
    }

    /// <summary>A NUL-padded string in a fixed number of bytes (as some effects save names), moving on by all of them.</summary>
    public string ReadFixedString(int size)
    {
        var count = Math.Min(size, Remaining);
        var text = Text(_start + Position, count);
        Position += count;
        return text;
    }

    /// <summary>
    /// A NUL-terminated string starting here, moving past its NUL, as the old Superscope and Dynamic Movement
    /// settings store their code (each piece as plain text up to a 0 byte).
    /// </summary>
    public string ReadTerminatedString(int maxLength = int.MaxValue)
    {
        var start = _start + Position;
        var end = start;
        var limit = _start + Length;
        while (end < limit && _data[end] != 0 && end - start < maxLength)
        {
            end++;
        }

        var text = Text(start, end - start);
        Position = Math.Min(Length, end - _start + 1);
        return text;
    }

    /// <summary>Text as AVS saved it: in the Windows code page of the time, which Latin-1 matches for everything code uses.</summary>
    private string Text(int at, int count)
    {
        var end = at;
        while (end < at + count && _data[end] != 0)
        {
            end++;
        }

        return Encoding.Latin1.GetString(_data, at, end - at);
    }
}
