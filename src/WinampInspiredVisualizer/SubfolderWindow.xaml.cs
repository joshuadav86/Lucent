using System.Windows;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// Asks, when a library folder with folders inside it is added, whether each of those folders gets its own
/// heading or all the music goes under the one folder.
/// </summary>
public partial class SubfolderWindow : Window
{
    private LibrarySubfolders _choice = LibrarySubfolders.Separate;

    private SubfolderWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// The answer, and whether to remember it. Closing the box keeps the folders separate, as before,
    /// without remembering.
    /// </summary>
    public static (LibrarySubfolders Choice, bool Remember) Ask(Window owner, IReadOnlyList<string> folders)
    {
        var box = new SubfolderWindow { Owner = owner };
        box.Heading.Text = folders.Count == 1
            ? $"“{LibraryFolderList.Name(folders[0])}” has folders inside it"
            : $"These {folders.Count} folders have folders inside them";
        var answered = box.ShowDialog() == true;
        return answered ? (box._choice, box.RememberSwitch.IsChecked == true) : (LibrarySubfolders.Separate, false);
    }

    private void OnTogether(object sender, RoutedEventArgs e)
    {
        _choice = LibrarySubfolders.Together;
        DialogResult = true;
    }

    private void OnSeparate(object sender, RoutedEventArgs e)
    {
        _choice = LibrarySubfolders.Separate;
        DialogResult = true;
    }
}
