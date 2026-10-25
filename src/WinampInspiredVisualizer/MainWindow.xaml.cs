using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Diagnostics;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Stores;
using WinampInspiredVisualizer.Streaming;
using WinampInspiredVisualizer.Themes;
using WinampInspiredVisualizer.Updates;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.Avs;
using WinampInspiredVisualizer.Visualizer.MilkDrop;
using WinampInspiredVisualizer.Windows;
using WinampInspiredVisualizer.Video;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

/// <summary>
/// The player window, and the app's controller: it owns the audio, the queue and the
/// library, and the windows around it (visuals, queue, library) call back into it.
/// </summary>
public partial class MainWindow : Window
{
    private const double MarqueeGap = 48;
    private const double MarqueeSpeed = 36;

    private readonly AudioPlayer _player = new();
    private readonly ObservableCollection<PlaylistEntry> _playlist = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly VisualizerHost _visualizer;
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly MusicLibrary _library = new();
    private readonly StoreService _stores;
    private readonly AppUpdater _updater = new();
    private readonly VisualizerWindow _visuals;
    private readonly QueueWindow _queue;
    private readonly LibraryWindow _libraryWindow;
    private readonly SpectrumMeter _meter;
    private readonly WindowSnapper _snapper;
    private readonly Random _random = new();
    // Shuffle remembers what it has played, so it plays everything once before repeating,
    // and so Previous can step back through it.
    private readonly List<PlaylistEntry> _shuffleHistory = new();
    private DateTime _optionsClosedAt;
    private DisplayInfo _display = DisplayInfo.Standard;
    private PlaylistEntry? _current;
    private bool _isDraggingSeek;
    private bool _updatingSeekBar;

    public MainWindow()
    {
        InitializeComponent();

        // The other windows are made after this one, so this stays the app's main window:
        // closing it closes Lucent.
        Application.Current.MainWindow = this;
        _visuals = new VisualizerWindow();
        _queue = new QueueWindow();
        _libraryWindow = new LibraryWindow();

        _snapper = new WindowSnapper(this);
        _snapper.MoveFinished += (_, _) => SavePlacements();
        _snapper.MoveFinished += (_, _) => OnPlayerResizeFinished();
        _snapper.PlayerShape = PlayerShape;
        SnapEveryWindow();
        SizeChanged += OnPlayerWidthChanged;
        StateChanged += OnPlayerStateChanged;
        SourceInitialized += (_, _) => (PresentationSource.FromVisual(this) as HwndSource)?.AddHook(RefuseMaximize);

        _queue.PlaylistView.ItemsSource = _playlist;
        _playlist.CollectionChanged += (_, _) =>
        {
            ShowQueueSize();
            _shuffleHistory.RemoveAll(entry => !_playlist.Contains(entry));
            ForgetUpNext();
            ShowNowPlaying(_current?.Track);
        };

        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
        _player.Volume = (float)VolumeSlider.Value;
        _player.Equalizer.Apply(_settings.Equalizer);
        _player.Equalizer.Balance = _settings.Balance;
        _player.TrackEnded += (_, _) => OnTrackEnded();
        _player.TrackAdvanced += (_, _) => OnTrackAdvanced();
        _player.VideoChanged += (_, _) => OnVideoChanged();
        ApplySoundSettings();
        _player.PlaybackFailed += (_, ex) => ShowError("Playback stopped because of an audio device error.", ex);

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _positionTimer.Tick += (_, _) =>
        {
            UpdatePosition();
            UpdateSongClock();
            LineUpNext();
            UpdateStationStatus();
        };
        _positionTimer.Start();

        _meter = new SpectrumMeter(_player.Capture) { Mode = _settings.Meter };
        MeterHost.Child = _meter;
        // The meter redraws with every frame the screen shows (usually 60 a second or more),
        // so it moves as smoothly as the visuals; a timer would hold it to about 20.
        CompositionTarget.Rendering += OnMeterFrame;

        _visualizer = new VisualizerHost(_player.Capture);
        _visualizer.DisplayChanged += (_, display) =>
        {
            _display = display;
            UpdateHdrHint();
        };
        _visualizer.StartPresetName = _settings.LastVisual;
        _visuals.Attach(_visualizer, _settings);
        _visuals.PlayHereRequested += (_, _) => RemoteTakes(remote => remote.PlayHereAsync());
        ApplyVisualizerSettings();

        ApplyLook();
        BuildThemeSwatches();
        ApplyClassicSkin();

        _libraryWindow.Attach(_library, _settings);
        _stores = new StoreService(_settings, LinkLibraryFolder);
        _library.SectionFolders = () => _stores.StoreFolders;
        _library.SetFolders(_settings.LibraryFolders, _settings.HiddenLibraryFolders, _settings.LibraryFiles,
            _settings.LibraryFoldersTogether);
        _stores.Start();

        foreach (var window in ToolWindows)
        {
            window.Player = this;
            window.IsVisibleChanged += (_, _) => OnToolWindowVisibilityChanged();
        }

        PreviewMouseWheel += OnPreviewMouseWheel;
        SetUpWindowPlaces();
        SetUpLucentFullScreen();
        if (Saved("Player") is { } placement)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = placement.Left;
            Top = placement.Top;
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Loaded += OnLoaded;
        SizeChanged += OnPlayerSizeChanged;
        UpdateShuffleRepeatButtons();
        TrackInfoWindow.TagsSaved += OnTagsSaved;
        ArtworkWindow.OpenChanged += OnArtworkOpenChanged;
        AnalyzerWindow.OpenChanged += OnAnalyzerOpenChanged;
        VideoWindow.OpenChanged += OnVideoOpenChanged;
        AudioTools.MainPlayerChanged += (_, _) => UpdatePlayPauseButton();
        AudioTools.FilesReplaced += OnTagsSaved;
        UpdatePlayPauseButton();
        ShowNowPlaying(null);
    }

    private void OnMeterFrame(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
        {
            _meter.Tick();
        }

        TickClassicPlayer();
    }

    /// <summary>True once the player is closing, so the other windows close for real instead of hiding.</summary>
    public bool IsShuttingDown { get; private set; }

    private IEnumerable<ToolWindow> ToolWindows => new ToolWindow[] { _visuals, _queue, _libraryWindow };

    // ---- Windows ----

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _openingWindows = true;
        // Owned windows minimize with the player and stay in front of it; this needs the player shown first.
        foreach (var window in ToolWindows)
        {
            window.Owner = this;
            _snapper.Add(window);
        }

        var firstTime = Saved("Player") is null;
        if (firstTime)
        {
            // The equalizer opens first so the first layout can make room for it under the player.
            if (_settings.ShowEqualizer)
            {
                OpenEqualizer();
            }

            ArrangeFirstTime();
        }
        else
        {
            foreach (var window in ToolWindows)
            {
                RestorePlacement(window);
            }
        }

        // The visuals open with the player, unless they were closed last time.
        if (_settings.ShowVisuals)
        {
            _visuals.Show();
        }

        // The queue and library open where they were left, or docked to the player the first time
        // (they keep the size they had).
        if (_settings.ShowQueue)
        {
            if (Saved(_queue.PlacementName) is null)
            {
                DockToPlayer(_queue);
            }

            _queue.Show();
        }

        if (_settings.ShowLibrary)
        {
            if (Saved(_libraryWindow.PlacementName) is null)
            {
                DockToPlayer(_libraryWindow);
            }

            _libraryWindow.Show();
        }

        // The equalizer opens where it was left (the first time, under the player, moving the queue down to make room).
        if (!firstTime && _settings.ShowEqualizer)
        {
            OpenEqualizer();
        }

        ReopenSmallWindows();

        // Every window gets its real (hidden) window now, so ones that start switched off still move
        // with the player while it's dragged, and turn up docked where they belong.
        foreach (var window in ToolWindows)
        {
            new WindowInteropHelper(window).EnsureHandle();
        }

        _openingWindows = false;
        UpdateWindowToggles();
        AlignBesidePlayerSoon();
        // Closed full screen: it opens full screen again, laid out on the screen it's on now.
        TileSoon();
        ReopenEditor();
        Activate();

        _updater.UpdateReady += version => Dispatcher.BeginInvoke(() =>
        {
            UpdateButton.ToolTip = UpdateButtonTip(version);
            UpdateButton.Visibility = Visibility.Visible;
            ShowUpdateStatus(ReadyStatus(version), readyToInstall: true);
        });
        _updater.Downloading += version => Dispatcher.BeginInvoke(() => ShowUpdateStatus($"Downloading Lucent {version}…", readyToInstall: false));
        _updater.EveryBuild = _settings.EveryBuild;
        _updater.InstallAutomatically = _settings.InstallUpdatesAutomatically;
        _ = _updater.RunAsync();
        AddStarterMusic();
        StartOpeningFiles();
        StartStreaming();
    }

    /// <summary>
    /// On a first start, puts the music that comes with Lucent in the library and the queue and plays it.
    /// It's an ordinary library folder from then on, and can be taken out.
    /// </summary>
    private async void AddStarterMusic()
    {
        if (!_settings.AddStarterMusic)
        {
            return;
        }

        _settings.AddStarterMusic = false;
        _settings.Save();
        var tracks = await Task.Run(() => StarterMusic.Install().Select(Track.FromFile).ToList());
        if (tracks.Count == 0)
        {
            return;
        }

        LinkLibraryFolder(StarterMusic.Folder);
        var first = new PlaylistEntry(tracks[0]);
        _playlist.Add(first);
        foreach (var track in tracks.Skip(1))
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        // It starts playing, with the visuals moving to it, unless something else was put on meanwhile.
        if (!_player.HasTrack)
        {
            PlayEntry(first);
        }
    }

    // True while the windows are being opened at start-up, when one window showing mustn't count
    // as the others being switched off.
    private bool _openingWindows;
    private bool _restartIntoUpdate;

    /// <summary>
    /// The player's height can change after it opens: Windows takes its standard frame away once
    /// the custom title bar is in place, and the player shrinks to fit. The windows stacked under
    /// it (the equalizer and the queue) follow its bottom edge so they stay docked, and the last
    /// one keeps its bottom edge where it was, in line with the windows beside it.
    /// </summary>
    private void OnPlayerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // While the player is dragged the snapper places every window. Its height can wobble then, as it
        // changes scale crossing onto another screen, and moving the windows under it here as well would
        // fight the snapper (and mix up the two screens' units), so they'd jump about.
        if (!e.HeightChanged || e.PreviousSize.Height <= 0 || WindowState != WindowState.Normal || _snapper.IsDragging)
        {
            return;
        }

        var delta = e.NewSize.Height - e.PreviousSize.Height;
        var candidates = new List<Window> { _queue, _libraryWindow, _visuals };
        if (EqualizerWindow.Current is { } equalizer)
        {
            candidates.Add(equalizer);
        }

        var column = new List<Window>();
        var edge = Top + e.PreviousSize.Height;
        // A locked window stays put, and so do the ones under it.
        while (candidates.FirstOrDefault(window => !column.Contains(window) && window.WindowState == WindowState.Normal
                   && !WindowLocks.IsLocked(window) && Math.Abs(window.Top - edge) < 2
                   && window.Left < Left + ActualWidth - 2 && window.Left + SizeOf(window).Width > Left + 2) is { } next)
        {
            column.Add(next);
            edge = next.Top + SizeOf(next).Height;
        }

        for (var i = 0; i < column.Count; i++)
        {
            var window = column[i];
            var height = SizeOf(window).Height;
            window.Top += delta;
            if (i == column.Count - 1 && window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip)
            {
                window.Height = Math.Max(window.MinHeight, height - delta);
            }
        }

        AlignBesidePlayer();
    }

    /// <summary>
    /// The first layout, like Winamp's: the equalizer (if it's open) and the queue under the player,
    /// the visuals beside them all, and the library (if it's open) on the other side, centred on the screen the player is on.
    /// </summary>
    private void ArrangeFirstTime()
    {
        var work = ScreenArea.Of(this);
        var playerWidth = ActualWidth;
        var playerHeight = ActualHeight;
        var equalizer = OpenEqualizerWindow;
        var equalizerHeight = equalizer is null ? 0 : SizeOf(equalizer).Height;
        var queueHeight = Math.Clamp(Math.Min(work.Height * 0.36, work.Height - playerHeight - equalizerHeight - 40), _queue.MinHeight, 360);
        // The windows beside the player run down to the bottom of the column under it, so a hidden
        // queue doesn't count.
        var showQueue = _queue.IsVisible || _openingWindows && _settings.ShowQueue;
        var stackHeight = playerHeight + equalizerHeight + (showQueue ? queueHeight : 0);
        var libraryWidth = _settings.ShowLibrary ? 360.0 : 0.0;
        var visualsWidth = Math.Clamp(work.Width - playerWidth - libraryWidth - 40, 420, 860);
        if (libraryWidth + playerWidth + visualsWidth > work.Width)
        {
            // A small screen (or a large scaling): narrower visuals, so the group still fits on it.
            visualsWidth = Math.Max(_visuals.MinWidth, work.Width - playerWidth - libraryWidth);
        }

        var groupWidth = libraryWidth + playerWidth + visualsWidth;
        var left = work.Left + Math.Max(0, (work.Width - groupWidth) / 2);
        var top = work.Top + Math.Max(0, (work.Height - stackHeight) / 2);
        if (WindowLocks.Holds(this))
        {
            // The player's locked where it is: the others are laid out around it there.
            left = Left - libraryWidth;
            top = Top;
        }

        // Placed by hand from here on, so Windows doesn't centre the player again when it settles its size.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = left + libraryWidth;
        Top = top;
        if (equalizer is not null)
        {
            Place(equalizer, Left, top + playerHeight, playerWidth, equalizerHeight);
        }

        Place(_queue, Left, top + playerHeight + equalizerHeight, playerWidth, queueHeight);
        Place(_visuals, Left + playerWidth, top, visualsWidth, stackHeight);
        if (_settings.ShowLibrary)
        {
            Place(_libraryWindow, left, top, libraryWidth, stackHeight);
        }
    }

    /// <summary>Puts a window at that place and size, unless it's locked where it is.</summary>
    private static void Place(Window window, double left, double top, double width, double height)
    {
        if (WindowLocks.Holds(window))
        {
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = left;
        window.Top = top;
        window.Width = width;
        window.Height = height;
    }

    private void RestorePlacement(ToolWindow window)
    {
        if (Saved(window.PlacementName) is { } placement)
        {
            Place(window, placement.Left, placement.Top, placement.Width, placement.Height);
            return;
        }

        // Never placed before (the library, usually): beside the player where there's room.
        var work = ScreenArea.Of(this);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var height = Math.Max(window.Height, ColumnTop - Top + (_queue.IsVisible ? _queue.ActualHeight : 0));
        window.Height = Math.Min(height, work.Height);
        window.Left = Left - window.Width >= work.Left ? Left - window.Width : Math.Min(Left + ActualWidth, work.Right - window.Width);
        window.Top = Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height));
    }

    /// <summary>Docks a window back against the player: the queue under it, the visuals to its right, the library to its left.</summary>
    private void PlaceBesidePlayer(ToolWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (window == _queue)
        {
            window.Left = Left;
            window.Top = ColumnTop;
            window.Width = ActualWidth;
        }
        else if (window == _visuals)
        {
            window.Left = Left + ActualWidth;
            window.Top = Top;
            window.Height = Math.Max(window.Height, ColumnTop - Top + (_queue.IsVisible ? _queue.ActualHeight : 0));
        }
        else
        {
            window.Left = Left - (window.ActualWidth > 0 ? window.ActualWidth : window.Width);
            window.Top = Top;
        }
    }

    /// <summary>
    /// Docks the queue or library against the player: the queue underneath (under the equalizer, if
    /// that's docked there, or above the player if there's no room below), the library to the left
    /// (or the right, past the visuals, if there's no room).
    /// </summary>
    private void DockToPlayer(ToolWindow window)
    {
        if (WindowLocks.Holds(window))
        {
            return;
        }

        var area = ScreenArea.Of(this);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        var playerBottom = ColumnTop;

        if (window == _queue)
        {
            var height = SizeOf(window).Height;
            if (area.Bottom - playerBottom < height)
            {
                // Make room under the player rather than open the queue off the top of the screen.
                LiftDocked(height - (area.Bottom - playerBottom));
                playerBottom = ColumnTop;
            }

            var roomBelow = area.Bottom - playerBottom;
            var roomAbove = Top - area.Top;
            // Above only when it fits there: past the top of the screen its title bar can't be reached.
            var below = roomBelow >= height || roomBelow >= roomAbove || roomAbove < window.MinHeight;
            height = Math.Max(window.MinHeight, Math.Min(height, below ? roomBelow : roomAbove));
            window.Width = ActualWidth;
            window.Height = height;
            window.Left = Left;
            window.Top = below ? playerBottom : Top - height;
            return;
        }

        // The library runs the height of the player and the queue, if the queue's docked to it.
        var top = Top;
        var bottom = playerBottom;
        if (_queue.IsVisible && Math.Abs(_queue.Left - Left) < 2)
        {
            if (Math.Abs(_queue.Top - playerBottom) < 2)
            {
                bottom = _queue.Top + _queue.ActualHeight;
            }
            else if (Math.Abs(_queue.Top + _queue.ActualHeight - Top) < 2)
            {
                top = _queue.Top;
            }
        }

        var width = SizeOf(window).Width;
        var left = Left - width;
        if (left < area.Left)
        {
            // No room on the left: go on the right, beyond the visuals if they're docked there.
            left = Left + ActualWidth;
            if (_visuals.IsVisible && !_visuals.IsFullscreen && Math.Abs(_visuals.Left - left) < 2)
            {
                left = _visuals.Left + _visuals.ActualWidth;
            }

            if (left + width > area.Right)
            {
                left = Math.Max(area.Left, Left - width);
            }
        }

        window.Left = left;
        window.Top = top;
        window.Height = Math.Max(window.MinHeight, Math.Min(bottom - top, area.Height));
    }

    /// <summary>
    /// Moves the player and the windows docked to it up the screen by up to <paramref name="distance"/>,
    /// no further than the top of the screen, to make room under them.
    /// </summary>
    private void LiftDocked(double distance)
    {
        if (WindowState != WindowState.Normal || WindowLocks.IsLocked(this))
        {
            return;
        }

        var area = ScreenArea.Of(this);
        var group = new List<Window> { this };
        var others = Application.Current.Windows.Cast<Window>()
            .Where(other => other != this && other.Owner == this && other.IsVisible && other.WindowState == WindowState.Normal
                && !(other == _visuals && _visuals.IsFullscreen) && !WindowLocks.IsLocked(other))
            .ToList();
        static Rect Bounds(Window window) => new(window.Left, window.Top, window.ActualWidth, window.ActualHeight);
        for (var i = 0; i < group.Count; i++)
        {
            foreach (var other in others.ToList())
            {
                if (Touches(Bounds(group[i]), Bounds(other)))
                {
                    group.Add(other);
                    others.Remove(other);
                }
            }
        }

        var lift = Math.Min(distance, group.Min(window => window.Top) - area.Top);
        if (lift <= 0)
        {
            return;
        }

        foreach (var window in group)
        {
            window.Top -= lift;
        }
    }

    /// <summary>
    /// Opens a small window (the artwork, the equalizer or the analyzer) docked to the outside of the
    /// player and the windows docked to it, in the first spot where it fits on the screen without covering
    /// anything, shrinking it a little if it has to. It then snaps and moves with them like the rest.
    /// </summary>
    public void DockNewWindow(Window window)
    {
        _snapper.Add(window);
        RememberWhenClosed(window);
        if (OpenWhereLeft(window))
        {
            return;
        }

        DockToGroup(window, Array.Empty<Window>());
    }

    // Finds the window a free spot against the player's group. The windows in `moving` are about to be
    // docked too, so where they are now doesn't count as taken.
    private void DockToGroup(Window window, IReadOnlyCollection<Window> moving)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var area = ScreenArea.Of(this);
        var shown = Application.Current.Windows.Cast<Window>()
            .Where(other => other != window && !moving.Contains(other) && other.IsVisible && other.WindowState == WindowState.Normal
                && (other == this || other.Owner == this) && !(other == _visuals && _visuals.IsFullscreen))
            .Select(other => new Rect(other.Left, other.Top, other.ActualWidth, other.ActualHeight))
            .ToList();

        // The player and everything docked to it, and the bottom of the column under the player.
        var player = new Rect(Left, Top, ActualWidth, ActualHeight);
        var group = new List<Rect> { player };
        var rest = shown.Where(rect => rect != player).ToList();
        for (var i = 0; i < group.Count; i++)
        {
            foreach (var rect in rest.ToList())
            {
                if (Touches(group[i], rect))
                {
                    group.Add(rect);
                    rest.Remove(rect);
                }
            }
        }

        var bounds = group.Aggregate(Rect.Union);
        var columnBottom = group.Where(rect => Math.Abs(rect.Left - player.Left) < 2).Max(rect => rect.Bottom);

        var want = SizeOf(window);
        var resizable = window.ResizeMode is ResizeMode.CanResize or ResizeMode.CanResizeWithGrip;
        // Windows reaching past the edge of the screen leave no room on that side, rather than less than none.
        static Size Room(double width, double height) => new(Math.Max(0, width), Math.Max(0, height));

        // Each spot: where its top-left corner goes for a given size, and how much room there is.
        var spots = new (Func<Size, Point> Corner, Size Room)[]
        {
            (size => new Point(bounds.Right, bounds.Top), Room(area.Right - bounds.Right, area.Bottom - bounds.Top)),
            (size => new Point(bounds.Left - size.Width, bounds.Top), Room(bounds.Left - area.Left, area.Bottom - bounds.Top)),
            (size => new Point(player.Left, columnBottom), Room(area.Right - player.Left, area.Bottom - columnBottom)),
            (size => new Point(bounds.Left, bounds.Bottom), Room(area.Right - bounds.Left, area.Bottom - bounds.Bottom)),
            (size => new Point(bounds.Left, bounds.Top - size.Height), Room(area.Right - bounds.Left, bounds.Top - area.Top)),
        };

        foreach (var shrink in new[] { false, true })
        {
            if (shrink && !resizable)
            {
                break;
            }

            foreach (var (corner, room) in spots)
            {
                var size = new Size(Math.Min(want.Width, room.Width), Math.Min(want.Height, room.Height));
                var fits = size.Width > 0 && size.Height > 0 && (shrink
                    ? size.Width >= window.MinWidth && size.Height >= window.MinHeight
                    : size.Width >= want.Width && size.Height >= want.Height);
                if (!fits)
                {
                    continue;
                }

                var spot = new Rect(corner(size), size);
                if (shown.Any(rect => Overlap(rect, spot)))
                {
                    continue;
                }

                Place(window, spot.Left, spot.Top, spot.Width, spot.Height);
                return;
            }
        }

        // Nowhere free on this screen: over the player, as before.
        window.WindowStartupLocation = WindowStartupLocation.CenterOwner;
    }

    private static bool Touches(Rect a, Rect b)
    {
        const double tolerance = 2;
        var overlapX = a.Left < b.Right && b.Left < a.Right;
        var overlapY = a.Top < b.Bottom && b.Top < a.Bottom;
        return overlapY && (Math.Abs(a.Right - b.Left) <= tolerance || Math.Abs(b.Right - a.Left) <= tolerance)
            || overlapX && (Math.Abs(a.Bottom - b.Top) <= tolerance || Math.Abs(b.Bottom - a.Top) <= tolerance);
    }

    private static bool Overlap(Rect a, Rect b)
    {
        var both = Rect.Intersect(a, b);
        return !both.IsEmpty && both.Width > 2 && both.Height > 2;
    }

    private bool IsDockedLeft(Window window) =>
        Math.Abs(window.Left + window.ActualWidth - Left) < 2 && window.Top < Top + ActualHeight && window.Top + window.ActualHeight > Top;

    // A window that hasn't been shown yet has no actual size, only the one it's been given.
    private static Size SizeOf(Window window) => new(
        window.ActualWidth > 0 ? window.ActualWidth : window.Width,
        window.ActualHeight > 0 ? window.ActualHeight : window.Height);

    private void OnArrangeWindows(object sender, RoutedEventArgs e) => ArrangeWindows();

    /// <summary>Puts every window back together around the player, as on the first run, and remembers it.</summary>
    private void ArrangeWindows()
    {
        LeaveLucentFullScreen(putBack: false);
        RestoreAllWindows();
        ArrangeFirstTime();
        SavePlacements();
        // Once the arranged windows have their new sizes, so the spots beside them are worked out from where they are now.
        Dispatcher.InvokeAsync(RedockSmallWindows, DispatcherPriority.Background);
    }

    /// <summary>Docks the analyzer, the artwork and the video back against the player's group, wherever they'd been moved to.</summary>
    private void RedockSmallWindows()
    {
        var moving = new[] { AnalyzerWindow.Current, ArtworkWindow.Current, VideoWindow.Current }
            .OfType<Window>()
            // A locked one stays where it is.
            .Where(window => window.IsVisible && window.WindowState == WindowState.Normal && !WindowLocks.IsLocked(window))
            .ToList();
        for (var i = 0; i < moving.Count; i++)
        {
            DockToGroup(moving[i], moving.Skip(i + 1).ToList());
        }
    }

    private void OnFillScreen(object sender, RoutedEventArgs e) => FillScreen();

    /// <summary>
    /// Docks the shown windows together and stretches them to fill the screen, once: the layout Lucent
    /// full screen keeps (MainWindow.FullScreen.cs), without staying full screen or remembering where the
    /// windows were. The library down the left, the player at the top with the equalizer and the queue
    /// under it to the bottom, and the visuals filling the rest, with the other windows under them.
    /// </summary>
    private void FillScreen()
    {
        LeaveLucentFullScreen(putBack: false);
        RestoreAllWindows();
        TileWindows();
    }

    private void OnShrinkWindows(object sender, RoutedEventArgs e) => ShrinkWindows();

    /// <summary>Docks the shown windows around the player at their smallest sizes.</summary>
    private void ShrinkWindows()
    {
        LeaveLucentFullScreen(putBack: false);
        RestoreAllWindows();
        var playerWidth = ActualWidth;
        var playerHeight = ActualHeight;
        var queueHeight = _queue.IsVisible ? _queue.MinHeight : 0;
        var equalizerHeight = 0.0;
        if (OpenEqualizerWindow is { } equalizer)
        {
            equalizerHeight = SizeOf(equalizer).Height;
            Place(equalizer, Left, Top + playerHeight, playerWidth, equalizerHeight);
        }

        var stackHeight = playerHeight + equalizerHeight + queueHeight;
        if (_queue.IsVisible)
        {
            Place(_queue, Left, Top + playerHeight + equalizerHeight, playerWidth, queueHeight);
        }

        if (_visuals.IsVisible)
        {
            Place(_visuals, Left + playerWidth, Top, _visuals.MinWidth, Math.Max(_visuals.MinHeight, stackHeight));
        }

        if (_libraryWindow.IsVisible)
        {
            Place(_libraryWindow, Left - _libraryWindow.MinWidth, Top, _libraryWindow.MinWidth, Math.Max(_libraryWindow.MinHeight, stackHeight));
        }

        SavePlacements();
    }

    /// <summary>Brings every window, the visuals included, out of maximized or fullscreen.</summary>
    private void RestoreAllWindows()
    {
        foreach (var window in ToolWindows)
        {
            if (window.WindowState != WindowState.Normal && !(window == _visuals && _visuals.IsFullscreen))
            {
                window.WindowState = WindowState.Normal;
            }
        }

        if (_visuals.IsFullscreen)
        {
            _visuals.ToggleFullscreen();
        }

        WindowState = WindowState.Normal;
    }

    private static bool IsOnScreen(Window window) =>
        IsOnScreen(new Rect(window.Left, window.Top, window.ActualWidth > 0 ? window.ActualWidth : window.Width,
            window.ActualHeight > 0 ? window.ActualHeight : window.Height));

    /// <summary>True when enough of the rectangle is on the screens to grab its title bar.</summary>
    private static bool IsOnScreen(Rect bounds)
    {
        if (double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top) || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height))
        {
            return false;
        }

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var visible = Rect.Intersect(screen, bounds);
        return !visible.IsEmpty && visible.Width >= 100 && visible.Height >= 40;
    }

    /// <summary>A saved position, if it's still on one of the screens.</summary>
    private WindowBounds? Saved(string name)
    {
        if (!_settings.Windows.TryGetValue(name, out var bounds) || bounds.Width < 50 || bounds.Height < 50)
        {
            return null;
        }

        return IsOnScreen(new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height)) ? bounds : null;
    }

    private void SavePlacements()
    {
        var docked = DockedGroup();
        Remember("Player", this, docked);
        foreach (var window in ToolWindows)
        {
            // While fullscreen, keep the size the visuals will go back to.
            if (window != _visuals || !_visuals.IsFullscreen)
            {
                Remember(window.PlacementName, window, docked);
            }
        }

        foreach (var window in SmallWindows)
        {
            if (WindowLocks.NameOf(window) is { } name && window.WindowState == WindowState.Normal)
            {
                Remember(name, window, docked);
            }
        }

        _settings.Save();
    }

    /// <summary>Remembers where a window is, and whether it's docked to the player (it touches one of the <paramref name="docked"/> windows).</summary>
    private void Remember(string name, Window window, List<Rect> docked)
    {
        if (!window.IsLoaded)
        {
            return;
        }

        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        if (!bounds.IsEmpty)
        {
            _settings.Windows[name] = new WindowBounds
            {
                Left = bounds.Left,
                Top = bounds.Top,
                Width = bounds.Width,
                Height = bounds.Height,
                Docked = window != this && docked.Any(rect => Touches(rect, bounds)),
            };
        }
    }

    private void OnToggleEqualizer(object sender, RoutedEventArgs e) => ToggleEqualizer();

    private void ToggleEqualizer() =>
        EqualizerWindow.Toggle(this, _player, _settings, OnEqualizerOpenChanged);

    private void OpenEqualizer() => EqualizerWindow.Show(this, _player, _settings, OnEqualizerOpenChanged);

    private void OnEqualizerOpenChanged(bool isOpen)
    {
        EqualizerToggle.IsChecked = isOpen;
        // Closing Lucent closes the equalizer too; that shouldn't count as switching it off.
        if (!IsShuttingDown)
        {
            _settings.ShowEqualizer = isOpen;
        }
    }

    private void OnToggleAnalyzer(object sender, RoutedEventArgs e) => ToggleAnalyzer();

    private void ToggleAnalyzer() => AnalyzerToggle.IsChecked = AnalyzerWindow.Toggle(this, _player, _settings);

    private void OnAnalyzerOpenChanged(object? sender, EventArgs e)
    {
        AnalyzerToggle.IsChecked = AnalyzerWindow.IsOpen;
        // Closing Lucent closes the analyzer too; that shouldn't count as switching it off.
        if (!IsShuttingDown)
        {
            _settings.ShowAnalyzer = AnalyzerWindow.IsOpen;
        }
    }

    /// <summary>The equalizer window, if it's open.</summary>
    private static Window? OpenEqualizerWindow => EqualizerWindow.Current is { IsVisible: true } equalizer ? equalizer : null;

    /// <summary>Where the queue docks: right under the player, or under the equalizer when that's docked there.</summary>
    private double ColumnTop
    {
        get
        {
            var playerBottom = Top + ActualHeight;
            return OpenEqualizerWindow is { } equalizer && Math.Abs(equalizer.Left - Left) < 2 && Math.Abs(equalizer.Top - playerBottom) < 2
                ? playerBottom + SizeOf(equalizer).Height
                : playerBottom;
        }
    }

    /// <summary>The bottom of the column under the player: the queue's bottom when it's docked there, otherwise <see cref="ColumnTop"/>.</summary>
    private double ColumnBottom
    {
        get
        {
            var top = ColumnTop;
            return _queue.IsVisible && _queue.WindowState == WindowState.Normal
                && Math.Abs(_queue.Left - Left) < 2 && Math.Abs(_queue.Top - top) < 2
                ? top + SizeOf(_queue).Height
                : top;
        }
    }

    /// <summary>
    /// Keeps the layout symmetrical: the visuals and library docked beside the player run from its
    /// top to the bottom of the column under it (the equalizer and queue), so all the windows line up
    /// along the bottom edge as well as the top.
    /// </summary>
    private void AlignBesidePlayer()
    {
        // Lucent full screen lays the windows out itself.
        if (IsShuttingDown || WindowState != WindowState.Normal || !IsLoaded || _settings.LucentFullScreen)
        {
            return;
        }

        // With nothing under the player there's no column to line up with, and the visuals would
        // shrink to a strip, so they keep their size.
        var bottom = ColumnBottom;
        if (bottom - (Top + ActualHeight) < 2)
        {
            return;
        }

        var height = bottom - Top;
        foreach (var window in new ToolWindow[] { _visuals, _libraryWindow })
        {
            if (!window.IsVisible || window.WindowState != WindowState.Normal || window == _visuals && _visuals.IsFullscreen
                || WindowLocks.IsLocked(window))
            {
                continue;
            }

            var width = SizeOf(window).Width;
            var beside = Math.Abs(window.Left - (Left + ActualWidth)) < 2 || Math.Abs(window.Left + width - Left) < 2;
            if (beside && Math.Abs(window.Top - Top) < 2)
            {
                window.Height = Math.Max(window.MinHeight, height);
            }
        }
    }

    // Once the windows that just opened or closed have settled their sizes.
    private void AlignBesidePlayerSoon() =>
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, AlignBesidePlayer);

    /// <summary>
    /// Docks the equalizer right under the player, the same width, like Winamp's. A queue docked
    /// under the player moves down beneath it, keeping its bottom edge where it was if it can, and
    /// moves back up when the equalizer closes. With no room under the player it docks beside the
    /// windows instead.
    /// </summary>
    public void DockEqualizer(Window equalizer)
    {
        var area = ScreenArea.Of(this);
        var playerBottom = Top + ActualHeight;
        var height = SizeOf(equalizer).Height;
        equalizer.Closing += (_, _) => CloseEqualizerGap(equalizer);
        if (OpenWhereLeft(equalizer))
        {
            _snapper.Add(equalizer);
            RememberWhenClosed(equalizer);
            AlignBesidePlayerSoon();
            return;
        }

        var queueBelow = _queue.IsVisible && Math.Abs(_queue.Left - Left) < 2 && Math.Abs(_queue.Top - playerBottom) < 2;
        var needed = playerBottom + height + (queueBelow ? _queue.MinHeight : 0) - area.Bottom;
        if (needed > 0)
        {
            // Make room under the player, so the equalizer doesn't end up somewhere odd (or on top of the player).
            LiftDocked(needed);
            playerBottom = Top + ActualHeight;
        }

        if (playerBottom + height > area.Bottom)
        {
            DockNewWindow(equalizer);
            return;
        }

        _snapper.Add(equalizer);
        RememberWhenClosed(equalizer);
        Place(equalizer, Left, playerBottom, ActualWidth, height);
        AlignBesidePlayerSoon();
        if (!_queue.IsVisible || WindowLocks.IsLocked(_queue) || Math.Abs(_queue.Left - Left) >= 2 || Math.Abs(_queue.Top - playerBottom) >= 2)
        {
            return;
        }

        var bottom = _queue.Top + _queue.ActualHeight;
        var queueTop = playerBottom + height;
        var newBottom = Math.Min(area.Bottom, Math.Max(bottom, queueTop + _queue.MinHeight));
        _queue.Top = queueTop;
        _queue.Height = Math.Max(_queue.MinHeight, newBottom - queueTop);

        // If the queue had to grow past the bottom of the windows beside it, they grow to match.
        if (newBottom > bottom + 1)
        {
            foreach (var window in new Window[] { _visuals, _libraryWindow })
            {
                if (window.IsVisible && window.WindowState == WindowState.Normal && !WindowLocks.IsLocked(window)
                    && Math.Abs(window.Top + window.ActualHeight - bottom) < 2)
                {
                    window.Height = newBottom - window.Top;
                }
            }
        }
    }

    // When the equalizer under the player closes, the queue under it moves up to the player again.
    private void CloseEqualizerGap(Window equalizer)
    {
        AlignBesidePlayerSoon();
        var top = equalizer.Top;
        var bottom = top + equalizer.ActualHeight;
        if (IsShuttingDown || Math.Abs(top - (Top + ActualHeight)) >= 2 || Math.Abs(equalizer.Left - Left) >= 2
            || !_queue.IsVisible || WindowLocks.IsLocked(_queue) || Math.Abs(_queue.Left - Left) >= 2 || Math.Abs(_queue.Top - bottom) >= 2)
        {
            return;
        }

        var queueBottom = _queue.Top + _queue.ActualHeight;
        _queue.Top = top;
        _queue.Height = queueBottom - top;
    }

    private void OnToggleVisuals(object sender, RoutedEventArgs e) => ToggleWindow(_visuals);

    private void OnToggleQueue(object sender, RoutedEventArgs e) => ToggleWindow(_queue);

    private void OnToggleLibrary(object sender, RoutedEventArgs e) => ToggleWindow(_libraryWindow);

    private void ToggleWindow(ToolWindow window)
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        if (!window.IsLoaded && Saved(window.PlacementName) is null)
        {
            RestorePlacement(window);
        }

        if (window != _visuals)
        {
            // The queue and library open docked to the player, unless they were left somewhere on their own.
            if (!OpenWhereLeft(window))
            {
                DockToPlayer(window);
            }
        }
        else if (!IsOnScreen(window))
        {
            // The visuals open where they were left, unless that's on a screen that's since been unplugged.
            PlaceBesidePlayer(window);
        }

        window.Show();

        // A library docked to the player's left grows to match when the queue opens underneath.
        if (window == _queue && _libraryWindow.IsVisible && IsDockedLeft(_libraryWindow))
        {
            DockToPlayer(_libraryWindow);
        }
    }

    private void OnToolWindowVisibilityChanged()
    {
        if (IsShuttingDown || _openingWindows)
        {
            return;
        }

        _settings.ShowVisuals = _visuals.IsVisible;
        _settings.ShowQueue = _queue.IsVisible;
        _settings.ShowLibrary = _libraryWindow.IsVisible;
        UpdateWindowToggles();
        SavePlacements();
        AlignBesidePlayerSoon();
    }

    private void UpdateWindowToggles()
    {
        VisualsToggle.IsChecked = _visuals.IsVisible;
        QueueToggle.IsChecked = _queue.IsVisible;
        LibraryToggle.IsChecked = _libraryWindow.IsVisible;
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnInstallUpdate(object sender, RoutedEventArgs e)
    {
        // The update is handed over once the player has closed, with a restart. The choice stays made
        // until it closes, including after something such as unsaved edits holds the close up.
        _restartIntoUpdate = true;

        // Close the Options popup first, then the player itself. A close posted with
        // SystemCommands.CloseWindow from this button, inside the popup, never arrived.
        OptionsPopup.IsOpen = false;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(Close));
    }

    private void OnBuyMeACoffee(object sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = false;
        SupportLink.Open();
    }

    private void OnReportBug(object sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = false;
        BugReport.Open();
    }

    // ---- Options ----

    /// <summary>Hands the graphics and visual-change settings to the visualizer.</summary>
    public void ApplyVisualizerSettings()
    {
        _visualizer.TargetPixels = GraphicsOptions.Pixels(_settings.Resolution);
        _visualizer.AutoResolution = _settings.Resolution == RenderResolution.Auto;
        _visualizer.Quality = GraphicsOptions.Quality(_settings.Detail);
        _visualizer.BloomStrength = GraphicsOptions.BloomStrength(_settings.Glow);
        AvsVisual.ModernLook = _settings.ModernAvs;
        MilkDropLook.Modern = _settings.ModernMilkDrop;
        SongLook.On = _settings.BuildUpsAndDrops;
        _visualizer.Hdr = _settings.Hdr;
        _visualizer.AutoCycle = _settings.AutoCycle;
        _visualizer.AutoCycleSeconds = _settings.AutoCycleSeconds;
        _visualizer.TransitionSeconds = GraphicsOptions.Seconds(_settings.TransitionSpeed);
        _visualizer.MorphTransitionsOnly = _settings.MorphTransitionsOnly;
        _visuals.ShowFrameRate(_settings.ShowFrameRate);
        _visuals.UpdateAutoCycleButton();
        _visuals.UpdatePreviousButton();
        AutoCycleSwitch.IsChecked = _settings.AutoCycle;
    }

    private void OnToggleOptions(object sender, RoutedEventArgs e) => ToggleOptions();

    private void ToggleOptions()
    {
        if (OptionsPopup.IsOpen)
        {
            OptionsPopup.IsOpen = false;
            return;
        }

        // The click that closed the panel (by clicking outside it, on this button) shouldn't reopen it.
        if (_visuals.IsFullscreen || (DateTime.UtcNow - _optionsClosedAt).TotalMilliseconds < 250)
        {
            OptionsButton.IsChecked = false;
            return;
        }

        ShowOptions();
    }

    private void ShowOptions()
    {
        ShowOptionValues();
        UpdateHdrHint();

        BuildClassicSkinSwatches();
        if (UpdatesTab.Visibility == Visibility.Visible)
        {
            ShowDefaultPlayerState();
        }

        // Line the panel's right edge up with the button's (the panel has a 16px margin for its shadow), or
        // with the classic player's.
        OptionsPopup.HorizontalOffset = ((FrameworkElement)OptionsPopup.PlacementTarget).ActualWidth - 356;
        OptionsPopup.IsOpen = true;
    }

    private void OnOptionsOpened(object? sender, EventArgs e) => OptionsButton.IsChecked = true;

    private void OnOptionsClosed(object? sender, EventArgs e)
    {
        OptionsButton.IsChecked = false;
        _optionsClosedAt = DateTime.UtcNow;
    }

    private void OnOptionsTab(object sender, RoutedEventArgs e)
    {
        var tab = (sender as RadioButton)?.Tag as string;
        GraphicsTab.Visibility = tab == "Graphics" ? Visibility.Visible : Visibility.Collapsed;
        VisualsTab.Visibility = tab == "Visuals" ? Visibility.Visible : Visibility.Collapsed;
        SoundTab.Visibility = tab == "Sound" ? Visibility.Visible : Visibility.Collapsed;
        ThemeTab.Visibility = tab == "Theme" ? Visibility.Visible : Visibility.Collapsed;
        UpdatesTab.Visibility = tab == "Updates" ? Visibility.Visible : Visibility.Collapsed;
        if (tab == "Updates")
        {
            ShowDefaultPlayerState();
        }
    }

    /// <summary>A choice in one of the option rows (resolution, detail, glow, timing, skin, theme, updates).</summary>
    private void OnOptionChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { GroupName: var group, Tag: string value })
        {
            return;
        }

        switch (group)
        {
            case "Resolution" when Enum.TryParse<RenderResolution>(value, out var resolution):
                _settings.Resolution = resolution;
                break;
            case "Detail" when Enum.TryParse<DetailLevel>(value, out var detail):
                _settings.Detail = detail;
                break;
            case "Glow" when Enum.TryParse<GlowLevel>(value, out var glow):
                _settings.Glow = glow;
                break;
            case "AutoCycleSeconds" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds):
                _settings.AutoCycleSeconds = seconds;
                break;
            case "TransitionSpeed" when Enum.TryParse<TransitionSpeed>(value, out var speed):
                _settings.TransitionSpeed = speed;
                break;
            case "PreviousVisual" when Enum.TryParse<PreviousVisualMode>(value, out var previous):
                _settings.PreviousVisual = previous;
                break;
            case "Theme":
                _settings.Theme = value;
                ApplyLook();
                break;
            case "Skin":
                // A skin brings the theme it was made for; another theme can be picked after it. Picking one
                // also brings back Lucent's own player if a classic skin was drawing it.
                _settings.Skin = value;
                _settings.ClassicSkin = null;
                ApplyClassicSkin();
                BuildClassicSkinSwatches();
                _settings.Theme = SkinManager.Find(value).Theme;
                ApplyLook();
                ShowOptionValues();
                break;
            case "Updates":
                _settings.EveryBuild = value == "EveryBuild";
                _settings.Save();
                _updater.EveryBuild = _settings.EveryBuild;
                _updater.ChannelChanged();
                UpdateButton.Visibility = Visibility.Collapsed;
                ShowUpdateStatus(null, readyToInstall: false);
                return;
            default:
                return;
        }

        ApplyVisualizerSettings();
        _settings.Save();
    }

    /// <summary>One of the on/off switches in the options panel.</summary>
    private void OnOptionSwitched(object sender, RoutedEventArgs e)
    {
        var on = (sender as CheckBox)?.IsChecked == true;
        if (sender == HdrSwitch)
        {
            _settings.Hdr = on;
        }
        else if (sender == FrameRateSwitch)
        {
            _settings.ShowFrameRate = on;
        }
        else if (sender == BuildUpsSwitch)
        {
            _settings.BuildUpsAndDrops = on;
        }
        else if (sender == AutoCycleSwitch)
        {
            _settings.AutoCycle = on;
        }
        else if (sender == MorphsOnlySwitch)
        {
            _settings.MorphTransitionsOnly = on;
        }
        else if (sender == LucentVisualsSwitch)
        {
            _settings.LucentVisuals = on;
            _visuals.ShowLucentOriginals(on);
        }
        else if (sender == MilkDropSwitch)
        {
            _settings.MilkDropVisuals = on;
            _visuals.ShowMilkDrop(on);
        }
        else if (sender == AvsSwitch)
        {
            _settings.AvsVisuals = on;
            _visuals.ShowAvs(on);
        }
        else if (sender == AutoInstallUpdatesSwitch)
        {
            _settings.InstallUpdatesAutomatically = on;
            _updater.InstallAutomatically = on;
            _settings.Save();
            if (_updater.ReadyVersion is { } ready)
            {
                UpdateButton.ToolTip = UpdateButtonTip(ready);
                ShowUpdateStatus(ReadyStatus(ready), readyToInstall: true);
            }

            return;
        }
        else if (sender == CrashReportsSwitch)
        {
            _settings.SendCrashReports = on;
            CrashReporter.Enabled = on;
            _settings.Save();
            return;
        }
        else if (sender == ModernAvsSwitch)
        {
            _visuals.UseModernAvs(on);
        }
        else if (sender == ModernMilkDropSwitch)
        {
            _visuals.UseModernMilkDrop(on);
        }

        ApplyVisualizerSettings();
        UpdateHdrHint();
        _settings.Save();
    }

    /// <summary>Marks the current settings in the options panel.</summary>
    private void ShowOptionValues()
    {
        foreach (var choice in LogicalDescendants<RadioButton>(OptionsPanel))
        {
            var current = choice.GroupName switch
            {
                "Resolution" => _settings.Resolution.ToString(),
                "Detail" => _settings.Detail.ToString(),
                "Glow" => _settings.Glow.ToString(),
                "AutoCycleSeconds" => _settings.AutoCycleSeconds.ToString(CultureInfo.InvariantCulture),
                "TransitionSpeed" => _settings.TransitionSpeed.ToString(),
                "PreviousVisual" => _settings.PreviousVisual.ToString(),
                "Theme" => ThemeManager.Find(_settings.Theme).Name,
                "Skin" => SkinManager.Find(_settings.Skin).Name,
                "Updates" => _settings.EveryBuild ? "EveryBuild" : "Stable",
                _ => null,
            };
            if (current is not null)
            {
                choice.IsChecked = string.Equals(choice.Tag as string, current, StringComparison.OrdinalIgnoreCase);
            }
        }

        HdrSwitch.IsChecked = _settings.Hdr;
        FrameRateSwitch.IsChecked = _settings.ShowFrameRate;
        BuildUpsSwitch.IsChecked = _settings.BuildUpsAndDrops;
        AutoCycleSwitch.IsChecked = _settings.AutoCycle;
        MorphsOnlySwitch.IsChecked = _settings.MorphTransitionsOnly;
        LucentVisualsSwitch.IsChecked = _settings.LucentVisuals;
        MilkDropSwitch.IsChecked = _settings.MilkDropVisuals;
        AvsSwitch.IsChecked = _settings.AvsVisuals;
        ModernAvsSwitch.IsChecked = _settings.ModernAvs;
        ModernMilkDropSwitch.IsChecked = _settings.ModernMilkDrop;
        CrashReportsSwitch.IsChecked = _settings.SendCrashReports;
        AutoInstallUpdatesSwitch.IsChecked = _settings.InstallUpdatesAutomatically;
        VersionText.Text = $"You have Lucent {AppInfo.Version}.";
        ShowSoundValues();
    }

    /// <summary>Applies the chosen theme and skin to every window.</summary>
    private void ApplyLook()
    {
        var theme = ThemeManager.Find(_settings.Theme);
        SkinManager.Apply(SkinManager.Find(_settings.Skin), theme);
        // Rebuilt afterwards rather than now, as this can run from a click on one of the skin previews.
        Dispatcher.BeginInvoke(() => BuildSkinSwatches(theme), DispatcherPriority.Input);
        // The skin may have changed the title's font, so its width (and whether it scrolls) may have changed too.
        Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    /// <summary>The skin picker. Each skin is previewed in the current theme's colours, so it's rebuilt when the theme changes.</summary>
    private void BuildSkinSwatches(Theme theme)
    {
        SkinSwatches.Children.Clear();
        foreach (var skin in SkinManager.Skins)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ThemeSwatch"),
                GroupName = "Skin",
                Tag = skin.Name,
                Content = SkinManager.CreatePreview(skin, theme),
                IsChecked = string.Equals(skin.Name, SkinManager.Find(_settings.Skin).Name, StringComparison.Ordinal),
            };
            swatch.Click += OnOptionChosen;
            SkinSwatches.Children.Add(swatch);
        }
    }

    private void BuildThemeSwatches()
    {
        foreach (var theme in ThemeManager.Themes)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("ThemeSwatch"),
                GroupName = "Theme",
                Tag = theme.Name,
                Content = ThemeManager.CreatePreview(theme),
            };
            swatch.Click += OnOptionChosen;
            ThemeSwatches.Children.Add(swatch);
        }
    }

    private void UpdateHdrHint()
    {
        HdrHint.Text = !_display.HdrOn
            ? "Your screen isn't in HDR mode. Turn on \"Use HDR\" in Windows display settings to use this."
            : _settings.Hdr
                ? $"On. Highlights can reach about {_display.PeakNits:0} nits on this screen."
                : "Your screen is in HDR mode, so highlights can shine far brighter than normal white.";
    }

    private static IEnumerable<T> LogicalDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in LogicalDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    // ---- Adding music ----

    public async void AddFilesFromDialog(Window owner)
    {
        var music = string.Join(";", AudioReaderFactory.SupportedExtensions.Select(ext => "*" + ext));
        var videos = string.Join(";", VideoFiles.Extensions.Select(ext => "*" + ext));
        var dialog = new OpenFileDialog
        {
            Title = "Add music and videos",
            Multiselect = true,
            Filter = $"Music and videos|{music};{videos}|Music files|{music}|Videos|{videos}|All files|*.*",
        };

        if (dialog.ShowDialog(owner) == true)
        {
            await AddPathsAsync(dialog.FileNames);
        }
    }

    public async void AddFolderFromDialog(Window owner)
    {
        var dialog = new OpenFolderDialog { Title = "Add a music folder", Multiselect = true };
        if (dialog.ShowDialog(owner) == true)
        {
            await AddPathsAsync(dialog.FolderNames);
        }
    }

    /// <summary>Music dropped on the queue: added to the end of it.</summary>
    public async void AddDroppedPaths(IEnumerable<string> paths) => await AddPathsAsync(TakeDroppedSkins(paths));

    /// <summary>Music dropped on the player (or the visuals): it replaces the queue and plays now, as in Winamp.</summary>
    public async void PlayDroppedPaths(IEnumerable<string> paths)
    {
        // A Winamp skin dropped with (or instead of) the music is put on, not played.
        var input = TakeDroppedSkins(paths);
        if (input.Count == 0)
        {
            return;
        }

        var tracks = await Task.Run(() => ExpandPaths(input).Select(Track.FromFile).ToList());
        ReplaceQueue(tracks, 0);
    }

    public void ClearQueue()
    {
        // A radio station isn't in the queue, so it plays on.
        if (_station is null)
        {
            _player.Close();
        }

        _current = null;
        _playlist.Clear();
        _shuffleHistory.Clear();
        ShowNowPlaying(null);
        UpdatePlayPauseButton();
    }

    private void OnDragOver(object sender, DragEventArgs e) => DroppedMusic.Accept(e);

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (DroppedMusic.Paths(e) is { Count: > 0 } paths)
        {
            PlayDroppedPaths(paths);
        }
    }

    /// <summary>Adds files and (recursively) folders, reading tags off the UI thread.</summary>
    private async Task AddPathsAsync(IEnumerable<string> paths)
    {
        var input = paths.ToList();
        var tracks = await Task.Run(() => ExpandPaths(input).Select(Track.FromFile).ToList());
        AddToQueue(tracks);
    }

    public void AddToQueue(IReadOnlyList<Track> tracks)
    {
        var wasEmpty = _playlist.Count == 0;
        foreach (var track in tracks)
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        // Start playing straight away when music is added to an empty, idle player.
        if (wasEmpty && _playlist.Count > 0 && !_player.HasTrack)
        {
            PlayEntry(_settings.Shuffle ? _playlist[_random.Next(_playlist.Count)] : _playlist[0]);
        }
    }

    private static IEnumerable<string> ExpandPaths(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            if (Directory.Exists(path))
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                        .Where(VideoFiles.IsPlayable)
                        .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                }
                catch (Exception)
                {
                    // A folder we can't read (permissions, disconnected drive) is skipped.
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
            else if (File.Exists(path) && VideoFiles.IsPlayable(path))
            {
                yield return path;
            }
        }
    }

    public void ShowInExplorer(string path)
    {
        try
        {
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open File Explorer.", ex);
        }
    }

    /// <summary>Plays a library track now, queueing it right after the current track if it isn't queued yet.</summary>
    public void PlayFromLibrary(LibraryItem item)
    {
        var entry = _playlist.FirstOrDefault(e => string.Equals(e.Track.Path, item.Path, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            entry = new PlaylistEntry(item.ToTrack());
            var index = _current is null ? _playlist.Count : _playlist.IndexOf(_current) + 1;
            _playlist.Insert(index, entry);
        }

        PlayEntry(entry);
    }

    public void ReplaceQueue(IReadOnlyList<Track> tracks, int startIndex)
    {
        if (tracks.Count == 0)
        {
            return;
        }

        ClearQueue();
        foreach (var track in tracks)
        {
            _playlist.Add(new PlaylistEntry(track));
        }

        PlayEntry(_playlist[Math.Clamp(startIndex, 0, _playlist.Count - 1)]);
    }

    // ---- Queue ----

    public void RemoveFromQueue(IEnumerable<PlaylistEntry> entries)
    {
        foreach (var entry in entries.ToList())
        {
            if (entry == _current)
            {
                _player.Close();
                _current = null;
            }

            _playlist.Remove(entry);
        }

        ShowNowPlaying(_current?.Track);
        UpdatePlayPauseButton();
    }

    public void PlayEntry(PlaylistEntry entry)
    {
        // Playing Lucent's own music: the other app being controlled pauses, and the player is Lucent's again.
        StopControllingRemote(pause: true);
        LeaveRadio();
        try
        {
            _player.Load(entry.Track.Path);
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't play \"{entry.Track.Title}\". The file may be damaged or in a format Windows can't decode.", ex);
            return;
        }

        ForgetUpNext();
        _player.Play();
        ShowEntryPlaying(entry);
    }

    /// <summary>Shows <paramref name="entry"/> as the track playing, once the player has it.</summary>
    private void ShowEntryPlaying(PlaylistEntry entry)
    {
        if (_current is not null)
        {
            _current.IsCurrent = false;
        }

        _current = entry;
        _current.IsCurrent = true;
        if (!_shuffleHistory.Contains(entry))
        {
            _shuffleHistory.Add(entry);
        }

        _queue.PlaylistView.SelectedItem = entry;
        _queue.PlaylistView.ScrollIntoView(entry);

        ShowNowPlaying(entry.Track);
        OutputStatus.Text = DescribeOutput();
        UpdatePlayPauseButton();
        UpdatePosition();
        UpdateSongClock();
        FindBeats(entry.Track.Path);
    }

    private void OnTrackEnded()
    {
        if (_station is not null)
        {
            OnStationEnded();
            return;
        }

        if (_current is { } ended)
        {
            CountPlay(ended.Track.Path);
        }

        if (_settings.Repeat == RepeatMode.One && _current is not null)
        {
            PlayEntry(_current);
            return;
        }

        PlayNext(wrap: _settings.Repeat == RepeatMode.All);
    }

    private void PlayNext(bool wrap)
    {
        if (RemoteTakes(remote => remote.NextAsync()))
        {
            return;
        }

        if (_station is not null)
        {
            StepStation(+1);
            return;
        }

        // A recorded DJ set with its songs marked: Next goes to the next song in it first.
        if (SkipWithinTrack(forward: true))
        {
            return;
        }

        if (_playlist.Count == 0)
        {
            return;
        }

        // The track already lined up to follow is the one to play, so a shuffle doesn't pick twice.
        var next = _upNext is { RepeatsCurrent: false } lined ? lined : ChooseNext(wrap);
        if (next is null)
        {
            StopAtEnd();
            return;
        }

        if (next.StartsNewRound)
        {
            _shuffleHistory.Clear();
        }

        PlayEntry(next.Entry);
    }

    /// <summary>End of the queue: rewind the last track and stop.</summary>
    private void StopAtEnd()
    {
        _player.Stop();
        UpdatePlayPauseButton();
    }

    private void PlayPrevious()
    {
        if (RemoteTakes(remote => remote.PreviousAsync()))
        {
            return;
        }

        if (_station is not null)
        {
            StepStation(-1);
            return;
        }

        if (_playlist.Count == 0)
        {
            return;
        }

        if (SkipWithinTrack(forward: false))
        {
            return;
        }

        // Like most players: restart the track unless we're right at its start.
        if (_player.HasTrack && _player.Position > TimeSpan.FromSeconds(3))
        {
            _player.Position = TimeSpan.Zero;
            return;
        }

        if (_settings.Shuffle && _current is not null)
        {
            var at = _shuffleHistory.IndexOf(_current);
            if (at > 0)
            {
                var previous = _shuffleHistory[at - 1];
                _shuffleHistory.RemoveAt(at);
                PlayEntry(previous);
                return;
            }
        }

        var index = _current is null ? 0 : _playlist.IndexOf(_current);
        PlayEntry(_playlist[Math.Max(0, index - 1)]);
    }

    private void OnToggleShuffle(object sender, RoutedEventArgs e) => ToggleShuffle();

    private void ToggleShuffle()
    {
        // While another app is controlled, it's that app's shuffle.
        if (RemoteTakes(remote => remote.ToggleShuffleAsync()))
        {
            return;
        }

        _settings.Shuffle = !_settings.Shuffle;
        _shuffleHistory.Clear();
        if (_current is not null)
        {
            _shuffleHistory.Add(_current);
        }

        ForgetUpNext();
        UpdateShuffleRepeatButtons();
        _settings.Save();
    }

    private void OnCycleRepeat(object sender, RoutedEventArgs e) => CycleRepeat();

    private void CycleRepeat()
    {
        if (RemoteTakes(remote => remote.CycleRepeatAsync()))
        {
            return;
        }

        _settings.Repeat = _settings.Repeat switch
        {
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        ForgetUpNext();
        UpdateShuffleRepeatButtons();
        _settings.Save();
    }

    private void OnThumbUp(object sender, RoutedEventArgs e) => ThumbUp();

    private void OnThumbDown(object sender, RoutedEventArgs e) => ThumbDown();

    // Thumbs up and down, for an app that has them (Pandora). Nothing happens for one that doesn't.
    private void ThumbUp()
    {
        if (_remote is { HasRating: true })
        {
            RemoteTakes(remote => remote.ThumbUpAsync());
        }

        UpdateShuffleRepeatButtons();
    }

    private void ThumbDown()
    {
        if (_remote is { HasRating: true })
        {
            RemoteTakes(remote => remote.ThumbDownAsync());
        }

        UpdateShuffleRepeatButtons();
    }

    private void UpdateShuffleRepeatButtons()
    {
        // An app with thumbs and no shuffle or repeat (Pandora's stations) shows its thumbs in their place.
        var thumbs = _remote is { HasRating: true, HasShuffle: false, HasRepeat: false };
        ShuffleButton.Visibility = !thumbs ? Visibility.Visible : Visibility.Collapsed;
        RepeatButton.Visibility = !thumbs ? Visibility.Visible : Visibility.Collapsed;
        ThumbUpButton.Visibility = thumbs ? Visibility.Visible : Visibility.Collapsed;
        ThumbDownButton.Visibility = thumbs ? Visibility.Visible : Visibility.Collapsed;
        if (thumbs)
        {
            ThumbUpButton.IsChecked = _remote!.State?.Rating == SongRating.Up;
            ThumbDownButton.IsChecked = _remote.State?.Rating == SongRating.Down;
            ThumbUpButton.ToolTip = _remote.ThumbUpTip;
            ThumbDownButton.ToolTip = _remote.ThumbDownTip;
        }

        ShuffleButton.IsChecked = ShownShuffle;
        RepeatButton.IsChecked = ShownRepeat != RepeatMode.Off;
        RepeatOneBadge.Visibility = ShownRepeat == RepeatMode.One ? Visibility.Visible : Visibility.Collapsed;
        if (_remote is { } remote)
        {
            ShuffleButton.ToolTip = remote.ShuffleTip;
            RepeatButton.ToolTip = remote.RepeatTip;
            return;
        }

        ShuffleButton.ToolTip = _settings.Shuffle ? "Shuffle is on (S)" : "Shuffle (S)";
        RepeatButton.ToolTip = _settings.Repeat switch
        {
            RepeatMode.All => "Repeating the whole queue. Click to repeat just this track (R)",
            RepeatMode.One => "Repeating this track. Click to stop repeating (R)",
            _ => "Repeat is off. Click to repeat the whole queue (R)",
        };
    }

    // ---- Transport ----

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnStop(object sender, RoutedEventArgs e) => StopPlayback();

    private void StopPlayback()
    {
        // Stop means stop: a song or video playing in the editor stops too.
        AudioTools.StopEditors();

        if (RemoteTakes(remote => remote.StopAsync()))
        {
            return;
        }

        if (_station is not null)
        {
            StopStation();
            return;
        }

        _player.Stop();
        UpdatePlayPauseButton();
        UpdatePosition();
    }

    private void OnNext(object sender, RoutedEventArgs e) => PlayNext(wrap: true);

    private void OnPrevious(object sender, RoutedEventArgs e) => PlayPrevious();

    private void TogglePlayPause()
    {
        if (RemoteTakes(remote => remote.PlayPauseAsync()))
        {
            return;
        }

        // A station can't be paused and picked up later, so pausing stops it and playing tunes in again.
        if (_station is { } station)
        {
            if (_player.IsPlaying || _tuning is not null)
            {
                StopStation();
            }
            else
            {
                PlayStation(station, _radioList);
            }

            return;
        }

        if (!_player.HasTrack)
        {
            var entry = _queue.PlaylistView.SelectedItem as PlaylistEntry ?? _playlist.FirstOrDefault();
            if (entry is not null)
            {
                PlayEntry(entry);
            }

            return;
        }

        if (_player.IsPlaying)
        {
            _player.Pause();
        }
        else
        {
            _player.Play();
        }

        UpdatePlayPauseButton();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e) => HandleShortcut(e);

    /// <summary>The keyboard shortcuts, which work from any of Lucent's windows.</summary>
    public void HandleShortcut(KeyEventArgs e)
    {
        if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control)
        {
            EditInfoForSelection();
            e.Handled = true;
            return;
        }

        // Media keys work everywhere, even while typing.
        if (MediaCommandFor(e.Key) is { } media)
        {
            RunMediaCommand(media, fromKey: true);
            e.Handled = true;
            return;
        }

        // Typing in a text box (or using Ctrl/Alt shortcuts) isn't a shortcut for the player.
        if (e.OriginalSource is TextBoxBase || Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Alt)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                TogglePlayPause();
                break;
            case Key.Z:
                PlayPrevious();
                break;
            case Key.X:
                PlayNext(wrap: true);
                break;
            case Key.F11 when Keyboard.Modifiers == ModifierKeys.Shift:
                ToggleLucentFullScreen();
                break;
            case Key.F11:
                _visuals.ToggleFullscreen();
                break;
            case Key.Escape when _visuals.IsFullscreen:
                _visuals.ToggleFullscreen();
                break;
            case Key.N:
                _visuals.ChangePreset(+1);
                break;
            case Key.B:
                _visuals.PreviousPreset();
                break;
            case Key.V:
                _visuals.TogglePresetPicker();
                break;
            case Key.A:
                _visuals.ToggleAutoCycle();
                AutoCycleSwitch.IsChecked = _settings.AutoCycle;
                break;
            case Key.F:
                _visuals.ToggleFavorite();
                break;
            case Key.H:
                ToggleTrackFavorite();
                break;
            case Key.W:
                ToggleWindow(_visuals);
                break;
            case Key.Q:
                ToggleWindow(_queue);
                break;
            case Key.D:
                ArrangeWindows();
                break;
            case Key.G:
                FillScreen();
                break;
            case Key.K:
                ShrinkWindows();
                break;
            case Key.L:
                ToggleWindow(_libraryWindow);
                break;
            case Key.J:
                ToggleLink();
                break;
            case Key.S:
                ToggleShuffle();
                break;
            case Key.T when Keyboard.Modifiers == ModifierKeys.Shift:
                ThumbDown();
                break;
            case Key.T:
                ThumbUp();
                break;
            case Key.R:
                CycleRepeat();
                break;
            case Key.C:
                ToggleArtwork();
                break;
            case Key.E:
                ToggleEqualizer();
                break;
            case Key.Y:
                ToggleAnalyzer();
                break;
            case Key.P:
                ToggleVideo();
                break;
            case Key.O:
                Activate();
                ToggleOptions();
                break;
            case Key.M:
                Activate();
                ToggleSources(SourcesButton);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // The mouse wheel anywhere over the player changes the volume, like Winamp.
        if (OptionsPopup.IsOpen)
        {
            return;
        }

        VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(e.Delta) * 0.05, 0, 1);
        e.Handled = true;
    }

    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_showingVolume)
        {
            return;
        }

        // While Lucent controls another app, the bar is that app's volume (or does nothing, when it can't be changed).
        if (_remote is { } remote)
        {
            if (remote.HasVolume)
            {
                remote.SetVolume(e.NewValue);
                VolumeText.Text = $"{e.NewValue * 100:0}%";
            }
            else
            {
                ShowVolume();
            }

            return;
        }

        _player.Volume = (float)e.NewValue;
        _settings.Volume = e.NewValue;
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{e.NewValue * 100:0}%";
        }
    }

    private void OnSeekDragStarted(object sender, DragStartedEventArgs e) => _isDraggingSeek = true;

    private void OnSeekDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isDraggingSeek = false;
        SeekTo(TimeSpan.FromSeconds(SeekBar.Value));
    }

    private void OnSeekValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeekBar)
        {
            return;
        }

        if (_isDraggingSeek)
        {
            ShowTime(TimeSpan.FromSeconds(e.NewValue), ShownDuration);
            return;
        }

        // A click on the bar (rather than a drag) jumps straight there.
        SeekTo(TimeSpan.FromSeconds(e.NewValue));
    }

    // ---- Display ----

    private void OnTimeClick(object sender, MouseButtonEventArgs e) => ToggleRemainingTime();

    private void ToggleRemainingTime()
    {
        _settings.ShowRemainingTime = !_settings.ShowRemainingTime;
        _settings.Save();
        UpdatePosition();
    }

    private void OnMeterClick(object sender, MouseButtonEventArgs e) => CycleMeter();

    /// <summary>The meter's (and the classic player's visualizer's) next mode: bars, a scope line, or off.</summary>
    private void CycleMeter()
    {
        _settings.Meter = _settings.Meter switch
        {
            MeterMode.Bars => MeterMode.Scope,
            MeterMode.Scope => MeterMode.Off,
            _ => MeterMode.Bars,
        };
        _meter.Mode = _settings.Meter;
        _settings.Save();
    }

    private void UpdatePosition()
    {
        if (_isDraggingSeek)
        {
            return;
        }

        var duration = ShownDuration;
        var position = ShownPosition;

        _updatingSeekBar = true;
        SeekBar.IsEnabled = ShownHasTrack && duration > TimeSpan.Zero;
        SeekBar.Maximum = Math.Max(1, duration.TotalSeconds);
        SeekBar.Value = Math.Min(position.TotalSeconds, SeekBar.Maximum);
        _updatingSeekBar = false;

        ShowTime(position, duration);
        UpdatePlayState();
    }

    private void ShowTime(TimeSpan position, TimeSpan duration)
    {
        TimeText.Text = _settings.ShowRemainingTime && duration > TimeSpan.Zero
            ? "-" + TimeFormat.Format(duration - position)
            : TimeFormat.Format(position);
    }

    private void UpdatePlayPauseButton()
    {
        var playing = ShownIsPlaying;
        PlayPauseIcon.Data = (Geometry)FindResource(playing ? "PauseIcon" : "PlayIcon");
        // The play triangle looks centred when nudged slightly right.
        PlayPauseIcon.Margin = playing ? new Thickness(0) : new Thickness(2, 0, 0, 0);
        UpdatePlayState();
        UpdateMediaControls();
    }

    /// <summary>The little play / pause / stop mark next to the time.</summary>
    private void UpdatePlayState()
    {
        var icon = ShownIsPlaying ? "PlaySmallIcon" : ShownHasTrack && ShownPosition > TimeSpan.Zero ? "PauseIcon" : "StopIcon";
        PlayStateIcon.Data = (Geometry)FindResource(icon);
    }

    private void ShowNowPlaying(Track? track)
    {
        if (_remote is not null)
        {
            ShowRemote();
            UpdateMediaControls();
            return;
        }

        if (_station is not null)
        {
            ShowStation();
            UpdateMediaControls();
            return;
        }

        ArtworkWindow.ShowTrack(track);
        UpdateMediaControls();
        UpdateTrackFavoriteButton();
        if (track is null)
        {
            SetMarquee("Nothing playing");
            TrackDetails.Text = _playlist.Count == 0 ? "Drop music here, or open Sources to begin" : $"{_playlist.Count} in the queue";
            Title = "Lucent";
            return;
        }

        var name = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} – {track.Title}";
        var number = _current is null ? 0 : _playlist.IndexOf(_current) + 1;
        SetMarquee(number > 0 ? $"{number}. {name}" + (track.Duration > TimeSpan.Zero ? $" ({track.DurationText})" : string.Empty) : name);
        var details = track.AudioDetails;
        TrackDetails.Text = string.IsNullOrEmpty(details) ? Path.GetFileName(track.Path) : details;
        TrackDetails.ToolTip = track.Path;
        Title = $"{name} - Lucent";
    }

    /// <summary>Shows the title, scrolling it sideways when it's too long to fit.</summary>
    private void SetMarquee(string text)
    {
        NowPlayingText.Text = text;
        NowPlayingRepeat.Text = text;
        UpdateMarquee();
    }

    private void OnMarqueeSizeChanged(object sender, SizeChangedEventArgs e) => UpdateMarquee();

    private void UpdateMarquee()
    {
        MarqueeStrip.BeginAnimation(Canvas.LeftProperty, null);
        Canvas.SetLeft(MarqueeStrip, 0);

        NowPlayingText.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var textWidth = NowPlayingText.DesiredSize.Width;
        var room = MarqueeArea.ActualWidth;
        if (room <= 0 || textWidth <= room)
        {
            NowPlayingRepeat.Visibility = Visibility.Collapsed;
            return;
        }

        // Two copies side by side: once the first has scrolled out, the second is exactly where it started.
        // Each lap pauses at the start so the beginning of the title can be read.
        NowPlayingRepeat.Visibility = Visibility.Visible;
        NowPlayingRepeat.Margin = new Thickness(MarqueeGap, 0, 0, 0);
        var distance = textWidth + MarqueeGap;
        var pause = TimeSpan.FromSeconds(2);
        var lap = pause + TimeSpan.FromSeconds(distance / MarqueeSpeed);
        var scroll = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = lap };
        scroll.KeyFrames.Add(new DiscreteDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(pause)));
        scroll.KeyFrames.Add(new LinearDoubleKeyFrame(-distance, KeyTime.FromTimeSpan(lap)));
        MarqueeStrip.BeginAnimation(Canvas.LeftProperty, scroll);
    }

    private void ShowError(string message, Exception ex)
    {
        UpdatePlayPauseButton();
        MessageBox.Show(this, $"{message}\n\nDetails: {ex.Message}", "Lucent",
            MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    /// <summary>Opens the Bandcamp and Beatport window: importing downloads, and syncing a Bandcamp collection.</summary>
    public void ShowStores() => StoreWindow.Open(this, _stores, _settings);

    /// <summary>
    /// Puts a finished recording's folder into the library (from the Record window), listed under its one
    /// heading the first time, so the recording and the ones after it show up there.
    /// </summary>
    public void AddRecordingToLibrary(string path)
    {
        if (Path.GetDirectoryName(path) is not { Length: > 0 } folder)
        {
            return;
        }

        if (LibraryFolderList.LinkedFolderOf(_settings.LibraryFolders, folder) is null)
        {
            LibraryFolderList.SetTogether(_settings.LibraryFoldersTogether, folder, on: true);
        }

        LinkLibraryFolder(folder);
    }

    /// <summary>Puts a folder into the library, if it isn't in it already (the Bandcamp folder, say).</summary>
    private void LinkLibraryFolder(string folder)
    {
        var before = _settings.LibraryFolders.Count;
        var hiddenBefore = _settings.HiddenLibraryFolders.Count;
        LibraryFolderList.Add(_settings.LibraryFolders, _settings.HiddenLibraryFolders, folder);
        if (_settings.LibraryFolders.Count != before || _settings.HiddenLibraryFolders.Count != hiddenBefore)
        {
            _settings.Save();
            _library.SetFolders(_settings.LibraryFolders, _settings.HiddenLibraryFolders, _settings.LibraryFiles,
                _settings.LibraryFoldersTogether);
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        SavePlacements();
        IsShuttingDown = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        TrackInfoWindow.TagsSaved -= OnTagsSaved;
        ArtworkWindow.OpenChanged -= OnArtworkOpenChanged;
        AnalyzerWindow.OpenChanged -= OnAnalyzerOpenChanged;
        VideoWindow.OpenChanged -= OnVideoOpenChanged;
        _settings.CollapsedLibraryFolders = _libraryWindow.CollapsedFolders.ToList();
        _settings.Save();
        _library.Dispose();
        _stores.Dispose();
        _positionTimer.Stop();
        StopStreaming();
        StopRadio();
        CompositionTarget.Rendering -= OnMeterFrame;
        _visualizer.Dispose();
        _player.Dispose();
        _updater.InstallOnExit(restart: _restartIntoUpdate);
    }
}
