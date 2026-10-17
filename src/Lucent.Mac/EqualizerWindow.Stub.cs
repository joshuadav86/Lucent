// STUB: owned by eq worker
using Avalonia.Controls;
using Lucent.Mac.Audio;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

public partial class EqualizerWindow : Window
{
    public static EqualizerWindow? Current => null;

    public static void Toggle(Window owner, AudioPlayer player, AppSettings settings, Action<bool>? openChanged = null)
    {
    }

    public static EqualizerWindow Show(Window owner, AudioPlayer player, AppSettings settings, Action<bool>? openChanged = null) => new();
}
