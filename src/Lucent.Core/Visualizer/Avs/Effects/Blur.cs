using System.Runtime.CompilerServices;

namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Blur (r_blur.cpp): mixes each pixel with its four neighbours, lightly, medium or heavily, rounding down or up.</summary>
[AvsEffect(6)]
public sealed class Blur : AvsEffect
{
    // Shifting a whole pixel right first clears the bits that would fall into the channel below.
    private const uint Mask1 = 0xfefefe;
    private const uint Mask2 = 0xfcfcfc;
    private const uint Mask3 = 0xf8f8f8;
    private const uint Mask4 = 0xf0f0f0;

    // 0 off, 1 medium, 2 light, 3 heavy (AVS's numbering).
    private int _level = 1;
    private int _roundUp;

    public Blur(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _level = config.ReadInt(_level);
        _roundUp = config.ReadInt(0);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint D2(int x) => ((uint)x & Mask1) >> 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint D4(int x) => ((uint)x & Mask2) >> 2;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint D8(int x) => ((uint)x & Mask3) >> 3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint D16(int x) => ((uint)x & Mask4) >> 4;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_level == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (w < 2 || h < 2)
        {
            return 0;
        }

        var level = _level;
        var up = _roundUp != 0;
        var f = framebuffer;
        context.ForEachRow(y =>
        {
            var row = y * w;
            if (y == 0 || y == h - 1)
            {
                // The top and bottom rows mix in the one row next to them.
                var n = y == 0 ? row + w : row - w;
                EdgeRow(f, fbout, row, n, w, level, up);
            }
            else
            {
                MiddleRow(f, fbout, row, w, level, up);
            }
        });
        return Swapped;
    }

    private static void EdgeRow(int[] f, int[] o, int row, int n, int w, int level, bool up)
    {
        var last = row + w - 1;
        if (level == 2)
        {
            uint corner = up ? 0x030303u : 0, centre = up ? 0x040404u : 0;
            o[row] = (int)(D2(f[row]) + D4(f[row]) + D8(f[row + 1]) + D8(f[n]) + corner);
            for (int at = row + 1, a = n + 1; at < last; at++, a++)
            {
                o[at] = (int)(D2(f[at]) + D8(f[at]) + D8(f[at + 1]) + D8(f[at - 1]) + D8(f[a]) + centre);
            }

            o[last] = (int)(D2(f[last]) + D4(f[last]) + D8(f[last - 1]) + D8(f[n + w - 1]) + corner);
        }
        else if (level == 3)
        {
            uint corner = up ? 0x010101u : 0, centre = up ? 0x020202u : 0;
            o[row] = (int)(D2(f[row + 1]) + D2(f[n]) + corner);
            for (int at = row + 1, a = n + 1; at < last; at++, a++)
            {
                o[at] = (int)(D4(f[at + 1]) + D4(f[at - 1]) + D2(f[a]) + centre);
            }

            o[last] = (int)(D2(f[last - 1]) + D2(f[n + w - 1]) + corner);
        }
        else
        {
            uint corner = up ? 0x020202u : 0, centre = up ? 0x030303u : 0;
            o[row] = (int)(D2(f[row]) + D4(f[row + 1]) + D4(f[n]) + corner);
            for (int at = row + 1, a = n + 1; at < last; at++, a++)
            {
                o[at] = (int)(D4(f[at]) + D4(f[at + 1]) + D4(f[at - 1]) + D4(f[a]) + centre);
            }

            o[last] = (int)(D2(f[last]) + D4(f[last - 1]) + D4(f[n + w - 1]) + corner);
        }
    }

    private static void MiddleRow(int[] f, int[] o, int row, int w, int level, bool up)
    {
        var last = row + w - 1;
        if (level == 2)
        {
            uint side = up ? 0x040404u : 0, centre = up ? 0x050505u : 0;
            o[row] = (int)(D2(f[row]) + D8(f[row]) + D8(f[row + 1]) + D8(f[row + w]) + D8(f[row - w]) + side);
            for (var at = row + 1; at < last; at++)
            {
                o[at] = (int)(D2(f[at]) + D4(f[at]) + D16(f[at + 1]) + D16(f[at - 1]) + D16(f[at + w]) + D16(f[at - w]) + centre);
            }

            o[last] = (int)(D2(f[last]) + D8(f[last]) + D8(f[last - 1]) + D8(f[last + w]) + D8(f[last - w]) + side);
        }
        else if (level == 3)
        {
            uint side = up ? 0x020202u : 0, centre = up ? 0x030303u : 0;
            o[row] = (int)(D2(f[row + 1]) + D4(f[row + w]) + D4(f[row - w]) + side);
            for (var at = row + 1; at < last; at++)
            {
                o[at] = (int)(D4(f[at + 1]) + D4(f[at - 1]) + D4(f[at + w]) + D4(f[at - w]) + centre);
            }

            o[last] = (int)(D2(f[last - 1]) + D4(f[last + w]) + D4(f[last - w]) + side);
        }
        else
        {
            uint side = up ? 0x030303u : 0, centre = up ? 0x040404u : 0;
            o[row] = (int)(D4(f[row]) + D4(f[row + 1]) + D4(f[row + w]) + D4(f[row - w]) + side);
            for (var at = row + 1; at < last; at++)
            {
                o[at] = (int)(D2(f[at]) + D8(f[at + 1]) + D8(f[at - 1]) + D8(f[at + w]) + D8(f[at - w]) + centre);
            }

            o[last] = (int)(D4(f[last]) + D4(f[last - 1]) + D4(f[last + w]) + D4(f[last - w]) + side);
        }
    }
}
