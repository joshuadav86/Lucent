namespace Lucent.Visualizer;

/// <summary>
/// Holds the visuals to about 60 frames a second on a faster screen (Options › Graphics, "Cap visuals
/// at 60 fps"). On a 120 Hz screen every other refresh is shown, on 144 Hz every other one too (72 fps):
/// whole refreshes keep the motion even. That's about half the drawing, and half the work for the
/// system's compositor (WindowServer on the Mac, DWM on Windows), for visuals made for 60 fps.
/// Screens at 90 Hz or slower are drawn at their own rate.
/// </summary>
public static class FrameRateCap
{
    private static volatile bool _on = true;

    /// <summary>Whether the cap is on. Set from the UI thread, read by the render threads.</summary>
    public static bool On
    {
        get => _on;
        set => _on = value;
    }

    /// <summary>How many screen refreshes each frame stays up for: 1 with the cap off or on a screen up to 90 Hz.</summary>
    public static int RefreshesPerFrame(int refreshRate) =>
        _on && refreshRate > 90 ? Math.Max(1, (int)Math.Round(refreshRate / 60.0)) : 1;

    /// <summary>The frame rate the visuals can reach on a screen at <paramref name="refreshRate"/>.</summary>
    public static int FrameRate(int refreshRate) => Math.Max(1, refreshRate / RefreshesPerFrame(refreshRate));

    /// <summary>
    /// The least time each frame should stay on screen, in seconds, or 0 for no limit. A little under the
    /// refreshes it should take, so timing noise never rounds it up to one refresh more.
    /// </summary>
    public static double MinimumFrameSeconds(int refreshRate)
    {
        var refreshes = RefreshesPerFrame(refreshRate);
        return refreshes <= 1 ? 0 : (refreshes - 0.25) / refreshRate;
    }
}
