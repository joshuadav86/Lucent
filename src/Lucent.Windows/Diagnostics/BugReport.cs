using System.Diagnostics;

namespace Lucent;

/// <summary>
/// The options panel's "Report a bug" button: opens a new GitHub issue from the bug report form, with the
/// version, Windows, graphics card and recent errors filled in. Nothing is sent from Lucent itself; the
/// person sees everything in their browser and decides whether to submit it.
/// </summary>
public static class BugReport
{
    // Same repository the updates come from; it kept its old name when the app became Lucent.
    private const string NewIssueUrl = "https://github.com/joshuadav86/Lucid/issues/new";

    // Browsers and GitHub cope with links of about 8,000 characters. Escaping can triple the log's
    // length, so it gets what's left after the rest.
    private const int MaxLogChars = 2000;

    public static string Url
    {
        get
        {
            var fields = new (string Id, string Value)[]
            {
                ("template", "bug_report.yml"),
                ("version", AppInfo.Version),
                ("windows", AppInfo.WindowsVersion),
                ("gpu", AppInfo.GraphicsCards),
                ("log", ErrorLog.ReadTail(MaxLogChars)),
            };
            var query = string.Join("&", fields
                .Where(entry => entry.Value.Length > 0)
                .Select(entry => $"{entry.Id}={Uri.EscapeDataString(entry.Value)}"));
            return $"{NewIssueUrl}?{query}";
        }
    }

    /// <summary>Opens the form in the default browser.</summary>
    public static void Open()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser to hand it to. Nothing else in Lucent depends on it.
        }
    }
}
