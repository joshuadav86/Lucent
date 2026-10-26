using System.Windows;

namespace Lucent.Windows;

/// <summary>
/// Files and folders dragged onto a Lucent window from Explorer. The windows take them on the way in
/// (the Preview drag events), so a drop lands whatever it's over: a text box or a list would
/// otherwise turn files away before the window saw them.
/// </summary>
public static class DroppedMusic
{
    /// <summary>Shows the copy cursor while files are over the window.</summary>
    public static void Accept(DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    /// <summary>The dropped files' paths, marking the drop handled; empty if it wasn't files.</summary>
    public static IReadOnlyList<string> Paths(DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } paths)
        {
            return Array.Empty<string>();
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
        return paths;
    }
}
