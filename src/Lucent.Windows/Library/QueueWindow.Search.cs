using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Lucent.Library;
using Lucent.Playlist;
using Lucent.Streaming;
using Lucent.Windows;

namespace Lucent;

// Search in the queue: the magnifier in the title bar (or Ctrl+F) opens a box under it that narrows the list to
// the songs with every typed word in their title, artist, album or file name, as the library's search does. It
// works on Spotify's playlist too, while that shows in the queue's place. It only hides rows: the queue plays in
// its own order as ever, and closing the search shows every song again.
public partial class QueueWindow
{
    private ObservableCollection<PlaylistEntry>? _queueEntries;
    private ListCollectionView? _queueView;
    private string[] _searchWords = Array.Empty<string>();

    // Searching waits for a short pause in the typing, as in the library.
    private DispatcherTimer? _searchTimer;

    /// <summary>True while words are typed in the search box.</summary>
    public bool IsSearching => _searchWords.Length > 0;

    /// <summary>Lists the queue, through a view of the window's own so the search can narrow it.</summary>
    public void ShowQueue(ObservableCollection<PlaylistEntry> entries)
    {
        _queueEntries = entries;
        _queueView = new ListCollectionView(entries);
        PlaylistView.ItemsSource = _queueView;
    }

    /// <summary>How long the queue is, in the title bar: "3 of 40 tracks" while a search narrows it.</summary>
    public void ShowQueueSize()
    {
        var total = _queueEntries?.Count ?? 0;
        var shown = _queueView?.Count ?? total;
        EmptyPlaylistHint.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        var size = total switch
        {
            0 => "Empty",
            1 => "1 track",
            _ => $"{total} tracks",
        };
        QueueCount.Text = IsSearching && total > 0 ? $"{shown} of {size}" : size;
        ShowNoMatchHint();
    }

    /// <summary>The Spotify playlist's size, "3 of 40 songs" while a search narrows it.</summary>
    private void ShowRemoteListSize()
    {
        if (_remoteList is { } list)
        {
            QueueCount.Text = IsSearching ? $"{RemoteListView.Items.Count} of {list.CountText}" : list.CountText;
        }

        ShowNoMatchHint();
    }

    private void ShowNoMatchHint()
    {
        var empty = ShowingRemoteList ? _remoteEntries.Count > 0 && RemoteListView.Items.Count == 0
            : _queueEntries is { Count: > 0 } && _queueView is { Count: 0 };
        NoMatchHint.Visibility = IsSearching && empty ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearchButton(object sender, RoutedEventArgs e)
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

        SearchRow.Visibility = Visibility.Visible;
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    /// <summary>Clears and hides the search box, showing every song again.</summary>
    public void CloseSearch()
    {
        var hadFocus = SearchBox.IsKeyboardFocusWithin;
        SearchBox.Clear();
        ApplySearch();
        SearchRow.Visibility = Visibility.Collapsed;
        if (hadFocus)
        {
            ShownSongList().Focus();
        }
    }

    private void OnCloseSearch(object sender, RoutedEventArgs e) => CloseSearch();

    private void OnSearchChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_searchTimer is null)
        {
            _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _searchTimer.Tick += (_, _) => ApplySearch();
        }

        _searchTimer.Stop();
        if (SearchBox.Text.Length == 0)
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

        _searchWords = words;
        if (_queueView is not null)
        {
            _queueView.Filter = IsSearching ? item => item is PlaylistEntry entry && SearchText.Matches(entry.SearchKey, words) : null;
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
        if (!IsSearching && PlaylistView.Items.OfType<PlaylistEntry>().FirstOrDefault(entry => entry.IsCurrent) is { } current)
        {
            PlaylistView.ScrollIntoView(current);
        }
    }

    /// <summary>Applies the search to Spotify's playlist, after it's listed again.</summary>
    private void FilterRemoteList()
    {
        var words = _searchWords;
        RemoteListView.Items.Filter = words.Length > 0 ? item => item is RemoteListEntry entry && SearchText.Matches(entry.SearchKey, words) : null;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when SearchBox.Text.Length > 0:
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
                if (list.Items.Count > 0 && list.Items[0] is { } first)
                {
                    ListRows.Focus(list, first);
                }

                e.Handled = true;
                break;
        }
    }

    private System.Windows.Controls.ListBox ShownSongList() => ShowingRemoteList ? RemoteListView : PlaylistView;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && !IsClassic)
        {
            OpenSearch();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
