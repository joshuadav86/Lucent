using System.Diagnostics;
using System.IO;
using Lucent.Video;

namespace Lucent.Playlist;

/// <summary>How far reading dropped (or picked) files and folders has got.</summary>
/// <param name="Found">Music files found so far.</param>
/// <param name="Read">Of those, how many have had their tags read.</param>
/// <param name="Reading">False while folders are still being searched, true once every file is known and tags are being read.</param>
public readonly record struct MusicLoadProgress(int Found, int Read, bool Reading)
{
    /// <summary>The share of the tags read, 0 to 1, or 0 while still searching.</summary>
    public double Fraction => Reading && Found > 0 ? (double)Read / Found : 0;

    /// <summary>What a note showing this progress says.</summary>
    public string Text => Reading
        ? $"Adding songs… {Read:N0} of {Found:N0}"
        : Found == 0 ? "Looking for music…" : $"Looking for music… {Found:N0} found";
}

/// <summary>
/// Turns files and folders (dropped, picked or opened) into tracks: folders are searched all the way down, each in
/// name order, and the tags are read. Large folders can take a while, so it says how far it has got as it goes.
/// </summary>
public static class MusicFiles
{
    /// <summary>The longest wait between two progress reports, so a note showing them stays lively without flooding the UI.</summary>
    private static readonly TimeSpan ReportEvery = TimeSpan.FromMilliseconds(100);

    /// <summary>Whether any of <paramref name="paths"/> is a folder, which can hold many songs and so takes a while.</summary>
    public static bool AnyFolder(IEnumerable<string> paths) => paths.Any(Directory.Exists);

    /// <summary>The playable files in <paramref name="paths"/>, with every folder searched (sub-folders too), in name order.</summary>
    public static List<string> Find(IEnumerable<string> paths, Action<int>? found = null)
    {
        var files = new List<string>();
        var clock = Stopwatch.StartNew();
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                var inFolder = new List<string>();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    {
                        if (!VideoFiles.IsPlayable(file))
                        {
                            continue;
                        }

                        inFolder.Add(file);
                        if (found is not null && clock.Elapsed >= ReportEvery)
                        {
                            found(files.Count + inFolder.Count);
                            clock.Restart();
                        }
                    }
                }
                catch (Exception)
                {
                    // A folder that can't be read (permissions, an unplugged drive) is skipped.
                    continue;
                }

                inFolder.Sort(StringComparer.OrdinalIgnoreCase);
                files.AddRange(inFolder);
            }
            else if (File.Exists(path) && VideoFiles.IsPlayable(path))
            {
                files.Add(path);
            }
        }

        return files;
    }

    /// <summary>
    /// The tracks in <paramref name="paths"/>, tags read. <paramref name="report"/> hears how far it has got, at most
    /// every tenth of a second, on whatever thread this runs on.
    /// </summary>
    public static List<Track> Load(IEnumerable<string> paths, Action<MusicLoadProgress>? report = null)
    {
        var files = Find(paths, report is null ? null : count => report(new MusicLoadProgress(count, 0, false)));
        var tracks = new List<Track>(files.Count);
        var clock = Stopwatch.StartNew();
        report?.Invoke(new MusicLoadProgress(files.Count, 0, true));
        foreach (var file in files)
        {
            tracks.Add(Track.FromFile(file));
            if (report is not null && clock.Elapsed >= ReportEvery)
            {
                report(new MusicLoadProgress(files.Count, tracks.Count, true));
                clock.Restart();
            }
        }

        return tracks;
    }

    /// <summary>The same path written the same way, so a folder dropped twice is known as the same folder.</summary>
    public static string Key(string path)
    {
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        }
        catch (Exception)
        {
            return path;
        }
    }
}
