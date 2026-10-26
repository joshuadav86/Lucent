namespace Lucent.Visualizer.MilkDrop;

/// <summary>
/// How MilkDrop presets look on screen: modernised (the Options switch; on unless it's turned off), so their
/// picture goes through Lucent's sharpening, glow, tone-mapping and vignette like Lucent's own visuals, or plain,
/// as MilkDrop showed them. It's the app's setting rather than a visualizer's, so recordings match the screen.
/// </summary>
public static class MilkDropLook
{
    public static bool Modern { get; set; } = true;

    /// <summary>
    /// Whether <paramref name="source"/> is a MilkDrop preset shown modernised, whose finished 8-bit picture the
    /// screen pass first brings into Lucent's light (see PSPresent in Engine.hlsl).
    /// </summary>
    public static bool BringsIntoLight(PresetSource source) => source.IsMilkDrop && Modern;
}
