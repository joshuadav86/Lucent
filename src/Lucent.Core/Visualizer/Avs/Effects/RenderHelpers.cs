namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Small pieces several Render effects share: colour cycling, the sound as AVS's scopes read it, and AVS's 3D matrices (matrix.cpp).</summary>
internal static class RenderHelpers
{
    /// <summary>
    /// The colour of the moment for effects that fade through up to 16 colours, 64 frames each
    /// (Simple, Oscilloscope Star, Ring, Rotating Stars, Dot Grid). Moves <paramref name="position"/> on a frame.
    /// </summary>
    public static int CycleColour(int[] colours, int count, ref int position)
    {
        position++;
        if (position >= count * 64)
        {
            position = 0;
        }

        var p = position / 64;
        var r = position & 63;
        var c1 = colours[p];
        var c2 = p + 1 < count ? colours[p + 1] : colours[0];
        var r1 = ((c1 & 255) * (63 - r) + (c2 & 255) * r) / 64;
        var r2 = (((c1 >> 8) & 255) * (63 - r) + ((c2 >> 8) & 255) * r) / 64;
        var r3 = (((c1 >> 16) & 255) * (63 - r) + ((c2 >> 16) & 255) * r) / 64;
        return r1 | (r2 << 8) | (r3 << 16);
    }

    /// <summary>
    /// Reads up to 16 colours as AVS's colour-cycling effects save them (a count, then the colours).
    /// A count over 16 gives <paramref name="tooMany"/> colours, as each effect did differently.
    /// </summary>
    public static int ReadColours(AvsConfigReader config, int[] colours, int count, int tooMany)
    {
        count = config.ReadInt(count);
        if (count <= 16)
        {
            for (var x = 0; x < count && config.HasInt; x++)
            {
                colours[x] = config.ReadInt();
            }

            return count;
        }

        return tooMany;
    }

    /// <summary>
    /// Fills <paramref name="data"/> (576 bytes) with one channel of the spectrum (<paramref name="type"/> 0) or
    /// waveform (1) as AVS's unsigned char pointers read it: <paramref name="channel"/> 0 left, 1 right, 2 or more
    /// both, averaged as signed chars the way AVS's center_channel was.
    /// </summary>
    public static void ReadChannel(AvsAudio audio, int type, int channel, byte[] data)
    {
        for (var i = 0; i < AvsAudio.Samples; i++)
        {
            data[i] = channel >= 2
                ? (byte)(audio.Signed(type, 0, i) / 2 + audio.Signed(type, 1, i) / 2)
                : (byte)audio.Unsigned(type, channel, i);
        }
    }

    /// <summary>The line width "Set render mode" chose, made thicker by the dot scale.</summary>
    public static int LineWidth(AvsRenderContext context) => Math.Max(1, (int)(context.Shared.LineWidth * context.DotScale));

    /// <summary>The size of a one-pixel dot at this dot scale.</summary>
    public static int DotSize(AvsRenderContext context) => Math.Max(1, (int)MathF.Round(context.DotScale));

    /// <summary>matrixRotate: a rotation of <paramref name="degrees"/> about axis <paramref name="m"/> (1 x, 2 y, 3 z).</summary>
    public static void MatrixRotate(float[] matrix, int m, float degrees)
    {
        degrees *= 3.141592653589f / 180.0f;
        Array.Clear(matrix, 0, 16);
        matrix[((m - 1) << 2) + m - 1] = 1.0f;
        matrix[15] = 1.0f;
        var m1 = m % 3;
        var m2 = (m1 + 1) % 3;
        var c = (float)Math.Cos(degrees);
        var s = (float)Math.Sin(degrees);
        matrix[(m1 << 2) + m1] = c;
        matrix[(m1 << 2) + m2] = s;
        matrix[(m2 << 2) + m2] = c;
        matrix[(m2 << 2) + m1] = -s;
    }

    /// <summary>matrixTranslate.</summary>
    public static void MatrixTranslate(float[] m, float x, float y, float z)
    {
        Array.Clear(m, 0, 16);
        m[0] = m[5] = m[10] = m[15] = 1.0f;
        m[3] = x;
        m[7] = y;
        m[11] = z;
    }

    /// <summary>matrixMultiply: <paramref name="dest"/> becomes <paramref name="src"/> times <paramref name="dest"/>.</summary>
    public static void MatrixMultiply(float[] dest, float[] src, float[] temp)
    {
        Array.Copy(dest, temp, 16);
        for (int i = 0, d = 0; i < 16; i += 4)
        {
            for (var j = 0; j < 4; j++)
            {
                dest[d++] = src[i] * temp[j] + src[i + 1] * temp[4 + j] + src[i + 2] * temp[8 + j] + src[i + 3] * temp[12 + j];
            }
        }
    }

    /// <summary>
    /// The 3D view Dot Plane and Dot Fountain look through: turned <paramref name="rotation"/> degrees about
    /// the vertical, tilted <paramref name="angle"/> degrees, and pushed back.
    /// </summary>
    public static void ViewMatrix(float[] matrix, float[] matrix2, float[] temp, float rotation, float angle)
    {
        MatrixRotate(matrix, 2, rotation);
        MatrixRotate(matrix2, 1, angle);
        MatrixMultiply(matrix, matrix2, temp);
        MatrixTranslate(matrix2, 0.0f, -20.0f, 400.0f);
        MatrixMultiply(matrix, matrix2, temp);
    }

    /// <summary>
    /// initcolortab for Dot Plane and Dot Fountain: 64 colours fading through the five, 16 steps between each two.
    /// </summary>
    public static void FiveColourTable(int[] colours, int[] table)
    {
        for (var t = 0; t < 4; t++)
        {
            var r = (colours[t] & 255) << 16;
            var g = ((colours[t] >> 8) & 255) << 16;
            var b = ((colours[t] >> 16) & 255) << 16;
            var dr = (((colours[t + 1] & 255) - (colours[t] & 255)) << 16) / 16;
            var dg = ((((colours[t + 1] >> 8) & 255) - ((colours[t] >> 8) & 255)) << 16) / 16;
            var db = ((((colours[t + 1] >> 16) & 255) - ((colours[t] >> 16) & 255)) << 16) / 16;
            for (var x = 0; x < 16; x++)
            {
                table[t * 16 + x] = (r >> 16) | ((g >> 16) << 8) | ((b >> 16) << 16);
                r += dr;
                g += dg;
                b += db;
            }
        }
    }
}
