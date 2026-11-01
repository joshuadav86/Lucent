namespace Lucent.Layout;

/// <summary>
/// How a list glides to the song playing, the same on both platforms: an eased scroll of a moment for a song nearby,
/// and for one far away (thousands of rows, say) a longer flight there, easing in and out, that still takes under a
/// second. Also how long the list leaves the user be after they scroll it themselves.
/// </summary>
/// <remarks>
/// A flight never moves the list faster than <see cref="TopSpeed"/>, so every frame shares most of its rows with the
/// one before and the eye can follow the motion at any refresh rate. Crossing thousands of rows at that speed would
/// take seconds, so a long flight sets off from where the list is, speeds up to the top speed, and at that speed (where
/// the rows are a blur anyway) skips the stretch in between, arriving a little way before the song to settle on it.
/// Before this, a long flight crossed whole screens of rows each frame, which looked like a low frame rate.
/// </remarks>
/// <param name="From">The scroll offset the glide sets off from.</param>
/// <param name="To">The scroll offset it settles on.</param>
/// <param name="Duration">How long it takes.</param>
/// <param name="Travel">How far the list is seen to move: the whole way, or less when the stretch in between is skipped.</param>
public readonly record struct ScrollGlide(double From, double To, TimeSpan Duration, double Travel)
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
    /// The fastest a list moves, in screens a second: a little faster than a glide to a song nearby at its quickest
    /// (two screens in 0.38 s sets off at about 16), so those are never cut short. At 144 Hz that is about an eighth of a
    /// screen a frame; at 60 Hz a third.
    /// </summary>
    public const double TopSpeed = 20;

    // An ease, in or out, of a cubic sets off (or arrives) at three times the average speed.
    private const double PeakOverAverage = 3;

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
        var distance = Math.Abs(to - from);
        var farthest = TopSpeed * Math.Max(1, viewport) * duration.TotalSeconds / PeakOverAverage;
        return new ScrollGlide(from, to, duration, Math.Min(distance, farthest));
    }

    /// <summary>Whether this is a long flight rather than a short glide.</summary>
    public bool IsLong => Duration > Short;

    /// <summary>Whether the stretch between where the list was and the song is skipped, at the top speed.</summary>
    public bool SkipsMiddle => Travel < Math.Abs(To - From);

    /// <summary>The offset <paramref name="elapsed"/> into the glide.</summary>
    public double At(TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed / Duration, 0, 1);
        // A short glide eases out (cubic): quick away, settling gently on the song. A long one also eases in, so it sets
        // off from where the list is rather than leaping, then settles the same way.
        var eased = IsLong
            ? t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(2 - 2 * t, 3) / 2
            : 1 - Math.Pow(1 - t, 3);
        if (!SkipsMiddle)
        {
            return From + (To - From) * eased;
        }

        // Halfway through the travel, at the top speed, the list goes on from the same distance short of the song.
        var moved = Travel * eased;
        var direction = Math.Sign(To - From);
        return moved <= Travel / 2 ? From + direction * moved : To - direction * (Travel - moved);
    }

    /// <summary>Whether the glide has reached its end <paramref name="elapsed"/> in.</summary>
    public bool IsDone(TimeSpan elapsed) => elapsed >= Duration;
}
