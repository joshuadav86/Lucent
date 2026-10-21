using System.Collections.Concurrent;
using System.IO;

namespace WinampInspiredVisualizer.Recording;

/// <summary>
/// The files the Record window is writing right now. The library leaves them out until they're finished:
/// their header is rewritten every couple of seconds, which would otherwise have it rescan the whole
/// library all through a set, and list a file whose length keeps changing.
/// </summary>
public static class RecordingsInProgress
{
    private static readonly ConcurrentDictionary<string, byte> Paths = new(StringComparer.OrdinalIgnoreCase);

    public static bool Contains(string path) => !Paths.IsEmpty && Paths.ContainsKey(Normalize(path));

    internal static void Add(string path) => Paths[Normalize(path)] = 0;

    internal static void Remove(string path) => Paths.TryRemove(Normalize(path), out _);

    private static string Normalize(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return path;
        }
    }
}
