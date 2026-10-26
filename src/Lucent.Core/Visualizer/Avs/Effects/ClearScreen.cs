namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Render / Clear screen (r_clear.cpp): fills the picture with a colour every frame (or only the first), or blends it in.</summary>
[AvsEffect(25)]
public sealed class ClearScreen : AvsEffect
{
    private int _enabled = 1;
    private int _colour;
    private int _blend;
    private int _blendAverage;
    private int _onlyFirst;
    private int _frames;

    public ClearScreen(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _enabled = config.ReadInt(_enabled);
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _blendAverage = config.ReadInt(_blendAverage);
        _onlyFirst = config.ReadInt(_onlyFirst);
    }

    public override void Restart() => _frames = 0;

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (_enabled == 0 || (_onlyFirst != 0 && _frames != 0))
        {
            return 0;
        }

        _frames++;
        var colour = _colour;
        int w = context.Width, h = context.Height;

        // Blend 2 is the line blend mode, any other blend additive; otherwise 50/50 or replace.
        var mode = _blend == 2 ? Shared.LineBlendMode : _blend != 0 ? 1 : _blendAverage != 0 ? 3 : 0;
        if (mode == 0)
        {
            Array.Fill(framebuffer, colour, 0, w * h);
            return 0;
        }

        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                framebuffer[at] = AvsBlend.Line(framebuffer[at], colour, mode);
            }
        });
        return 0;
    }
}
