namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Rotating Stars (r_rotstar.cpp): two five-pointed stars circling the centre, each swelling with its channel's bass peaks.</summary>
[AvsEffect(13)]
public sealed class RotatingStars : AvsEffect
{
    private readonly int[] _colours = new int[16];
    private int _colourCount = 1;
    private int _colourPosition;
    private double _rotation;

    public RotatingStars(AvsShared shared)
        : base(shared)
    {
        _colours[0] = 0xffffff;
    }

    public override void Load(AvsConfigReader config)
    {
        _colourCount = RenderHelpers.ReadColours(config, _colours, _colourCount, 0);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_colourCount == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var audio = context.Audio;
        var colour = RenderHelpers.CycleColour(_colours, _colourCount, ref _colourPosition);
        var blend = Shared.LineBlendMode;
        var lineWidth = RenderHelpers.LineWidth(context);
        var x = (int)(Math.Cos(_rotation) * w / 4.0);
        var y = (int)(Math.Sin(_rotation) * h / 4.0);
        for (var c = 0; c < 2; c++)
        {
            var r2 = -_rotation;
            var s = 0;
            var a = x;
            var b = y;

            // The biggest clear peak in the low spectrum (read as signed chars, as AVS did).
            for (var l = 3; l < 14; l++)
            {
                var value = audio.Signed(0, c, l);
                if (value > s && value > audio.Signed(0, c, l + 1) + 4 && value > audio.Signed(0, c, l - 1) + 4)
                {
                    s = value;
                }
            }

            if (c == 1)
            {
                a = -a;
                b = -b;
            }

            var vw = w / 8.0 * (s + 9) / 88.0;
            var vh = h / 8.0 * (s + 9) / 88.0;
            var lx = w / 2 + a + (int)(Math.Cos(r2) * vw);
            var ly = h / 2 + b + (int)(Math.Sin(r2) * vh);
            r2 += 3.14159 * 4.0 / 5.0;
            for (var t = 0; t < 5; t++)
            {
                var nx = (int)(Math.Cos(r2) * vw + w / 2 + a);
                var ny = (int)(Math.Sin(r2) * vh + h / 2 + b);
                r2 += 3.14159 * 4.0 / 5.0;
                AvsDraw.Line(framebuffer, lx, ly, nx, ny, w, h, colour, lineWidth, blend);
                lx = nx;
                ly = ny;
            }
        }

        _rotation += 0.1;
        return 0;
    }
}
