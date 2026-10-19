namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Invert (r_invert.cpp): turns every colour into its opposite (black to white, red to cyan).</summary>
[AvsEffect(37)]
public sealed class Invert : AvsEffect
{
    private int _enabled = 1;

    public Invert(AvsShared shared)
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

        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                framebuffer[at] ^= 0xffffff;
            }
        });
        return 0;
    }
}
