using System.IO;
using Lucent.Settings;

namespace Lucent.Visualizer;

/// <summary>
/// The visuals' right-click Advanced menu: finding a visual's file, making it editable, and saying which one to
/// recompile. Shared by Windows and Mac, which only differ in how they show a file and open an editor.
/// </summary>
/// <remarks>
/// The visuals Lucent ships live beside the app, which an update replaces (and on a Mac sits inside the signed
/// app), so a built-in visual is never edited where it is. "Edit shader" copies it to the user's Presets folder
/// first. A file there with the same name replaces the built-in one when the visuals load, which is how the
/// user's own visuals already work, so the edited copy is the one that recompiles and the built-in comes back
/// when the copy is deleted.
/// </remarks>
public static class ShaderEditing
{
    /// <summary>The built-in visuals' folder, beside the app.</summary>
    public static string BuiltInFolder => Path.Combine(AppContext.BaseDirectory, "Presets");

    /// <summary>The user's own visuals, which load over the built-in ones and survive updates.</summary>
    public static string UserFolder => Path.Combine(AppDataFolder.Path, "Presets");

    /// <summary>
    /// Whether the visual can be opened in a text editor and recompiled: Lucent's shaders and MilkDrop presets
    /// (both text). AVS presets are binary files made in Winamp's editor, so they can only be shown in their folder.
    /// </summary>
    public static bool CanEdit(PresetSource source) => !source.IsAvs;

    /// <summary>
    /// The file the visual draws from: the user's edited copy once there is one, otherwise the file it loaded
    /// from, or null when that file isn't on disk (a Windows build run without its Presets folder).
    /// </summary>
    public static string? FileOf(PresetSource source, string? builtInFolder = null, string? userFolder = null)
    {
        var copy = CopyPath(source, builtInFolder ?? BuiltInFolder, userFolder ?? UserFolder);
        if (File.Exists(copy))
        {
            return copy;
        }

        return File.Exists(source.FilePath) ? source.FilePath : null;
    }

    /// <summary>
    /// The file to open in an editor: the user's own visual as it is, or a copy of a built-in one in the user's
    /// Presets folder (made the first time, from the code the visual loaded with).
    /// </summary>
    public static string EditableFile(PresetSource source, string? builtInFolder = null, string? userFolder = null)
    {
        var copy = CopyPath(source, builtInFolder ?? BuiltInFolder, userFolder ?? UserFolder);
        if (File.Exists(copy))
        {
            return copy;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
        File.WriteAllText(copy, source.Code);
        return copy;
    }

    /// <summary>
    /// The visual the Advanced menu works on: the one showing, unless the last one recompiled failed to compile
    /// (the visuals then moved on to another), so the broken one stays a click away to fix and try again.
    /// </summary>
    public static string? Target(string? showing, string? lastRecompiled, Func<string, bool> hasFailed) =>
        lastRecompiled is not null && hasFailed(lastRecompiled) ? lastRecompiled : showing;

    /// <summary>
    /// The compiler's error when a problem the visuals reported ("A visual didn't load. Name: error") is about
    /// <paramref name="name"/>, cut to its first line and a length a toast can hold; otherwise null.
    /// </summary>
    public static string? CompileError(string problem, string? name)
    {
        if (name is null)
        {
            return null;
        }

        var marker = $"{name}: ";
        // The name starts the problem or its second sentence, so "Diver" doesn't match "Ring Diver: ...".
        var start = problem.StartsWith(marker, StringComparison.Ordinal) ? 0 : problem.IndexOf(". " + marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        if (start > 0)
        {
            start += 2;
        }

        var error = problem[(start + marker.Length)..].Trim();
        var end = error.IndexOfAny(new[] { '\r', '\n' });
        if (end > 0)
        {
            error = error[..end].TrimEnd();
        }

        const int MaxLength = 220;
        return error.Length > MaxLength ? error[..(MaxLength - 1)].TrimEnd() + "…" : error;
    }

    // Where the user's copy of a visual goes: the same place inside their Presets folder as it has inside the
    // app's (so a MilkDrop preset stays in MilkDrop\), or the file itself when it's already the user's.
    private static string CopyPath(PresetSource source, string builtInFolder, string userFolder)
    {
        if (source.IsOwn)
        {
            return source.FilePath;
        }

        var relative = Path.GetRelativePath(builtInFolder, source.FilePath);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            relative = Path.GetFileName(source.FilePath);
        }

        return Path.Combine(userFolder, relative);
    }
}
