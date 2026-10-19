namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Water (r_water.cpp): ripples spread out from whatever changes in the picture, like the surface of water.</summary>
[AvsEffect(20)]
public sealed class Water : AvsEffect
{
    private int _enabled = 1;
    private int[] _lastFrame = Array.Empty<int>();

    public Water(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
    }

    public override void Restart()
    {
        Array.Clear(_lastFrame);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_lastFrame.Length != w * h)
        {
            _lastFrame = new int[w * h];
        }

        if (w < 2 || h < 2)
        {
            Array.Copy(framebuffer, fbout, w * h);
            return Swapped;
        }

        var last = _lastFrame;
        context.ForEachRow(y =>
        {
            var top = y == 0;
            var bottom = y == h - 1;
            var row = y * w;
            for (var x = 0; x < w; x++)
            {
                var at = row + x;
                int r = 0, g = 0, b = 0, count = 0;
                if (x > 0)
                {
                    Add(framebuffer[at - 1], ref r, ref g, ref b);
                    count++;
                }

                if (x < w - 1)
                {
                    Add(framebuffer[at + 1], ref r, ref g, ref b);
                    count++;
                }

                if (!top)
                {
                    Add(framebuffer[at - w], ref r, ref g, ref b);
                    count++;
                }

                if (!bottom)
                {
                    Add(framebuffer[at + w], ref r, ref g, ref b);
                    count++;
                }

                // AVS halves the sum of three or four neighbours; at the corners, where there are two, it doesn't.
                if (count > 2)
                {
                    r /= 2;
                    g /= 2;
                    b /= 2;
                }

                var l = last[at];
                r = Math.Clamp(r - (l & 0xff), 0, 255);
                g = Math.Clamp(g - (l & 0xff00), 0, 255 * 256);
                b = Math.Clamp(b - (l & 0xff0000), 0, 255 * 65536);
                fbout[at] = r | (g & 0xff00) | (b & 0xff0000);
            }

            Array.Copy(framebuffer, row, last, row, w);
        });

        return Swapped;
    }

    private static void Add(int c, ref int r, ref int g, ref int b)
    {
        r += c & 0xff;
        g += c & 0xff00;
        b += c & 0xff0000;
    }
}
