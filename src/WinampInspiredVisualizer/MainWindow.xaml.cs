using System.Reflection;
using System.Windows;

namespace WinampInspiredVisualizer;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"Version {version?.ToString(3)}";
    }
}
