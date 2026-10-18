namespace WinampInspiredVisualizer.Editor.Effects;

/// <summary>
/// One step of processing in the editor: it takes a piece of audio (the selection, or the
/// whole file) and renders a new piece to put in its place.
/// </summary>
/// <remarks>
/// Effects run on a background thread and must not touch the UI. The input is a private
/// copy, so an effect may change it in place and return it. The output may be a different
/// length (pitch with speed change, a plugin's reverb tail), but must have the same sample
/// rate and number of channels. Anything that renders audio, such as a VST plugin, can be
/// added by implementing this interface.
/// </remarks>
public interface IAudioEffect
{
    /// <summary>What the effect did, in a few words, for the Undo button: "Normalize to -1 dB".</summary>
    string Description { get; }

    /// <summary>More about what it did, for the history list, when the description doesn't say it all.</summary>
    string? Details => null;

    /// <summary>Renders the effect. Throws <see cref="OperationCanceledException"/> if cancelled.</summary>
    AudioBuffer Process(AudioBuffer input, EffectContext context);
}

/// <summary>What an effect gets besides the audio: progress reporting, cancelling, and a way to explain its result.</summary>
public sealed class EffectContext
{
    private readonly IProgress<double>? _progress;

    public EffectContext(IProgress<double>? progress, CancellationToken cancellation)
    {
        _progress = progress;
        Cancellation = cancellation;
    }

    public CancellationToken Cancellation { get; }

    /// <summary>
    /// A short message for the user about the result, such as "Reached -15.2 LUFS; any louder would clip".
    /// Left null when there is nothing to say.
    /// </summary>
    public string? Note { get; set; }

    /// <summary>Reports how far along the effect is, from 0 to 1, and stops if the user cancelled.</summary>
    public void Report(double fraction)
    {
        Cancellation.ThrowIfCancellationRequested();
        _progress?.Report(Math.Clamp(fraction, 0, 1));
    }
}
