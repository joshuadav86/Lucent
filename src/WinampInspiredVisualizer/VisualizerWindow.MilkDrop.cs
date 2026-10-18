using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace WinampInspiredVisualizer;

/// <summary>Bringing in MilkDrop presets, from the visuals' menu. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    /// <summary>
    /// A "MilkDrop presets" submenu: switch them on or off, import files or a whole pack's folder, or open
    /// the folder they're kept in.
    /// </summary>
    private MenuItem MilkDropMenu()
    {
        var parent = new MenuItem { Header = "MilkDrop presets" };
        var shown = Item("Show MilkDrop presets", string.Empty, () =>
        {
            ShowMilkDrop(!_settings.MilkDropVisuals);
            Player?.ShowOptionsState();
        });
        shown.IsChecked = _settings.MilkDropVisuals;
        parent.Items.Add(shown);
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Import MilkDrop presets…", string.Empty, ImportMilkDropFiles));
        parent.Items.Add(Item("Import a folder of presets…", string.Empty, ImportMilkDropFolder));
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Open the MilkDrop folder", string.Empty, OpenMilkDropFolder));
        return parent;
    }

    /// <summary>The Import button on the picker's MilkDrop folder: presets one by one, or a whole pack's folder.</summary>
    private void OnImportIntoMilkDropFolder(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        menu.Items.Add(Item("Import MilkDrop presets…", string.Empty, ImportMilkDropFiles));
        menu.Items.Add(Item("Import a folder of presets…", string.Empty, ImportMilkDropFolder));
        menu.IsOpen = true;
        e.Handled = true;
    }

    /// <summary>
    /// Lists MilkDrop presets in the picker and lets Auto play them, or leaves them out of both. One on
    /// screen stays until the next visual.
    /// </summary>
    public void ShowMilkDrop(bool shown)
    {
        _settings.MilkDropVisuals = shown;
        _settings.Save();
        ShowActiveList();
    }

    private void ImportMilkDropFiles()
    {
        LeaveFullscreenForDialog();
        var dialog = new OpenFileDialog
        {
            Title = "Import MilkDrop presets",
            Filter = "MilkDrop presets (*.milk)|*.milk",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            ShowImport(MilkDropImport.ImportFiles(dialog.FileNames));
        }
    }

    private void ImportMilkDropFolder()
    {
        LeaveFullscreenForDialog();
        var dialog = new OpenFolderDialog { Title = "Import a folder of MilkDrop presets" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ShowImport(MilkDropImport.ImportFolder(dialog.FolderName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VisualizerStatus.Text = $"Couldn't read that folder: {ex.Message}";
            VisualizerStatus.ToolTip = VisualizerStatus.Text;
        }
    }

    private void ShowImport(MilkDropImportResult result)
    {
        var message = result.Added switch
        {
            0 => "No MilkDrop presets were found there.",
            1 => "Added 1 MilkDrop preset.",
            _ => $"Added {result.Added} MilkDrop presets.",
        };
        if (result.Skipped > 0)
        {
            message += $" {result.Skipped} file{(result.Skipped == 1 ? " wasn't a preset and was" : "s weren't presets and were")} left out.";
        }

        if (result.Added > 0)
        {
            // They're in the MilkDrop folder of the picker from now on; show the first one.
            _host?.ReloadPresets(result.First);
        }

        VisualizerStatus.Text = message;
        VisualizerStatus.ToolTip = message;
    }

    private static void OpenMilkDropFolder()
    {
        try
        {
            Directory.CreateDirectory(PresetLibrary.MilkDropFolder);
            Process.Start("explorer.exe", $"\"{PresetLibrary.MilkDropFolder}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("MilkDrop folder", ex);
        }
    }

    private void LeaveFullscreenForDialog()
    {
        if (_isFullscreen)
        {
            ToggleFullscreen();
        }
    }
}
