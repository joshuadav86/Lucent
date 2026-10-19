namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Fast Brightness (r_fastbright.cpp): doubles every channel (stopping at full), or halves it.</summary>
[AvsEffect(44)]
public sealed class FastBrightness : AvsEffect
{
    // 0 doubles, 1 halves, anything else does nothing.
    private int _direction;

    public FastBrightness(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _direction = config.ReadInt(0);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var direction = _direction;
        if (direction is not (0 or 1))
        {
            return 0;
        }

        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                if (direction == 0)
                {
                    var b = Math.Min((c & 0xff) << 1, 0xff);
                    var g = Math.Min((c & 0xff00) << 1, 0xff00);
                    var r = Math.Min((c & 0xff0000) << 1, 0xff0000);
                    framebuffer[at] = r | g | b;
                }
                else
                {
                    framebuffer[at] = (c >> 1) & 0x7f7f7f;
                }
            }
        });
        return 0;
    }
}
