using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Video;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>
/// The video window: a video's pictures (a file or a video station), in a window of their own, so the visuals can
/// play beside it. It docks, snaps, locks and moves with the player like the other windows, and goes full screen.
/// A click plays or pauses, a double-click goes full screen, and right-click has the menu.
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

    private VideoWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        Width = Math.Max(MinWidth, settings.VideoWindowWidth);
        Height = Math.Max(MinHeight, settings.VideoWindowHeight);
        _fullScreen = new FullScreen(this, FullScreenButton, FullScreenIcon);
        _fullScreen.Changed += (_, _) => OnFullScreenChanged();

        // A click waits to be sure it isn't the start of a double-click.
        _clickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime()) };
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
                Picture.Cursor = Cursors.None;
            }
        };

        IsVisibleChanged += (_, _) => Update();
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

        var window = new VideoWindow(settings) { Owner = owner };
        if (owner is MainWindow main)
        {
            main.DockNewWindow(window);
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        }

        _instance = window;
        window.Show();
        window.Update();
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return true;
    }

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

    private void Update()
    {
        var video = _video;
        var name = video is null ? null
            : !string.IsNullOrWhiteSpace(_title) ? _title
            : !string.IsNullOrWhiteSpace(video.Title) ? video.Title
            : video.IsLive ? video.Source : Path.GetFileNameWithoutExtension(video.Source);
        TitleText.Text = name ?? string.Empty;
        Title = name is null ? "Video - Lucent" : $"{name} - Video - Lucent";

        var show = video is { HasPicture: true };
        VideoImage.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PlaceholderTitle.Text = video is null ? "No video playing" : video.IsLive ? "Waiting for the picture…" : "This video has no picture";
        PlaceholderText.Text = video is null ? "Videos and video stations play here, beside the visuals." : string.Empty;

        var render = show && IsVisible;
        if (render && !_rendering)
        {
            _rendering = true;
            CompositionTarget.Rendering += OnRendering;
        }
        else if (!render && _rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        if (!show)
        {
            VideoImage.Source = null;
            _bitmap = null;
            _frameShown = -1;
        }
    }

    // With every frame the screen shows: puts up the video's newest picture, when there's a new one.
    private void OnRendering(object? sender, EventArgs e)
    {
        if (_video is not { } video || WindowState == WindowState.Minimized)
        {
            return;
        }

        var frames = video.Frames;
        if (frames.Count == _frameShown)
        {
            return;
        }

        frames.CopyLatest((pixels, width, height, stride) =>
        {
            if (_bitmap is null || _bitmap.PixelWidth != width || _bitmap.PixelHeight != height)
            {
                _bitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                VideoImage.Source = _bitmap;
            }

            _bitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride * height, stride);
            _frameShown = frames.Count;
        });
    }

    // ---- Clicks and the menu ----

    private void OnPictureMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _clickTimer.Stop();
            _skipClick = true;
            _fullScreen.Toggle();
            e.Handled = true;
        }
    }

    private void OnPictureMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_skipClick)
        {
            // The button coming up after a double-click.
            _skipClick = false;
            return;
        }

        if (_video is not null)
        {
            _clickTimer.Stop();
            _clickTimer.Start();
        }
    }

    private void OnPictureRightClick(object sender, MouseButtonEventArgs e)
    {
        ShowMenu();
        e.Handled = true;
    }

    private void OnPictureMouseMove(object sender, MouseEventArgs e)
    {
        if (_fullScreen.IsOn)
        {
            Picture.Cursor = Cursors.Arrow;
            _hideCursorTimer.Stop();
            _hideCursorTimer.Start();
        }
    }

    /// <summary>Full screen, the player's controls, editing the video and keeping on top, at the mouse.</summary>
    private void ShowMenu()
    {
        var menu = new ContextMenu { PlacementTarget = Picture, Placement = PlacementMode.MousePoint };
        menu.Items.Add(Item(_fullScreen.IsOn ? "Leave full screen" : "Full screen", _fullScreen.IsOn ? "Esc" : "F11", _fullScreen.Toggle));
        if (Owner is MainWindow player)
        {
            menu.Items.Add(new Separator());
            player.AddPlaybackItems(menu);
            if (_video is { IsLive: false } video)
            {
                // The editor cuts MP4, M4V and MOV files without re-encoding; other videos only play for now.
                var path = video.Source;
                var editable = Editor.VideoEdit.IsEditableVideo(path);
                var edit = Item(editable ? "Edit video…" : "Edit video (MP4, M4V and MOV only, for now)", null, () => player.OpenInEditor(player, path));
                edit.IsEnabled = editable;
                menu.Items.Add(new Separator());
                menu.Items.Add(edit);
            }
        }

        menu.Items.Add(new Separator());
        var onTop = Item("Keep on top", null, () => Topmost = !Topmost);
        onTop.IsChecked = Topmost;
        menu.Items.Add(onTop);
        LucentFullScreen.AddTo(menu);
        menu.IsOpen = true;
    }

    private static MenuItem Item(string header, string? keys, Action action)
    {
        var item = new MenuItem { Header = header, InputGestureText = keys };
        item.Click += (_, _) => action();
        return item;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    // ---- Window ----

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
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
        if (Owner is not null && e.InputSource is not null)
        {
            var forwarded = new KeyEventArgs(e.KeyboardDevice, e.InputSource, e.Timestamp, e.Key)
            {
                RoutedEvent = Keyboard.PreviewKeyDownEvent,
            };
            Owner.RaiseEvent(forwarded);
            e.Handled = forwarded.Handled;
        }
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnFullScreenChanged()
    {
        // Full screen is all picture: the title bar comes back with Esc, F11 or a double-click.
        TitleRow.Height = new GridLength(_fullScreen.IsOn ? 0 : 36);
        _hideCursorTimer.Stop();
        Picture.Cursor = Cursors.Arrow;
        if (_fullScreen.IsOn)
        {
            _hideCursorTimer.Start();
        }

        OnWindowStateChanged(this, EventArgs.Empty);
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = WindowState == WindowState.Maximized && !_fullScreen.IsOn ? new Thickness(7) : new Thickness(0);
        BorderThickness = new Thickness(_fullScreen.IsOn ? 0 : 1);
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        var bounds = WindowState == WindowState.Normal && !_fullScreen.IsOn ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.VideoWindowWidth = bounds.Width;
            _settings.VideoWindowHeight = bounds.Height;
            _settings.Save();
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _clickTimer.Stop();
        _hideCursorTimer.Stop();
        if (_rendering)
        {
            _rendering = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        if (_instance == this)
        {
            _instance = null;
        }

        OpenChanged?.Invoke(null, EventArgs.Empty);
    }
}
