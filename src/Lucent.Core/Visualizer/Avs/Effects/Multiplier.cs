namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Trans / Multiplier (r_multiplier.cpp): multiplies every channel by 8, 4, 2, 1/2, 1/4 or 1/8, or turns
/// every lit pixel white ("infinite root") or every pixel that isn't pure white black ("infinite square").
/// </summary>
[AvsEffect("Multiplier")]
public sealed class Multiplier : AvsEffect
{
    private const int InfiniteRoot = 0;
    private const int Times8 = 1;
    private const int Times4 = 2;
    private const int Times2 = 3;
    private const int Half = 4;
    private const int Quarter = 5;
    private const int Eighth = 6;
    private const int InfiniteSquare = 7;

    private int _mode = Times2;

    public Multiplier(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        // As the plug-in: settings of any other size reset to 0 (infinite root).
        _mode = config.Length == 4 ? config.ReadInt() : InfiniteRoot;
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        var mode = _mode;
        if (mode is < InfiniteRoot or > InfiniteSquare)
        {
            return 0;
        }

        var shift = mode switch
        {
            Times8 => 3,
            Times4 => 2,
            Times2 => 1,
            Half => 1,
            Quarter => 2,
            Eighth => 3,
            _ => 0,
        };
        var darkMask = (0xff >> shift) * 0x010101;
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at] & 0xffffff;
                framebuffer[at] = mode switch
                {
                    InfiniteRoot => c != 0 ? 0xffffff : 0,
                    InfiniteSquare => c == 0xffffff ? c : 0,
                    Times8 or Times4 or Times2 =>
                        Math.Min((c & 0xff) << shift, 0xff)
                        | Math.Min((c & 0xff00) << shift, 0xff00)
                        | Math.Min((c & 0xff0000) << shift, 0xff0000),
                    _ => (c >> shift) & darkMask,
                };
            }
        });
        return 0;
    }
}
