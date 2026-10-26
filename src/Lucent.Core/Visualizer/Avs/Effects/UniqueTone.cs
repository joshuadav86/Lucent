namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Unique tone (r_onetone.cpp): repaints the picture in shades of one colour, each pixel as bright
/// as its brightest channel (or the reverse), replacing the picture or blending additively or 50/50.
/// </summary>
[AvsEffect(38)]
public sealed class UniqueTone : AvsEffect
{
    private readonly int[] _shades = new int[256];
    private int _enabled = 1;
    private int _colour = 0xffffff;
    private int _blend;
    private int _blendAverage;
    private int _invert;

    public UniqueTone(AvsShared shared)
        : base(shared)
    {
        MakeTable();
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _invert = config.ReadInt(_invert);
        MakeTable();
    }

    private void MakeTable()
    {
        for (var i = 0; i < 256; i++)
        {
            var b = (byte)(i / 255.0 * (float)(_colour & 0xff));
            var g = (byte)(i / 255.0 * (float)((_colour >> 8) & 0xff));
            var r = (byte)(i / 255.0 * (float)((_colour >> 16) & 0xff));
            _shades[i] = b | (g << 8) | (r << 16);
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        var shades = _shades;
        var invert = _invert != 0;
        var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 2 : 0;
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var p = framebuffer[at];
                var depth = Math.Max(Math.Max(p & 0xff, (p >> 8) & 0xff), (p >> 16) & 0xff);
                var c = shades[invert ? 255 - depth : depth];
                framebuffer[at] = mode switch
                {
                    1 => AvsBlend.Add(p, c),
                    2 => AvsBlend.Average(p, c),
                    _ => c,
                };
            }
        });
        return 0;
    }
}
