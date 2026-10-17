using Velopack;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// Starts Lucent. Velopack runs first: when the installer or an update launches Lucent.exe to set
/// up shortcuts or clean up, it does that and exits before any window opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();
        AppDataFolder.CopyFromOldName();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
