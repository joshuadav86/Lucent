// STUB: owned by editor worker
using Avalonia.Controls;
using Lucent.Mac.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac.Editor;

public static class AudioTools
{
    public static event EventHandler? MainPlayerChanged;

    public static event EventHandler<TagsSavedEventArgs>? FilesReplaced;

    public static void OpenEditor(Window owner, AudioPlayer player, string path) => MainPlayerChanged?.GetType();

    public static void Convert(Window owner, AppSettings settings, IReadOnlyList<string> paths) => FilesReplaced?.GetType();

    public static void ChooseFilesToConvert(Window owner, AppSettings settings)
    {
    }

    public static void ShowMenu(Control target, Window owner, AudioPlayer player, AppSettings settings)
    {
    }
}
