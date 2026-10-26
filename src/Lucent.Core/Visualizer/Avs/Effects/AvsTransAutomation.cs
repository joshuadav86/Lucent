namespace Lucent.Visualizer.Avs.Effects;

/// <summary>
/// Misc / AVSTrans Automation ("Misc: AVSTrans Automation", e_eeltrans.cpp): rewrote the other effects' code
/// before it compiled, adding its own shorthand. Lucent doesn't translate code, so it does nothing here.
/// </summary>
[AvsEffect("Misc: AVSTrans Automation")]
public sealed class AvsTransAutomation : AvsEffect
{
    public AvsTransAutomation(AvsShared shared)
        : base(shared)
    {
    }

    /// <summary>The code it added to the start of every effect's code.</summary>
    public string Code { get; private set; } = string.Empty;

    public override void Load(AvsConfigReader config)
    {
        var enabled = config.ReadInt(1) != 0;
        config.ReadInt(); // log
        config.ReadInt(); // translate the first level
        config.ReadInt(); // read code from comments
        Code = config.Remaining > 0 ? config.ReadTerminatedString() : string.Empty;
        if (enabled)
        {
            Warn("AVSTrans code translation isn't supported, so this preset's code runs as written.");
        }
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout) => 0;
}
