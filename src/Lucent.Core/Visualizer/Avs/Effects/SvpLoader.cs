namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Render / SVP Loader (r_svp.cpp): ran a Sonique visualization plug-in (an SVP or UVS file). Those are Windows programs Lucent can't run, so it draws nothing.</summary>
[AvsEffect(10)]
public sealed class SvpLoader : AvsEffect
{
    private const int PathLength = 260;

    public SvpLoader(AvsShared shared)
        : base(shared)
    {
    }

    /// <summary>The plug-in it ran.</summary>
    public string Library { get; private set; } = string.Empty;

    public override void Load(AvsConfigReader config)
    {
        if (config.Remaining >= PathLength)
        {
            Library = config.ReadFixedString(PathLength);
        }

        Warn(Library.Length > 0
            ? $"Sonique plug-ins (\"{Library}\") can't run in Lucent, so the SVP Loader is left blank."
            : "Sonique plug-ins can't run in Lucent, so the SVP Loader is left blank.");
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout) => 0;
}
