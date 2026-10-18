using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace Lucent.Mac.Windows;

/// <summary>
/// Files and folders dragged onto a Lucent window from Finder. Every window takes them, whatever part
/// of it they're dropped on: the handlers see drag events other controls already marked handled.
/// </summary>
public static class DroppedMusic
{
    /// <summary>Shows the copy cursor while files are over the window, and tells Finder they're wanted.</summary>
    public static void Accept(DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>
    /// The dropped files' paths, read while the drop is still happening (macOS only lends its drag
    /// data for the length of the drop), and marks the drop handled.
    /// </summary>
    public static IReadOnlyList<string> Paths(DragEventArgs e)
    {
        var paths = e.DataTransfer.TryGetFiles() is { } files
            ? files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList()
            : new List<string>();
        if (paths.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        return paths;
    }
}
