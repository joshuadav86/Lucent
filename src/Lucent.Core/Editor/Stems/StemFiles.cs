using WinampInspiredVisualizer.Export;

namespace WinampInspiredVisualizer.Editor.Stems;

/// <summary>Saves stems as files: "Song - Vocals.flac" and so on, in a "Song stems" folder beside the song.</summary>
public static class StemFiles
{
    /// <summary>
    /// A new folder for the stems of <paramref name="sourcePath"/>: "Song stems", or "Song stems 2" and up if
    /// that's taken, so earlier stems are never written over.
    /// </summary>
    public static string NewFolderFor(string sourcePath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var folder = Path.Combine(parent, $"{name} stems");
        for (var n = 2; Directory.Exists(folder) || File.Exists(folder); n++)
        {
            folder = Path.Combine(parent, $"{name} stems {n}");
        }

        return folder;
    }

    /// <summary>
    /// Writes every stem into <paramref name="folder"/>, with the song's tags and the stem's name added to its
    /// title. Returns the files written, in the editor's order (vocals, drums, bass, other instruments).
    /// </summary>
    public static IReadOnlyList<string> Save(StemSet stems, string sourcePath, string folder, ExportOptions options,
        Action<double>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var sources = StemNames.All.Where(source => stems[source] is not null).ToList();
        var written = new List<string>();
        for (var i = 0; i < sources.Count; i++)
        {
            var source = sources[i];
            var stem = stems[source]!;
            var target = Path.Combine(folder, $"{name} - {StemNames.Title(source)}{options.Extension}");
            var done = i;
            ExportPipeline.Write(new AudioBufferSampleProvider(stem), stem.Frames, target, sourcePath, audioChanged: true, options,
                replaceExisting: true, fraction => progress?.Invoke((done + fraction) / sources.Count), cancellation);
            NameInTitle(target, StemNames.Title(source));
            written.Add(target);
        }

        return written;
    }

    /// <summary>Adds " (Vocals)" and so on to the copied title, so the stems can be told apart in a library.</summary>
    private static void NameInTitle(string path, string stem)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : $"{file.Tag.Title} ({stem})";
            file.Tag.Title = title;
            file.Save();
        }
        catch (Exception)
        {
            // A file TagLib can't tag still has its name to go by.
        }
    }
}
