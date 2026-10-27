using Avalonia;
using Avalonia.Controls;

namespace Lucent.Mac;

// Play/Pause, Next and Previous on Lucent's icon: the menu that opens when the icon in the Dock is
// right-clicked (or held), with the playing song at the top, as the Music app has. Windows has the
// same buttons under the player's taskbar preview.
public partial class MainWindow
{
    private const string TitleSuffix = " - Lucent";

    private NativeMenuItem[] _iconSongItems = [];
    private NativeMenuItem? _iconSong;
    private NativeMenuItem? _iconPlayPause;

    private void BuildIconControls()
    {
        if (Application.Current is not { } app)
        {
            return;
        }

        var nowPlaying = new NativeMenuItem("Now Playing") { IsEnabled = false };
        var song = new NativeMenuItem { IsEnabled = false };
        var songEnd = new NativeMenuItemSeparator();
        var playPause = Item("Play", TogglePlayPause);
        _iconSongItems = [nowPlaying, song, songEnd];
        _iconSong = song;
        _iconPlayPause = playPause;

        var menu = new NativeMenu();
        menu.Add(nowPlaying);
        menu.Add(song);
        menu.Add(songEnd);
        menu.Add(playPause);
        menu.Add(Item("Next", () => PlayNext(wrap: true)));
        menu.Add(Item("Previous", PlayPrevious));
        NativeDock.SetMenu(app, menu);

        // The song is whatever the player's title shows: a track, a station's song, or another app's.
        PropertyChanged += (_, e) =>
        {
            if (e.Property == TitleProperty)
            {
                UpdateIconControls();
            }
        };

        UpdateIconControls();
    }

    /// <summary>Shows the playing song, and Play or Pause, in the Dock menu.</summary>
    private void UpdateIconControls()
    {
        if (_iconSong is null || _iconPlayPause is null)
        {
            return;
        }

        var title = Title ?? string.Empty;
        var song = title.EndsWith(TitleSuffix, StringComparison.Ordinal) ? title[..^TitleSuffix.Length] : null;
        _iconSong.Header = song;
        // The Now Playing heading, the song, and the line under them show only while there's a song.
        foreach (var item in _iconSongItems)
        {
            item.IsVisible = !string.IsNullOrEmpty(song);
        }

        _iconPlayPause.Header = ShownIsPlaying ? "Pause" : "Play";
    }
}
