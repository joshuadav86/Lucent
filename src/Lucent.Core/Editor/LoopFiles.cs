using System.IO;
using Lucent.Editor.Dsp;
using Lucent.Export;

namespace Lucent.Editor;

/// <summary>Saves loops from the loop maker as files of their own, named and tagged with their length, tempo and key.</summary>
public static class LoopFiles
{
    /// <summary>
    /// A new folder for loops from <paramref name="sourcePath"/>: "Song loops", or "Song loops 2" and up if that's
    /// taken, so earlier loops are never written over.
    /// </summary>
    public static string NewFolderFor(string sourcePath)
    {
        var parent = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var name = Path.GetFileNameWithoutExtension(sourcePath);
        var folder = Path.Combine(parent, $"{name} loops");
        for (var n = 2; Directory.Exists(folder) || File.Exists(folder); n++)
        {
            folder = Path.Combine(parent, $"{name} loops {n}");
        }

        return folder;
    }

    /// <summary>
    /// "Song - loop 1 - 4 bars, 124 BPM, Am": the loop's number among those saved together, its length, and the
    /// song's tempo and key, as music software lists loops.
    /// </summary>
    public static string Name(string sourcePath, int number, MusicalLength length, BeatGrid? grid, MusicalKey? key)
    {
        var parts = new List<string> { length.Text };
        if (grid is not null)
        {
            parts.Add(grid.TempoText);
        }

        if (key is { IsSure: true } sure)
        {
            parts.Add(sure.TagText);
        }

        var name = $"{Path.GetFileNameWithoutExtension(sourcePath)} - loop {number} - {string.Join(", ", parts)}";
        return string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '-' : c));
    }

    /// <summary>
    /// Writes each loop from <paramref name="audio"/> into <paramref name="folder"/>, with the song's tags, "(Loop n)"
    /// added to the title, and the tempo and key. Returns the files written, in the order given.
    /// </summary>
    public static IReadOnlyList<string> Save(AudioBuffer audio, IReadOnlyList<FrameRange> loops, string sourcePath, string folder,
        ExportOptions options, BeatGrid? grid, MusicalKey? key, Action<double>? progress, CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        var written = new List<string>();
        for (var i = 0; i < loops.Count; i++)
        {
            var range = loops[i];
            var length = grid?.Measure(range) ?? default;
            var name = grid is null ? $"{Path.GetFileNameWithoutExtension(sourcePath)} - loop {i + 1}" : Name(sourcePath, i + 1, length, grid, key);
            var target = Path.Combine(folder, name + options.Extension);
            var slice = audio.Slice(range.Start, range.Length);
            var done = i;
            ExportPipeline.Write(new AudioBufferSampleProvider(slice), slice.Frames, target, sourcePath, audioChanged: true, options,
                replaceExisting: true, fraction => progress?.Invoke((done + fraction) / loops.Count), cancellation);
            Tag(target, i + 1, grid, key);
            written.Add(target);
        }

        return written;
    }

    private static void Tag(string path, int number, BeatGrid? grid, MusicalKey? key)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            var title = string.IsNullOrWhiteSpace(file.Tag.Title) ? Path.GetFileNameWithoutExtension(path) : $"{file.Tag.Title} (Loop {number})";
            file.Tag.Title = title;
            if (grid is not null)
            {
                file.Tag.BeatsPerMinute = (uint)Math.Round(grid.Tempo);
            }

            if (key is { IsSure: true } sure)
            {
                file.Tag.InitialKey = sure.TagText;
            }

            file.Save();
        }
        catch (Exception)
        {
            // A file TagLib can't tag still has its name to go by.
        }
    }
}
