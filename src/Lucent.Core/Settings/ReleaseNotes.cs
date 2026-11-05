namespace Lucent.Settings;

/// <summary>
/// After an update to a new stable release, Lucent asks once whether to open that release's entry on the
/// website's updates page (lucentaudio.net/updates/). A first start doesn't ask, and neither do testers on
/// every build: their builds come before the release's entry is written.
/// </summary>
public static class ReleaseNotes
{
    public const string PageUrl = "https://lucentaudio.net/updates/";

    /// <summary>
    /// The release ("1.3") to ask about now, or null. Either way it notes this version's release as asked, so
    /// the question comes once. <paramref name="version"/> is the app's version, 1.3.build;
    /// <paramref name="firstStart"/> is true on a new install.
    /// </summary>
    public static string? Due(AppSettings settings, string version, bool firstStart)
    {
        if (Release(version) is not { } release)
        {
            return null;
        }

        var name = $"{release.Major}.{release.Minor}";
        var asked = settings.ReleaseNotesAsked;
        if (asked == name)
        {
            return null;
        }

        settings.ReleaseNotesAsked = name;
        var newer = asked is null || (Version.TryParse(asked, out var before) && release > before);
        return newer && !firstStart && !settings.EveryBuild ? name : null;
    }

    /// <summary>The updates page at <paramref name="release"/>'s entry ("1.3" is #v1-3).</summary>
    public static string PageFor(string release) => $"{PageUrl}#v{release.Replace('.', '-')}";

    /// <summary>"1.3.1250" is release 1.3. A build CI didn't stamp (1.0.0, 0.x) isn't a release.</summary>
    private static Version? Release(string version)
    {
        if (!Version.TryParse(version, out var parsed) || parsed.Major < 1 || parsed.Build <= 0)
        {
            return null;
        }

        return new Version(parsed.Major, parsed.Minor);
    }
}
