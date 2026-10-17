using Velopack;

namespace WinampInspiredVisualizer;

/// <summary>
/// Starts Lucid. Velopack runs first: when the installer or an update launches Lucid.exe to set
/// up shortcuts or clean up, it does that and exits before any window opens.
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
