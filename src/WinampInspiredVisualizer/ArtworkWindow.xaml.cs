using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Streaming;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

/// <summary>
/// A separate window showing the playing track's cover as big as you like. It follows the music:
/// call <see cref="ShowTrack"/> whenever the track changes.
/// </summary>
public partial class ArtworkWindow : Window
{
    // Big enough to stay sharp on a 4K screen, small enough not to hold huge images in memory.
    private const int MaxDisplayPixels = 2048;

    private static ArtworkWindow? _instance;

    private readonly AudioPlayer _player;
    private readonly AppSettings _settings;
    private Track? _track;
    private RemoteTrack? _remote;
    private CoverImage? _cover;
    private CancellationTokenSource? _loading;

    private ArtworkWindow(AudioPlayer player, AppSettings settings)
    {
        InitializeComponent();
        _player = player;
        _settings = settings;

        Width = Math.Max(MinWidth, settings.ArtworkWindowWidth);
        Height = Math.Max(MinHeight, settings.ArtworkWindowHeight);
        if (settings.ArtworkWindowLeft is double left && settings.ArtworkWindowTop is double top && IsOnScreen(left, top, Width, Height))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = left;
            Top = top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        Topmost = settings.ArtworkWindowOnTop;
        OnTopToggle.IsChecked = Topmost;
    }

    /// <summary>Raised when the artwork window opens or closes, so a toggle button can follow it.</summary>
    public static event EventHandler? OpenChanged;

    public static bool IsOpen => _instance is not null;

    /// <summary>The open artwork window, if there is one.</summary>
    public static Window? Current => _instance;

    /// <summary>Opens the artwork window showing <paramref name="track"/>, or closes it if it's open. Returns whether it's now open.</summary>
    public static bool Toggle(Window owner, AudioPlayer player, AppSettings settings, Track? track)
    {
        if (_instance is not null)
        {
            _instance.Close();
            return false;
        }

        var window = new ArtworkWindow(player, settings) { Owner = owner };
        (owner as MainWindow)?.DockNewWindow(window);
        _instance = window;
        window.Show();
        window.Display(track, force: true);
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    /// <summary>Shows a newly playing track (or refreshed info for the same one). Does nothing while the window is closed.</summary>
    public static void ShowTrack(Track? track) => _instance?.Display(track, force: false);

    /// <summary>Shows the song another app is playing (see MainWindow.Streaming.cs), with the cover that app gave.</summary>
    public static void ShowRemote(RemoteTrack? track, byte[]? artwork) => _instance?.DisplayRemote(track, artwork);

    private void DisplayRemote(RemoteTrack? track, byte[]? artwork)
    {
        _loading?.Cancel();
        _track = null;
        _remote = track;
        TrackText.Text = track is null
            ? string.Empty
            : string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Title}  ·  {track.Artist}";
        Title = track is null ? "Artwork - Lucent" : $"{track.Title} - Artwork - Lucent";
        ShowCover(artwork is null ? null : CoverArt.Decode(artwork, MaxDisplayPixels));
    }

    private void Display(Track? track, bool force)
    {
        // A refreshed Track (after editing its info) is a new object, so this reloads it too.
        if (!force && _remote is null && ReferenceEquals(track, _track))
        {
            return;
        }

        _remote = null;
        _track = track;
        TrackText.Text = track is null
            ? string.Empty
            : string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Title}  ·  {track.Artist}";
        Title = track is null ? "Artwork - Lucent" : $"{track.Title} - Artwork - Lucent";
        LoadCover();
    }

    private async void LoadCover()
    {
        _loading?.Cancel();
        var loading = new CancellationTokenSource();
        _loading = loading;

        CoverImage? cover = null;
        if (_track is { } track)
        {
            try
            {
                cover = await CoverArt.LoadForTrackAsync(track.Path, MaxDisplayPixels, loading.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        if (!loading.IsCancellationRequested)
        {
            ShowCover(cover);
        }
    }

    private void ShowCover(CoverImage? cover)
    {
        var changed = cover is null || _cover is null || !cover.Data.AsSpan().SequenceEqual(_cover.Data);
        _cover = cover;

        if (cover is null)
        {
            ArtImage.Source = null;
            ArtImage.Visibility = Visibility.Collapsed;
            Placeholder.Visibility = Visibility.Visible;
            PlaceholderTitle.Text = _track is null && _remote is null ? "Nothing playing" : "No artwork";
            PlaceholderText.Text = _remote is not null ? "No cover came with this song, from the app or station playing it."
                : _track is null ? "Artwork appears here when music plays."
                : "Add a cover with Edit info (right-click here), or put a cover.jpg in the sound's folder.";
            return;
        }

        ArtImage.Source = cover.Image;
        ArtImage.ToolTip = $"{cover.SizeText} · {ImageFormats.Name(cover.MimeType)}"
            + (_remote is not null ? " · from the app or station playing it" : cover.FilePath is null ? " · stored in the music file" : $" · {Path.GetFileName(cover.FilePath)} in the sound's folder");
        ArtImage.Visibility = Visibility.Visible;
        Placeholder.Visibility = Visibility.Collapsed;
        if (changed)
        {
            ArtImage.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
        }
    }

    // ---- Menu and keys ----

    private void OnArtContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        CopyMenuItem.IsEnabled = _cover is not null;
        SaveMenuItem.IsEnabled = _cover is not null;
        EditMenuItem.IsEnabled = _track is not null;
        OnTopMenuItem.IsChecked = Topmost;
    }

    private void OnCopyImage(object sender, RoutedEventArgs e) => CopyImage();

    private void OnSaveImage(object sender, RoutedEventArgs e) => SaveImage();

    private void OnEditInfo(object sender, RoutedEventArgs e) => EditInfo();

    private void OnToggleOnTop(object sender, RoutedEventArgs e)
    {
        Topmost = !_settings.ArtworkWindowOnTop;
        _settings.ArtworkWindowOnTop = Topmost;
        OnTopToggle.IsChecked = Topmost;
        _settings.Save();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = Keyboard.Modifiers == ModifierKeys.Control;
        switch (e.Key)
        {
            // C opens and closes this window from the main window, so it closes it here too.
            case Key.Escape:
            case Key.C when Keyboard.Modifiers == ModifierKeys.None:
                Close();
                break;
            case Key.C when ctrl:
                CopyImage();
                break;
            case Key.S when ctrl:
                SaveImage();
                break;
            case Key.I when ctrl:
                EditInfo();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void CopyImage()
    {
        if (_cover is null)
        {
            return;
        }

        try
        {
            // Copy the full-size picture, not the one shrunk for the screen.
            Clipboard.SetImage(CoverArt.Decode(_cover.Data, 0)?.Image ?? _cover.Image);
        }
        catch (Exception ex)
        {
            ShowProblem("Couldn't copy the picture. Another program may be using the clipboard; try again in a moment.", ex);
        }
    }

    private void SaveImage()
    {
        if (_cover is null)
        {
            return;
        }

        var extension = ImageFormats.ExtensionFor(_cover.MimeType);
        var dialog = new SaveFileDialog
        {
            Title = "Save the artwork",
            FileName = SafeFileName(_track?.Title ?? _remote?.Title) + extension,
            Filter = $"{ImageFormats.Name(_cover.MimeType)} image|*{extension}|All files|*.*",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            File.WriteAllBytes(dialog.FileName, _cover.Data);
        }
        catch (Exception ex)
        {
            ShowProblem("Couldn't save the picture there. Try another folder.", ex);
        }
    }

    private void EditInfo()
    {
        if (_track is not null)
        {
            TrackInfoWindow.Open(Owner ?? this, _player, _settings, new[] { _track.Path });
        }
    }

    /// <summary>"Song name" made safe to use as a file name, or "cover".</summary>
    internal static string SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "cover";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "cover" : safe;
    }

    private void ShowProblem(string message, Exception ex) =>
        MessageBox.Show(this, $"{message}\n\nDetails: {ex.Message}", "Lucent", MessageBoxButton.OK, MessageBoxImage.Warning);

    // ---- Window ----

    private void OnArtMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }
        else if (e.ButtonState == MouseButtonState.Pressed && WindowState == WindowState.Normal)
        {
            DragMove();
        }
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.ArtworkWindowLeft = bounds.Left;
            _settings.ArtworkWindowTop = bounds.Top;
            _settings.ArtworkWindowWidth = bounds.Width;
            _settings.ArtworkWindowHeight = bounds.Height;
            _settings.Save();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _loading?.Cancel();
        if (_instance == this)
        {
            _instance = null;
        }

        OpenChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>True when enough of the window's title bar would be on a screen to grab it.</summary>
    private static bool IsOnScreen(double left, double top, double width, double height)
    {
        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var titleBar = new Rect(left, top, width, 36);
        var visible = Rect.Intersect(screen, titleBar);
        return !visible.IsEmpty && visible.Width >= 80 && visible.Height >= 20;
    }
}
