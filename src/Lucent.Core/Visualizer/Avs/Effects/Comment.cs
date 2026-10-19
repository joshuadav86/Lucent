namespace WinampInspiredVisualizer.Visualizer.Avs.Effects;

/// <summary>Misc / Comment (r_comment.cpp): a note the preset's author left; it draws nothing.</summary>
[AvsEffect(21)]
public sealed class Comment : AvsEffect
{
    public Comment(AvsShared shared)
        : base(shared)
    {
    }

    /// <summary>The author's note.</summary>
    public string Text { get; private set; } = string.Empty;

    public override void Load(AvsConfigReader config)
    {
        Text = config.ReadString();
    }

    public override int Render(AvsRenderContext context, int[] framebuffer, int[] fbout) => 0;
}
