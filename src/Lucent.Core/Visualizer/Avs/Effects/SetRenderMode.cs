namespace Lucent.Visualizer.Avs.Effects;

/// <summary>Misc / Set render mode (r_linemode.cpp): sets how the lines and dots of the effects after it are blended onto the picture, and how wide lines are.</summary>
[AvsEffect(40)]
public sealed class SetRenderMode : AvsEffect
{
    private int _newMode = unchecked((int)0x80010000);

    public SetRenderMode(AvsShared shared)
        : base(shared)
    {
    }

    public override void Load(AvsConfigReader config)
    {
        _newMode = config.ReadInt(_newMode);
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout)
    {
        // The top bit is the effect's "enabled" box.
        if ((_newMode & unchecked((int)0x80000000)) != 0)
        {
            Shared.LineBlendMode = _newMode & 0x7fffffff;
        }

        return 0;
    }
}
