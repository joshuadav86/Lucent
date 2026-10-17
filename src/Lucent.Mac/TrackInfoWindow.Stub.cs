// STUB: owned by trackinfo worker
using Avalonia.Controls;
using Lucent.Mac.Audio;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

public partial class TrackInfoWindow : Window
{
    public static event EventHandler<TagsSavedEventArgs>? TagsSaved;

    public static void Open(Window owner, AudioPlayer player, AppSettings settings, IEnumerable<string> paths) =>
        TagsSaved?.GetType();
}
