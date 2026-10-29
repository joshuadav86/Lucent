using System.ComponentModel;
using Lucent.Playlist;
using Lucent.Streaming;

namespace Lucent.Library;

/// <summary>A music file found in a library folder. Shows its file name until its tags have been read.</summary>
public sealed class LibraryItem : INotifyPropertyChanged
{
    private string _title;
    private string _artist = string.Empty;
    private string _durationText = string.Empty;
    private string? _searchKey;

    public LibraryItem(string path, string folder, string directory, DateTime added, string? section = null,
        string? sectionDirectory = null)
    {
        Path = path;
        Folder = folder;
        Directory = directory;
        Section = section ?? folder;
        SectionDirectory = sectionDirectory ?? directory;
        Added = added;
        FileName = System.IO.Path.GetFileNameWithoutExtension(path);
        _title = FileName;
        Heading = Section;
        Subheading = Folder;
    }

    /// <summary>A row standing in for an empty playlist, so its heading still shows. It isn't a file.</summary>
    public static LibraryItem Placeholder(string folder, string text) =>
        new(string.Empty, folder, string.Empty, DateTime.MinValue) { _title = text };

    /// <summary>
    /// The rows for another app's playlists (Spotify's, Music's) under PLAYLISTS: one row a playlist, under the app's heading,
    /// split into the user's own ("Your playlists") and other people's ("Following"), each a subheading that can be closed.
    /// The app's first list (Liked Songs, Favorite Songs) stays at the top whatever the order.
    /// </summary>
    /// <param name="section">The app's heading: its name.</param>
    public static List<LibraryItem> RemotePlaylistRows(RemoteApp app, string section, IReadOnlyList<RemotePlaylist> playlists,
        RemotePlaylistSort sort)
    {
        var pinned = playlists.Take(1).Where(playlist => playlist.Songs is null && playlist.Owner is null);
        IEnumerable<RemotePlaylist> rest = playlists.Skip(pinned.Count());
        rest = sort switch
        {
            RemotePlaylistSort.Name => rest.OrderBy(playlist => playlist.Name, StringComparer.CurrentCultureIgnoreCase),
            RemotePlaylistSort.MostSongs => rest.OrderByDescending(playlist => playlist.Songs ?? -1),
            _ => rest,
        };

        return pinned.Concat(rest.Where(playlist => playlist.Owner is null)).Concat(rest.Where(playlist => playlist.Owner is not null))
            .Select(playlist =>
            {
                var songs = playlist.Songs switch
                {
                    null => "Your liked songs",
                    1 => "1 song",
                    var many => $"{many:N0} songs",
                };
                var heading = playlist.Owner is null ? "Your playlists" : "Following";
                return new LibraryItem(string.Empty, $"{section} / {heading}", string.Empty, DateTime.MinValue, section)
                {
                    _title = playlist.Name,
                    _remoteDetails = playlist.Owner is { } owner ? $"{songs}  ·  by {owner}" : songs,
                    RemotePlaylist = playlist,
                    RemoteApp = app,
                };
            })
            .ToList();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsPlaceholder => Path.Length == 0;

    /// <summary>The other app's playlist this row plays (<see cref="RemotePlaylistRows"/>), or null for any other row.</summary>
    public RemotePlaylist? RemotePlaylist { get; private init; }

    /// <summary>The app <see cref="RemotePlaylist"/> is played in.</summary>
    public RemoteApp RemoteApp { get; private init; }

    private string _remoteDetails = string.Empty;

    /// <summary>Whether its row has a little cover beside it: every song does, a playlist's stand-in row doesn't.</summary>
    public bool ShowsCover => !IsPlaceholder;

    public string Path { get; }

    public string FileName { get; }

    /// <summary>The folder heading this file is listed under, relative to its library folder.</summary>
    public string Folder { get; }

    /// <summary>
    /// The full path of the folder its heading stands for: the folder the file is in, or its library folder
    /// when everything in that is listed together.
    /// </summary>
    public string Directory { get; }

    /// <summary>
    /// The top heading the file is listed under: its <see cref="Folder"/>, or a store's one heading ("Bandcamp")
    /// that its folder is a subheading of.
    /// </summary>
    public string Section { get; }

    /// <summary>The full path of the folder behind <see cref="Section"/>.</summary>
    public string SectionDirectory { get; }

    /// <summary>Whether <see cref="Folder"/> is a subheading under <see cref="Section"/>, not a heading of its own.</summary>
    public bool IsInSection => !string.Equals(Section, Folder, StringComparison.Ordinal);

    /// <summary>The subheading's name, without the store's in front: "Artist / Album".</summary>
    public string SubFolder => IsInSection && Folder.StartsWith(Section + " / ", StringComparison.Ordinal)
        ? Folder[(Section.Length + 3)..]
        : Folder;

    /// <summary>
    /// The heading the song is listed under: its <see cref="Section"/>, or with the library's headings by artist or
    /// album (<see cref="LibraryGroups.Assign"/>), its artist's or album's.
    /// </summary>
    public string Heading { get; private set; }

    /// <summary>
    /// The subheading it's listed under inside <see cref="Heading"/>: its <see cref="Folder"/>, or its album under its
    /// artist's heading. The same as <see cref="Heading"/> when it sits right under the heading.
    /// </summary>
    public string Subheading { get; private set; }

    /// <summary>Whether <see cref="Subheading"/> is a subheading of its own under <see cref="Heading"/>.</summary>
    public bool HasSubheading => !string.Equals(Heading, Subheading, StringComparison.Ordinal);

    /// <summary>What the subheading shows: the folder inside a store's ("Artist / Album"), or the album ("Isles (2021)").</summary>
    public string SubheadingLabel => _subheadingLabel ?? SubFolder;

    private string? _subheadingLabel;

    /// <summary>Lists the song under other headings. Set by <see cref="LibraryGroups.Assign"/>.</summary>
    internal void SetHeadings(string heading, string subheading, string subheadingLabel)
    {
        Heading = heading;
        Subheading = subheading;
        _subheadingLabel = subheadingLabel;
    }

    /// <summary>When the file arrived: the later of its created and modified times.</summary>
    public DateTime Added { get; }

    public string Title => _title;

    public string Artist => _artist;

    public string Album => Track?.Album ?? string.Empty;

    public string DurationText => _durationText;

    public string Details => RemotePlaylist is not null ? _remoteDetails : IsPlaceholder ? string.Empty : string.IsNullOrEmpty(_artist)
        ? $"{Added:d}{StatsText}"
        : $"{_artist}  ·  {Added:d}{StatsText}";

    /// <summary>How many times it's been played, here and in players it was imported from.</summary>
    public int Plays => IsPlaceholder ? 0 : TrackStats.Shared.PlaysOf(Path);

    /// <summary>Stars, 1 to 5, or 0 for none.</summary>
    public int Rating => IsPlaceholder ? 0 : TrackStats.Shared.RatingOf(Path);

    public DateTime? LastPlayed => IsPlaceholder ? null : TrackStats.Shared.LastPlayedOf(Path);

    /// <summary>"  ·  ★★★★  ·  12 plays", leaving out what isn't known.</summary>
    private string StatsText
    {
        get
        {
            var rating = Rating;
            var plays = Plays;
            var text = rating > 0 ? $"  ·  {TrackStats.Stars(rating)}" : string.Empty;
            return plays > 0 ? $"{text}  ·  {plays:N0} {(plays == 1 ? "play" : "plays")}" : text;
        }
    }

    /// <summary>The track with its tags, once they have been read.</summary>
    public Track? Track { get; private set; }

    public bool HasTags => Track is not null;

    /// <summary>
    /// The title, artist, album, album artist, genre, composer and file name, ready for the library's search box to look in.
    /// </summary>
    public string SearchKey => _searchKey ??= RemotePlaylist is { } playlist
        ? SearchText.Key(new[] { playlist.Name, playlist.Owner ?? string.Empty })
        : SearchText.Key(new[]
        {
            _title, _artist, Album, Track?.Tags.AlbumArtist, Track?.Tags.Genre, Track?.Tags.Composer, FileName,
        });

    /// <summary>
    /// What the row shows in each of the tag columns (<see cref="LibraryColumns.Shown"/>). A playlist's row,
    /// or an empty playlist's stand-in, has none.
    /// </summary>
    public IReadOnlyList<LibraryCell> Cells => IsPlaceholder || RemotePlaylist is not null
        ? Array.Empty<LibraryCell>()
        : LibraryColumns.Shown
            .Select(column => new LibraryCell(LibraryColumns.Text(column, this), LibraryColumns.Width(column), LibraryColumns.AlignRight(column)))
            .ToList();

    /// <summary>Whether the row has tag columns, so its time gets a divider line before it too.</summary>
    public bool HasCells => LibraryColumns.Shown.Count > 0 && !IsPlaceholder && RemotePlaylist is null;

    /// <summary>Shows a new set of tag columns.</summary>
    public void ColumnsChanged()
    {
        Raise(nameof(Cells));
        Raise(nameof(HasCells));
    }

    public Track ToTrack() => Track ?? Track.FromFile(Path);

    public void ApplyTags(Track track)
    {
        Track = track;
        _title = track.Title;
        _artist = track.Artist;
        _durationText = track.DurationText;
        _searchKey = null;
        Raise(nameof(Title));
        Raise(nameof(Artist));
        Raise(nameof(DurationText));
        Raise(nameof(Details));
        Raise(nameof(Cells));
    }

    /// <summary>Whether it's the song playing, marked in the library with a bar beside it.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            if (_isCurrent != value)
            {
                _isCurrent = value;
                Raise(nameof(IsCurrent));
            }
        }
    }

    private bool _isCurrent;

    /// <summary>Shows a new play count or rating.</summary>
    public void StatsChanged()
    {
        Raise(nameof(Details));
        if (LibraryColumns.Shown.Any(column => column is LibraryColumn.Rating or LibraryColumn.Plays or LibraryColumn.LastPlayed))
        {
            Raise(nameof(Cells));
        }
    }

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
