namespace Lucent.Playlist;

/// <summary>Where a long file was left, in seconds from its start.</summary>
public sealed record ResumePoint(string Path, double Seconds);

/// <summary>
/// Where long files were stopped (DJ mixes, live sets, audiobooks), so they start again there: Options > Sound "Pick
/// up long files where you left off". Only files of <see cref="LongerThan"/> or more count, and a place near either
/// end isn't kept, so a file played through starts from the top next time. The places are kept in the settings
/// (<c>ResumePoints</c>), oldest first, the newest <see cref="MostKept"/>.
/// </summary>
public static class ResumePoints
{
    /// <summary>How long a file has to be for its place to be kept.</summary>
    public static readonly TimeSpan LongerThan = TimeSpan.FromMinutes(20);

    /// <summary>Places this close to the start or the end aren't kept.</summary>
    public static readonly TimeSpan Margin = TimeSpan.FromSeconds(30);

    /// <summary>How many files' places are kept, at most; the ones left longest ago go first.</summary>
    public const int MostKept = 100;

    /// <summary>Whether a file this long has its place kept.</summary>
    public static bool Counts(TimeSpan duration) => duration >= LongerThan;

    /// <summary>Where to start <paramref name="path"/>, or null for the start.</summary>
    public static TimeSpan? Where(IReadOnlyList<ResumePoint> points, string path, TimeSpan duration)
    {
        if (!Counts(duration) || points.LastOrDefault(point => point.Path == path) is not { } point || !double.IsFinite(point.Seconds))
        {
            return null;
        }

        var place = TimeSpan.FromSeconds(point.Seconds);
        return place > Margin && place < duration - Margin ? place : null;
    }

    /// <summary>
    /// Keeps <paramref name="position"/> as where <paramref name="path"/> was left, or forgets it when it's near the
    /// start or end (played through, or only just started). Returns true when anything changed.
    /// </summary>
    public static bool Note(List<ResumePoint> points, string path, TimeSpan position, TimeSpan duration)
    {
        if (!Counts(duration) || path.Contains("://", StringComparison.Ordinal))
        {
            return false;
        }

        var index = points.FindIndex(point => point.Path == path);
        if (position <= Margin || position >= duration - Margin)
        {
            if (index < 0)
            {
                return false;
            }

            points.RemoveAt(index);
            return true;
        }

        var seconds = Math.Round(position.TotalSeconds, 1);
        if (index >= 0 && index == points.Count - 1 && Math.Abs(points[index].Seconds - seconds) < 1)
        {
            return false;
        }

        // The newest goes last, and the oldest go once there are too many.
        if (index >= 0)
        {
            points.RemoveAt(index);
        }

        points.Add(new ResumePoint(path, seconds));
        if (points.Count > MostKept)
        {
            points.RemoveRange(0, points.Count - MostKept);
        }

        return true;
    }
}
