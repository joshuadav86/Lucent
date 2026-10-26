namespace Lucent.Audio;

/// <summary>
/// A stream that keeps time by itself rather than only when it's read, such as a video, which goes on showing
/// pictures. The player tells it when play starts, pauses and stops, so it can do the same.
/// </summary>
public interface IPlayerControlled
{
    /// <summary>The player has started (or carried on) playing.</summary>
    void Playing();

    /// <summary>The player has paused.</summary>
    void Paused();

    /// <summary>The player has stopped, and gone back to the start.</summary>
    void Stopped();
}
