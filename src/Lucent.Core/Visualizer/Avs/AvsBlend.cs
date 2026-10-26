using System.Runtime.CompilerServices;

namespace Lucent.Visualizer.Avs;

/// <summary>
/// AVS's ways of putting one colour onto another (r_defs.h): pixels are 0x00RRGGBB in an int, and
/// each channel is worked out on its own, clipping at 0 and 255 as AVS's 8-bit maths did.
/// </summary>
public static class AvsBlend
{
    /// <summary>g_blendtable: i * j / 255, rounded down, for scaling one 0..255 value by another.</summary>
    public static readonly byte[] Table = MakeTable();

    private static byte[] MakeTable()
    {
        var table = new byte[256 * 256];
        for (var i = 0; i < 256; i++)
        {
            for (var j = 0; j < 256; j++)
            {
                table[i * 256 + j] = (byte)(i / 255.0 * j);
            }
        }

        return table;
    }

    /// <summary>g_blendtable[a][b].</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Scale(int a, int b) => Table[(a << 8) | b];

    /// <summary>BLEND: adds the two, each channel clipping at 255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Add(int a, int b)
    {
        var r = Math.Min((a & 0xff) + (b & 0xff), 0xff);
        r |= Math.Min((a & 0xff00) + (b & 0xff00), 0xff00);
        r |= Math.Min((a & 0xff0000) + (b & 0xff0000), 0xff0000);
        return r;
    }

    /// <summary>BLEND_MAX: the brighter of each channel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Max(int a, int b) =>
        Math.Max(a & 0xff, b & 0xff) | Math.Max(a & 0xff00, b & 0xff00) | Math.Max(a & 0xff0000, b & 0xff0000);

    /// <summary>BLEND_MIN: the darker of each channel.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Min(int a, int b) =>
        Math.Min(a & 0xff, b & 0xff) | Math.Min(a & 0xff00, b & 0xff00) | Math.Min(a & 0xff0000, b & 0xff0000);

    /// <summary>BLEND_AVG: half of each, added.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Average(int a, int b) => ((a >> 1) & 0x7f7f7f) + ((b >> 1) & 0x7f7f7f);

    /// <summary>BLEND_SUB: a minus b, each channel stopping at 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Subtract(int a, int b) =>
        Math.Max((a & 0xff) - (b & 0xff), 0) | Math.Max((a & 0xff00) - (b & 0xff00), 0) | Math.Max((a & 0xff0000) - (b & 0xff0000), 0);

    /// <summary>BLEND_MUL: each channel times the other's, over 255.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Multiply(int a, int b) =>
        Scale(a & 0xff, b & 0xff) | (Scale((a >> 8) & 0xff, (b >> 8) & 0xff) << 8) | (Scale((a >> 16) & 0xff, (b >> 16) & 0xff) << 16);

    /// <summary>BLEND_ADJ: <paramref name="v"/>/255 of a plus the rest of b (v is 0..255).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Adjust(int a, int b, int v)
    {
        var u = 255 - v;
        var r = Scale(a & 0xff, v) + Scale(b & 0xff, u);
        r |= (Scale((a >> 8) & 0xff, v) + Scale((b >> 8) & 0xff, u)) << 8;
        r |= (Scale((a >> 16) & 0xff, v) + Scale((b >> 16) & 0xff, u)) << 16;
        return r;
    }

    /// <summary>
    /// BLEND4: the colour between four pixels (p[at], p[at + 1], p[at + width], p[at + width + 1]),
    /// <paramref name="xp"/> and <paramref name="yp"/> (0..255) of the way across and down. The
    /// caller makes sure all four are inside the image.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Bilinear(int[] p, int at, int width, int xp, int yp)
    {
        var a1 = Scale(255 - xp, 255 - yp);
        var a2 = Scale(xp, 255 - yp);
        var a3 = Scale(255 - xp, yp);
        var a4 = Scale(xp, yp);
        int c1 = p[at], c2 = p[at + 1], c3 = p[at + width], c4 = p[at + width + 1];
        var r = Scale(c1 & 0xff, a1) + Scale(c2 & 0xff, a2) + Scale(c3 & 0xff, a3) + Scale(c4 & 0xff, a4);
        var g = Scale((c1 >> 8) & 0xff, a1) + Scale((c2 >> 8) & 0xff, a2) + Scale((c3 >> 8) & 0xff, a3) + Scale((c4 >> 8) & 0xff, a4);
        var b = Scale((c1 >> 16) & 0xff, a1) + Scale((c2 >> 16) & 0xff, a2) + Scale((c3 >> 16) & 0xff, a3) + Scale((c4 >> 16) & 0xff, a4);
        return Math.Min(r, 255) | (Math.Min(g, 255) << 8) | (Math.Min(b, 255) << 16);
    }

    /// <summary>BLEND4_16: <see cref="Bilinear"/> with the fractions given in 16.16 fixed point (only bits 8..15 count).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Bilinear16(int[] p, int at, int width, int xp, int yp) =>
        Bilinear(p, at, width, (xp >> 8) & 0xff, (yp >> 8) & 0xff);

    /// <summary>
    /// BLEND_LINE: puts a dot or line pixel on the image the way "Set render mode" last said
    /// (<see cref="AvsShared.LineBlendMode"/>): the low byte picks the blend, bits 8..15 are the
    /// adjustable blend's amount.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Line(int under, int colour, int lineBlendMode) => (lineBlendMode & 0xff) switch
    {
        1 => Add(under, colour),
        2 => Max(under, colour),
        3 => Average(under, colour),
        4 => Subtract(under, colour),
        5 => Subtract(colour, under),
        6 => Multiply(under, colour),
        7 => Adjust(under, colour, (lineBlendMode >> 8) & 0xff),
        8 => under ^ colour,
        9 => Min(under, colour),
        _ => colour,
    };

    /// <summary>
    /// One of the blends an effect offers in its settings, as most AVS effects number them:
    /// 0 replace, 1 additive, 2 50/50, 3 the default line blend. Effects with their own numbering
    /// do their own mapping.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Pixel(int under, int colour, int mode, int lineBlendMode) => mode switch
    {
        1 => Add(under, colour),
        2 => Average(under, colour),
        3 => Line(under, colour, lineBlendMode),
        _ => colour,
    };
}
