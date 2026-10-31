namespace Lucent.Audio;

/// <summary>
/// What the equalizer is doing to the streaming app Lucent controls, for the equalizer window's status line.
/// </summary>
/// <param name="App">The app's name, such as "Spotify".</param>
/// <param name="Shaping">
/// The equalizer the app's sound is going through right now (a Mac plays it through a copy of the player's), or null
/// while it isn't. On Windows, where Equalizer APO does the shaping, the player's own equalizer.
/// </param>
/// <param name="Problem">Why the app isn't being shaped though it was asked for, or null.</param>
/// <param name="Action">The label of a button that would get it shaped ("Turn on", "Set up"), or null.</param>
public sealed record StreamingEqualizerStatus(string App, Equalizer? Shaping, string? Problem, string? Action)
{
    /// <summary>The status line's words.</summary>
    public string Text => Shaping is not null ? $"The EQ is shaping {App}'s sound too"
        : Problem is not null ? Problem
        : Action is not null ? $"{App} plays its own sound, so this EQ can't change it yet"
        : $"{App} will play through the EQ as soon as it plays";
}
