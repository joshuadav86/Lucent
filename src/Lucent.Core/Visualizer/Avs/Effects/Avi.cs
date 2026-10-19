namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / AVI (r_avi.cpp): played a video file over the picture. Lucent can't play video files in presets, so it draws nothing.</summary>
[AvsEffect(32)]
public sealed class Avi : AvsEffect
{
    public Avi(AvsShared shared)
        : base(shared)
    {
    }

    /// <summary>The video file it played.</summary>
    public string FileName { get; private set; } = string.Empty;

    public override void Load(AvsConfigReader config)
    {
        var enabled = config.ReadInt(1);
        config.ReadInt(); // additive
        config.ReadInt(); // 50/50
        FileName = config.Remaining > 0 ? config.ReadTerminatedString(255) : string.Empty;
        if (enabled != 0)
        {
            Warn(FileName.Length > 0
                ? $"Video files (\"{FileName}\") can't play in Lucent, so the AVI effect is left blank."
                : "Video files can't play in Lucent, so the AVI effect is left blank.");
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout) => 0;
}
