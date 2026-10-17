using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// The ways into the audio editor and the converter, kept here so the main window only needs a few
/// one-line handlers. Also makes sure closing Lucid doesn't silently lose edits or half-written files.
/// </summary>
public static class AudioTools
{
    private static readonly List<EditorWindow> Editors = new();
    private static readonly List<ExportWindow> Converters = new();
    private static readonly HashSet<Window> WatchedOwners = new();

    /// <summary>Set when the user asked to quit while something was still working: quit once it has stopped.</summary>
    private static Window? _closeWhenIdle;

    /// <summary>
    /// Raised after the editor pauses the main player, or the player reopens a song that was saved over,
    /// so the main window can refresh its play button.
    /// </summary>
    public static event EventHandler? MainPlayerChanged;

    /// <summary>
    /// Raised after songs have been saved over (by the editor's Save, or the converter replacing files), so the
    /// queue, library and now playing display can show their new length and info.
    /// </summary>
    public static event EventHandler<TagsSavedEventArgs>? FilesReplaced;

    internal static void NotifyMainPlayerChanged() => MainPlayerChanged?.Invoke(null, EventArgs.Empty);

    internal static void NotifyFilesReplaced(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            FilesReplaced?.Invoke(null, new TagsSavedEventArgs(paths));
        }
    }

    /// <summary>
    /// Opens a song in the editor, or brings its editor to the front if it's already open.
    /// <paramref name="player"/> is the main player: the editor pauses it while it plays, and has it let go of
    /// the song for a moment when saving over it.
    /// </summary>
    public static void OpenEditor(Window owner, AudioPlayer player, string path)
    {
        if (!File.Exists(path))
        {
            MessageBox.Show(owner, $"\"{Path.GetFileName(path)}\" can't be found. It may have been moved or deleted.",
                "Audio editor", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var existing = Editors.FirstOrDefault(editor => SamePath(editor.FilePath, path));
        if (existing is not null)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        Watch(owner);
        var window = new EditorWindow(path, player) { Owner = owner };
        Editors.Add(window);
        (Application.Current.MainWindow as MainWindow)?.AddToSnapping(window);
        window.Closing += (_, e) =>
        {
            // This runs after the editor's own check. Refused while idle means the user chose to keep
            // their unsaved edits, so Lucid shouldn't quit after all.
            if (e.Cancel && !window.IsBusy)
            {
                _closeWhenIdle = null;
            }
        };
        window.Closed += (_, _) =>
        {
            Editors.Remove(window);
            QuitIfIdle();
        };
        window.Show();
    }

    /// <summary>Asks for a song and opens it in the editor.</summary>
    public static void ChooseFileToEdit(Window owner, AudioPlayer player)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a song to edit",
            Filter = $"Music files|{Patterns()}|All files|*.*",
        };
        if (dialog.ShowDialog(owner) == true)
        {
            OpenEditor(owner, player, dialog.FileName);
        }
    }

    /// <summary>
    /// Opens the converter for some songs. It stays open while it works, so the music can keep playing.
    /// Missing files are left out; the format last used comes from <paramref name="settings"/> and is saved back to it.
    /// </summary>
    public static void Convert(Window owner, AppSettings settings, IReadOnlyList<string> paths)
    {
        var files = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (files.Count == 0)
        {
            MessageBox.Show(owner, paths.Count == 0 ? "Choose one or more songs to convert first." : "Those songs can't be found. They may have been moved or deleted.",
                "Convert", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Watch(owner);
        var window = ExportWindow.ShowConverter(owner, files, settings);
        Converters.Add(window);
        window.Closed += (_, _) =>
        {
            Converters.Remove(window);
            QuitIfIdle();
        };
    }

    /// <summary>Asks for songs and opens the converter for them.</summary>
    public static void ChooseFilesToConvert(Window owner, AppSettings settings)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose songs to convert",
            Multiselect = true,
            Filter = $"Music files|{Patterns()}|All files|*.*",
        };
        if (dialog.ShowDialog(owner) == true)
        {
            Convert(owner, settings, dialog.FileNames);
        }
    }

    /// <summary>
    /// The menu under the title bar's Edit button: edit or convert the song that's playing, or choose files.
    /// </summary>
    public static void ShowMenu(FrameworkElement target, Window owner, AudioPlayer player, AppSettings settings)
    {
        var currentPath = player.CurrentPath;
        var playing = currentPath is not null && File.Exists(currentPath);
        var name = playing ? $" ({Path.GetFileNameWithoutExtension(currentPath)})" : string.Empty;
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        menu.Items.Add(Item($"Edit the song that's playing{name}", playing, () => OpenEditor(owner, player, currentPath!)));
        menu.Items.Add(Item("Convert the song that's playing…", playing, () => Convert(owner, settings, new[] { currentPath! })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Choose a song to edit…", true, () => ChooseFileToEdit(owner, player)));
        menu.Items.Add(Item("Choose songs to convert…", true, () => ChooseFilesToConvert(owner, settings)));
        menu.Closed += (_, _) =>
        {
            // The button is lit while its menu is open, like the other title-bar buttons.
            if (target is ToggleButton toggle)
            {
                toggle.IsChecked = false;
            }
        };
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    private static string Patterns() =>
        string.Join(";", AudioReaderFactory.SupportedExtensions.Select(ext => "*" + ext));

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---- Closing Lucid ----

    private static void Watch(Window owner)
    {
        if (WatchedOwners.Add(owner))
        {
            owner.Closing += OnOwnerClosing;
        }
    }

    /// <summary>
    /// Owned windows close with the main window, so ask about unsaved edits first, and let running work
    /// stop cleanly (removing its half-written temporary files) before Lucid actually quits.
    /// </summary>
    private static void OnOwnerClosing(object? sender, CancelEventArgs e)
    {
        if (sender is not Window owner)
        {
            return;
        }

        foreach (var editor in Editors.Where(editor => !editor.IsBusy).ToList())
        {
            if (!editor.ConfirmClose())
            {
                e.Cancel = true;
                _closeWhenIdle = null;
                return;
            }
        }

        var busyEditors = Editors.Where(editor => editor.IsBusy).ToList();
        var busyConverters = Converters.Where(converter => converter.IsRunning).ToList();
        if (busyEditors.Count == 0 && busyConverters.Count == 0)
        {
            return;
        }

        e.Cancel = true;
        if (_closeWhenIdle is not null)
        {
            // Already winding down after the user said yes.
            return;
        }

        var what = busyConverters.Count > 0 ? "still converting" : "still working in the audio editor";
        var answer = MessageBox.Show(owner, $"Lucid is {what}. Stop and close Lucid?", "Lucid",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return;
        }

        // Each window cancels its work and closes once it has cleaned up; the last one closes Lucid.
        _closeWhenIdle = owner;
        foreach (var window in busyEditors.Cast<Window>().Concat(busyConverters))
        {
            window.Close();
        }
    }

    private static void QuitIfIdle()
    {
        if (_closeWhenIdle is { } owner
            && !Editors.Any(editor => editor.IsBusy)
            && !Converters.Any(converter => converter.IsRunning))
        {
            _closeWhenIdle = null;
            // Let the closing window finish first.
            owner.Dispatcher.BeginInvoke(owner.Close);
        }
    }
}
