namespace Lucent.Layout;

/// <summary>
/// How a list glides to the song playing, the same on both platforms: an eased scroll of a moment, and for a song far
/// away (thousands of rows, say) a jump to a little short of it first, so the glide is always the last stretch only.
/// Also how long the list leaves the user be after they scroll it themselves.
/// </summary>
public static class ScrollGlide
{
    /// <summary>How long the glide takes.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(380);

    /// <summary>How long after the user scrolls the list (or moves through it with the keys) it stays where they put it.</summary>
    public static readonly TimeSpan HoldAfterUserScroll = TimeSpan.FromSeconds(4);

    // Further than this many screens away, the list jumps to this many screens short and glides the rest.
    private const double FarScreens = 2;
    private const double GlideScreens = 1.5;

    /// <summary>
    /// Where the scroll offset for a row from <paramref name="rowTop"/> of <paramref name="rowHeight"/> puts it in the middle
    /// of a <paramref name="viewport"/>, as far as the list can scroll (<paramref name="extent"/> tall).
    /// </summary>
    public static double CenteredOffset(double rowTop, double rowHeight, double viewport, double extent) =>
        Math.Clamp(rowTop + rowHeight / 2 - viewport / 2, 0, Math.Max(0, extent - viewport));

    /// <summary>Where the glide from <paramref name="from"/> to <paramref name="to"/> starts: where it is, unless that's far away.</summary>
    public static double Start(double from, double to, double viewport)
    {
        var reach = Math.Max(1, viewport) * GlideScreens;
        return Math.Abs(to - from) <= Math.Max(1, viewport) * FarScreens ? from : to - Math.Sign(to - from) * reach;
    }

    /// <summary>The offset <paramref name="elapsed"/> into a glide from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public static double At(double start, double end, TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed / Duration, 0, 1);
        // Ease out (cubic): quick away, settling gently on the song.
        var eased = 1 - Math.Pow(1 - t, 3);
        return start + (end - start) * eased;
    }

    /// <summary>Whether the glide has reached its end <paramref name="elapsed"/> in.</summary>
    public static bool IsDone(TimeSpan elapsed) => elapsed >= Duration;
}
