using System.Windows;

namespace Lucent;

public partial class App : Application
{
    /// <summary>The music Lucent was started to open (a song double-clicked in Explorer, say).</summary>
    public static IReadOnlyList<string> StartupPaths { get; set; } = [];
}
