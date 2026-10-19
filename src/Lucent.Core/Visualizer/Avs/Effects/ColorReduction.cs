namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Trans / Color Reduction (r_colorreduction.cpp): keeps only the top few bits of each channel, leaving fewer shades.</summary>
[AvsEffect("Color Reduction")]
public sealed class ColorReduction : AvsEffect
{
    // The settings were a 260-byte file name (unused) and then the number of bits to keep.
    private const int SettingsSize = 264;

    private int _levels = 7;

    public ColorReduction(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        // As the plug-in: settings of any other size reset everything to 0.
        if (config.Length == SettingsSize)
        {
            config.Position = SettingsSize - 4;
            _levels = config.ReadInt();
        }
        else
        {
            _levels = 0;
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        // Bits kept per channel, 0 to 8 (out-of-range values clear everything, as the plug-in's loop did).
        var mask = _levels is >= 0 and <= 8 ? (0xff << (8 - _levels)) & 0xff : 0;
        if (mask == 0xff)
        {
            return 0;
        }

        mask |= (mask << 8) | (mask << 16);
        var w = context.Width;
        context.ForEachRow(y =>
        {
            for (int at = y * w, end = at + w; at < end; at++)
            {
                framebuffer[at] &= mask;
            }
        });
        return 0;
    }
}
