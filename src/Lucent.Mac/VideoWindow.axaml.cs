using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Video;

namespace Lucent.Mac;

/// <summary>
/// The video window: a video's pictures (a file or a video station), in a window of their own, so the visuals can
/// play beside it. It docks, snaps, locks and moves with the player like the other windows, and goes full screen.
/// A click plays or pauses, a double-click goes full screen, and right-click or Control-click has the menu.
/// </summary>
public partial class VideoWindow : Window
{
    private static VideoWindow? _instance;

    // What's playing, kept while the window is closed so it shows straight away when it opens.
    private static VideoStream? _video;
    private static string? _title;

    private readonly AppSettings _settings;
    private readonly FullScreen _fullScreen;
    private readonly DispatcherTimer _clickTimer;
    private readonly DispatcherTimer _hideCursorTimer;
    private WriteableBitmap? _bitmap;
    private long _frameShown = -1;
    private bool _rendering;
    private bool _skipClick;

    // For the XAML previewer only.
    public VideoWindow()
        : this(new AppSettings())
    {
    }

    private VideoWindow(AppSettings settings)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        TitleIcon.Source = EqualizerWindow.LucentIcon();
        _settings = settings;
        Width = Math.Max(MinWidth, settings.VideoWindowWidth);
        Height = Math.Max(MinHeight, settings.VideoWindowHeight);
        _fullScreen = new FullScreen(this, FullScreenButton, FullScreenIcon);
        _fullScreen.Changed += (_, _) => OnFullScreenChanged();

        // A click waits to be sure it isn't the start of a double-click.
        _clickTimer = new DispatcherTimer { Interval = DoubleClickTime() };
        _clickTimer.Tick += (_, _) =>
        {
            _clickTimer.Stop();
            (Owner as MainWindow)?.PlayOrPause();
        };

        // Full screen, the pointer gets out of the way once the mouse is left alone.
        _hideCursorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _hideCursorTimer.Tick += (_, _) =>
        {
            _hideCursorTimer.Stop();
            if (_fullScreen.IsOn)
            {
                Picture.Cursor = new Cursor(StandardCursorType.None);
            }
        };

        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => Update();
        Closing += OnClosing;
        Closed += OnClosed;
    }

    /// <summary>Raised when the video window opens or closes, so a toggle button can follow it.</summary>
    public static event EventHandler? OpenChanged;

    public static bool IsOpen => _instance is not null;

    /// <summary>The open video window, if there is one.</summary>
    public static Window? Current => _instance;

    /// <summary>Opens the video window docked beside the player's windows, or closes it if it's open. Returns whether it's now open.</summary>
    public static bool Toggle(Window owner, AppSettings settings)
    {
        if (_instance is not null)
        {
            _instance.Close();
            return false;
        }

        var window = Create(settings);
        if (owner is MainWindow main)
        {
            main.DockNewWindow(window);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _instance = window;
        window.Show(owner);
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

    /// <summary>A new video window that isn't shown yet. (Toggle is the way to open one; the screenshots use this.)</summary>
    internal static VideoWindow Create(AppSettings settings) => new(settings);

    /// <summary>
    /// The video playing, or null, and what to call it (a station's name, say; otherwise the video's own title or
    /// its file's name). Shown here whenever the window is open.
    /// </summary>
    public static void ShowVideo(VideoStream? video, string? title)
    {
        var changed = !ReferenceEquals(video, _video);
        _video = video;
        _title = title;
        if (_instance is { } window)
        {
            if (changed)
            {
                window._frameShown = -1;
            }

            window.Update();
        }
    }

    // ---- The picture ----

    internal void Update()
    {
        var video = _video;
        var name = video is null ? null
            : !string.IsNullOrWhiteSpace(_title) ? _title
            : !string.IsNullOrWhiteSpace(video.Title) ? video.Title
            : video.IsLive ? video.Source : Path.GetFileNameWithoutExtension(video.Source);
        TitleText.Text = name ?? string.Empty;
        Title = name is null ? "Video - Lucent" : $"{name} - Video - Lucent";

        var show = video is { HasPicture: true };
        VideoImage.IsVisible = show;
        Placeholder.IsVisible = !show;
        PlaceholderTitle.Text = video is null ? "No video playing" : video.IsLive ? "Waiting for the picture…" : "This video has no picture";
        PlaceholderText.Text = video is null ? "Videos and video stations play here, beside the visuals." : string.Empty;
        PlaceholderText.IsVisible = video is null;

        if (show && !_rendering)
        {
            _rendering = true;
            RequestAnimationFrame(OnFrame);
        }
        else if (!show)
        {
            _rendering = false;
            VideoImage.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
            _frameShown = -1;
        }
    }

    // With every frame the screen shows: puts up the video's newest picture, when there's a new one.
    private void OnFrame(TimeSpan time)
    {
        if (!_rendering)
        {
            return;
        }

        RequestAnimationFrame(OnFrame);
        if (_video is not { } video || !IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var frames = video.Frames;
        if (frames.Count == _frameShown)
        {
            return;
        }

        var shown = frames.CopyLatest((pixels, width, height, stride) =>
        {
            if (_bitmap is null || _bitmap.PixelSize.Width != width || _bitmap.PixelSize.Height != height)
            {
                _bitmap?.Dispose();
                _bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                VideoImage.Source = _bitmap;
            }

            using (var target = _bitmap.Lock())
            {
                CopyRows(pixels, stride, target.Address, target.RowBytes, Math.Min(stride, target.RowBytes), height);
            }

            _frameShown = frames.Count;
        });
        if (shown)
        {
            VideoImage.InvalidateVisual();
        }
    }

    private static unsafe void CopyRows(nint source, int sourceStride, nint target, int targetStride, int rowBytes, int rows)
    {
        if (sourceStride == targetStride)
        {
            Buffer.MemoryCopy((void*)source, (void*)target, (long)targetStride * rows, (long)sourceStride * rows);
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            Buffer.MemoryCopy((byte*)source + (long)row * sourceStride, (byte*)target + (long)row * targetStride, rowBytes, rowBytes);
        }
    }

    // ---- Clicks and the menu ----

    private void OnPicturePressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Picture).Properties;
        if (point.IsLeftButtonPressed && e.ClickCount == 2 && !e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            _clickTimer.Stop();
            _skipClick = true;
            _fullScreen.Toggle();
            e.Handled = true;
        }
    }

    private void OnPictureReleased(object? sender, PointerReleasedEventArgs e)
    {
        switch (e.InitialPressMouseButton)
        {
            // On a Mac, Control-click is a right-click.
            case MouseButton.Left when e.KeyModifiers.HasFlag(KeyModifiers.Control):
            case MouseButton.Right:
                ShowMenu();
                e.Handled = true;
                break;
            case MouseButton.Left when _skipClick:
                // The button coming up after a double-click.
                _skipClick = false;
                break;
            case MouseButton.Left when _video is not null:
                _clickTimer.Stop();
                _clickTimer.Start();
                break;
        }
    }

    private void OnPictureMoved(object? sender, PointerEventArgs e)
    {
        if (_fullScreen.IsOn)
        {
            Picture.Cursor = Cursor.Default;
            _hideCursorTimer.Stop();
            _hideCursorTimer.Start();
        }
    }

    /// <summary>Full screen, the player's controls, editing the video and keeping on top, at the mouse.</summary>
    private void ShowMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(_fullScreen.IsOn ? "Leave full screen" : "Full screen",
            _fullScreen.IsOn ? new KeyGesture(Key.Escape) : VisualizerWindow.FullscreenGesture, _fullScreen.Toggle));
        if (Owner is MainWindow player)
        {
            menu.Items.Add(new Separator());
            player.AddPlaybackItems(menu);
            if (_video is { IsLive: false } video)
            {
                // The editor cuts MP4, M4V and MOV files without re-encoding; other videos only play for now.
                var path = video.Source;
                var (header, editable) = WinampInspiredVisualizer.Editor.VideoEdit.EditorMenuItem(path);
                var edit = Item(header, null, () => player.OpenInEditor(player, path));
                edit.IsEnabled = editable;
                menu.Items.Add(new Separator());
                menu.Items.Add(edit);
            }
        }

        menu.Items.Add(new Separator());
        var onTop = Item("Keep on top", null, () => Topmost = !Topmost);
        onTop.ToggleType = MenuItemToggleType.CheckBox;
        onTop.IsChecked = Topmost;
        menu.Items.Add(onTop);
        LucentFullScreen.AddTo(menu);
        menu.Open(Picture);
    }

    private static MenuItem Item(string header, KeyGesture? keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGesture = keys };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>The longest gap between the clicks of a double-click, as set in System Settings.</summary>
    private TimeSpan DoubleClickTime()
    {
        var time = PlatformSettings?.GetDoubleTapTime(PointerType.Mouse) ?? TimeSpan.Zero;
        return time > TimeSpan.Zero ? time : TimeSpan.FromMilliseconds(500);
    }

    // ---- Window ----

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (FullScreen.IsToggleKey(e))
        {
            _fullScreen.Toggle();
            e.Handled = true;
            return;
        }

        // Esc leaves full screen first, then closes the window.
        if (e.Key == Key.Escape)
        {
            if (_fullScreen.IsOn)
            {
                _fullScreen.Toggle();
            }
            else
            {
                Close();
            }

            e.Handled = true;
            return;
        }

        // Everything else goes to the player, so its shortcuts (Space, and P to close this) keep
        // working while this window has the focus.
        if (Owner is MainWindow player)
        {
            player.HandleShortcut(e);
        }
    }

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    private void OnFullScreenChanged()
    {
        // Full screen is all picture: the title bar comes back with Esc, ⌃⌘F or a double-click.
        TitleBar.IsVisible = !_fullScreen.IsOn;
        Frame.BorderThickness = new Thickness(_fullScreen.IsOn ? 0 : 1);
        _hideCursorTimer.Stop();
        Picture.Cursor = Cursor.Default;
        if (_fullScreen.IsOn)
        {
            _hideCursorTimer.Start();
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // While full screen, keep the size it would go back to.
        if (WindowState == WindowState.Normal && !_fullScreen.IsOn && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            _settings.VideoWindowWidth = ClientSize.Width;
            _settings.VideoWindowHeight = ClientSize.Height;
            _settings.Save();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _clickTimer.Stop();
        _hideCursorTimer.Stop();
        _rendering = false;
        if (_instance == this)
        {
            _instance = null;
        }

        OpenChanged?.Invoke(null, EventArgs.Empty);
    }
}
