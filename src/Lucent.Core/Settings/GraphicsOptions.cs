namespace Lucent.Settings;

/// <summary>How many pixels the visuals are drawn with.</summary>
public enum RenderResolution
{
    /// <summary>The visual area's own size.</summary>
    Window,

    /// <summary>About 1920 x 1080 pixels' worth, in the visual area's shape.</summary>
    FullHd,

    /// <summary>About 2560 x 1440 pixels' worth.</summary>
    QuadHd,

    /// <summary>About 3840 x 2160 pixels' worth.</summary>
    UltraHd,

    /// <summary>The visual area's own size, or fewer pixels while a visual can't keep up with the screen.</summary>
    Auto,
}

/// <summary>How much work each visual puts into fine detail (ray-march steps, fractal depth).</summary>
public enum DetailLevel
{
    Low,
    Normal,
    High,
    Ultra,
}

public enum GlowLevel
{
    Off,
    Soft,
    Normal,
    Strong,
}

public enum TransitionSpeed
{
    Quick,
    Smooth,
    Slow,
}

/// <summary>What each option means to the renderer.</summary>
public static class GraphicsOptions
{
    /// <summary>Pixels to draw, or 0 to match the window (Automatic draws fewer while a visual can't keep up).</summary>
    public static int Pixels(RenderResolution resolution) => resolution switch
    {
        RenderResolution.FullHd => 1920 * 1080,
        RenderResolution.QuadHd => 2560 * 1440,
        RenderResolution.UltraHd => 3840 * 2160,
        _ => 0,
    };

    public static float Quality(DetailLevel detail) => (float)detail;

    public static float BloomStrength(GlowLevel glow) => glow switch
    {
        GlowLevel.Off => 0f,
        GlowLevel.Soft => 0.5f,
        GlowLevel.Strong => 1.7f,
        _ => 1f,
    };

    public static float Seconds(TransitionSpeed speed) => speed switch
    {
        TransitionSpeed.Quick => 1.5f,
        TransitionSpeed.Slow => 5f,
        _ => 3f,
    };
}
