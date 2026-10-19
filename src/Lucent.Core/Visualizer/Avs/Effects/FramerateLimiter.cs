namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>
/// Misc / Framerate Limiter ("VFX FRAMERATE LIMITER", e_frameratelimiter.cpp): held AVS to a set frame rate.
/// Lucent runs presets at a steady frame rate of its own, so it does nothing here.
/// </summary>
[AvsEffect("VFX FRAMERATE LIMITER")]
public sealed class FramerateLimiter : AvsEffect
{
    public FramerateLimiter(AvsShared shared)
        : base(shared)
    {
    }

    /// <summary>Whether the limiter was switched on.</summary>
    public bool Enabled { get; private set; } = true;

    /// <summary>The frames per second it held AVS to.</summary>
    public int Limit { get; private set; } = 30;

    public override void Load(AvsConfigReader config)
    {
        Enabled = config.ReadInt(1) != 0;
        Limit = config.ReadInt(30);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout) => 0;
}
