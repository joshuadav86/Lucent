using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Lucent.Mac.Audio;
using Lucent.Mac.Library;
using Lucent.Mac.Windows;
using Lucent.Library;
using Lucent.Radio;
using Lucent.Settings;
using Lucent.Streaming;
using Track = Lucent.Playlist.Track;

namespace Lucent.Mac;

/// <summary>
/// A separate window showing the playing track's cover as big as you like. It follows the music:
/// call <see cref="ShowTrack"/> whenever the track changes.
/// </summary>
public partial class ArtworkWindow : Window
{
    // Big enough to stay sharp on a 5K screen, small enough not to hold huge images in memory.
    private const int MaxDisplayPixels = 2048;

    // The height of the title bar, which has to stay reachable on a screen.
    private const double TitleBarHeight = 36;

    private static ArtworkWindow? _instance;

    private readonly AudioPlayer _player;
    private readonly AppSettings _settings;
    private Track? _track;
    private RemoteTrack? _remote;
    private CoverImage? _cover;
    private CancellationTokenSource? _loading;

    // Where the window was while it wasn't zoomed (maximized), to remember on closing (WPF's RestoreBounds).
    private PixelPoint _normalPosition;
    private Size _normalSize;

    /// <summary>Only for Avalonia's XAML loader and previewer; the app opens it with Toggle.</summary>
    public ArtworkWindow()
        : this(new AudioPlayer(), new AppSettings())
    {
    }

    private ArtworkWindow(AudioPlayer player, AppSettings settings)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _player = player;
        _settings = settings;
        LucentFullScreen.AddTo(ArtMenu, this);

        Width = Math.Max(MinWidth, settings.ArtworkWindowWidth);
        Height = Math.Max(MinHeight, settings.ArtworkWindowHeight);
        if (settings.ArtworkWindowLeft is double left && settings.ArtworkWindowTop is double top && IsOnScreen(left, top, Width))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)Math.Round(left), (int)Math.Round(top));
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _normalSize = new Size(Width, Height);
        Topmost = settings.ArtworkWindowOnTop;
        OnTopToggle.IsChecked = Topmost;

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PositionChanged += (_, _) => RememberNormalBounds();
        Resized += (_, _) => RememberNormalBounds();

        Opened += (_, _) => CenterOnVisiblePart();
        ArtArea.SizeChanged += (_, _) => CenterOnVisiblePart();
        PositionChanged += (_, _) => CenterOnVisiblePart();
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

        var window = new ArtworkWindow(player, settings);
        (owner as MainWindow)?.DockNewWindow(window);
        _instance = window;
        window.Show(owner);
        window.Display(track, force: true);
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    /// <summary>Shows a newly playing track (or refreshed info for the same one). Does nothing while the window is closed.</summary>
    public static void ShowTrack(Track? track) => _instance?.Display(track, force: false);

    /// <summary>
    /// For the screenshots: a window showing <paramref name="track"/> with its cover already loaded,
    /// without docking it or opening it for real.
    /// </summary>
    internal static ArtworkWindow CreatePreview(AudioPlayer player, AppSettings settings, Track? track)
    {
        var window = new ArtworkWindow(player, settings);
        window.ShowTrackText(track);
        window.ShowCover(track is null ? null : CoverArt.LoadForTrack(track.Path, MaxDisplayPixels), animate: false);
        return window;
    }

    /// <summary>For the screenshots: a window showing a song (or a station) with the cover given, as a station's or streaming app's song shows.</summary>
    internal static ArtworkWindow CreatePreview(AudioPlayer player, AppSettings settings, RemoteTrack track, byte[] cover)
    {
        var window = new ArtworkWindow(player, settings);
        window.DisplayRemote(track, null);
        window.ShowCover(CoverArt.Decode(cover, MaxDisplayPixels), animate: false);
        return window;
    }

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
        ShowTrackText(track);
        LoadCover();
    }

    private void ShowTrackText(Track? track)
    {
        _track = track;
        TrackText.Text = track is null
            ? string.Empty
            : string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Title}  ·  {track.Artist}";
        Title = track is null ? "Artwork - Lucent" : $"{track.Title} - Artwork - Lucent";
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

    private void ShowCover(CoverImage? cover, bool animate = true)
    {
        var changed = cover is null || _cover is null || !cover.Data.AsSpan().SequenceEqual(_cover.Data);
        _cover = cover;

        if (cover is null)
        {
            ArtImage.Source = null;
            ToolTip.SetTip(ArtImage, null);
            ArtImage.IsVisible = false;
            Placeholder.IsVisible = true;
            PlaceholderTitle.Text = _track is null && _remote is null ? "Nothing playing" : "No artwork";
            PlaceholderText.Text = _remote is not null ? "No cover came with this song, from the app or station playing it."
                : _track is null ? "Artwork appears here when music plays."
                : "Add a cover with Edit info (right-click here), or put a cover.jpg in the sound's folder.";
            return;
        }

        ArtImage.Source = cover.Image;
        ToolTip.SetTip(ArtImage, $"{cover.SizeText} · {ImageFormats.Name(cover.MimeType)}"
            + (_remote is not null ? " · from the app or station playing it" : cover.FilePath is null ? " · stored in the music file" : $" · {Path.GetFileName(cover.FilePath)} in the sound's folder"));
        ArtImage.IsVisible = true;
        Placeholder.IsVisible = false;
        if (changed && animate)
        {
            _ = FadeIn(ArtImage, TimeSpan.FromMilliseconds(220));
        }
    }

    /// <summary>Fades a control in from nothing, like WPF's DoubleAnimation(0, 1) on Opacity.</summary>
    internal static Task FadeIn(Animatable target, TimeSpan duration)
    {
        var fade = new Animation
        {
            Duration = duration,
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(OpacityProperty, 0.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(OpacityProperty, 1.0) } },
            },
        };
        return fade.RunAsync(target);
    }

    // ---- Menu and keys ----

    private void OnArtContextMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CopyMenuItem.IsEnabled = _cover is not null;
        SaveMenuItem.IsEnabled = _cover is not null;
        EditMenuItem.IsEnabled = _track is not null;
        OnTopMenuItem.IsChecked = Topmost;

        // While a station plays, its website, support page and today's shows are a right-click away here too.
        foreach (var added in ArtMenu.Items.OfType<Control>().Where(item => item.Tag == MainWindow.StationItemMark).ToList())
        {
            ArtMenu.Items.Remove(added);
        }

        if (PlayingStation is not null && Owner is MainWindow player)
        {
            player.AddStationItems(ArtMenu, ArtMenu.Items.IndexOf(SaveMenuItem) + 1);
        }
    }

    /// <summary>The station on, when this window is showing it (not a song from the queue or another app).</summary>
    private RadioStation? PlayingStation =>
        _remote is { } remote && (Owner as MainWindow)?.CurrentStation is { } station && station.Id == remote.Id ? station : null;

    private void OnCopyImage(object? sender, RoutedEventArgs e) => _ = CopyImageAsync();

    private void OnSaveImage(object? sender, RoutedEventArgs e) => _ = SaveImageAsync();

    private void OnEditInfo(object? sender, RoutedEventArgs e) => EditInfo();

    private void OnToggleOnTop(object? sender, RoutedEventArgs e)
    {
        Topmost = !_settings.ArtworkWindowOnTop;
        _settings.ArtworkWindowOnTop = Topmost;
        OnTopToggle.IsChecked = Topmost;
        _settings.Save();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        var command = e.KeyModifiers == KeyModifiers.Meta;
        switch (e.Key)
        {
            // C opens and closes this window from the main window, so it closes it here too.
            case Key.Escape:
            case Key.C when e.KeyModifiers == KeyModifiers.None:
                Close();
                break;
            case Key.C when command:
                _ = CopyImageAsync();
                break;
            case Key.S when command:
                _ = SaveImageAsync();
                break;
            case Key.I when command:
                EditInfo();
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private async Task CopyImageAsync()
    {
        if (_cover is null)
        {
            return;
        }

        try
        {
            // Copy the full-size picture, not the one shrunk for the screen.
            var cover = _cover;
            var full = await Task.Run(() => CoverArt.Decode(cover.Data, 0)?.Image ?? cover.Image);
            if (TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                await clipboard.SetBitmapAsync(full);
            }
        }
        catch (Exception ex)
        {
            await ShowProblemAsync("Couldn't copy the picture. Another app may be using the clipboard; try again in a moment.", ex);
        }
    }

    private async Task SaveImageAsync()
    {
        if (_cover is null)
        {
            return;
        }

        var cover = _cover;
        var extension = ImageFormats.ExtensionFor(cover.MimeType);
        IStorageFile? file;
        try
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the artwork",
                SuggestedFileName = SafeFileName(_track?.Title ?? _remote?.Title) + extension,
                DefaultExtension = extension,
                ShowOverwritePrompt = true,
                FileTypeChoices = new[]
                {
                    new FilePickerFileType($"{ImageFormats.Name(cover.MimeType)} image") { Patterns = new[] { "*" + extension } },
                    FilePickerFileTypes.All,
                },
            });
        }
        catch (Exception ex)
        {
            await ShowProblemAsync("Couldn't open the save panel.", ex);
            return;
        }

        if (file is null)
        {
            return;
        }

        try
        {
            await using var stream = await file.OpenWriteAsync();
            stream.SetLength(0);
            await stream.WriteAsync(cover.Data);
        }
        catch (Exception ex)
        {
            await ShowProblemAsync("Couldn't save the picture there. Try another folder.", ex);
        }
    }

    private void EditInfo()
    {
        if (_track is not null)
        {
            TrackInfoWindow.Open(Owner as Window ?? this, _player, _settings, new[] { _track.Path });
        }
    }

    /// <summary>"Song name" made safe to use as a file name, or "cover".</summary>
    internal static string SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "cover";
        }

        // The Mac only forbids "/" (and NUL), but Finder shows ":" as "/", so both are swapped out.
        var invalid = Path.GetInvalidFileNameChars().Append(':').Append('/').ToHashSet();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "cover" : safe;
    }

    private Task ShowProblemAsync(string message, Exception ex) =>
        MessageWindow.ShowAsync(this, $"{message}\n\nDetails: {ex.Message}", "Lucent");

    // ---- Window ----

    private void OnArtPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            // A panel in the single window doesn't fill the screen on its own.
            if (!SingleWindow.Holds(this))
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }

            e.Handled = true;
        }
        else if (WindowState == WindowState.Normal)
        {
            SingleWindow.FrameOf(this).BeginMoveDrag(e);
            e.Handled = true;
        }
    }

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Centres the cover (or the placeholder) in the part of the window that's on its screen, so a window
    /// running past the screen's edge or under the Dock doesn't leave the picture off-centre in what you see.
    /// </summary>
    private void CenterOnVisiblePart()
    {
        var margin = new Thickness(0);
        if (IsVisible && WindowState == WindowState.Normal && ArtArea.Bounds is { Width: > 0, Height: > 0 } bounds
            && ArtArea.TranslatePoint(new Point(0, 0), this) is { } origin)
        {
            var art = new Rect(this.Left() + origin.X, this.Top() + origin.Y, bounds.Width, bounds.Height);
            var seen = art.Intersect(Placement.WorkArea(this));
            if (seen.Width >= MinWidth / 2 && seen.Height >= MinWidth / 2)
            {
                margin = new Thickness(seen.Left - art.Left, seen.Top - art.Top, art.Right - seen.Right, art.Bottom - seen.Bottom);
            }
        }

        ArtImage.Margin = margin;
        Placeholder.Margin = margin;
    }

    private void RememberNormalBounds()
    {
        if (WindowState == WindowState.Normal && IsVisible)
        {
            _normalPosition = Position;
            _normalSize = ClientSize;
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        RememberNormalBounds();
        if (_normalSize.Width > 0 && _normalSize.Height > 0)
        {
            _settings.ArtworkWindowLeft = _normalPosition.X;
            _settings.ArtworkWindowTop = _normalPosition.Y;
            _settings.ArtworkWindowWidth = _normalSize.Width;
            _settings.ArtworkWindowHeight = _normalSize.Height;
            _settings.Save();
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        RememberNormalBounds();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _loading?.Cancel();
        if (_instance == this)
        {
            _instance = null;
            OpenChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>True when enough of the window's title bar would be on a screen to grab it.</summary>
    private bool IsOnScreen(double left, double top, double width)
    {
        foreach (var screen in Screens.All)
        {
            var scale = screen.Scaling;
            var titleBar = new PixelRect((int)left, (int)top, (int)(width * scale), (int)(TitleBarHeight * scale));
            var visible = screen.Bounds.Intersect(titleBar);
            if (visible.Width >= 80 * scale && visible.Height >= 20 * scale)
            {
                return true;
            }
        }

        return false;
    }
}
