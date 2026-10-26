namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Render / Ring (r_oscring.cpp): a closed ring whose radius follows the waveform or spectrum around its edge, in cycling colours.</summary>
[AvsEffect(14)]
public sealed class Ring : AvsEffect
{
    private readonly int[] _colours = new int[16];
    private readonly byte[] _data = new byte[AvsAudio.Samples];
    private int _effect = 0 | (2 << 2) | (2 << 4);
    private int _colourCount = 1;
    private int _size = 8;
    private int _source;
    private int _colourPosition;

    public Ring(AvsShared shared)
        : base(shared)
    {
        _colours[0] = 0xffffff;
    }

    public override void Load(AvsConfigReader config)
    {
        _effect = config.ReadInt(_effect);
        _colourCount = RenderHelpers.ReadColours(config, _colours, _colourCount, 1);
        _size = config.ReadInt(_size);
        _source = config.ReadInt(_source);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_colourCount == 0)
        {
            return 0;
        }

        int w = context.Width, h = context.Height;
        var colour = RenderHelpers.CycleColour(_colours, _colourCount, ref _colourPosition);

        // Source 0 is the waveform, anything else the spectrum.
        RenderHelpers.ReadChannel(context.Audio, _source != 0 ? 0 : 1, (_effect >> 2) & 3, _data);
        var data = _data;
        var yPosition = _effect >> 4;
        var blend = Shared.LineBlendMode;
        var lineWidth = RenderHelpers.LineWidth(context);

        var s = _size / 32.0;
        var length = Math.Min(h * s, w * s);
        var centreX = yPosition == 2 ? w / 2 : yPosition == 0 ? w / 4 : w / 2 + w / 4;
        var centreY = h / 2;
        var a = 0.0;
        var scale = Level(data, 0);
        var lx = (int)(centreX + Math.Cos(a) * length * scale);
        var ly = (int)(centreY + Math.Sin(a) * length * scale);
        for (var q = 1; q <= 80; q++)
        {
            a -= 3.14159 * 2.0 / 80.0;
            scale = Level(data, q > 40 ? 80 - q : q);
            var tx = (int)(centreX + Math.Cos(a) * length * scale);
            var ty = (int)(centreY + Math.Sin(a) * length * scale);
            if ((tx >= 0 && tx < w && ty >= 0 && ty < h) || (lx >= 0 && lx < w && ly >= 0 && ly < h))
            {
                AvsDraw.Line(framebuffer, tx, ty, lx, ly, w, h, colour, lineWidth, blend);
            }

            lx = tx;
            ly = ty;
        }

        return 0;
    }

    /// <summary>How far out the ring is at point <paramref name="q"/> (0..40), 0.1 to 1.</summary>
    private double Level(byte[] data, int q) => _source == 0
        ? 0.1 + (data[q] ^ 128) / 255.0 * 0.9
        : 0.1 + (data[q * 2] / 2 + data[q * 2 + 1] / 2) / 255.0 * 0.9;
}
