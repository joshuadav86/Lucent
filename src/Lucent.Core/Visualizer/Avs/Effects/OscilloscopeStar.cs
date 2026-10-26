namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Render / Oscilloscope Star (r_oscstar.cpp): five arms of waveform spinning out from a centre, like a star, in cycling colours.</summary>
[AvsEffect(2)]
public sealed class OscilloscopeStar : AvsEffect
{
    private readonly int[] _colours = new int[16];
    private readonly byte[] _data = new byte[AvsAudio.Samples];
    private int _effect = 0 | (2 << 2) | (2 << 4);
    private int _colourCount = 1;
    private int _size = 8;
    private int _rotationSpeed = 3;
    private int _colourPosition;
    private double _rotation;

    public OscilloscopeStar(AvsShared shared)
        : base(shared)
    {
        _colours[0] = 0xffffff;
    }

    public override void Load(AvsConfigReader config)
    {
        _effect = config.ReadInt(_effect);
        _colourCount = RenderHelpers.ReadColours(config, _colours, _colourCount, 1);
        _size = config.ReadInt(_size);
        _rotationSpeed = config.ReadInt(_rotationSpeed);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_colourCount == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var colour = RenderHelpers.CycleColour(_colours, _colourCount, ref _colourPosition);
        RenderHelpers.ReadChannel(context.Audio, 1, (_effect >> 2) & 3, _data);
        var data = _data;
        var yPosition = _effect >> 4;
        var blend = Shared.LineBlendMode;
        var lineWidth = RenderHelpers.LineWidth(context);

        var size = _size / 32.0;
        var length = Math.Min((int)(h * size), (int)(w * size));
        var centreX = yPosition == 2 ? w / 2 : yPosition == 0 ? w / 4 : w / 2 + w / 4;
        var ii = 0;
        for (var q = 0; q < 5; q++)
        {
            var s = Math.Sin(_rotation + q * (3.14159 * 2.0 / 5.0));
            var c = Math.Cos(_rotation + q * (3.14159 * 2.0 / 5.0));
            var p = 0.0;
            var lx = centreX;
            var ly = h / 2;
            var t = 64;
            var dp = length / (double)t;
            var dfactor = 1.0 / 1024.0;
            double hw = length;
            while (t-- > 0)
            {
                // The waveform as signed values.
                var ale = ((data[ii] ^ 128) - 128) * dfactor * hw;
                ii++;
                var x = centreX + (int)(c * p) - (int)(s * ale);
                var y = h / 2 + (int)(s * p) + (int)(c * ale);
                if ((x >= 0 && x < w && y >= 0 && y < h) || (lx >= 0 && lx < w && ly >= 0 && ly < h))
                {
                    AvsDraw.Line(framebuffer, x, y, lx, ly, w, h, colour, lineWidth, blend);
                }

                lx = x;
                ly = y;
                p += dp;
                dfactor -= (1.0 / 1024.0f - 1.0 / 128.0f) / 64.0f;
            }
        }

        _rotation += 0.01 * _rotationSpeed;
        if (_rotation >= 3.14159 * 2)
        {
            _rotation -= 3.14159 * 2;
        }

        return 0;
    }
}
