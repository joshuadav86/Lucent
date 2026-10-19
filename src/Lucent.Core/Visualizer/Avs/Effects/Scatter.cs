namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Scatter (r_scat.cpp): replaces each pixel with a random one up to three pixels away, leaving four rows at the top and bottom as they were.</summary>
[AvsEffect(16)]
public sealed class Scatter : AvsEffect
{
    private readonly int[] _offsets = new int[512];
    private int _enabled = 1;
    private int _tableWidth;

    public Scatter(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_tableWidth != w)
        {
            for (var i = 0; i < 512; i++)
            {
                var xp = i % 8 - 4;
                var yp = i / 8 % 8 - 4;
                if (xp < 0)
                {
                    xp++;
                }

                if (yp < 0)
                {
                    yp++;
                }

                _offsets[i] = w * yp + xp;
            }

            _tableWidth = w;
        }

        var top = Math.Min(4, h);
        var bottom = Math.Max(top, h - 4);
        Array.Copy(framebuffer, 0, fbout, 0, top * w);
        Array.Copy(framebuffer, bottom * w, fbout, bottom * w, (h - bottom) * w);

        // AVS called rand() for every pixel; each row gets its own little generator, seeded from the
        // preset's random numbers once a frame, so rows can be done in parallel and still come out the same.
        var seed = (uint)Shared.Random.Next();
        var offsets = _offsets;
        var length = w * h;
        context.ForEachRow(top, bottom, y =>
        {
            var state = Mix(seed ^ (uint)y * 0x9e3779b9u) | 1u;
            for (int at = y * w, end = at + w; at < end; at++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var from = at + offsets[state & 511];
                fbout[at] = framebuffer[(uint)from < (uint)length ? from : at];
            }
        });
        return Swapped;
    }

    private static uint Mix(uint x)
    {
        x ^= x >> 16;
        x *= 0x7feb352du;
        x ^= x >> 15;
        x *= 0x846ca68bu;
        x ^= x >> 16;
        return x;
    }
}
