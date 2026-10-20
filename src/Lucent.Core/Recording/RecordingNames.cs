using System.Globalization;
using System.IO;

namespace WinampInspiredVisualizer.Recording;

/// <summary>What a recording's file name is made from, besides the template's own words.</summary>
/// <param name="Name">What's being recorded ("DJ set", "Friday at the Loft").</param>
/// <param name="Started">When the recording started.</param>
/// <param name="Input">The audio interface's name.</param>
public sealed record RecordingNameParts(string Name, DateTime Started, int SampleRate, int BitDepth, string Input);

/// <summary>
/// Names recordings from a template such as "{name} {date} {time}", so a night's takes sort by when they
/// were made and never write over each other. The words in braces are listed in <see cref="Tokens"/>.
/// </summary>
public static class RecordingNames
{
    /// <summary>The words a template can use, each with what it becomes, for the Record window's buttons.</summary>
    public static readonly IReadOnlyList<(string Token, string Meaning)> Tokens = new[]
    {
        ("{name}", "the name above"),
        ("{date}", "the date, like 2026-10-20"),
        ("{time}", "the time it started, like 21-30"),
        ("{take}", "01, 02, 03… counting up so no name is used twice"),
        ("{weekday}", "the day, like Friday"),
        ("{rate}", "the sample rate, like 48k"),
        ("{bits}", "the bit depth, like 24bit"),
        ("{input}", "the audio interface's name"),
    };

    // Windows' list of characters a file name can't have, kept on the Mac too so files copy to a PC.
    private static readonly char[] Invalid = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0' };

    /// <summary>The file name (without folder or extension) the template gives, with take number <paramref name="take"/>.</summary>
    public static string Expand(string template, RecordingNameParts parts, int take)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            template = Settings.RecordingSettings.DefaultTemplate;
        }

        var rate = parts.SampleRate % 1000 == 0
            ? $"{parts.SampleRate / 1000}k"
            : (parts.SampleRate / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        var text = Replace(template, "{name}", parts.Name.Trim());
        text = Replace(text, "{date}", parts.Started.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        text = Replace(text, "{time}", parts.Started.ToString("HH-mm", CultureInfo.InvariantCulture));
        text = Replace(text, "{take}", take.ToString("00", CultureInfo.InvariantCulture));
        text = Replace(text, "{weekday}", parts.Started.ToString("dddd", CultureInfo.CurrentCulture));
        text = Replace(text, "{rate}", rate);
        text = Replace(text, "{bits}", parts.BitDepth == 32 ? "32bit float" : $"{parts.BitDepth}bit");
        text = Replace(text, "{input}", parts.Input.Trim());
        return Clean(text);
    }

    /// <summary>
    /// The full path for the next recording in <paramref name="folder"/>: the template's name with the
    /// lowest take number that isn't taken, and "(2)", "(3)"… on the end if the template has no {take}.
    /// </summary>
    public static string NextPath(string folder, string template, RecordingNameParts parts, string extension = ".wav")
    {
        var usesTake = template.Contains("{take}", StringComparison.OrdinalIgnoreCase);
        for (var take = 1; take < 10000; take++)
        {
            var name = Expand(template, parts, take);
            var path = Path.Combine(folder, name + extension);
            if (!Taken(path))
            {
                return path;
            }

            if (!usesTake)
            {
                for (var n = 2; ; n++)
                {
                    var numbered = Path.Combine(folder, $"{name} ({n}){extension}");
                    if (!Taken(numbered))
                    {
                        return numbered;
                    }
                }
            }
        }

        return Path.Combine(folder, Expand(template, parts, 1) + " " + Guid.NewGuid().ToString("N")[..6] + extension);
    }

    /// <summary>The name of the copy recorded without the plugins, beside the main one.</summary>
    public static string DryPath(string path) =>
        Path.Combine(Path.GetDirectoryName(path) ?? ".", Path.GetFileNameWithoutExtension(path) + " (dry)" + Path.GetExtension(path));

    // A name's in use if the recording, its dry copy or its cue sheet is there already.
    private static bool Taken(string path) =>
        File.Exists(path) || File.Exists(DryPath(path)) || File.Exists(Path.ChangeExtension(path, ".cue"));

    private static string Replace(string text, string token, string value) =>
        text.Replace(token, value, StringComparison.OrdinalIgnoreCase);

    // Takes out characters a file name can't have and tidies the spaces they leave.
    private static string Clean(string name)
    {
        var cleaned = new string(name.Select(c => Invalid.Contains(c) || char.IsControl(c) ? ' ' : c).ToArray());
        while (cleaned.Contains("  ", StringComparison.Ordinal))
        {
            cleaned = cleaned.Replace("  ", " ", StringComparison.Ordinal);
        }

        cleaned = cleaned.Trim(' ', '.', '-', '_');
        if (cleaned.Length > 150)
        {
            cleaned = cleaned[..150].TrimEnd();
        }

        return cleaned.Length == 0 ? "Recording" : cleaned;
    }
}
