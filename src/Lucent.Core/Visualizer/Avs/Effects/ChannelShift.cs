namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Channel Shift (r_chanshift.cpp): swaps the red, green and blue channels round, optionally picking a new order on each beat.</summary>
[AvsEffect("Channel Shift")]
public sealed class ChannelShift : AvsEffect
{
    // The modes are saved as the numbers of the settings dialog's buttons.
    private const int Gbr = 1018;
    private const int Brg = 1019;
    private const int Rbg = 1020;
    private const int Bgr = 1021;
    private const int Grb = 1022;
    private const int Rgb = 1183;

    private static readonly int[] RandomModes = { Rgb, Rbg, Gbr, Grb, Brg, Bgr };

    private int _mode = Rbg;
    private int _onBeat = 1;

    public ChannelShift(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        // The plug-in copied its 8 bytes of settings straight in, and ignored anything longer.
        if (config.Length <= 8)
        {
            _mode = config.ReadInt(_mode);
            _onBeat = config.ReadInt(_onBeat);
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        if (context.IsBeat && _onBeat != 0)
        {
            _mode = RandomModes[Shared.Random.Next(RandomModes.Length)];
        }

        // Where each new byte (blue, green, red in memory order) takes its value from.
        (int b, int g, int r) = _mode switch
        {
            Rbg => (8, 0, 16),
            Brg => (8, 16, 0),
            Bgr => (16, 8, 0),
            Gbr => (16, 0, 8),
            Grb => (0, 16, 8),
            _ => (-1, -1, -1),
        };
        if (b < 0)
        {
            return 0;
        }

        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                var c = framebuffer[at];
                framebuffer[at] = ((c >> b) & 0xff) | (((c >> g) & 0xff) << 8) | (((c >> r) & 0xff) << 16);
            }
        });
        return 0;
    }
}
