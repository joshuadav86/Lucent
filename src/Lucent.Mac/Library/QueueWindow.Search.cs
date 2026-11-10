using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Lucent.Library;
using Lucent.Mac.Windows;
using Lucent.Playlist;
using Lucent.Streaming;

namespace Lucent.Mac;

// Search in the queue: the magnifier in the title bar (or ⌘F) opens a box under it that narrows the list to
// the songs with every typed word in their title, artist, album or file name, as the library's search does. It
// works on Spotify's or Music's playlist too, while that shows in the queue's place. It only hides rows: the queue
// plays in its own order as ever, and closing the search shows every song again.
public partial class QueueWindow
{
    private ObservableCollection<PlaylistEntry>? _queueEntries;

    // The queue's songs that match the search, in queue order, kept in step with the queue while it changes.
    private readonly ObservableCollection<PlaylistEntry> _queueMatches = new();
    private string[] _searchWords = Array.Empty<string>();

    // Searching waits for a short pause in the typing, as in the library.
    private DispatcherTimer? _searchTimer;

    static QueueWindow()
    {
        // A class handler runs before the window's own handlers, so ⌘F is the queue's before the player
        // sees it, as with the OnPreviewKeyDown override on Windows.
        KeyDownEvent.AddClassHandler<QueueWindow>((window, e) => window.OnWindowKeyDown(e), RoutingStrategies.Tunnel);
    }

    /// <summary>True while words are typed in the search box.</summary>
    public bool IsSearching => _searchWords.Length > 0;

    /// <summary>Lists the queue; while a search narrows it, only its matching songs.</summary>
    public void ShowQueue(ObservableCollection<PlaylistEntry> entries)
    {
        _queueEntries = entries;
        // Before the player's own handler, so the count it shows is of the songs listed now.
        entries.CollectionChanged += OnQueueChanged;
        PlaylistView.ItemsSource = entries;
    }

    private void OnQueueChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsSearching)
        {
            MatchQueue();
        }
    }

    /// <summary>How long the queue is, in the title bar: "3 of 40 tracks" while a search narrows it.</summary>
    public void ShowQueueSize()
    {
        var total = _queueEntries?.Count ?? 0;
        EmptyPlaylistHint.IsVisible = total == 0;
        var size = total switch
        {
            0 => "Empty",
            1 => "1 track",
            _ => $"{total} tracks",
        };
        QueueCount.Text = IsSearching && total > 0 ? $"{_queueMatches.Count} of {size}" : size;
        ShowNoMatchHint();
    }

    /// <summary>The other app's playlist's size, "3 of 40 songs" while a search narrows it.</summary>
    private void ShowRemoteListSize()
    {
        if (_remoteList is { } list)
        {
            QueueCount.Text = IsSearching ? $"{RemoteListView.ItemCount} of {list.CountText}" : list.CountText;
        }

        ShowNoMatchHint();
    }

    private void ShowNoMatchHint()
    {
        var empty = ShowingRemoteList ? _remoteEntries.Count > 0 && RemoteListView.ItemCount == 0
            : _queueEntries is { Count: > 0 } && _queueMatches.Count == 0;
        NoMatchHint.IsVisible = IsSearching && empty;
    }

    private void OnSearchButton(object? sender, RoutedEventArgs e)
    {
        if (SearchRow.IsVisible)
        {
            CloseSearch();
        }
        else
        {
            OpenSearch();
        }
    }

    /// <summary>Shows the search box, with the keyboard in it and what's there selected so typing replaces it.</summary>
    public void OpenSearch()
    {
        if (IsClassic)
        {
            // A classic skin's playlist frame has no room for the box.
            return;
        }

        SearchRow.IsVisible = true;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Clears and hides the search box, showing every song again.</summary>
    public void CloseSearch()
    {
        var hadFocus = SearchBox.IsKeyboardFocusWithin;
        SearchBox.Clear();
        ApplySearch();
        SearchRow.IsVisible = false;
        if (hadFocus)
        {
            ShownSongList().Focus();
        }
    }

    private void OnCloseSearch(object? sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (_searchTimer is null)
        {
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchTimer.Tick += (_, _) => ApplySearch();
        }

        _searchTimer.Stop();
        if (string.IsNullOrEmpty(SearchBox.Text))
        {
            // Clearing the search shows everything again straight away.
            ApplySearch();
        }
        else
        {
            _searchTimer.Start();
        }
    }

    /// <summary>Narrows the lists to the songs matching what's typed in the search box.</summary>
    private void ApplySearch()
    {
        _searchTimer?.Stop();
        var words = SearchText.Words(SearchBox.Text);
        if (words.SequenceEqual(_searchWords))
        {
            return;
        }

        var wasSearching = IsSearching;
        _searchWords = words;
        if (IsSearching)
        {
            MatchQueue();
            if (!wasSearching)
            {
                PlaylistView.ItemsSource = _queueMatches;
            }
        }
        else
        {
            PlaylistView.ItemsSource = _queueEntries;
            _queueMatches.Clear();
        }

        FilterRemoteList();
        if (ShowingRemoteList)
        {
            ShowRemoteListSize();
        }
        else
        {
            ShowQueueSize();
        }

        // The song playing stays in view when the search is cleared.
        if (!IsSearching && _queueEntries?.FirstOrDefault(entry => entry.IsCurrent) is { } current)
        {
            PlaylistView.ScrollIntoView(current);
        }
    }

    /// <summary>
    /// Brings the matching songs up to date in place, moving, adding and removing only what changed, so the
    /// rows chosen and the scroll position stay as they were.
    /// </summary>
    private void MatchQueue()
    {
        var words = _searchWords;
        var matches = _queueEntries?.Where(entry => SearchText.Matches(entry.SearchKey, words)).ToList() ?? new List<PlaylistEntry>();
        for (var i = 0; i < matches.Count; i++)
        {
            if (i < _queueMatches.Count && ReferenceEquals(_queueMatches[i], matches[i]))
            {
                continue;
            }

            var at = IndexOf(_queueMatches, matches[i], from: i + 1);
            if (at >= 0)
            {
                _queueMatches.Move(at, i);
            }
            else
            {
                _queueMatches.Insert(i, matches[i]);
            }
        }

        while (_queueMatches.Count > matches.Count)
        {
            _queueMatches.RemoveAt(_queueMatches.Count - 1);
        }
    }

    private static int IndexOf(IList<PlaylistEntry> list, PlaylistEntry entry, int from)
    {
        for (var i = from; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], entry))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Lists the other app's playlist, or only its matching songs while searching.</summary>
    private void FilterRemoteList()
    {
        var words = _searchWords;
        var chosen = RemoteListView.SelectedItem;
        RemoteListView.ItemsSource = words.Length > 0
            ? _remoteEntries.Where(entry => SearchText.Matches(entry.SearchKey, words)).ToList()
            : _remoteEntries;
        RemoteListView.SelectedItem = chosen;
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when !string.IsNullOrEmpty(SearchBox.Text):
                SearchBox.Clear();
                e.Handled = true;
                break;
            case Key.Escape:
                // Already empty: the box closes, and the keys go back to the list (and the player's shortcuts).
                CloseSearch();
                ShownSongList().Focus();
                e.Handled = true;
                break;
            case Key.Down:
            case Key.Enter:
                // Into the results: the first match is selected, and Enter again plays it.
                ApplySearch();
                var list = ShownSongList();
                if (list.ItemCount > 0 && list.Items[0] is { } first)
                {
                    ListRows.Focus(list, first);
                }

                e.Handled = true;
                break;
        }
    }

    private ListBox ShownSongList() => ShowingRemoteList ? RemoteListView : PlaylistView;

    private void OnWindowKeyDown(KeyEventArgs e)
    {
        // ⌘F, as Ctrl+F on Windows.
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Meta && !IsClassic)
        {
            OpenSearch();
            e.Handled = true;
        }
    }
}
