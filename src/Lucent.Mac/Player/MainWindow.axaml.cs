using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Editor;
using Lucent.Mac.Library;
using Lucent.Mac.Stores;
using Lucent.Mac.Themes;
using Lucent.Mac.Updates;
using Lucent.Mac.Visualizer;
using Lucent.Mac.Windows;
using Lucent.Audio;
using Lucent.Diagnostics;
using Lucent.Layout;
using Lucent.Library;
using Lucent.Playlist;
using Lucent.Settings;
using Lucent.Streaming;
using Lucent.Visualizer.Avs;
using Lucent.Visualizer.MilkDrop;
using Lucent.Video;
using Track = Lucent.Playlist.Track;

namespace Lucent.Mac;

/// <summary>
/// The player window, and the app's controller: it owns the audio, the queue and the
/// library, and the windows around it (visuals, queue, library) call back into it.
/// </summary>
public partial class MainWindow : Window
{
    private const double MarqueeGap = 48;
    private const double MarqueeSpeed = 36;

    /// <summary>What the HDR switch says when the screen isn't showing HDR.</summary>
    private const string HdrOffHint =
        "Your screen isn't showing HDR. Use an HDR or XDR display, with High Dynamic Range on in System Settings > Displays, to use this.";

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
    private readonly Stopwatch _marqueeClock = new();
    private DateTime _optionsClosedAt;
    private DisplayInfo _display = DisplayInfo.Standard;
    private PlaylistEntry? _current;
    private bool _isDraggingSeek;
    private bool _updatingSeekBar;
    private double _marqueeDistance;
    private double _wheel;
    private bool _closed;
    private bool _restartIntoUpdate;

    public MainWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "Lucent-64.png");
        if (File.Exists(icon))
        {
            var bitmap = new Bitmap(icon);
            Icon = new WindowIcon(bitmap);
            TitleIcon.Source = bitmap;
        }

        // The time's digits all take the same width, so it doesn't jiggle as it counts.
        try
        {
            TimeText.FontFeatures = new FontFeatureCollection { FontFeature.Parse("+tnum") };
        }
        catch (Exception)
        {
            // A font without tabular figures just keeps its own.
        }

        // The other windows are made after this one, so this stays the app's main window:
        // closing it closes Lucent.
        _visuals = new VisualizerWindow();
        _queue = new QueueWindow();
        _libraryWindow = new LibraryWindow();

        _snapper = new WindowSnapper(this);
        _snapper.MoveFinished += (_, _) => SavePlacements();
        _snapper.MoveFinished += (_, _) => OnPlayerResizeFinished();
        SnapEveryWindow();
        SizeChanged += OnPlayerWidthChanged;
        LucentPlayer.SizeChanged += (_, _) => KeepPlayerShape();
        Opened += (_, _) => KeepPlayerShape();

        _queue.PlaylistView.ItemsSource = _playlist;
        _playlist.CollectionChanged += (_, _) =>
        {
            ShowQueueSize();
            _shuffleHistory.RemoveAll(entry => !_playlist.Contains(entry));
            ForgetUpNext();
            ShowNowPlaying(_current?.Track);
            SaveQueueSoon();
        };

        VolumeSlider.Value = Math.Clamp(_settings.Volume, 0, 1);
        ApplyOwnVolume();
        _player.Equalizer.Apply(_settings.Equalizer);
        _player.Equalizer.Balance = _settings.Balance;
        EqualizerWindow.StreamingActionClicked += OnStreamingEqualizerAction;
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
            ObserveForScrobbling();
            FollowComputerVolume();
            WatchListening();
        };
        _positionTimer.Start();
        StartScrobbling();

        _meter = new SpectrumMeter(_player.Capture) { Mode = _settings.Meter };
        MeterHost.Child = _meter;

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
        SetUpTempoKey();

        foreach (var window in ToolWindows)
        {
            window.Player = this;
            window.PropertyChanged += (_, e) =>
            {
                if (e.Property == IsVisibleProperty)
                {
                    OnToolWindowVisibilityChanged();
                }
            };
        }

        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                OnPlayerWindowStateChanged(e.GetOldValue<WindowState>());
            }
        };

        // Keys and dropped music, from anywhere in the player.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        AddHandler(PointerWheelChangedEvent, OnPreviewMouseWheel, RoutingStrategies.Tunnel);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver, handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDrop, handledEventsToo: true);

        // Dragging the seek bar (its knob or its track) shows the time, and jumps there when let go.
        SeekBar.AddHandler(PointerPressedEvent, (_, _) => OnSeekDragStarted(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerReleasedEvent, (_, _) => OnSeekDragCompleted(), RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekBar.AddHandler(PointerCaptureLostEvent, (_, _) => OnSeekDragCompleted(), handledEventsToo: true);
        SeekBar.AddHandler(Thumb.DragStartedEvent, (_, _) => OnSeekDragStarted());
        SeekBar.AddHandler(Thumb.DragCompletedEvent, (_, _) => OnSeekDragCompleted());

        SetUpWindowPlaces();
        SetUpSavedLayouts();
        SetUpLucentFullScreen();
        SubmenuTips.Install();
        if (Saved("Player") is { } placement)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            this.MoveTo(placement.Left, placement.Top);
        }
        else
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        Opened += OnLoaded;
        SizeChanged += OnPlayerSizeChanged;
        Closing += OnClosing;
        Closed += OnClosed;
        UpdateShuffleRepeatButtons();
        TrackInfoWindow.TagsSaved += OnTagsSaved;
        ArtworkWindow.OpenChanged += OnArtworkOpenChanged;
        AnalyzerWindow.OpenChanged += OnAnalyzerOpenChanged;
        VideoWindow.OpenChanged += OnVideoOpenChanged;
        AudioTools.MainPlayerChanged += OnMainPlayerChanged;
        AudioTools.FilesReplaced += OnTagsSaved;
        BuildMenuBar();
        BuildIconControls();
        UpdatePlayPauseButton();
        ShowNowPlaying(null);
    }

    /// <summary>
    /// Once per screen frame: the meter moves as smoothly as the visuals (a timer would hold it to
    /// about 20 a second), and a long title scrolls.
    /// </summary>
    private void OnFrame(TimeSpan time)
    {
        if (_closed)
        {
            return;
        }

        if (WindowState != WindowState.Minimized)
        {
            if (!_previewing)
            {
                _meter.Tick();
            }

            StepMarquee();
        }

        TickClassicPlayer();

        RequestAnimationFrame(OnFrame);
    }

    private void OnMainPlayerChanged(object? sender, EventArgs e) => UpdatePlayPauseButton();

    /// <summary>True once the player is closing, so the other windows close for real instead of hiding.</summary>
    public bool IsShuttingDown { get; private set; }

    private IEnumerable<ToolWindow> ToolWindows => new ToolWindow[] { _visuals, _queue, _libraryWindow };

    // The player's own place on the screen, as WPF's Left, Top, ActualWidth and ActualHeight.
    private double Left
    {
        get => Placement.Left(this);
        set => Placement.SetLeft(this, value);
    }

    private double Top
    {
        get => Placement.Top(this);
        set => Placement.SetTop(this, value);
    }

    private double ActualWidth => Placement.ActualWidth(this);

    private double ActualHeight => Placement.ActualHeight(this);

    // ---- Windows ----

    private void OnLoaded(object? sender, EventArgs e)
    {
        _openingWindows = true;
        foreach (var window in ToolWindows)
        {
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
            ShowTool(_visuals);
        }

        // The queue and library open where they were left, or docked to the player the first time
        // (they keep the size they had).
        if (_settings.ShowQueue)
        {
            if (Saved(_queue.PlacementName) is null)
            {
                DockToPlayer(_queue);
            }

            ShowTool(_queue);
        }

        if (_settings.ShowLibrary)
        {
            if (Saved(_libraryWindow.PlacementName) is null)
            {
                DockToPlayer(_libraryWindow);
            }

            ShowTool(_libraryWindow);
        }

        // The equalizer opens where it was left (the first time, under the player, moving the queue down to make room).
        if (!firstTime && _settings.ShowEqualizer)
        {
            OpenEqualizer();
        }

        ReopenSmallWindows();

        _openingWindows = false;
        UpdateWindowToggles();
        AlignBesidePlayerSoon();
        // Closed full screen: it opens full screen again, laid out on the screen it's on now.
        TileSoon();
        ReopenEditor();
        Activate();
        RequestAnimationFrame(OnFrame);
        LayoutCheck.RunIfAsked(this, _visuals);

        _updater.UpdateReady += version => Dispatcher.UIThread.Post(() =>
        {
            ToolTip.SetTip(UpdateButton, UpdateButtonTip(version));
            UpdateButton.IsVisible = true;
            ShowUpdateStatus(ReadyStatus(version), readyToInstall: true);
        });
        _updater.Downloading += version => Dispatcher.UIThread.Post(() => ShowUpdateStatus($"Downloading Lucent {version}…", readyToInstall: false));
        _updater.EveryBuild = _settings.EveryBuild;
        _updater.InstallAutomatically = _settings.InstallUpdatesAutomatically;
        _ = _updater.RunAsync();
        RestoreQueue();
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
        // The screenshots' made-up queue stays as it is.
        if (!_settings.AddStarterMusic || _previewing)
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

    /// <summary>
    /// Shows one of the windows around the player. They belong to the player: they stay in front of
    /// it, minimize with it and stay out of the Dock's window list.
    /// </summary>
    private void ShowTool(Window window)
    {
        if (!window.IsVisible)
        {
            window.Show(this);
        }
    }

    // True while the windows are being opened at start-up, when one window showing mustn't count
    // as the others being switched off.
    private bool _openingWindows;

    /// <summary>
    /// The player's height can change after it opens (a skin with bigger type, say), and the
    /// windows stacked under it (the equalizer and the queue) follow its bottom edge so they stay
    /// docked, and the last one keeps its bottom edge where it was, in line with the windows beside it.
    /// </summary>
    private void OnPlayerSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (!e.HeightChanged || e.PreviousSize.Height <= 0 || WindowState != WindowState.Normal)
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
                   && !WindowLocks.IsLocked(window) && Math.Abs(window.Top() - edge) < 2
                   && window.Left() < Left + ActualWidth - 2 && window.Left() + Placement.SizeOf(window).Width > Left + 2) is { } next)
        {
            column.Add(next);
            edge = next.Top() + Placement.SizeOf(next).Height;
        }

        for (var i = 0; i < column.Count; i++)
        {
            var window = column[i];
            var height = Placement.SizeOf(window).Height;
            window.SetTop(window.Top() + delta);
            if (i == column.Count - 1 && window.CanResize)
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
        var work = Placement.WorkArea(this);
        var playerWidth = ActualWidth;
        var playerHeight = ActualHeight;
        var equalizer = OpenEqualizerWindow;
        var equalizerHeight = equalizer is null ? 0 : Placement.SizeOf(equalizer).Height;
        var queueHeight = Math.Clamp(Math.Min(work.Height * 0.36, work.Height - playerHeight - equalizerHeight - 40), _queue.MinHeight, Math.Max(_queue.MinHeight, 360));
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

        // Placed by hand from here on, so the player isn't centred again when it settles its size.
        WindowStartupLocation = WindowStartupLocation.Manual;
        MovePlayer(left + libraryWidth, top);
        if (equalizer is not null)
        {
            Place(equalizer, Left, top + playerHeight, playerWidth, equalizerHeight);
        }

        Place(_queue, Left, top + playerHeight + equalizerHeight, playerWidth, queueHeight);
        Place(_visuals, Left + playerWidth, top, visualsWidth, stackHeight);
        // A closed library waits on the player's left too, so it opens docked there rather than wherever it was.
        Place(_libraryWindow, Left - 360, top, 360, stackHeight);
    }

    /// <summary>Puts a window at that place and size, unless it's locked where it is.</summary>
    private static void Place(Window window, double left, double top, double width, double height)
    {
        if (!WindowLocks.Holds(window))
        {
            Placement.Place(window, left, top, width, height);
        }
    }

    private void RestorePlacement(ToolWindow window)
    {
        if (Saved(window.PlacementName) is { } placement)
        {
            Place(window, placement.Left, placement.Top, placement.Width, placement.Height);
            return;
        }

        // Never placed before (the library, usually): beside the player where there's room.
        var work = Placement.WorkArea(this);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        var size = Placement.SizeOf(window);
        var height = Math.Max(size.Height, ColumnTop - Top + (_queue.IsVisible ? _queue.ActualHeight() : 0));
        window.Height = Math.Min(height, work.Height);
        var windowLeft = Left - size.Width >= work.Left ? Left - size.Width : Math.Min(Left + ActualWidth, work.Right - size.Width);
        window.MoveTo(windowLeft, Math.Clamp(Top, work.Top, Math.Max(work.Top, work.Bottom - window.Height)));
    }

    /// <summary>Docks a window back against the player: the queue under it, the visuals to its right, the library to its left.</summary>
    private void PlaceBesidePlayer(ToolWindow window)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        if (window == _queue)
        {
            window.MoveTo(Left, ColumnTop);
            window.Width = ActualWidth;
        }
        else if (window == _visuals)
        {
            window.MoveTo(Left + ActualWidth, Top);
            window.Height = Math.Max(Placement.SizeOf(window).Height, ColumnTop - Top + (_queue.IsVisible ? _queue.ActualHeight() : 0));
        }
        else
        {
            window.MoveTo(Left - Placement.SizeOf(window).Width, Top);
        }
    }

    /// <summary>
    /// Docks the queue or library against the player: the queue underneath (under the equalizer, if
    /// that's docked there, or above the player if there's no room below), the library to the left,
    /// lined up with the player's top and the bottom of the column under it. Near the left of the screen
    /// the player and the windows docked to it move right to make room, and the library narrows if it
    /// still doesn't fit; only if it can't fit at all does it go on the right, past the visuals.
    /// </summary>
    private void DockToPlayer(ToolWindow window)
    {
        if (WindowLocks.Holds(window))
        {
            return;
        }

        var area = Placement.WorkArea(this);
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.WindowState = WindowState.Normal;
        var playerBottom = ColumnTop;

        if (window == _queue)
        {
            var height = Placement.SizeOf(window).Height;
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
            window.MoveTo(Left, below ? playerBottom : Top - height);
            return;
        }

        // The library runs the height of the player and the queue, if the queue's docked to it.
        var top = Top;
        var bottom = playerBottom;
        if (_queue.IsVisible && Math.Abs(_queue.Left() - Left) < 2)
        {
            if (Math.Abs(_queue.Top() - playerBottom) < 2)
            {
                bottom = _queue.Top() + _queue.ActualHeight();
            }
            else if (Math.Abs(_queue.Top() + _queue.ActualHeight() - Top) < 2)
            {
                top = _queue.Top();
            }
        }

        var width = Placement.SizeOf(window).Width;
        if (Left - width < area.Left)
        {
            // Make room on the left rather than open the library on the far side of the visuals.
            SlideDocked(area.Left - (Left - width));
            if (Left - width < area.Left && Left - area.Left >= window.MinWidth)
            {
                width = Left - area.Left;
                window.Width = width;
            }
        }

        var left = Left - width;
        if (left < area.Left)
        {
            // No room on the left even so: go on the right, beyond the visuals if they're docked there.
            left = Left + ActualWidth;
            if (_visuals.IsVisible && !_visuals.IsFullscreen && Math.Abs(_visuals.Left() - left) < 2)
            {
                left = _visuals.Left() + _visuals.ActualWidth();
            }

            if (left + width > area.Right)
            {
                left = Math.Max(area.Left, Left - width);
            }
        }

        window.MoveTo(left, top);
        window.Height = Math.Max(window.MinHeight, Math.Min(bottom - top, area.Height));
    }

    /// <summary>
    /// Moves the player and the windows docked to it up the screen by up to <paramref name="distance"/>,
    /// no further than the top of the screen, to make room under them.
    /// </summary>
    private void LiftDocked(double distance)
    {
        var area = Placement.WorkArea(this);
        var (group, others) = MovableGroup();
        var lift = group.Count == 0 ? 0 : Math.Min(distance, group.Min(window => window.Top()) - area.Top);
        if (lift > 0)
        {
            MoveGroup(group, others, 0, -lift);
        }
    }

    /// <summary>
    /// Moves the player and the windows docked to it right by up to <paramref name="distance"/>, no further
    /// than the right of the screen, to make room on their left.
    /// </summary>
    private void SlideDocked(double distance)
    {
        var area = Placement.WorkArea(this);
        var (group, others) = MovableGroup();
        var slide = group.Count == 0 ? 0 : Math.Min(distance, area.Right - group.Max(window => window.Left() + window.ActualWidth()));
        if (slide > 0)
        {
            MoveGroup(group, others, slide, 0);
        }
    }

    /// <summary>
    /// The player and the shown windows docked to it that aren't locked (none if the player can't move), and
    /// the other shown windows that belong to it.
    /// </summary>
    private (List<Window> Group, List<Window> Others) MovableGroup()
    {
        if (WindowState != WindowState.Normal || WindowLocks.IsLocked(this))
        {
            return (new List<Window>(), new List<Window>());
        }

        var group = new List<Window> { this };
        var others = OwnedWindows
            .Where(other => other.IsVisible && other.WindowState == WindowState.Normal
                && !(other == _visuals && _visuals.IsFullscreen))
            .ToList();
        for (var i = 0; i < group.Count; i++)
        {
            foreach (var other in others.ToList())
            {
                // A locked window stays where it is, and goes back there once the player has moved.
                if (!WindowLocks.IsLocked(other) && Touches(Placement.BoundsOf(group[i]), Placement.BoundsOf(other)))
                {
                    group.Add(other);
                    others.Remove(other);
                }
            }
        }

        return (group, others);
    }

    private void MoveGroup(List<Window> group, List<Window> others, double dx, double dy)
    {
        // The player first: on the Mac the windows that belong to it move with it, so each of the
        // others is then put where it belongs (and the ones not docked to it back where they were).
        var before = others.Select(other => (Window: other, other.Position)).ToList();
        var targets = group.Skip(1).Select(window => (Window: window, Left: window.Left() + dx, Top: window.Top() + dy)).ToList();
        this.MoveTo(Left + dx, Top + dy);
        foreach (var (window, left, top) in targets)
        {
            window.MoveTo(left, top);
        }

        foreach (var (window, position) in before)
        {
            window.Position = position;
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
        var area = Placement.WorkArea(this);
        var shown = new[] { (Window)this }.Concat(OwnedWindows)
            .Where(other => other != window && !moving.Contains(other) && other.IsVisible && other.WindowState == WindowState.Normal
                && !(other == _visuals && _visuals.IsFullscreen))
            .Select(Placement.BoundsOf)
            .ToList();

        // The player and everything docked to it, and the bottom of the column under the player.
        var player = Placement.BoundsOf(this);
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

        var bounds = group.Aggregate((a, b) => a.Union(b));
        var columnBottom = group.Where(rect => Math.Abs(rect.Left - player.Left) < 2).Max(rect => rect.Bottom);

        var want = Placement.SizeOf(window);
        var resizable = window.CanResize;
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
        var both = a.Intersect(b);
        return both.Width > 2 && both.Height > 2;
    }

    private bool IsDockedLeft(Window window) =>
        Math.Abs(window.Left() + window.ActualWidth() - Left) < 2 && window.Top() < Top + ActualHeight && window.Top() + window.ActualHeight() > Top;

    /// <summary>
    /// Widens the library to <paramref name="width"/>, for a tag column it's just been given. Docked on the
    /// player's left, or with any other window against its right edge, it grows to the left, so it stays
    /// docked instead of sliding over that window and coming loose. Near the left of the screen the player's
    /// group moves right to make room, and past that it grows only as far as fits. A locked library keeps its size.
    /// </summary>
    public void WidenLibrary(double width)
    {
        var library = _libraryWindow;
        var current = library.ActualWidth();
        if (width <= current || WindowLocks.IsLocked(library))
        {
            return;
        }

        if (library.WindowState != WindowState.Normal || !HasWindowOnRight(library))
        {
            library.Width = width;
            return;
        }

        var area = Placement.WorkArea(library);
        if (IsDockedLeft(library) && library.Left() - (width - current) < area.Left)
        {
            SlideDocked(area.Left - (library.Left() - (width - current)));
        }

        var right = library.Left() + current;
        var left = Math.Max(area.Left, right - width);
        library.MoveTo(left, library.Top());
        library.Width = right - left;
    }

    /// <summary>True when the player or another of its windows sits against this window's right edge.</summary>
    private bool HasWindowOnRight(Window window)
    {
        var right = window.Left() + window.ActualWidth();
        return OwnedWindows.Append(this).Any(other => other != window && other.IsVisible && other.WindowState == WindowState.Normal
            && Math.Abs(other.Left() - right) < 2
            && other.Top() < window.Top() + window.ActualHeight() && other.Top() + other.ActualHeight() > window.Top());
    }

    private void OnArrangeWindows(object? sender, RoutedEventArgs e) => ArrangeWindows();

    /// <summary>Puts every window back together around the player, as on the first run, and remembers it.</summary>
    private void ArrangeWindows()
    {
        LeaveLucentFullScreen(putBack: false);
        RestoreAllWindows();
        ArrangeFirstTime();
        SavePlacements();
        // Once the arranged windows have their new sizes, so the spots beside them are worked out from where they are now.
        Dispatcher.UIThread.Post(RedockSmallWindows, DispatcherPriority.Background);
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

    private void OnFillScreen(object? sender, RoutedEventArgs e) => FillScreen();

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

    private void OnShrinkWindows(object? sender, RoutedEventArgs e) => ShrinkWindows();

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
            equalizerHeight = Placement.SizeOf(equalizer).Height;
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

    private bool IsOnScreen(Window window) => IsOnScreen(Placement.BoundsOf(window));

    /// <summary>True when enough of the rectangle is on the screens to grab its title bar.</summary>
    private bool IsOnScreen(Rect bounds)
    {
        if (double.IsNaN(bounds.Left) || double.IsNaN(bounds.Top) || double.IsNaN(bounds.Width) || double.IsNaN(bounds.Height))
        {
            return false;
        }

        // On any one of the screens (they needn't make a rectangle together, as they do on Windows).
        foreach (var screen in Screens.All)
        {
            var scale = Placement.Scale(this);
            var area = new Rect(screen.Bounds.X / scale, screen.Bounds.Y / scale, screen.Bounds.Width / scale, screen.Bounds.Height / scale);
            var visible = area.Intersect(bounds);
            if (visible.Width >= 100 && visible.Height >= 40)
            {
                return true;
            }
        }

        return Screens.All.Count == 0;
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
            if (WindowLocks.NameOf(window) is { } name)
            {
                Remember(name, window, docked);
            }
        }

        _settings.Save();
    }

    /// <summary>Remembers where a window is, and whether it's docked to the player (it touches one of the <paramref name="docked"/> windows).</summary>
    private void Remember(string name, Window window, List<Rect> docked)
    {
        // Only a window that has been shown has a place of its own; a minimized or fullscreen one keeps
        // the place it had before.
        if (window.Bounds.Width <= 0 || window.WindowState != WindowState.Normal)
        {
            return;
        }

        var bounds = Placement.BoundsOf(window);
        if (bounds.Width > 0 && bounds.Height > 0)
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

    private void OnToggleEqualizer(object? sender, RoutedEventArgs e) => ToggleEqualizer();

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

    private void OnToggleAnalyzer(object? sender, RoutedEventArgs e) => ToggleAnalyzer();

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
            return OpenEqualizerWindow is { } equalizer && Math.Abs(equalizer.Left() - Left) < 2 && Math.Abs(equalizer.Top() - playerBottom) < 2
                ? playerBottom + Placement.SizeOf(equalizer).Height
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
                && Math.Abs(_queue.Left() - Left) < 2 && Math.Abs(_queue.Top() - top) < 2
                ? top + Placement.SizeOf(_queue).Height
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

            var width = Placement.SizeOf(window).Width;
            var beside = Math.Abs(window.Left() - (Left + ActualWidth)) < 2 || Math.Abs(window.Left() + width - Left) < 2;
            if (beside && Math.Abs(window.Top() - Top) < 2)
            {
                window.Height = Math.Max(window.MinHeight, height);
            }
        }
    }

    // Once the windows that just opened or closed have settled their sizes.
    private void AlignBesidePlayerSoon() =>
        Dispatcher.UIThread.Post(AlignBesidePlayer, DispatcherPriority.ContextIdle);

    /// <summary>
    /// Docks the equalizer right under the player, the same width, like Winamp's. A queue docked
    /// under the player moves down beneath it, keeping its bottom edge where it was if it can, and
    /// moves back up when the equalizer closes. With no room under the player it docks beside the
    /// windows instead.
    /// </summary>
    public void DockEqualizer(Window equalizer)
    {
        equalizer.Closing += (_, e) =>
        {
            // Only when it's closed on its own: closing with the player leaves the queue where it was.
            if (e.CloseReason == WindowCloseReason.WindowClosing)
            {
                CloseEqualizerGap(equalizer);
            }
        };

        // It goes back where it was left (opening with the player, where it was when Lucent last closed).
        if (OpenWhereLeft(equalizer))
        {
            _snapper.Add(equalizer);
            RememberWhenClosed(equalizer);
            AlignBesidePlayerSoon();
            return;
        }

        var area = Placement.WorkArea(this);
        var playerBottom = Top + ActualHeight;
        var height = Placement.SizeOf(equalizer).Height;
        var queueBelow = _queue.IsVisible && Math.Abs(_queue.Left() - Left) < 2 && Math.Abs(_queue.Top() - playerBottom) < 2;
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
        if (!_queue.IsVisible || WindowLocks.IsLocked(_queue) || Math.Abs(_queue.Left() - Left) >= 2 || Math.Abs(_queue.Top() - playerBottom) >= 2)
        {
            return;
        }

        var bottom = _queue.Top() + _queue.ActualHeight();
        var queueTop = playerBottom + height;
        var newBottom = Math.Min(area.Bottom, Math.Max(bottom, queueTop + _queue.MinHeight));
        _queue.SetTop(queueTop);
        _queue.Height = Math.Max(_queue.MinHeight, newBottom - queueTop);

        // If the queue had to grow past the bottom of the windows beside it, they grow to match.
        if (newBottom > bottom + 1)
        {
            foreach (var window in new Window[] { _visuals, _libraryWindow })
            {
                if (window.IsVisible && window.WindowState == WindowState.Normal && !WindowLocks.IsLocked(window)
                    && Math.Abs(window.Top() + window.ActualHeight() - bottom) < 2)
                {
                    window.Height = newBottom - window.Top();
                }
            }
        }
    }

    // When the equalizer under the player closes, the queue under it moves up to the player again.
    private void CloseEqualizerGap(Window equalizer)
    {
        AlignBesidePlayerSoon();
        var top = equalizer.Top();
        var bottom = top + equalizer.ActualHeight();
        if (IsShuttingDown || Math.Abs(top - (Top + ActualHeight)) >= 2 || Math.Abs(equalizer.Left() - Left) >= 2
            || !_queue.IsVisible || WindowLocks.IsLocked(_queue) || Math.Abs(_queue.Left() - Left) >= 2 || Math.Abs(_queue.Top() - bottom) >= 2)
        {
            return;
        }

        var queueBottom = _queue.Top() + _queue.ActualHeight();
        _queue.SetTop(top);
        _queue.Height = queueBottom - top;
    }

    private void OnToggleVisuals(object? sender, RoutedEventArgs e) => ToggleWindow(_visuals);

    private void OnToggleQueue(object? sender, RoutedEventArgs e) => ToggleWindow(_queue);

    private void OnToggleLibrary(object? sender, RoutedEventArgs e) => ToggleWindow(_libraryWindow);

    private void ToggleWindow(ToolWindow window)
    {
        if (window.IsVisible)
        {
            window.Hide();
            return;
        }

        if (window.Bounds.Width <= 0 && Saved(window.PlacementName) is null)
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

        ShowTool(window);

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

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>
    /// The windows put away while the player is in the Dock, to bring back with it. As on Windows,
    /// the windows the player owns (visuals, queue, library, equalizer and the rest) go with it.
    /// macOS won't send a window that belongs to another to the Dock, so they're taken off the
    /// screen instead, natively: to Avalonia (and the saved settings) they're still open.
    /// </summary>
    private readonly List<Window> _awayWithPlayer = new();

    private void OnPlayerWindowStateChanged(WindowState previous)
    {
        if (WindowState == WindowState.Minimized)
        {
            foreach (var window in OwnedWindows.Where(window => window.IsVisible).ToList())
            {
                if (NativeWindow(window) is var native && native != IntPtr.Zero)
                {
                    _awayWithPlayer.Add(window);
                    Interop.ObjC.SendVoid(native, "orderOut:", IntPtr.Zero);
                }
            }
        }
        else if (previous == WindowState.Minimized)
        {
            foreach (var window in _awayWithPlayer.Where(window => window.IsVisible))
            {
                if (NativeWindow(window) is var native && native != IntPtr.Zero)
                {
                    Interop.ObjC.SendVoid(native, "orderFront:", IntPtr.Zero);
                }
            }

            _awayWithPlayer.Clear();
            Activate();
        }
    }

    private static IntPtr NativeWindow(Window window) =>
        window.TryGetPlatformHandle() is Avalonia.Platform.IMacOSTopLevelPlatformHandle { NSWindow: var native } ? native : IntPtr.Zero;

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    private void OnInstallUpdate(object? sender, RoutedEventArgs e)
    {
        // Shutting down before closing, so the windows around the player close with it instead of
        // hiding (which would stop the player closing, and save them as closed for after the restart).
        // The update is handed over once the player has closed, with a restart; the choice stays made
        // until it closes, including after something such as unsaved edits holds the close up.
        _restartIntoUpdate = true;
        IsShuttingDown = true;
        Close();
        if (!_closed)
        {
            IsShuttingDown = false;
        }
    }

    private void OnBuyMeACoffee(object? sender, RoutedEventArgs e)
    {
        OptionsPopup.IsOpen = false;
        SupportLink.Open();
    }

    private void OnReportBug(object? sender, RoutedEventArgs e)
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
        Lucent.Visualizer.SongLook.On = _settings.BuildUpsAndDrops;
        _visualizer.Pointer.On = _settings.MouseMovesParticles;
        _visualizer.Hdr = _settings.Hdr;
        _visualizer.AutoCycle = _settings.AutoCycle;
        _visualizer.AutoCycleSeconds = _settings.AutoCycleSeconds;
        _visualizer.TransitionSeconds = GraphicsOptions.Seconds(_settings.TransitionSpeed);
        _visualizer.MorphTransitionsOnly = _settings.MorphTransitionsOnly;
        _visuals.ShowFrameRate(_settings.ShowFrameRate);
        ApplyPower();
        _visuals.UpdateAutoCycleButton();
        _visuals.UpdatePreviousButton();
        AutoCycleSwitch.IsChecked = _settings.AutoCycle;
    }

    private void OnToggleOptions(object? sender, RoutedEventArgs e) => ToggleOptions();

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
        if (UpdatesTab.IsVisible)
        {
            ShowDefaultPlayerState();
        }

        // The panel's right edge lines up with the button's (Placement and HorizontalOffset in the XAML
        // allow for the panel's 16px margin, which makes room for its shadow).
        OptionsPopup.IsOpen = true;
    }

    private void OnOptionsOpened(object? sender, EventArgs e) => OptionsButton.IsChecked = true;

    private void OnOptionsClosed(object? sender, EventArgs e)
    {
        OptionsButton.IsChecked = false;
        _optionsClosedAt = DateTime.UtcNow;
    }

    private void OnOptionsTab(object? sender, RoutedEventArgs e)
    {
        var tab = (sender as RadioButton)?.Tag as string;
        GraphicsTab.IsVisible = tab == "Graphics";
        VisualsTab.IsVisible = tab == "Visuals";
        SoundTab.IsVisible = tab == "Sound";
        ThemeTab.IsVisible = tab == "Theme";
        UpdatesTab.IsVisible = tab == "Updates";
        LastfmTab.IsVisible = tab == "Lastfm";
        LibraryTab.IsVisible = tab == "Library";
        if (tab == "Updates")
        {
            ShowDefaultPlayerState();
        }
    }

    /// <summary>Opens Options on its Updates tab.</summary>
    private void ShowUpdatesTab()
    {
        Activate();
        if (!OptionsPopup.IsOpen && !_visuals.IsFullscreen)
        {
            ShowOptions();
        }

        UpdatesTabButton.IsChecked = true;
        OnOptionsTab(UpdatesTabButton, new RoutedEventArgs());
    }

    /// <summary>A choice in one of the option rows (resolution, detail, glow, timing, skin, theme, updates).</summary>
    private void OnOptionChosen(object? sender, RoutedEventArgs e)
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
            case "Power" when Enum.TryParse<PowerMode>(value, out var power):
                _settings.Power = power;
                break;
            case "AutoCycleSeconds" when int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds):
                _settings.AutoCycleSeconds = seconds;
                break;
            case "TransitionSpeed" when Enum.TryParse<TransitionSpeed>(value, out var speed):
                _settings.TransitionSpeed = speed;
                break;
            case "PreviousVisual" when Enum.TryParse<Lucent.Visualizer.PreviousVisualMode>(value, out var previous):
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
                UpdateButton.IsVisible = false;
                ShowUpdateStatus(null, readyToInstall: false);
                return;
            default:
                return;
        }

        ApplyVisualizerSettings();
        _settings.Save();
    }

    /// <summary>One of the on/off switches in the options panel.</summary>
    private void OnOptionSwitched(object? sender, RoutedEventArgs e)
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
        else if (sender == MouseParticlesSwitch)
        {
            _settings.MouseMovesParticles = on;
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
                ToolTip.SetTip(UpdateButton, UpdateButtonTip(ready));
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
        foreach (var choice in OptionsPanel.GetLogicalDescendants().OfType<RadioButton>())
        {
            var current = choice.GroupName switch
            {
                "Resolution" => _settings.Resolution.ToString(),
                "Detail" => _settings.Detail.ToString(),
                "Glow" => _settings.Glow.ToString(),
                "Power" => _settings.Power.ToString(),
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
        MouseParticlesSwitch.IsChecked = _settings.MouseMovesParticles;
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
        ShowTempoKeyState();
        ShowLastfmState();
    }

    /// <summary>Applies the chosen theme and skin to every window.</summary>
    private void ApplyLook()
    {
        var theme = ThemeManager.Find(_settings.Theme);
        SkinManager.Apply(SkinManager.Find(_settings.Skin), theme);
        _meter.InvalidateVisual();
        // Rebuilt afterwards rather than now, as this can run from a click on one of the skin previews.
        Dispatcher.UIThread.Post(() => BuildSkinSwatches(theme), DispatcherPriority.Input);
        // The skin may have changed the title's font, so its width (and whether it scrolls) may have changed too.
        Dispatcher.UIThread.Post(UpdateMarquee, DispatcherPriority.Loaded);
    }

    /// <summary>The skin picker. Each skin is previewed in the current theme's colours, so it's rebuilt when the theme changes.</summary>
    private void BuildSkinSwatches(Theme theme)
    {
        SkinSwatches.Children.Clear();
        foreach (var skin in SkinManager.Skins)
        {
            var swatch = new RadioButton
            {
                Theme = (ControlTheme)this.FindResource("ThemeSwatch")!,
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
                Theme = (ControlTheme)this.FindResource("ThemeSwatch")!,
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
            ? HdrOffHint
            : _settings.Hdr
                ? $"On. Highlights can reach about {_display.PeakNits:0} nits on this screen."
                : "Your screen is showing HDR, so highlights can shine far brighter than normal white.";
    }

    // ---- Adding music ----

    public async void AddFilesFromDialog(Window owner)
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Add music and videos",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Music and videos")
                {
                    Patterns = AudioReaderFactory.SupportedExtensions.Concat(VideoFiles.Extensions).Distinct().Select(ext => "*" + ext).ToList(),
                },
                new FilePickerFileType("Music files") { Patterns = AudioReaderFactory.SupportedExtensions.Select(ext => "*" + ext).ToList() },
                new FilePickerFileType("Videos") { Patterns = VideoFiles.Extensions.Select(ext => "*" + ext).ToList() },
                FilePickerFileTypes.All,
            },
        });

        if (files.Count > 0)
        {
            await AddPathsAsync(files.Select(file => file.TryGetLocalPath()).OfType<string>(), owner);
        }
    }

    public async void AddFolderFromDialog(Window owner)
    {
        var folders = await owner.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add a music folder", AllowMultiple = true });
        if (folders.Count > 0)
        {
            await AddPathsAsync(folders.Select(folder => folder.TryGetLocalPath()).OfType<string>(), owner);
        }
    }

    /// <summary>Music dropped on the queue (<paramref name="over"/>): added to the end of it.</summary>
    public async void AddDroppedPaths(IEnumerable<string> paths, Control? over = null) => await AddPathsAsync(TakeDroppedSkins(paths), over);

    /// <summary>Music dropped on the player (or the visuals): it replaces the queue and plays now, as in Winamp.</summary>
    public async void PlayDroppedPaths(IEnumerable<string> paths, Control? over = null)
    {
        // A Winamp skin dropped with (or instead of) the music is put on, not played.
        var input = TakeDroppedSkins(paths);
        if (input.Count == 0)
        {
            return;
        }

        if (await LoadPathsAsync(input, over, "Playing") is { } tracks)
        {
            ReplaceQueue(tracks, 0);
        }
    }

    public void ClearQueue()
    {
        // A radio station isn't in the queue, so it plays on.
        if (_station is null)
        {
            _player.Close();
        }

        _current = null;
        _libraryWindow.ShowLocalPlaying(null);
        _playlist.Clear();
        _shuffleHistory.Clear();
        ShowNowPlaying(null);
        UpdatePlayPauseButton();
    }

    private static void OnDragOver(object? sender, DragEventArgs e) => DroppedMusic.Accept(e);

    private void OnDrop(object? sender, DragEventArgs e)
    {
        if (DroppedMusic.Paths(e) is { Count: > 0 } paths)
        {
            PlayDroppedPaths(paths, this);
        }
    }

    /// <summary>Adds files and (recursively) folders, reading tags off the UI thread.</summary>
    private async Task AddPathsAsync(IEnumerable<string> paths, Control? over = null)
    {
        if (await LoadPathsAsync(paths.ToList(), over, "Added") is { } tracks)
        {
            AddToQueue(tracks);
        }
    }

    /// <summary>Files and folders being read now, so the same folder dropped again while it loads is only added once.</summary>
    private readonly HashSet<string> _loadingPaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Reads the tracks in files and folders off the UI thread. A folder can hold thousands of songs and take a while,
    /// so a note over <paramref name="over"/> (the window they were dropped on, or the player) says how far it has
    /// got, then how many were added, "<paramref name="done"/> 1,204 songs". Null when every path is already being read.
    /// </summary>
    private async Task<List<Track>?> LoadPathsAsync(IReadOnlyList<string> paths, Control? over, string done)
    {
        var input = new List<string>();
        var keys = new List<string>();
        foreach (var path in paths)
        {
            var key = MusicFiles.Key(path);
            if (_loadingPaths.Add(key))
            {
                input.Add(path);
                keys.Add(key);
            }
        }

        if (input.Count == 0)
        {
            // Dropped again while it's still being read: its note is already up, so nothing more to do.
            return null;
        }

        Toast.Busy? note = null;
        try
        {
            over ??= this;
            Action<MusicLoadProgress>? report = null;
            if (MusicFiles.AnyFolder(input))
            {
                var busy = Toast.ShowBusy(over, new MusicLoadProgress(0, 0, false).Text);
                report = progress => Dispatcher.UIThread.Post(() => busy.Update(progress.Text, progress.Fraction));
                note = busy;
            }

            var tracks = await Task.Run(() => MusicFiles.Load(input, report));
            if (note is not null)
            {
                note.Close();
                Toast.Show(over, tracks.Count switch
                {
                    0 => "No music found there",
                    1 => $"{done} 1 song",
                    _ => $"{done} {tracks.Count:N0} songs",
                });
            }

            return tracks;
        }
        finally
        {
            note?.Close();
            foreach (var key in keys)
            {
                _loadingPaths.Remove(key);
            }
        }
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

    /// <summary>Shows a file (or folder) in Finder, selected.</summary>
    public void ShowInExplorer(string path)
    {
        try
        {
            var start = new ProcessStartInfo("open") { UseShellExecute = false };
            start.ArgumentList.Add("-R");
            start.ArgumentList.Add(path);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open Finder.", ex);
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
                _libraryWindow.ShowLocalPlaying(null);
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
            ShowError($"Couldn't play \"{entry.Track.Title}\". The file may be damaged or in a format macOS can't decode.", ex);
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
        SaveQueueSoon();
        if (!_shuffleHistory.Contains(entry))
        {
            _shuffleHistory.Add(entry);
        }

        _queue.PlaylistView.SelectedItem = entry;
        _queue.PlaylistView.ScrollIntoView(entry);
        _libraryWindow.ShowLocalPlaying(entry.Track.Path);

        ShowNowPlaying(entry.Track);
        OutputStatus.Text = DescribeOutput();
        UpdatePlayPauseButton();
        UpdatePosition();
        UpdateSongClock();
        FindBeats(entry.Track.Path);
        _tempoKey?.Played(entry.Track.Path);
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

    private void OnToggleShuffle(object? sender, RoutedEventArgs e) => ToggleShuffle();

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

    private void OnCycleRepeat(object? sender, RoutedEventArgs e) => CycleRepeat();

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

    private void OnThumbUp(object? sender, RoutedEventArgs e) => ThumbUp();

    private void OnThumbDown(object? sender, RoutedEventArgs e) => ThumbDown();

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
        ShuffleButton.IsVisible = !thumbs;
        RepeatButton.IsVisible = !thumbs;
        ThumbUpButton.IsVisible = thumbs;
        ThumbDownButton.IsVisible = thumbs;
        if (thumbs)
        {
            ThumbUpButton.IsChecked = _remote!.State?.Rating == SongRating.Up;
            ThumbDownButton.IsChecked = _remote.State?.Rating == SongRating.Down;
            ToolTip.SetTip(ThumbUpButton, _remote.ThumbUpTip);
            ToolTip.SetTip(ThumbDownButton, _remote.ThumbDownTip);
        }

        ShuffleButton.IsChecked = ShownShuffle;
        RepeatButton.IsChecked = ShownRepeat != RepeatMode.Off;
        RepeatOneBadge.IsVisible = ShownRepeat == RepeatMode.One;
        if (_remote is { } remote)
        {
            ToolTip.SetTip(ShuffleButton, remote.ShuffleTip);
            ToolTip.SetTip(RepeatButton, remote.RepeatTip);
            return;
        }

        ToolTip.SetTip(ShuffleButton, _settings.Shuffle ? "Shuffle is on (S)" : "Shuffle (S)");
        ToolTip.SetTip(RepeatButton, _settings.Repeat switch
        {
            RepeatMode.All => "Repeating the whole queue. Click to repeat just this track (R)",
            RepeatMode.One => "Repeating this track. Click to stop repeating (R)",
            _ => "Repeat is off. Click to repeat the whole queue (R)",
        });
    }

    // ---- Transport ----

    private void OnPlayPause(object? sender, RoutedEventArgs e) => TogglePlayPause();

    private void OnStop(object? sender, RoutedEventArgs e) => StopPlayback();

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

    private void OnNext(object? sender, RoutedEventArgs e) => PlayNext(wrap: true);

    private void OnPrevious(object? sender, RoutedEventArgs e) => PlayPrevious();

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

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e) => HandleShortcut(e);

    /// <summary>The keyboard shortcuts, which work from any of Lucent's windows.</summary>
    public void HandleShortcut(KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.I && e.KeyModifiers == KeyModifiers.Meta)
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

        // Typing in a text box (or using ⌘, ⌃ or ⌥ shortcuts) isn't a shortcut for the player.
        if (e.Source is TextBox || e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control or KeyModifiers.Alt)
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
            case Key.F11 when e.KeyModifiers == KeyModifiers.Shift:
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
            case Key.D when e.KeyModifiers == KeyModifiers.Shift:
                TidyLayout();
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
            case Key.T when e.KeyModifiers == KeyModifiers.Shift:
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
            case var key when e.KeyModifiers == KeyModifiers.None && LayoutNumber(key) is { } number
                && SavedLayouts.ForKey(_settings.Layouts, number) is { } layout:
                OpenLayout(layout);
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnPreviewMouseWheel(object? sender, PointerWheelEventArgs e)
    {
        // Scrolling anywhere over the player changes the volume, like Winamp's mouse wheel.
        if (OptionsPopup.IsOpen)
        {
            return;
        }

        // A mouse wheel moves a whole step a notch; a trackpad sends many small amounts, which add up to steps.
        _wheel += e.Delta.Y;
        var steps = (int)Math.Truncate(_wheel);
        if (steps != 0)
        {
            _wheel -= steps;
            VolumeSlider.Value = Math.Clamp(VolumeSlider.Value + Math.Sign(steps) * 0.05 * Math.Min(Math.Abs(steps), 3), 0, 1);
        }

        e.Handled = true;
    }

    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
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

        // With the visuals listening to every app, the bar is the Mac's volume (see MainWindow.ComputerVolume.cs).
        if (BarIsComputerVolume)
        {
            ComputerVolume.Set(e.NewValue);
            VolumeText.Text = $"{e.NewValue * 100:0}%";
            return;
        }

        _player.Volume = (float)e.NewValue;
        _settings.Volume = e.NewValue;
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{e.NewValue * 100:0}%";
        }
    }

    private void OnSeekDragStarted()
    {
        if (SeekBar.IsEnabled)
        {
            _isDraggingSeek = true;
        }
    }

    private void OnSeekDragCompleted()
    {
        if (!_isDraggingSeek)
        {
            return;
        }

        _isDraggingSeek = false;
        SeekTo(TimeSpan.FromSeconds(SeekBar.Value));
    }

    private void OnSeekValueChanged(object? sender, RangeBaseValueChangedEventArgs e)
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

    private void OnTimeClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            ToggleRemainingTime();
        }
    }

    private void ToggleRemainingTime()
    {
        _settings.ShowRemainingTime = !_settings.ShowRemainingTime;
        _settings.Save();
        UpdatePosition();
    }

    private void OnMeterClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left)
        {
            CycleMeter();
        }
    }

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
        PlayPauseIcon.Data = (Geometry)this.FindResource(playing ? "PauseIcon" : "PlayIcon")!;
        // The play triangle looks centred when nudged slightly right.
        PlayPauseIcon.Margin = playing ? new Thickness(0) : new Thickness(2, 0, 0, 0);
        UpdatePlayState();
        UpdateMediaControls();
        UpdateIconControls();
    }

    /// <summary>The little play / pause / stop mark next to the time.</summary>
    private void UpdatePlayState()
    {
        var icon = ShownIsPlaying ? "PlaySmallIcon" : ShownHasTrack && ShownPosition > TimeSpan.Zero ? "PauseIcon" : "StopIcon";
        PlayStateIcon.Data = (Geometry)this.FindResource(icon)!;
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
            ToolTip.SetTip(TrackDetails, null);
            Title = "Lucent";
            return;
        }

        var name = string.IsNullOrEmpty(track.Artist) ? track.Title : $"{track.Artist} – {track.Title}";
        var number = _current is null ? 0 : _playlist.IndexOf(_current) + 1;
        SetMarquee(number > 0 ? $"{number}. {name}" + (track.Duration > TimeSpan.Zero ? $" ({track.DurationText})" : string.Empty) : name);
        var details = track.AudioDetails;
        TrackDetails.Text = string.IsNullOrEmpty(details) ? Path.GetFileName(track.Path) : details;
        ToolTip.SetTip(TrackDetails, track.Path);
        Title = $"{name} - Lucent";
    }

    /// <summary>Shows the title, scrolling it sideways when it's too long to fit.</summary>
    private void SetMarquee(string text)
    {
        NowPlayingText.Text = text;
        NowPlayingRepeat.Text = text;
        UpdateMarquee();
    }

    private void OnMarqueeSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateMarquee();

    private void UpdateMarquee()
    {
        _marqueeDistance = 0;
        _marqueeClock.Reset();
        Canvas.SetLeft(MarqueeStrip, 0);

        NowPlayingText.Measure(Size.Infinity);
        var textWidth = NowPlayingText.DesiredSize.Width;
        var room = MarqueeArea.Bounds.Width;
        if (room <= 0 || textWidth <= room)
        {
            NowPlayingRepeat.IsVisible = false;
            return;
        }

        // Two copies side by side: once the first has scrolled out, the second is exactly where it started.
        // Each lap pauses at the start so the beginning of the title can be read.
        NowPlayingRepeat.IsVisible = true;
        NowPlayingRepeat.Margin = new Thickness(MarqueeGap, 0, 0, 0);
        _marqueeDistance = textWidth + MarqueeGap;
        _marqueeClock.Start();
    }

    /// <summary>Moves a scrolling title along: a pause at the start of each lap, then a steady scroll by one copy's width.</summary>
    private void StepMarquee()
    {
        if (_marqueeDistance <= 0)
        {
            return;
        }

        var pause = 2.0;
        var lap = pause + _marqueeDistance / MarqueeSpeed;
        var time = _marqueeClock.Elapsed.TotalSeconds % lap;
        var left = time < pause ? 0 : -_marqueeDistance * (time - pause) / (lap - pause);
        Canvas.SetLeft(MarqueeStrip, left);
    }

    private async void ShowError(string message, Exception ex)
    {
        UpdatePlayPauseButton();
        await MessageWindow.ShowAsync(this, $"{message}\n\nDetails: {ex.Message}", "Lucent");
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

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        SavePlacements();
        IsShuttingDown = true;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        TrackInfoWindow.TagsSaved -= OnTagsSaved;
        ArtworkWindow.OpenChanged -= OnArtworkOpenChanged;
        AnalyzerWindow.OpenChanged -= OnAnalyzerOpenChanged;
        VideoWindow.OpenChanged -= OnVideoOpenChanged;
        AudioTools.MainPlayerChanged -= OnMainPlayerChanged;
        AudioTools.FilesReplaced -= OnTagsSaved;
        SaveQueue();
        _settings.CollapsedLibraryFolders = _libraryWindow.CollapsedFolders.ToList();
        _settings.Save();

        // The other windows close for good with the player.
        foreach (var window in ToolWindows)
        {
            window.Close();
        }

        _tempoKey?.Dispose();
        _library.Dispose();
        _stores.Dispose();
        _positionTimer.Stop();
        StopStreaming();
        StopRadio();
        _visualizer.Dispose();
        _player.Dispose();
        _updater.InstallOnExit(restart: _restartIntoUpdate);
    }
}
