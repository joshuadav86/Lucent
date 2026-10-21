using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Video;

namespace Lucent.Mac.Editor;

/// <summary>
/// The ways into the audio editor and the converter, kept here so the main window only needs a few
/// one-line handlers. Also makes sure closing Lucent doesn't silently lose edits or half-written files.
/// </summary>
public static class AudioTools
{
    private static readonly List<EditorWindow> Editors = new();
    private static readonly List<ExportWindow> Converters = new();
    private static readonly HashSet<Window> WatchedOwners = new();
    private static RecordWindow? _recorder;

    /// <summary>Set when the user asked to quit while something was still working: quit once it has stopped.</summary>
    private static Window? _closeWhenIdle;

    /// <summary>An owner whose closing has been checked (edits confirmed, nothing running), so it may close now.</summary>
    private static Window? _closeApproved;

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

    /// <summary>
    /// Raised when an editor starts or stops playing, so the equalizer can say it isn't shaping that sound: the editor
    /// plays through its own output, and its spectrum only reacts because it's listening to every app.
    /// </summary>
    public static event EventHandler? EditorPlayingChanged;

    /// <summary>True while an audio editor is playing.</summary>
    public static bool IsEditorPlaying => Editors.Any(editor => editor.IsPlaying);

    internal static void NotifyEditorPlayingChanged() => EditorPlayingChanged?.Invoke(null, EventArgs.Empty);

    internal static void NotifyFilesReplaced(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            FilesReplaced?.Invoke(null, new TagsSavedEventArgs(paths));
        }
    }

    /// <summary>The player window, for the editor's Batch button and window snapping.</summary>
    internal static MainWindow? MainWindow =>
        (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;

    /// <summary>
    /// Opens a song in the editor, or brings its editor to the front if it's already open. The editor that's open
    /// takes the song in place of its own when that loses nothing (it's empty, or has no unsaved edits), so one
    /// editor can be left docked to the player; otherwise the song opens in another editor.
    /// <paramref name="player"/> is the main player: the editor pauses it while it plays, and has it let go of
    /// the song for a moment when saving over it.
    /// </summary>
    public static async void OpenEditor(Window owner, AudioPlayer player, string path)
    {
        if (!File.Exists(path))
        {
            _ = MessageWindow.ShowAsync(owner, $"\"{Path.GetFileName(path)}\" can't be found. It may have been moved or deleted.",
                "Audio editor");
            return;
        }

        var existing = Editors.FirstOrDefault(editor => !editor.IsEmpty && SamePath(editor.FilePath, path));
        if (existing is not null)
        {
            BringForward(existing);
            return;
        }

        if ((Editors.FirstOrDefault(editor => editor.IsEmpty && editor.CanOpenAnother) ?? Editors.FirstOrDefault(editor => editor.CanOpenAnother)) is { } reusable)
        {
            BringForward(reusable);
            await reusable.OpenAsync(path);
            return;
        }

        NewEditor(owner, player, path);
    }

    /// <summary>
    /// The Edit menu's "Open the audio editor": brings an open editor to the front, or opens an empty one docked to
    /// the player, to drop sounds into.
    /// </summary>
    public static void ShowEditor(Window owner, AudioPlayer player)
    {
        if (Editors.LastOrDefault() is { } open)
        {
            BringForward(open);
            return;
        }

        NewEditor(owner, player, null);
    }

    /// <summary>Opens the empty editor again with the player if it was open when Lucent last quit.</summary>
    public static void ReopenEditor(Window owner, AudioPlayer player)
    {
        if (Editors.Count == 0 && EditorSettings.Load().OpenAtStart)
        {
            NewEditor(owner, player, null);
        }
    }

    private static void BringForward(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
    }

    private static void NewEditor(Window owner, AudioPlayer player, string? path)
    {
        Watch(owner);
        var window = new EditorWindow(path ?? string.Empty, player);
        Editors.Add(window);
        Dock(window);
        window.Closed += (_, _) =>
        {
            Editors.Remove(window);
            NotifyEditorPlayingChanged();
            QuitIfIdle();
        };
        window.Show(owner);
    }

    /// <summary>
    /// Puts a new editor where the last one was left, from the player's corner, so it's docked in the same place
    /// wherever the player is now; the first time (or if that's off the screen), in a free spot docked to the
    /// player's group. Either way it snaps to and moves with the player like the other windows.
    /// </summary>
    private static void Dock(EditorWindow window)
    {
        if (MainWindow is not { } main)
        {
            return;
        }

        var settings = EditorSettings.Load();
        if (Editors.Count == 1 && main.WindowState == WindowState.Normal
            && settings is { WindowOffsetX: { } x, WindowOffsetY: { } y, WindowWidth: { } width, WindowHeight: { } height }
            && width >= window.MinWidth && height >= window.MinHeight
            && IsOnScreen(main, new Rect(main.Left() + x, main.Top() + y, width, height)))
        {
            main.AddToSnapping(window);
            Placement.Place(window, main.Left() + x, main.Top() + y, width, height);
            return;
        }

        if (Editors.Count == 1)
        {
            main.DockNewWindow(window);
        }
        else
        {
            // A second editor, for a song opened while the first has unsaved edits: where macOS puts it, as before.
            main.AddToSnapping(window);
        }
    }

    /// <summary>True when enough of the window's title bar is on a screen to grab it.</summary>
    private static bool IsOnScreen(Window main, Rect bounds)
    {
        var titleBar = Placement.VirtualScreen(main).Intersect(new Rect(bounds.X, bounds.Y, bounds.Width, 40));
        return titleBar.Width >= 100 && titleBar.Height >= 20;
    }

    /// <summary>
    /// Opens the Record window (there's one at a time, as there's one input to record from), or brings it to the front.
    /// <paramref name="player"/> is the main player, whose visuals follow the input while the window is open.
    /// </summary>
    public static void OpenRecorder(Window owner, AudioPlayer player, AppSettings settings)
    {
        if (_recorder is { } open)
        {
            if (open.WindowState == WindowState.Minimized)
            {
                open.WindowState = WindowState.Normal;
            }

            open.Activate();
            return;
        }

        Watch(owner);
        var window = new RecordWindow(player, settings);
        _recorder = window;
        MainWindow?.AddToSnapping(window);
        window.Closed += (_, _) =>
        {
            _recorder = null;
            QuitIfIdle();
        };
        window.Show(owner);
    }

    /// <summary>The user chose to keep an editor (and its unsaved edits) open, so Lucent shouldn't quit after all.</summary>
    internal static void CancelQuit() => _closeWhenIdle = null;

    /// <summary>Asks for a song and opens it in the editor.</summary>
    public static async void ChooseFileToEdit(Window owner, AudioPlayer player)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose a sound to edit",
            FileTypeFilter = FileTypes(videos: true),
        });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
        {
            OpenEditor(owner, player, path);
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
            _ = MessageWindow.ShowAsync(owner,
                paths.Count == 0 ? "Choose one or more sounds to convert first." : "Those sounds can't be found. They may have been moved or deleted.",
                "Convert");
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
    public static async void ChooseFilesToConvert(Window owner, AppSettings settings)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choose sounds to convert",
            AllowMultiple = true,
            FileTypeFilter = FileTypes(),
        });
        var paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
        {
            Convert(owner, settings, paths);
        }
    }

    /// <summary>
    /// The menu under the title bar's Edit button: record, edit or convert the song that's playing, or choose files.
    /// </summary>
    public static void ShowMenu(Control target, Window owner, AudioPlayer player, AppSettings settings)
    {
        var currentPath = player.CurrentPath;
        var playing = currentPath is not null && File.Exists(currentPath);
        var name = playing ? $" ({Path.GetFileNameWithoutExtension(currentPath)})" : string.Empty;
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedLeft };
        menu.Items.Add(Item("Record from an audio interface…", true, () => OpenRecorder(owner, player, settings)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open the audio editor", true, () => ShowEditor(owner, player)));
        menu.Items.Add(Item($"Edit the sound that's playing{name}", playing, () => OpenEditor(owner, player, currentPath!)));
        menu.Items.Add(Item("Convert the sound that's playing…", playing, () => Convert(owner, settings, new[] { currentPath! })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Choose a sound to edit…", true, () => ChooseFileToEdit(owner, player)));
        menu.Items.Add(Item("Choose sounds to convert…", true, () => ChooseFilesToConvert(owner, settings)));
        menu.Closed += (_, _) =>
        {
            // The button is lit while its menu is open, like the other title-bar buttons.
            if (target is ToggleButton toggle)
            {
                toggle.IsChecked = false;
            }
        };
        menu.Open(target);
    }

    private static MenuItem Item(string header, bool enabled, Action action)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        item.Click += (_, _) => action();
        return item;
    }

    // With `videos`, the ones the editor can cut too.
    private static List<FilePickerFileType> FileTypes(bool videos = false) => new()
    {
        new FilePickerFileType(videos ? "Music and video files" : "Music files")
        {
            Patterns = AudioReaderFactory.SupportedExtensions.Concat(videos ? Mp4Cutter.Extensions : Enumerable.Empty<string>())
                .Distinct(StringComparer.OrdinalIgnoreCase).Select(ext => "*" + ext).ToList(),
        },
        new FilePickerFileType("All files") { Patterns = new[] { "*" } },
    };

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && b.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---- Closing Lucent ----

    private static void Watch(Window owner)
    {
        if (WatchedOwners.Add(owner))
        {
            owner.Closing += OnOwnerClosing;
            owner.Closed += (_, _) => WatchedOwners.Remove(owner);
        }
    }

    /// <summary>
    /// Owned windows close with the main window, so ask about unsaved edits first, and let running work
    /// stop cleanly (removing its half-written temporary files) before Lucent actually quits.
    /// </summary>
    /// <remarks>
    /// Avalonia can't wait in a Closing handler the way WPF's MessageBox does, so the close is held back,
    /// the questions are asked, and the window is closed again once the answers allow it.
    /// </remarks>
    private static void OnOwnerClosing(object? sender, WindowClosingEventArgs e)
    {
        if (sender is not Window owner || e.Cancel)
        {
            return;
        }

        if (ReferenceEquals(_closeApproved, owner))
        {
            _closeApproved = null;
            return;
        }

        var unsaved = Editors.Where(editor => !editor.IsBusy && editor.NeedsCloseConfirmation).ToList();
        var busyEditors = Editors.Where(editor => editor.IsBusy).ToList();
        var busyConverters = Converters.Where(converter => converter.IsRunning).ToList();
        if (unsaved.Count == 0 && busyEditors.Count == 0 && busyConverters.Count == 0 && _recorder is not { IsBusy: true })
        {
            return;
        }

        e.Cancel = true;
        if (_closeWhenIdle is not null)
        {
            // Already winding down after the user said yes.
            return;
        }

        Dispatcher.UIThread.Post(async () => await AskBeforeClosingAsync(owner, unsaved));
    }

    private static async Task AskBeforeClosingAsync(Window owner, IReadOnlyList<EditorWindow> unsaved)
    {
        foreach (var editor in unsaved)
        {
            if (!await editor.ConfirmCloseAsync())
            {
                _closeWhenIdle = null;
                return;
            }
        }

        var busyEditors = Editors.Where(editor => editor.IsBusy).ToList();
        var busyConverters = Converters.Where(converter => converter.IsRunning).ToList();
        var recording = _recorder is { IsBusy: true };
        if (busyEditors.Count == 0 && busyConverters.Count == 0 && !recording)
        {
            _closeApproved = owner;
            owner.Close();
            return;
        }

        var what = recording ? "still recording" : busyConverters.Count > 0 ? "still converting" : "still working in the audio editor";
        var answer = await MessageWindow.ShowAsync(owner, $"Lucent is {what}. Stop and close Lucent?", "Lucent",
            MessageButtons.YesNo, MessageResult.No);
        if (answer != MessageResult.Yes)
        {
            return;
        }

        // Each window cancels its work and closes once it has cleaned up; the last one closes Lucent.
        _closeWhenIdle = owner;
        foreach (var window in busyEditors.Cast<Window>().Concat(busyConverters))
        {
            window.Close();
        }

        // The recording is finished and saved before the window closes.
        _recorder?.StopAndClose();
        QuitIfIdle();
    }

    private static void QuitIfIdle()
    {
        if (_closeWhenIdle is { } owner
            && !Editors.Any(editor => editor.IsBusy)
            && !Converters.Any(converter => converter.IsRunning)
            && _recorder is not { IsBusy: true })
        {
            _closeWhenIdle = null;
            // Let the closing window finish first.
            Dispatcher.UIThread.Post(owner.Close);
        }
    }
}
