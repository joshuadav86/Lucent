namespace Lucent.Layout;

/// <summary>
/// How a list glides to the song playing, the same on both platforms: an eased scroll of a moment for a song nearby,
/// and for one far away (thousands of rows, say) a longer flight the whole way there, easing in and out, that still
/// takes under a second. Also how long the list leaves the user be after they scroll it themselves.
/// </summary>
public readonly record struct ScrollGlide(double From, double To, TimeSpan Duration)
{
    /// <summary>How long a glide to a song nearby takes.</summary>
    public static readonly TimeSpan Short = TimeSpan.FromMilliseconds(380);

    /// <summary>The longest a glide takes, however far the song is.</summary>
    public static readonly TimeSpan Longest = TimeSpan.FromMilliseconds(850);

    /// <summary>How long after the user scrolls the list (or moves through it with the keys) it stays where they put it.</summary>
    public static readonly TimeSpan HoldAfterUserScroll = TimeSpan.FromSeconds(4);

    // Within this many screens a glide is a short one. Beyond, each doubling of the distance adds a little time, so a
    // song ten screens away takes about 0.6 s and one a few hundred away the longest.
    private const double NearScreens = 2;
    private static readonly TimeSpan PerDoubling = TimeSpan.FromMilliseconds(90);

    /// <summary>
    /// Where the scroll offset for a row from <paramref name="rowTop"/> of <paramref name="rowHeight"/> puts it in the middle
    /// of a <paramref name="viewport"/>, as far as the list can scroll (<paramref name="extent"/> tall).
    /// </summary>
    public static double CenteredOffset(double rowTop, double rowHeight, double viewport, double extent) =>
        Math.Clamp(rowTop + rowHeight / 2 - viewport / 2, 0, Math.Max(0, extent - viewport));

    /// <summary>The glide from <paramref name="from"/> to <paramref name="to"/> in a list <paramref name="viewport"/> tall.</summary>
    public static ScrollGlide Between(double from, double to, double viewport)
    {
        var screens = Math.Abs(to - from) / Math.Max(1, viewport);
        var duration = screens <= NearScreens
            ? Short
            : TimeSpan.FromTicks(Math.Min(Longest.Ticks, Short.Ticks + (long)(PerDoubling.Ticks * Math.Log2(screens / NearScreens))));
        return new ScrollGlide(from, to, duration);
    }

    /// <summary>Whether this is a long flight rather than a short glide.</summary>
    public bool IsLong => Duration > Short;

    /// <summary>The offset <paramref name="elapsed"/> into the glide.</summary>
    public double At(TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed / Duration, 0, 1);
        // A short glide eases out (cubic): quick away, settling gently on the song. A long one also eases in, so it sets
        // off from where the list is rather than leaping, then settles the same way.
        var eased = IsLong
            ? t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(2 - 2 * t, 3) / 2
            : 1 - Math.Pow(1 - t, 3);
        return From + (To - From) * eased;
    }

    /// <summary>Whether the glide has reached its end <paramref name="elapsed"/> in.</summary>
    public bool IsDone(TimeSpan elapsed) => elapsed >= Duration;
}
