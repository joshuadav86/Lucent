// STUB: owned by trackinfo worker
using Avalonia.Controls;
using Lucent.Mac.Audio;
using WinampInspiredVisualizer.Settings;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace Lucent.Mac;

public partial class ArtworkWindow : Window
{
    public static event EventHandler? OpenChanged;

    public static bool IsOpen => false;

    public static bool Toggle(Window owner, AudioPlayer player, AppSettings settings, Track? track)
    {
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return false;
    }

    public static void ShowTrack(Track? track)
    {
    }
}
