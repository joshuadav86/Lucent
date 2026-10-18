using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// Asks, when a library folder with folders inside it is added, whether each of those folders gets its own
/// heading or all the music goes under the one folder.
/// </summary>
public partial class SubfolderWindow : Window
{
    public SubfolderWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close(null);
            }
        };
    }

    /// <summary>
    /// The answer, and whether to remember it. Closing the box keeps the folders separate, as before,
    /// without remembering.
    /// </summary>
    /// <remarks>Windows' version waits in ShowDialog; Avalonia's dialogs are awaited instead.</remarks>
    public static async Task<(LibrarySubfolders Choice, bool Remember)> AskAsync(Window owner, IReadOnlyList<string> folders)
    {
        var box = new SubfolderWindow();
        box.Heading.Text = folders.Count == 1
            ? $"“{LibraryFolderList.Name(folders[0])}” has folders inside it"
            : $"These {folders.Count} folders have folders inside them";
        var choice = await box.ShowDialog<LibrarySubfolders?>(owner);
        return choice is { } answer
            ? (answer, box.RememberSwitch.IsChecked == true)
            : (LibrarySubfolders.Separate, false);
    }

    private void OnTogether(object? sender, RoutedEventArgs e) => Close(LibrarySubfolders.Together);

    private void OnSeparate(object? sender, RoutedEventArgs e) => Close(LibrarySubfolders.Separate);
}
