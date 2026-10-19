namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / OnBeat Clear (r_nfclr.cpp): fills the picture with a colour (or blends it in half way) every so many beats.</summary>
[AvsEffect(5)]
public sealed class OnBeatClear : AvsEffect
{
    private int _colour = 0xffffff;
    private int _blend;
    private int _everyBeats = 1;
    private int _beatCount;
    private int _otherCount;

    public OnBeatClear(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _colour = config.ReadInt(_colour);
        _blend = config.ReadInt(_blend);
        _everyBeats = config.ReadInt(_everyBeats);
    }

    public override void Restart()
    {
        _beatCount = 0;
        _otherCount = 0;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (context.IsBeat)
        {
            if (_everyBeats != 0 && ++_beatCount >= _everyBeats)
            {
                _beatCount = 0;
                _otherCount = 0;
                var colour = _colour;
                if (_blend == 0)
                {
                    Array.Fill(framebuffer, colour, 0, context.Width * context.Height);
                }
                else
                {
                    var w = context.Width;
                    context.ForEachRow(y =>
                    {
                        for (int at = y * w, end = at + w; at < end; at++)
                        {
                            framebuffer[at] = AvsBlend.Average(framebuffer[at], colour);
                        }
                    });
                }
            }
        }
        else if (++_otherCount >= _everyBeats)
        {
            _otherCount = 0;
        }

        return 0;
    }
}
