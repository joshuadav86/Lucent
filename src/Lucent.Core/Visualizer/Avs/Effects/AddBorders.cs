namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Add Borders (Virtual Effect: Addborders, e_addborders.cpp): paints a frame of one colour round the edge of the picture, its width a percentage of the picture's size.</summary>
[AvsEffect("Virtual Effect: Addborders")]
public sealed class AddBorders : AvsEffect
{
    private bool _enabled;
    private int _colour;
    private int _size = 1;

    public AddBorders(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(0) != 0;
        _colour = config.ReadInt(0);
        _size = Math.Clamp(config.ReadInt(1), 1, 50);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (!_enabled)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var colour = _colour & 0xffffff;
        var top = Math.Min(Math.Max(h * _size / 100, 1), h);
        var side = Math.Min(Math.Max(w * _size / 100, 1), w);
        context.ForEachRow(y =>
        {
            var row = y * w;
            if (y < top || y >= h - top)
            {
                Array.Fill(framebuffer, colour, row, w);
                return;
            }

            Array.Fill(framebuffer, colour, row, side);
            Array.Fill(framebuffer, colour, row + w - side, side);
        });
        return 0;
    }
}
