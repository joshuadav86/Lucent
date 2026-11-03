using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Lucent;

public partial class App : Application
{
    /// <summary>The music Lucent was started to open (a song double-clicked in Explorer, say).</summary>
    public static IReadOnlyList<string> StartupPaths { get; set; } = [];

    static App()
    {
        // WPF's own text layout places letters at fractions of a pixel, which smears 11 to 13 px text
        // across pixels and reads as blurry, most of all at 100% scaling. Display mode lines each letter
        // up with the pixel grid the way the rest of Windows draws text, so it comes out sharp. Windows,
        // menus, and tooltips each start a tree of their own, so each gets it.
        foreach (var type in new[] { typeof(Window), typeof(ContextMenu), typeof(ToolTip), typeof(Popup) })
        {
            TextOptions.TextFormattingModeProperty.OverrideMetadata(type, new FrameworkPropertyMetadata(TextFormattingMode.Display,
                FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));
        }
    }
}
