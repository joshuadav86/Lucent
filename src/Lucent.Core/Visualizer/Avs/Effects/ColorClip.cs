namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Color Clip (r_contrast.cpp): replaces every pixel darker than a colour (or brighter than it, or
/// near it) with another colour.
/// </summary>
[AvsEffect(12)]
public sealed class ColorClip : AvsEffect
{
    // 0 off, 1 below, 2 above, anything else near.
    private int _mode = 1;
    private int _colour = 0x202020;
    private int _colourOut = 0x202020;
    private int _distance = 10;

    public ColorClip(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _mode = config.ReadInt(_mode);
        _colour = config.ReadInt(_colour);
        _colourOut = config.ReadInt(_colour);
        _distance = config.ReadInt(_distance);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_mode == 0)
        {
            return 0;
        }

        var w = context.Width;
        var output = _colourOut & 0xffffff;
        int fr = _colour & 0xff, fg = (_colour >> 8) & 0xff, fb = (_colour >> 16) & 0xff;
        var mode = _mode;
        var limit = (long)_distance * 2 * (_distance * 2);
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var a = framebuffer[at];
                int r = a & 0xff, g = (a >> 8) & 0xff, b = (a >> 16) & 0xff;
                bool hit;
                if (mode == 1)
                {
                    hit = r <= fr && g <= fg && b <= fb;
                }
                else if (mode == 2)
                {
                    hit = r >= fr && g >= fg && b >= fb;
                }
                else
                {
                    int dr = r - fr, dg = g - fg, db = b - fb;
                    hit = dr * dr + dg * dg + db * db <= limit;
                }

                if (hit)
                {
                    framebuffer[at] = output;
                }
            }
        });
        return 0;
    }
}
