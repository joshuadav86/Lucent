using System.Text.RegularExpressions;

namespace Lucent.Diagnostics;

/// <summary>
/// Takes anything personal out of the text a crash report carries: file paths (they name the user's folders
/// and music), web addresses past their site, email addresses, and the user's and computer's names. What's
/// left says what went wrong without saying whose computer it was or what was playing.
/// </summary>
public static class CrashReportScrubber
{
    // Anything longer is cut: a report needs the start of a message, not a page of it.
    private const int MaxLength = 4000;

    private static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly Regex Url = new(@"\b[a-zA-Z][a-zA-Z0-9+.\-]*://(?<host>[^/\s'""<>?#]*)[^\s'""<>]*", RegexOptions.Compiled);

    private static readonly Regex Email = new(@"[\w.+\-]+@[\w\-]+(?:\.[\w\-]+)+", RegexOptions.Compiled);

    // A .NET stack trace line's source file: "in D:\a\Lucent\src\Player.cs:line 12" keeps just "Player.cs".
    private static readonly Regex SourceFile = new(@"\bin (?<path>[^\r\n]*?[\\/](?<file>[^\\/\r\n]+)):line (?<line>\d+)", RegexOptions.Compiled);

    // Where a path starts: a drive (C:\), a network share (\\server\), home (~/), or a folder people's files live under.
    private const string PathStart =
        @"(?:[A-Za-z]:[\\/]|\\\\[^\\\s]+\\|~(?=[\\/])|/(?:Users|home|Volumes|private|var|tmp|mnt|media|run|opt|Library|Applications|System|usr|etc|root)(?=[/\s'""]|$))";

    // A path in quotes is everything between the quotes, spaces and all.
    private static readonly Regex QuotedPath = new(@"(?<q>['""`])(?<path>" + PathStart + @"[^'""`\r\n]*)\k<q>", RegexOptions.Compiled);

    // A path out of quotes can't be told from the words after it, so it runs to the end of the line or the
    // next bracket, comma, or quote. That can take a few words with it; it never leaves part of a name behind.
    private static readonly Regex BarePath = new(@"(?<![\w.:/\\~\-])" + PathStart + @"[^\r\n,;()\[\]<>""'`]*", RegexOptions.Compiled);

    private static readonly Regex Extension = new(@"\.(?<ext>[A-Za-z0-9]{1,5})$", RegexOptions.Compiled);

    private static readonly Regex FileEnd = new(@"\.[A-Za-z0-9]{1,5}(?=[\s.:]|$)", RegexOptions.Compiled);

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (Home.Length > 1)
        {
            text = text.Replace(Home, "~", OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        text = Url.Replace(text, match => match.Groups["host"].Value.Length > 0
            ? $"{match.Value[..match.Value.IndexOf("://", StringComparison.Ordinal)]}://{match.Groups["host"].Value}/…"
            : "<link>");
        text = Email.Replace(text, "<email>");
        text = SourceFile.Replace(text, match => $"in {match.Groups["file"].Value}:line {match.Groups["line"].Value}");
        text = QuotedPath.Replace(text, match => $"{match.Groups["q"].Value}{Describe(match.Groups["path"].Value)}{match.Groups["q"].Value}");
        text = BarePath.Replace(text, match =>
        {
            // A file's extension ends its path ("…\Song Name.wav while decoding"); without one, a sentence's
            // full stop or a trailing space isn't part of it.
            var lastFolder = match.Value.LastIndexOfAny(['/', '\\']);
            var extension = FileEnd.Matches(match.Value, lastFolder + 1).LastOrDefault();
            var path = extension is not null ? match.Value[..(extension.Index + extension.Length)] : match.Value.TrimEnd('.', ' ', ':');
            return Describe(path) + match.Value[path.Length..];
        });

        text = ReplaceWord(text, Environment.UserName, "<user>");
        text = ReplaceWord(text, Environment.MachineName, "<computer>");

        return text.Length <= MaxLength ? text : text[..MaxLength] + "…";
    }

    /// <summary>
    /// "&lt;path&gt;.flac": the kind of file, which often matters to a crash, without its name or folder.
    /// </summary>
    private static string Describe(string path)
    {
        var extension = Extension.Match(path);
        return extension.Success ? $"<path>.{extension.Groups["ext"].Value.ToLowerInvariant()}" : "<path>";
    }

    private static string ReplaceWord(string text, string word, string replacement)
    {
        // Very short names would match ordinary words.
        if (string.IsNullOrWhiteSpace(word) || word.Length < 3)
        {
            return text;
        }

        return Regex.Replace(text, $@"(?<![\w]){Regex.Escape(word)}(?![\w])", replacement, RegexOptions.IgnoreCase);
    }
}
