using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Media;
using Lucent.Playlist;
using Track = Lucent.Playlist.Track;
using Lucent.Radio;

namespace Lucent.Mac;

// Favourite songs and saved playlists. The queue and library windows build their menus from these.
public partial class MainWindow
{
    private HashSet<string>? _favoriteTracks;
    private IDisposable? _heartFill;
    private IDisposable? _heartStroke;

    private HashSet<string> FavoriteTrackSet =>
        _favoriteTracks ??= new HashSet<string>(_settings.FavoriteTracks, StringComparer.OrdinalIgnoreCase);

    public bool IsFavoriteTrack(string path) => FavoriteTrackSet.Contains(path);

    public int FavoriteTrackCount => FavoriteTrackSet.Count;

    /// <summary>Marks songs as favourites, or takes them off.</summary>
    public void SetFavoriteTracks(IEnumerable<string> paths, bool favorite)
    {
        foreach (var path in paths)
        {
            if (favorite)
            {
                if (FavoriteTrackSet.Add(path))
                {
                    _settings.FavoriteTracks.Add(path);
                }
            }
            else if (FavoriteTrackSet.Remove(path))
            {
                _settings.FavoriteTracks.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            }
        }

        _settings.Save();
        UpdateTrackFavoriteButton();
        _libraryWindow.FavoritesChanged();
        LoveOnLastfm(paths, favorite);
    }

    private void OnToggleTrackFavorite(object? sender, RoutedEventArgs e) => ToggleTrackFavorite();

    private void ToggleTrackFavorite()
    {
        // While a radio station is on, the heart stars the station.
        if (_station is { } station)
        {
            SetStationFavorite(station, !RadioGuide.IsFavorite(_settings, station));
            return;
        }

        if (_current is { } entry)
        {
            SetFavoriteTracks(new[] { entry.Track.Path }, !IsFavoriteTrack(entry.Track.Path));
        }
    }

    private void UpdateTrackFavoriteButton()
    {
        var favorite = _station is not null ? RadioGuide.IsFavorite(_settings, _station) : _current is not null && IsFavoriteTrack(_current.Track.Path);
        var has = _current is not null || _station is not null;
        TrackFavoriteButton.IsEnabled = has;
        TrackFavoriteButton.Opacity = has ? 1 : 0.4;
        var thing = _station is not null ? "station" : "sound";
        ToolTip.SetTip(TrackFavoriteButton, favorite ? $"Remove this {thing} from your favorites (H)" : $"Add this {thing} to your favorites (H)");

        // The heart follows the theme's colours: filled in the accent when it's a favourite, an outline otherwise.
        _heartFill?.Dispose();
        _heartStroke?.Dispose();
        _heartFill = null;
        if (favorite)
        {
            _heartFill = TrackFavoriteIcon.Bind(Shape.FillProperty, TrackFavoriteIcon.GetResourceObservable("AccentBrush"));
            _heartStroke = TrackFavoriteIcon.Bind(Shape.StrokeProperty, TrackFavoriteIcon.GetResourceObservable("AccentBrush"));
        }
        else
        {
            TrackFavoriteIcon.Fill = Brushes.Transparent;
            _heartStroke = TrackFavoriteIcon.Bind(Shape.StrokeProperty, TrackFavoriteIcon.GetResourceObservable("MutedTextBrush"));
        }
    }

    /// <summary>Plays the favourite songs (replacing the queue), or adds them to the end of it.</summary>
    public async void PlayFavorites(bool replace)
    {
        var paths = _settings.FavoriteTracks.ToList();
        var tracks = await Task.Run(() => paths.Where(File.Exists).Select(Track.FromFile).ToList());
        if (tracks.Count == 0)
        {
            await MessageWindow.ShowAsync(this, "You haven't got any favorite sounds yet. Click the heart in the player, or right-click sounds in the queue or library, to add some.");
            return;
        }

        UseTracks(tracks, replace);
    }

    /// <summary>Plays a saved playlist (replacing the queue), or adds it to the end of the queue.</summary>
    public async void PlaySavedPlaylist(string name, bool replace)
    {
        List<Track> tracks;
        try
        {
            tracks = await Task.Run(() => SavedPlaylists.Read(name).Select(Track.FromFile).ToList());
        }
        catch (Exception ex)
        {
            ShowError($"Couldn't open the playlist \"{name}\".", ex);
            return;
        }

        if (tracks.Count == 0)
        {
            await MessageWindow.ShowAsync(this, $"None of the sounds in \"{name}\" could be found. They may have been moved or deleted.");
            return;
        }

        UseTracks(tracks, replace);
    }

    private void UseTracks(IReadOnlyList<Track> tracks, bool replace)
    {
        if (replace)
        {
            ReplaceQueue(tracks, _settings.Shuffle ? _random.Next(tracks.Count) : 0);
        }
        else
        {
            AddToQueue(tracks);
        }
    }

    /// <summary>Saves everything in the queue as a playlist.</summary>
    public async void SaveQueueAsPlaylist(Window owner)
    {
        if (_playlist.Count == 0)
        {
            return;
        }

        var tracks = _playlist.Select(entry => entry.Track).ToList();
        if (await AskForPlaylistName(owner, "Save the queue as a playlist", $"{tracks.Count} {(tracks.Count == 1 ? "sound" : "sounds")}. Playlists are .m3u8 files, so other players can open them too.", "Save") is { } name)
        {
            SavePlaylist(() => SavedPlaylists.Write(name, tracks));
        }
    }

    /// <summary>
    /// The "Add to playlist" submenu for songs in the queue or library: each saved playlist, then New playlist….
    /// </summary>
    public MenuItem AddToPlaylistMenu(Window owner, Func<Task<List<Track>>> songs)
    {
        var menu = new MenuItem { Header = "Add to playlist" };
        foreach (var name in SavedPlaylists.Names())
        {
            var item = new MenuItem { Header = name };
            item.Click += async (_, _) =>
            {
                var tracks = await songs();
                SavePlaylist(() => SavedPlaylists.Append(name, tracks));
            };
            menu.Items.Add(item);
        }

        if (menu.Items.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        var create = new MenuItem { Header = "New playlist…" };
        create.Click += async (_, _) =>
        {
            if (await AskForPlaylistName(owner, "New playlist", "The sounds you chose go in it first.", "Create") is { } name)
            {
                var tracks = await songs();
                SavePlaylist(() => SavedPlaylists.Write(name, tracks));
            }
        };
        menu.Items.Add(create);
        return menu;
    }

    /// <summary>The favourite-or-not menu item for songs in the queue or library.</summary>
    public MenuItem FavoriteMenuItem(IReadOnlyList<string> paths)
    {
        // With a mix selected, the item adds the ones that aren't favourites yet.
        var allFavorite = paths.Count > 0 && paths.All(IsFavoriteTrack);
        var item = new MenuItem
        {
            Header = allFavorite ? "Remove from favorites" : "Add to favorites",
            IsEnabled = paths.Count > 0,
        };
        item.Click += (_, _) => SetFavoriteTracks(paths, !allFavorite);
        return item;
    }

    /// <summary>The menu under PLAYLISTS in the queue: the favourites, each saved playlist, and saving the queue.</summary>
    public void FillPlaylistsMenu(ContextMenu menu, Window owner)
    {
        menu.Items.Clear();

        var favorites = new MenuItem { Header = $"Favorite sounds  ({FavoriteTrackCount})" };
        AddPlayItems(favorites, replace => PlayFavorites(replace));
        menu.Items.Add(favorites);

        var names = SavedPlaylists.Names();
        if (names.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var name in names)
        {
            var playlist = new MenuItem { Header = name };
            AddPlayItems(playlist, replace => PlaySavedPlaylist(name, replace));
            playlist.Items.Add(new Separator());

            var rename = new MenuItem { Header = "Rename…" };
            rename.Click += (_, _) => RenamePlaylist(owner, name);
            playlist.Items.Add(rename);

            var delete = new MenuItem { Header = "Delete" };
            delete.Click += (_, _) => DeletePlaylist(owner, name);
            playlist.Items.Add(delete);
            menu.Items.Add(playlist);
        }

        menu.Items.Add(new Separator());
        var save = new MenuItem { Header = "Save the queue as a playlist…", IsEnabled = _playlist.Count > 0 };
        save.Click += (_, _) => SaveQueueAsPlaylist(owner);
        menu.Items.Add(save);

        var open = new MenuItem { Header = "Open the playlists folder" };
        open.Click += (_, _) =>
        {
            Directory.CreateDirectory(SavedPlaylists.Folder);
            ShowInExplorer(SavedPlaylists.Folder);
        };
        menu.Items.Add(open);
    }

    /// <summary>Makes a new, empty playlist, to fill later with Add to playlist.</summary>
    public async void CreatePlaylist(Window owner)
    {
        if (await AskForPlaylistName(owner, "New playlist", "It starts empty. Right-click sounds and choose Add to playlist to fill it.", "Create") is { } name)
        {
            SavePlaylist(() => SavedPlaylists.Write(name, Array.Empty<Track>()));
        }
    }

    public async void RenamePlaylist(Window owner, string name)
    {
        if (await AskForPlaylistName(owner, "Rename playlist", "Choose a new name.", "Rename", name) is { } newName && newName != name)
        {
            SavePlaylist(() => SavedPlaylists.Rename(name, newName));
        }
    }

    public async void DeletePlaylist(Window owner, string name)
    {
        if (await MessageWindow.ShowAsync(owner, $"Delete the playlist \"{name}\"? The sounds themselves aren't touched.", "Lucent",
                MessageButtons.OkCancel) == MessageResult.Ok)
        {
            SavePlaylist(() => SavedPlaylists.Delete(name));
        }
    }

    /// <summary>Takes these files out of a playlist, wherever they appear in it. The files themselves stay.</summary>
    public void RemoveFromPlaylist(string name, IReadOnlyCollection<string> paths)
    {
        var remove = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        SavePlaylist(() => SavedPlaylists.Write(name,
            SavedPlaylists.Read(name).Where(path => !remove.Contains(path)).Select(Track.FromFile).ToList()));
    }

    private static void AddPlayItems(MenuItem parent, Action<bool> play)
    {
        var playNow = new MenuItem { Header = "Play" };
        playNow.Click += (_, _) => play(true);
        parent.Items.Add(playNow);

        var queue = new MenuItem { Header = "Add to queue" };
        queue.Click += (_, _) => play(false);
        parent.Items.Add(queue);
    }

    /// <summary>Asks for a playlist name that works as a file name and no other playlist has. Null if cancelled.</summary>
    private static async Task<string?> AskForPlaylistName(Window owner, string heading, string hint, string action, string initial = "")
    {
        var name = await PromptWindow.AskAsync(owner, heading, hint, action, initial, answer =>
        {
            var clean = SavedPlaylists.CleanName(answer);
            if (clean.Length == 0)
            {
                return "Use some letters or numbers in the name.";
            }

            return !string.Equals(clean, initial, StringComparison.OrdinalIgnoreCase) && SavedPlaylists.Exists(clean)
                ? "There's already a playlist with that name."
                : null;
        });
        return name is null ? null : SavedPlaylists.CleanName(name);
    }

    private void SavePlaylist(Action save)
    {
        try
        {
            save();
        }
        catch (Exception ex)
        {
            ShowError("Couldn't save the playlist.", ex);
        }

        _libraryWindow.PlaylistsChanged();
    }
}
