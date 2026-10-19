namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Brightness (r_bright.cpp): scales each channel up or down by its own amount, replacing the
/// picture or blending additively or 50/50, and optionally leaving alone pixels near a chosen colour.
/// </summary>
[AvsEffect(22)]
public sealed class Brightness : AvsEffect
{
    private readonly int[] _red = new int[256];
    private readonly int[] _green = new int[256];
    private readonly int[] _blue = new int[256];
    private int _enabled = 1;
    private int _blend;
    private int _blendAverage = 1;
    private int _redAmount;
    private int _greenAmount;
    private int _blueAmount;
    private int _colour;
    private int _exclude;
    private int _distance = 16;
    private bool _tablesReady;

    public Brightness(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _redAmount = config.ReadInt(_redAmount);
        _greenAmount = config.ReadInt(_greenAmount);
        _blueAmount = config.ReadInt(_blueAmount);
        config.ReadInt(); // "dissociate" only tied the sliders together in AVS's editor.
        _colour = config.ReadInt(_colour);
        _exclude = config.ReadInt(_exclude);
        _distance = config.ReadInt(_distance);
        _tablesReady = false;
    }

    // As AVS: 16.16 fixed point, up to 17x brighter for positive amounts, down to black for negative ones.
    private static int Multiplier(int amount) =>
        (int)((1 + (amount < 0 ? 1 : 16) * ((float)amount / 4096)) * 65536.0);

    private void MakeTables()
    {
        int rm = Multiplier(_redAmount), gm = Multiplier(_greenAmount), bm = Multiplier(_blueAmount);
        for (var n = 0; n < 256; n++)
        {
            _red[n] = Math.Clamp((int)(n * rm & 0xffff0000), 0, 0xff0000);
            _green[n] = Math.Clamp((n * gm >> 8) & 0xffff00, 0, 0xff00);
            _blue[n] = Math.Clamp((n * bm >> 16) & 0xffff, 0, 0xff);
        }

        _tablesReady = true;
    }

    private static bool InRange(int colour, int reference, int distance) =>
        Math.Abs((colour & 0xff) - (reference & 0xff)) <= distance
        && Math.Abs((colour & 0xff00) - (reference & 0xff00)) <= distance << 8
        && Math.Abs((colour & 0xff0000) - (reference & 0xff0000)) <= distance << 16;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0)
        {
            return 0;
        }

        if (!_tablesReady)
        {
            MakeTables();
        }

        int[] red = _red, green = _green, blue = _blue;
        var mode = _blend != 0 ? 1 : _blendAverage != 0 ? 2 : 0;
        var exclude = _exclude != 0;
        int colour = _colour, distance = _distance;
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var pixel = framebuffer[at];
                if (exclude && InRange(pixel, colour, distance))
                {
                    continue;
                }

                var bright = red[(pixel >> 16) & 0xff] | green[(pixel >> 8) & 0xff] | blue[pixel & 0xff];
                framebuffer[at] = mode switch
                {
                    1 => AvsBlend.Add(pixel, bright),
                    2 => AvsBlend.Average(pixel, bright),
                    _ => bright,
                };
            }
        });
        return 0;
    }
}
