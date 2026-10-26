using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using Microsoft.Win32;
using Lucent.Audio;
using Lucent.Library;
using Lucent.Settings;
using Lucent.Video;

namespace Lucent.Editor;

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
    /// <summary>The editors that are open, the first opened first.</summary>
    public static IReadOnlyList<EditorWindow> OpenEditors => Editors;

    public static bool IsEditorPlaying => Editors.Any(editor => editor.IsPlaying);

    internal static void NotifyEditorPlayingChanged() => EditorPlayingChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>Stops every editor that's playing; the player's Stop stops them too.</summary>
    public static void StopEditors()
    {
        foreach (var editor in Editors.ToList())
        {
            editor.StopPlaying();
        }
    }

    internal static void NotifyFilesReplaced(IReadOnlyList<string> paths)
    {
        if (paths.Count > 0)
        {
            FilesReplaced?.Invoke(null, new TagsSavedEventArgs(paths));
        }
    }

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
            MessageBox.Show(owner, $"\"{Path.GetFileName(path)}\" can't be found. It may have been moved or deleted.",
                "Video and audio editor", MessageBoxButton.OK, MessageBoxImage.Information);
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
    /// The title bar's "Open the video and audio editor": brings an open editor to the front, or opens an empty one docked to
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
        var window = new EditorWindow(path, player) { Owner = owner };
        Editors.Add(window);
        Dock(window);
        window.Closing += (_, e) =>
        {
            // This runs after the editor's own check. Refused while idle means the user chose to keep
            // their unsaved edits, so Lucent shouldn't quit after all.
            if (e.Cancel && !window.IsBusy)
            {
                _closeWhenIdle = null;
            }
        };
        window.Closed += (_, _) =>
        {
            Editors.Remove(window);
            NotifyEditorPlayingChanged();
            QuitIfIdle();
        };
        window.Show();
    }

    /// <summary>
    /// Puts a new editor where the last one was left, from the player's corner, so it's docked in the same place
    /// wherever the player is now; the first time (or if that's off the screen), in a free spot docked to the
    /// player's group. Either way it snaps to and moves with the player like the other windows.
    /// </summary>
    private static void Dock(EditorWindow window)
    {
        if (Application.Current.MainWindow is not MainWindow main)
        {
            return;
        }

        KeepInFrontOfPlayer(main);
        var settings = EditorSettings.Load();
        if (Editors.Count == 1 && main.WindowState == WindowState.Normal
            && settings is { WindowOffsetX: { } x, WindowOffsetY: { } y, WindowWidth: { } width, WindowHeight: { } height }
            && width >= window.MinWidth && height >= window.MinHeight
            && IsOnScreen(new Rect(main.Left + x, main.Top + y, width, height)))
        {
            main.AddToSnapping(window);
            window.WindowStartupLocation = WindowStartupLocation.Manual;
            window.Left = main.Left + x;
            window.Top = main.Top + y;
            window.Width = width;
            window.Height = height;
            return;
        }

        if (Editors.Count == 1)
        {
            main.DockNewWindow(window);
        }
        else
        {
            // A second editor, for a song opened while the first has unsaved edits: over the player, as before.
            main.AddToSnapping(window);
        }
    }

    private static bool _keptInFront;

    /// <summary>
    /// Brings the open editors back in front of the player's other windows (the visuals, queue, library and the
    /// rest) whenever the player comes to the front, such as when it's clicked or dragged. Where there's no free
    /// room beside the player's windows (a small or zoomed-in screen), the editor sits over them instead of docked,
    /// and without this the visuals and the rest drew over it as the player was moved under it.
    /// </summary>
    private static void KeepInFrontOfPlayer(Window main)
    {
        if (_keptInFront)
        {
            return;
        }

        _keptInFront = true;
        main.Activated += (_, _) =>
        {
            foreach (var editor in Editors.Where(editor => editor.IsVisible && editor.WindowState != WindowState.Minimized))
            {
                var handle = new WindowInteropHelper(editor).Handle;
                if (handle != IntPtr.Zero)
                {
                    SetWindowPos(handle, HWND_TOP, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);
                }
            }
        };
    }

    private static readonly IntPtr HWND_TOP = IntPtr.Zero;
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int width, int height, uint flags);

    /// <summary>True when enough of the window's title bar is on a screen to grab it.</summary>
    private static bool IsOnScreen(Rect bounds)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = Rect.Intersect(screen, new Rect(bounds.Left, bounds.Top, bounds.Width, 40));
        return !titleBar.IsEmpty && titleBar.Width >= 100 && titleBar.Height >= 20;
    }

    /// <summary>Asks for a song and opens it in the editor.</summary>
    public static void ChooseFileToEdit(Window owner, AudioPlayer player)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Choose a sound or video to edit",
            Filter = $"Music and video files|{Patterns()};{string.Join(";", Mp4Cutter.Extensions.Select(ext => "*" + ext))}|All files|*.*",
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
            MessageBox.Show(owner, paths.Count == 0 ? "Choose one or more sounds to convert first." : "Those sounds can't be found. They may have been moved or deleted.",
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
            Title = "Choose sounds to convert",
            Multiselect = true,
            Filter = $"Music files|{Patterns()}|All files|*.*",
        };
        if (dialog.ShowDialog(owner) == true)
        {
            Convert(owner, settings, dialog.FileNames);
        }
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
        var window = new RecordWindow(player, settings) { Owner = owner };
        _recorder = window;
        (Application.Current.MainWindow as MainWindow)?.AddToSnapping(window);
        window.Closed += (_, _) =>
        {
            _recorder = null;
            QuitIfIdle();
        };
        window.Show();
    }

    /// <summary>
    /// The menu under the title bar's Edit button: record, edit or convert the song that's playing, or choose files.
    /// </summary>
    public static void ShowMenu(FrameworkElement target, Window owner, AudioPlayer player, AppSettings settings)
    {
        var currentPath = player.CurrentPath;
        var playing = currentPath is not null && File.Exists(currentPath);
        var name = playing ? $" ({Path.GetFileNameWithoutExtension(currentPath)})" : string.Empty;
        var menu = new ContextMenu { PlacementTarget = target, Placement = PlacementMode.Bottom };
        menu.Items.Add(Item("Record from an audio interface…", true, () => OpenRecorder(owner, player, settings)));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Open the video and audio editor", true, () => ShowEditor(owner, player)));
        menu.Items.Add(Item($"Edit what's playing{name}", playing, () => OpenEditor(owner, player, currentPath!)));
        menu.Items.Add(Item("Convert the sound that's playing…", playing, () => Convert(owner, settings, new[] { currentPath! })));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Choose a sound or video to edit…", true, () => ChooseFileToEdit(owner, player)));
        menu.Items.Add(Item("Choose sounds to convert…", true, () => ChooseFilesToConvert(owner, settings)));
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
        a.Length > 0 && b.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    // ---- Closing Lucent ----

    private static void Watch(Window owner)
    {
        if (WatchedOwners.Add(owner))
        {
            owner.Closing += OnOwnerClosing;
        }
    }

    /// <summary>
    /// Owned windows close with the main window, so ask about unsaved edits first, and let running work
    /// stop cleanly (removing its half-written temporary files) before Lucent actually quits.
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
        var recording = _recorder is { IsBusy: true };
        if (busyEditors.Count == 0 && busyConverters.Count == 0 && !recording)
        {
            return;
        }

        e.Cancel = true;
        if (_closeWhenIdle is not null)
        {
            // Already winding down after the user said yes.
            return;
        }

        var what = recording ? "still recording" : busyConverters.Count > 0 ? "still converting" : "still working in the editor";
        var answer = MessageBox.Show(owner, $"Lucent is {what}. Stop and close Lucent?", "Lucent",
            MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
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
            owner.Dispatcher.BeginInvoke(owner.Close);
        }
    }
}
