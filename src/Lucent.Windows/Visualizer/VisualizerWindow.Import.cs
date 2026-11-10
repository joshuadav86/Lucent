using System.Windows;
using Microsoft.Win32;
using Lucent.Visualizer;
using Lucent.Windows;

namespace Lucent;

/// <summary>"Import visuals…": bringing in shaders and presets, from the visuals' menu or Options › Visuals. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    /// <summary>
    /// Asks for visual files (Lucent shaders, MilkDrop and AVS presets), copies them into the user's Presets
    /// folder and shows the first one. <paramref name="from"/> is the window asking (Options), or null for the
    /// visuals' own menu; the dialog and the message show over it.
    /// </summary>
    public void ImportVisuals(Window? from = null)
    {
        if (from is null)
        {
            LeaveFullscreenForDialog();
        }

        var patterns = string.Join(";", VisualImport.Extensions.Select(extension => $"*.{extension}"));
        var dialog = new OpenFileDialog
        {
            Title = "Import visuals",
            Filter = $"Visuals ({patterns})|{patterns}",
            Multiselect = true,
        };
        if (dialog.ShowDialog(from ?? this) != true)
        {
            return;
        }

        var result = VisualImport.ImportFiles(
            dialog.FileNames, PresetLibrary.UserPresetsFolder, PresetLibrary.MilkDropFolder, PresetLibrary.AvsFolder);
        foreach (var (file, reason) in result.Skipped)
        {
            ErrorLog.Write($"Visual {file} wasn't imported", reason);
        }

        if (result.Added > 0)
        {
            // They're in the picker from now on; show the first one.
            _host?.ReloadPresets(result.First);
        }

        VisualizerStatus.Text = result.Message;
        VisualizerStatus.ToolTip = result.Message;
        Toast.Show(from ?? this, result.Message);
    }
}
