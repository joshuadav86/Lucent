namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Normalise (e_normalise.cpp): stretches the picture's levels so its darkest channel becomes black and its brightest full.</summary>
[AvsEffect("Trans: Normalise")]
public sealed class Normalise : AvsEffect
{
    private readonly int[] _scale = new int[256];
    private byte[] _rowMax = Array.Empty<byte>();
    private byte[] _rowMin = Array.Empty<byte>();
    private bool _enabled = true;

    public Normalise(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(0) != 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (!_enabled)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        if (_rowMax.Length != h)
        {
            _rowMax = new byte[h];
            _rowMin = new byte[h];
        }

        byte[] rowMax = _rowMax, rowMin = _rowMin;
        context.ForEachRow(y =>
        {
            int max = 0, min = 255;
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                int r = (c >> 16) & 0xff, g = (c >> 8) & 0xff, b = c & 0xff;
                max = Math.Max(max, Math.Max(r, Math.Max(g, b)));
                min = Math.Min(min, Math.Min(r, Math.Min(g, b)));
            }

            rowMax[y] = (byte)max;
            rowMin[y] = (byte)min;
        });

        int high = 0, low = 255;
        for (var y = 0; y < h; y++)
        {
            high = Math.Max(high, rowMax[y]);
            low = Math.Min(low, rowMin[y]);
        }

        if (high == 255 && low == 0)
        {
            return 0;
        }

        var scale = _scale;
        Array.Clear(scale);
        if (high > low)
        {
            var step = 255 / (float)(high - low);
            for (int i = low, j = 0; i <= high; i++, j++)
            {
                scale[i] = (int)(uint)(j * step);
            }
        }

        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                framebuffer[at] = scale[c & 0xff] | (scale[(c >> 8) & 0xff] << 8) | (scale[(c >> 16) & 0xff] << 16);
            }
        });
        return 0;
    }
}
