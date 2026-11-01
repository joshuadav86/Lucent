using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.VisualTree;
using Lucent.Mac.Windows;
using Lucent.ClassicSkins;
using Lucent.Playlist;

namespace Lucent.Mac;

// The queue in a classic skin: Winamp 2's playlist frame (pledit.bmp) round the queue's own list, which takes
// on the skin's colours and font. Lucent.Core's ClassicPlaylistFrame draws the frame; the list stays Lucent's,
// so selecting, dragging, keys and the right-click menu work as ever.
public partial class QueueWindow
{
    private ClassicPlaylistFrame? _frame;
    private ClassicSurfaceView? _classicView;
    private bool _classicOn;
    private Grid? _classicRoot;
    private Grid? _classicLists;
    private object? _lucentContent;
    private Panel? _listsHome;
    private Size _lucentSize;
    private IDataTemplate? _lucentRow;
    private IDataTemplate? _lucentRemoteRow;
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
                list.Classes.Remove("classic");
                ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Auto);
            }

            PlaylistView.ItemTemplate = _lucentRow;
            RemoteListView.ItemTemplate = _lucentRemoteRow;
            Content = _lucentContent;
            CanResize = true;
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
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
                ContextMenu = new ContextMenu(),
            };
            LucentFullScreen.AddTo(_classicView.ContextMenu, this);
            _classicLists = new Grid();
            _classicRoot = new Grid { Children = { _classicView, _classicLists } };
            _listsHome = (Panel)PlaylistView.Parent!;
            _lucentRow = PlaylistView.ItemTemplate;
            _lucentRemoteRow = RemoteListView.ItemTemplate;
            foreach (var list in new ListBox[] { PlaylistView, RemoteListView })
            {
                list.AddHandler(ScrollViewer.ScrollChangedEvent, (_, _) => ShowClassicState(force: true));
                list.SelectionChanged += (_, _) => UpdateRunningTime();
            }

            ((INotifyCollectionChanged)PlaylistView.Items).CollectionChanged += (_, _) => UpdateRunningTime();
            Activated += (_, _) => ShowClassicState(force: true);
            Deactivated += (_, _) => ShowClassicState(force: true);
            // A layout, Lucent full screen or a swap can size the window; the frame follows it.
            SizeChanged += (_, _) => FollowWindowSize();
        }

        if (!_classicOn)
        {
            _classicOn = true;
            _lucentContent = Content;
            _lucentSize = Placement.SizeOf(this);

            // The lists move into the frame; the rest of Lucent's queue (its title bar and buttons) waits behind.
            _listsHome!.Children.Remove(PlaylistView);
            _listsHome.Children.Remove(RemoteListView);
            _classicLists!.Children.Add(PlaylistView);
            _classicLists.Children.Add(RemoteListView);
            foreach (var list in new ListBox[] { PlaylistView, RemoteListView })
            {
                list.Classes.Add("classic");
                // The skin draws its own scroll bar in the frame's right edge.
                ScrollViewer.SetVerticalScrollBarVisibility(list, ScrollBarVisibility.Hidden);
            }

            PlaylistView.ItemTemplate = (IDataTemplate)Resources["ClassicQueueRow"]!;
            RemoteListView.ItemTemplate = (IDataTemplate)Resources["ClassicRemoteRow"]!;

            // The size it has now, in the skin's steps.
            var scale = Math.Max(1, zoom);
            var (width, height) = ClassicPlaylistFrame.Snap(_lucentSize.Width / scale, _lucentSize.Height / scale);
            _frame.SetSize(width, height);
            UpdateRunningTime();
        }

        _frame.Skin = skin;
        _classicView!.Zoom = zoom;
        var style = skin.Playlist;
        Resources["ClassicTextBrush"] = new SolidColorBrush(Color.FromUInt32(style.Normal));
        Resources["ClassicCurrentBrush"] = new SolidColorBrush(Color.FromUInt32(style.Current));
        Resources["ClassicSelectedBrush"] = new SolidColorBrush(Color.FromUInt32(style.SelectedBackground));
        Resources["ClassicFont"] = new FontFamily($"{style.Font}, Arial, Helvetica");
        Content = _classicRoot;
        CanResize = false;
        MinWidth = 0;
        MinHeight = 0;
        FitClassic();
    }

    /// <summary>Sizes the window to the frame, and lays the lists over its middle.</summary>
    private void FitClassic()
    {
        var scale = _classicView!.Scale;
        LayOutClassic();
        Width = _frame!.Width * scale;
        Height = _frame.Height * scale;
    }

    /// <summary>
    /// Sizes the frame to the window when something other than the frame's own corner sized it (a saved layout,
    /// Lucent full screen, a swap), so the bottom bar stays at the bottom and the songs stop above it.
    /// </summary>
    private void FollowWindowSize()
    {
        if (!_classicOn)
        {
            return;
        }

        var scale = _classicView!.Scale;
        var (windowWidth, windowHeight) = (Bounds.Width, Bounds.Height);
        var width = (int)Math.Round(windowWidth / scale);
        var height = (int)Math.Round(windowHeight / scale);
        if (windowWidth <= 0 || windowHeight <= 0 || (width == _frame!.Width && height == _frame.Height))
        {
            return;
        }

        _frame.Fill(width, height);
        LayOutClassic();
    }

    /// <summary>Lays the lists over the frame's middle, at the frame's scale, and draws it.</summary>
    private void LayOutClassic()
    {
        var scale = _classicView!.Scale;
        var (left, top, width, height) = _frame!.ListArea;
        // Winamp's rows are 13 skin pixels high, in 9-pixel text.
        Resources["ClassicRowHeight"] = 13 * scale;
        Resources["ClassicFontSize"] = 9.5 * scale;
        _classicLists!.Margin = new Thickness(left * scale, (top + 2) * scale, (_frame.Width - left - width) * scale,
            (_frame.Height - top - height + 1) * scale);
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
            var room = scroller.Extent.Height - scroller.Viewport.Height;
            _frame!.CanScroll = room > 0.5;
            _frame.ScrollFraction = room > 0.5 ? scroller.Offset.Y / room : 0;
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

    private ScrollViewer? Scroller() => ShownList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault();

    private void ScrollTo(double fraction)
    {
        if (Scroller() is { } scroller)
        {
            scroller.Offset = new Vector(scroller.Offset.X, fraction * Math.Max(0, scroller.Extent.Height - scroller.Viewport.Height));
        }
    }

    private void ScrollBy(int rows)
    {
        if (Scroller() is { } scroller)
        {
            var row = Resources["ClassicRowHeight"] is double height ? height : 26;
            scroller.Offset = new Vector(scroller.Offset.X, Math.Max(0, scroller.Offset.Y + rows * row));
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
                    (SelectedEntries().FirstOrDefault() is { } first ? Lucent.Editor.VideoEdit.EditorMenuItem(first.Track.Path).Header : "Open in editor…",
                        () => OnOpenInEditor(this, new RoutedEventArgs())),
                    ("Show in Finder", () => OnShowInExplorer(this, new RoutedEventArgs())));
                break;
            case ClassicPlaylistCommand.List:
                if (Player is not null)
                {
                    var menu = new ContextMenu { Placement = PlacementMode.Pointer };
                    Player.FillPlaylistsMenu(menu, this);
                    menu.Open(_classicView);
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
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        foreach (var (header, action) in items)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        menu.Open(_classicView);
    }

    private void InvertSelection()
    {
        var selected = new HashSet<PlaylistEntry>(SelectedEntries());
        PlaylistView.UnselectAll();
        foreach (var entry in PlaylistView.Items.OfType<PlaylistEntry>().Where(entry => !selected.Contains(entry)))
        {
            PlaylistView.SelectedItems?.Add(entry);
        }
    }
}
