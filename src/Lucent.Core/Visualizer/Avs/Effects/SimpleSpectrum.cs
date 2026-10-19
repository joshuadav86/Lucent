namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / Simple (r_simple.cpp): a plain spectrum analyzer or oscilloscope, drawn solid, as lines, or as dots, in cycling colours.</summary>
[AvsEffect(0)]
public sealed class SimpleSpectrum : AvsEffect
{
    private readonly int[] _colours = new int[16];
    private readonly byte[] _data = new byte[AvsAudio.Samples];
    private int _effect = 0 | (2 << 2) | (2 << 4);
    private int _colourCount = 1;
    private int _colourPosition;

    public SimpleSpectrum(AvsShared shared)
        : base(shared)
    {
        _colours[0] = 0xffffff;
    }

    public override void Load(AvsConfigReader config)
    {
        _effect = config.ReadInt(_effect);
        _colourCount = RenderHelpers.ReadColours(config, _colours, _colourCount, 0);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_colourCount == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var yscale = h / 2.0f / 256.0f;
        var xscale = 288.0f / w;
        var whichChannel = (_effect >> 2) & 3;
        var yPosition = (_effect >> 4) & 3;
        var colour = RenderHelpers.CycleColour(_colours, _colourCount, ref _colourPosition);

        // Bit 1 picks the oscilloscope (waveform) over the analyzer (spectrum).
        RenderHelpers.ReadChannel(context.Audio, (_effect & 3) > 1 ? 1 : 0, whichChannel, _data);
        var data = _data;
        var blend = Shared.LineBlendMode;
        var lineWidth = RenderHelpers.LineWidth(context);
        var dotSize = RenderHelpers.DotSize(context);

        if ((_effect & (1 << 6)) != 0)
        {
            if ((_effect & 2) != 0)
            {
                // Dot scope.
                var yh = yPosition * h / 2;
                if (yPosition == 2)
                {
                    yh = h / 4;
                }

                for (var x = 0; x < w; x++)
                {
                    var r = x * xscale;
                    var s1 = r - (int)r;
                    var yr = (data[(int)r] ^ 128) * (1.0f - s1) + (data[(int)r + 1] ^ 128) * s1;
                    var y = yh + (int)(yr * yscale);
                    if (y >= 0 && y < h)
                    {
                        AvsDraw.Dot(framebuffer, x, y, w, h, colour, dotSize, 0);
                    }
                }
            }
            else
            {
                // Dot analyzer.
                var h2 = h / 2;
                var ys = yscale;
                var xs = 200.0f / w;
                var adj = 1;
                if (yPosition != 1)
                {
                    ys = -ys;
                    adj = 0;
                }

                if (yPosition == 2)
                {
                    h2 -= (int)(ys * 256 / 2);
                }

                for (var x = 0; x < w; x++)
                {
                    var r = x * xs;
                    var s1 = r - (int)r;
                    var yr = data[(int)r] * (1.0f - s1) + data[(int)r + 1] * s1;
                    var y = h2 + adj + (int)(yr * ys - 1.0f);
                    if (y >= 0 && y < h)
                    {
                        AvsDraw.Dot(framebuffer, x, y, w, h, colour, dotSize, 0);
                    }
                }
            }

            return 0;
        }

        switch (_effect & 3)
        {
            case 0:
            {
                // Solid analyzer.
                var h2 = h / 2;
                var ys = yscale;
                var xs = 200.0f / w;
                var adj = 1;
                if (yPosition != 1)
                {
                    ys = -ys;
                    adj = 0;
                }

                if (yPosition == 2)
                {
                    h2 -= (int)(ys * 256 / 2);
                }

                for (var x = 0; x < w; x++)
                {
                    var r = x * xs;
                    var s1 = r - (int)r;
                    var yr = data[(int)r] * (1.0f - s1) + data[(int)r + 1] * s1;
                    AvsDraw.Line(framebuffer, x, h2 - adj, x, h2 + adj + (int)(yr * ys - 1.0f), w, h, colour, lineWidth, blend);
                }

                break;
            }

            case 1:
            {
                // Line analyzer.
                var h2 = h / 2;
                var xs = 1.0f / xscale * (288.0f / 200.0f);
                var ys = yscale;
                if (yPosition != 1)
                {
                    ys = -ys;
                }

                if (yPosition == 2)
                {
                    h2 -= (int)(ys * 256 / 2);
                }

                var ly = h2 + (int)(data[0] * ys);
                var lx = 0;
                for (var x = 1; x < 200; x++)
                {
                    var oy = h2 + (int)(data[x] * ys);
                    var ox = (int)(x * xs);
                    AvsDraw.Line(framebuffer, lx, ly, ox, oy, w, h, colour, lineWidth, blend);
                    ly = oy;
                    lx = ox;
                }

                break;
            }

            case 2:
            {
                // Line scope.
                var xs = 1.0f / xscale;
                var yh = yPosition == 2 ? h / 4 : yPosition * h / 2;
                var lx = 0;
                var ly = yh + (int)((data[0] ^ 128) * yscale);
                for (var x = 1; x < 288; x++)
                {
                    var ox = (int)(x * xs);
                    var oy = yh + (int)((data[x] ^ 128) * yscale);
                    AvsDraw.Line(framebuffer, lx, ly, ox, oy, w, h, colour, lineWidth, blend);
                    lx = ox;
                    ly = oy;
                }

                break;
            }

            default:
            {
                // Solid scope.
                var yh = yPosition * h / 2;
                if (yPosition == 2)
                {
                    yh = h / 4;
                }

                var ys = yh + (int)(yscale * 128.0f);
                for (var x = 0; x < w; x++)
                {
                    var r = x * xscale;
                    var s1 = r - (int)r;
                    var yr = (data[(int)r] ^ 128) * (1.0f - s1) + (data[(int)r + 1] ^ 128) * s1;
                    AvsDraw.Line(framebuffer, x, ys - 1, x, yh + (int)(yr * yscale), w, h, colour, lineWidth, blend);
                }

                break;
            }
        }

        return 0;
    }
}
