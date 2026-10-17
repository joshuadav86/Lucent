// STUB: owned by eq worker
using Avalonia.Controls;
using Lucent.Mac.Audio;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

public partial class AnalyzerWindow : Window
{
    public static event EventHandler? OpenChanged;

    public static bool IsOpen => false;

    public static bool Toggle(Window owner, AudioPlayer player, AppSettings settings)
    {
        OpenChanged?.Invoke(null, EventArgs.Empty);
        return false;
    }
}
