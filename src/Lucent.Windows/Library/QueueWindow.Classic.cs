using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shell;
using Lucent.ClassicSkins;
using Lucent.Playlist;
using Lucent.Windows;

namespace Lucent;

// The queue in a classic skin: Winamp 2's playlist frame (pledit.bmp) round the queue's own list, which takes
// on the skin's colours and font. Lucent.Core's ClassicPlaylistFrame draws the frame; the list stays Lucent's,
// so selecting, dragging, keys and the right-click menu work as ever.
public partial class QueueWindow
{
    private const double CaptionHeight = 40;

    private ClassicPlaylistFrame? _frame;
    private ClassicSurfaceView? _classicView;
    private bool _classicOn;
    private Grid? _classicRoot;
    private Grid? _classicLists;
    private object? _lucentContent;
    private Panel? _listsHome;
    private Size _lucentSize;
    private DataTemplate? _lucentRow;
    private DataTemplate? _lucentRemoteRow;
    private object? _shownState;

    /// <summary>True while the queue shows in a classic skin.</summary>
    public bool IsClassic => _classicOn;

    /// <summary>Shows the queue in a classic skin's playlist, or as Lucent's own with null.</summary>
    public void SetClassicSkin(ClassicSkin? skin, double zoom)
    {
        if (skin is null)
        {
            if (!_classicOn)
            {
                return;
            }

            _classicOn = false;
            _classicLists!.Children.Clear();
            // Back where they were: after the title bar, before the note and the empty queue's hint.
            _listsHome!.Children.Insert(1, PlaylistView);
            _listsHome.Children.Insert(2, RemoteListView);
            foreach (var list in new ListBox[] { PlaylistView, RemoteListView })
            {
                list.ClearValue(ItemsControl.ItemContainerStyleProperty);
                ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            }

            PlaylistView.ItemTemplate = _lucentRow;
            RemoteListView.ItemTemplate = _lucentRemoteRow;
            Content = _lucentContent;
            SetChrome(CaptionHeight, resizeBorder: 6);
            ResizeMode = ResizeMode.CanResize;
            MinWidth = 300;
            MinHeight = 160;
            Width = _lucentSize.Width;
            Height = _lucentSize.Height;
            return;
        }

        if (_frame is null)
        {
            _frame = new ClassicPlaylistFrame(skin);
            _frame.Command += OnClassicCommand;
            _frame.ScrollRequested += ScrollTo;
            _frame.ResizeRequested += (width, height) =>
            {
                _frame.SetSize(width, height);
                FitClassic();
            };
            _classicView = new ClassicSurfaceView(_frame)
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                ContextMenu = new ContextMenu(),
            };
            LucentFullScreen.AddTo(_classicView.ContextMenu, this);
            _classicLists = new Grid();
            _classicRoot = new Grid { Children = { _classicView, _classicLists } };
            _listsHome = (Panel)PlaylistView.Parent;
            _lucentRow = PlaylistView.ItemTemplate;
            _lucentRemoteRow = RemoteListView.ItemTemplate;
            foreach (var list in new ListBox[] { PlaylistView, RemoteListView })
            {
                list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => ShowClassicState(force: true)));
                list.SelectionChanged += (_, _) => UpdateRunningTime();
            }

            ((INotifyCollectionChanged)PlaylistView.Items).CollectionChanged += (_, _) => UpdateRunningTime();
            Activated += (_, _) => ShowClassicState(force: true);
            Deactivated += (_, _) => ShowClassicState(force: true);
        }

        if (!_classicOn)
        {
            _classicOn = true;
            _lucentContent = Content;
            _lucentSize = new Size(ActualWidth > 0 ? ActualWidth : Width, ActualHeight > 0 ? ActualHeight : Height);

            // The lists move into the frame; the rest of Lucent's queue (its title bar and buttons) waits behind.
            _listsHome!.Children.Remove(PlaylistView);
            _listsHome.Children.Remove(RemoteListView);
            _classicLists!.Children.Add(PlaylistView);
            _classicLists.Children.Add(RemoteListView);
            foreach (var list in new ListBox[] { PlaylistView, RemoteListView })
            {
                list.ItemContainerStyle = (Style)Resources["ClassicRowItem"];
                // The skin draws its own scroll bar in the frame's right edge.
                ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Hidden);
            }

            PlaylistView.ItemTemplate = (DataTemplate)Resources["ClassicRow"];
            RemoteListView.ItemTemplate = (DataTemplate)Resources["ClassicRow"];

            // The size it has now, in the skin's steps.
            var scale = Math.Max(1, zoom);
            var (width, height) = ClassicPlaylistFrame.Snap(_lucentSize.Width / scale, _lucentSize.Height / scale);
            _frame.SetSize(width, height);
            UpdateRunningTime();
        }

        _frame.Skin = skin;
        _classicView!.Zoom = zoom;
        var style = skin.Playlist;
        Resources["ClassicTextBrush"] = Brush(style.Normal);
        Resources["ClassicCurrentBrush"] = Brush(style.Current);
        Resources["ClassicSelectedBrush"] = Brush(style.SelectedBackground);
        Resources["ClassicFont"] = new FontFamily($"{style.Font}, Arial");
        Content = _classicRoot;
        // The skin's own title bar moves the window, and its own corner resizes it.
        SetChrome(0, resizeBorder: 0);
        ResizeMode = ResizeMode.NoResize;
        MinWidth = 0;
        MinHeight = 0;
        FitClassic();
    }

    private static SolidColorBrush Brush(uint argb)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
        brush.Freeze();
        return brush;
    }

    private void SetChrome(double caption, double resizeBorder) => WindowChrome.SetWindowChrome(this, new WindowChrome
    {
        CaptionHeight = caption,
        ResizeBorderThickness = new Thickness(resizeBorder),
        GlassFrameThickness = new Thickness(0),
        CornerRadius = new CornerRadius(0),
        UseAeroCaptionButtons = false,
    });

    /// <summary>Sizes the window to the frame, and lays the lists over its middle.</summary>
    private void FitClassic()
    {
        var scale = _classicView!.Scale;
        var (left, top, width, height) = _frame!.ListArea;
        // Winamp's rows are 13 skin pixels high, in 9-pixel text.
        Resources["ClassicRowHeight"] = 13 * scale;
        Resources["ClassicFontSize"] = 9.5 * scale;
        _classicLists!.Margin = new Thickness(left * scale, (top + 2) * scale, (_frame.Width - left - width) * scale,
            (_frame.Height - top - height + 1) * scale);
        Width = _frame.Width * scale;
        Height = _frame.Height * scale;
        ShowClassicState(force: true);
    }

    /// <summary>
    /// The frame's running time, time and scroll bar, from the player (once a screen frame) and the list. It's
    /// only drawn again when something it shows has changed.
    /// </summary>
    public void ShowClassicState(ClassicPlayState state, TimeSpan position, TimeSpan duration, bool showRemaining)
    {
        if (!_classicOn)
        {
            return;
        }

        _frame!.PlayState = state;
        _frame.Position = position;
        _frame.Duration = duration;
        _frame.ShowRemaining = showRemaining;
        ShowClassicState(force: false);
    }

    /// <summary>Winamp's running time: the selected songs' length, and the whole queue's.</summary>
    private void UpdateRunningTime()
    {
        if (!_classicOn)
        {
            return;
        }

        var entries = PlaylistView.Items.OfType<PlaylistEntry>().ToList();
        var selected = new HashSet<PlaylistEntry>(SelectedEntries());
        var total = entries.Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Track.Duration);
        var chosen = entries.Where(selected.Contains).Aggregate(TimeSpan.Zero, (sum, entry) => sum + entry.Track.Duration);
        _frame!.RunningTime = $"{TimeFormat.Format(chosen)}/{TimeFormat.Format(total)}";
        ShowClassicState(force: true);
    }

    private void ShowClassicState(bool force)
    {
        if (!_classicOn || !IsVisible)
        {
            return;
        }

        if (Scroller() is { } scroller)
        {
            var room = scroller.ExtentHeight - scroller.ViewportHeight;
            _frame!.CanScroll = room > 0.01;
            _frame.ScrollFraction = room > 0.01 ? scroller.VerticalOffset / room : 0;
        }

        _frame!.IsActive = IsActive;

        // The time shows whole seconds, and blinks twice a second while paused.
        var state = (_frame.PlayState, (int)_frame.Position.TotalSeconds, _frame.ShowRemaining, DateTime.UtcNow.Millisecond >= 500);
        if (force || !Equals(state, _shownState))
        {
            _shownState = state;
            _classicView!.Refresh();
        }
    }

    private ListBox ShownList => RemoteListView.IsVisible ? RemoteListView : PlaylistView;

    private ScrollViewer? Scroller() => FindScroller(ShownList);

    private static ScrollViewer? FindScroller(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if ((child as ScrollViewer ?? FindScroller(child)) is { } found)
            {
                return found;
            }
        }

        return null;
    }

    private void ScrollTo(double fraction)
    {
        if (Scroller() is { } scroller)
        {
            scroller.ScrollToVerticalOffset(fraction * Math.Max(0, scroller.ExtentHeight - scroller.ViewportHeight));
        }
    }

    private void ScrollBy(int rows)
    {
        if (Scroller() is { } scroller)
        {
            // A virtualizing list scrolls a row at a time; otherwise by the rows' height.
            var row = scroller.CanContentScroll ? 1 : Resources["ClassicRowHeight"] is double height ? height : 26;
            scroller.ScrollToVerticalOffset(Math.Max(0, scroller.VerticalOffset + rows * row));
        }
    }

    private void OnClassicCommand(ClassicPlaylistCommand command)
    {
        switch (command)
        {
            case ClassicPlaylistCommand.Close:
                Hide();
                break;
            case ClassicPlaylistCommand.Add:
                ShowClassicMenu(
                    ("Add files…", () => Player?.AddFilesFromDialog(this)),
                    ("Add a folder…", () => Player?.AddFolderFromDialog(this)));
                break;
            case ClassicPlaylistCommand.Remove:
                ShowClassicMenu(
                    ("Remove the selected songs", () => Player?.RemoveFromQueue(SelectedEntries())),
                    ("Keep only the selected songs", () =>
                    {
                        var keep = new HashSet<PlaylistEntry>(SelectedEntries());
                        Player?.RemoveFromQueue(PlaylistView.Items.OfType<PlaylistEntry>().Where(entry => !keep.Contains(entry)).ToList());
                    }),
                    ("Clear the queue", () => Player?.ClearQueue()));
                break;
            case ClassicPlaylistCommand.Select:
                ShowClassicMenu(
                    ("Select all", () => PlaylistView.SelectAll()),
                    ("Select none", () => PlaylistView.UnselectAll()),
                    ("Invert the selection", InvertSelection));
                break;
            case ClassicPlaylistCommand.Misc:
                ShowClassicMenu(
                    ("Edit info…", () => OnEditInfo(this, new RoutedEventArgs())),
                    (SelectedEntries().FirstOrDefault() is { } first ? Editor.VideoEdit.EditorMenuItem(first.Track.Path).Header : "Open in editor…",
                        () => OnOpenInEditor(this, new RoutedEventArgs())),
                    ("Show in File Explorer", () => OnShowInExplorer(this, new RoutedEventArgs())));
                break;
            case ClassicPlaylistCommand.List:
                if (Player is not null)
                {
                    var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
                    Player.FillPlaylistsMenu(menu, this);
                    menu.IsOpen = true;
                }

                break;
            case ClassicPlaylistCommand.ScrollUp:
                ScrollBy(-4);
                break;
            case ClassicPlaylistCommand.ScrollDown:
                ScrollBy(4);
                break;
            default:
                Player?.RunClassicCommand(command switch
                {
                    ClassicPlaylistCommand.Previous => ClassicCommand.Previous,
                    ClassicPlaylistCommand.Play => ClassicCommand.Play,
                    ClassicPlaylistCommand.Pause => ClassicCommand.Pause,
                    ClassicPlaylistCommand.Stop => ClassicCommand.Stop,
                    ClassicPlaylistCommand.Next => ClassicCommand.Next,
                    ClassicPlaylistCommand.Eject => ClassicCommand.Open,
                    _ => ClassicCommand.ToggleTimeMode,
                });
                break;
        }
    }

    private void ShowClassicMenu(params (string Header, Action Action)[] items)
    {
        var menu = new ContextMenu { Placement = PlacementMode.MousePoint };
        foreach (var (header, action) in items)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void InvertSelection()
    {
        var selected = new HashSet<PlaylistEntry>(SelectedEntries());
        PlaylistView.UnselectAll();
        foreach (var entry in PlaylistView.Items.OfType<PlaylistEntry>().Where(entry => !selected.Contains(entry)))
        {
            PlaylistView.SelectedItems.Add(entry);
        }
    }
}
