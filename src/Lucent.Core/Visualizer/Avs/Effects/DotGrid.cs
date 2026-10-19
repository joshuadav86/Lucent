namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Dot Grid (r_dotgrid.cpp): a regular grid of dots that can drift across the picture, in cycling colours.</summary>
[AvsEffect(17)]
public sealed class DotGrid : AvsEffect
{
    private readonly int[] _colours = new int[16];
    private int _colourCount = 1;
    private int _colourPosition;
    private int _xPosition;
    private int _yPosition;
    private int _xMove = 128;
    private int _yMove = 128;
    private int _spacing = 8;
    private int _blend = 3;

    public DotGrid(AvsShared shared)
        : base(shared)
    {
        _colours[0] = 0xffffff;
    }

    public override void Load(AvsConfigReader config)
    {
        _colourCount = RenderHelpers.ReadColours(config, _colours, _colourCount, 0);
        _spacing = config.ReadInt(_spacing);
        _xMove = config.ReadInt(_xMove);
        _yMove = config.ReadInt(_yMove);
        _blend = config.ReadInt(_blend);
    }

    public override void Restart()
    {
        _xPosition = 0;
        _yPosition = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_colourCount == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var colour = RenderHelpers.CycleColour(_colours, _colourCount, ref _colourPosition);
        if (_spacing < 2)
        {
            _spacing = 2;
        }

        // AVS added spacing*256 until the position wasn't negative; the same, in one step.
        var period = _spacing * 256L;
        if (_yPosition < 0)
        {
            _yPosition = (int)((_yPosition % period + period) % period);
        }

        if (_xPosition < 0)
        {
            _xPosition = (int)((_xPosition % period + period) % period);
        }

        var spacing = _spacing;
        var sy = (_yPosition >> 8) % spacing;
        var sx = (_xPosition >> 8) % spacing;

        // 1 additive, 2 50/50, 3 the line blend mode, anything else replace.
        var mode = _blend switch
        {
            1 => 1,
            2 => 3,
            3 => Shared.LineBlendMode,
            _ => 0,
        };

        // Dots grow with the dot scale, but stay apart so the grid still reads as a grid.
        var size = Math.Clamp(RenderHelpers.DotSize(context), 1, Math.Max(1, spacing - 1));
        if (size <= 1)
        {
            context.ForEachRow(0, (h - sy + spacing - 1) / spacing, row =>
            {
                var at = (sy + row * spacing) * w;
                for (var x = sx; x < w; x += spacing)
                {
                    framebuffer[at + x] = AvsBlend.Line(framebuffer[at + x], colour, mode);
                }
            });
        }
        else
        {
            for (var y = sy; y < h; y += spacing)
            {
                for (var x = sx; x < w; x += spacing)
                {
                    AvsDraw.Dot(framebuffer, x, y, w, h, colour, size, mode);
                }
            }
        }

        _xPosition += _xMove;
        _yPosition += _yMove;
        return 0;
    }
}
