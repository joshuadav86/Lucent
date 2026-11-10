using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Lucent.Mac.Visuals;
using Lucent.Mac.Windows;
using Lucent.Visualizer;

namespace Lucent.Mac;

/// <summary>"Import visuals…": bringing in shaders and presets, from the visuals' menu or Options › Visuals. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    /// <summary>
    /// Asks for visual files (Lucent shaders, MilkDrop and AVS presets), copies them into the user's Presets
    /// folder and shows the first one. <paramref name="from"/> is the window asking (Options), or null for the
    /// visuals' own menu; the dialog and the message show over it.
    /// </summary>
    public async void ImportVisuals(Window? from = null)
    {
        if (from is null)
        {
            LeaveFullscreenForDialog();
        }

        var owner = from ?? this;
        var chosen = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import visuals",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Visuals") { Patterns = VisualImport.Extensions.Select(extension => $"*.{extension}").ToArray() },
            },
        });
        var paths = chosen.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count == 0)
        {
            return;
        }

        var files = new MetalShaderFiles();
        var result = VisualImport.ImportFiles(paths, files.UserPresetsFolder, files.MilkDropFolder, files.AvsFolder);
        foreach (var (file, reason) in result.Skipped)
        {
            ErrorLog.Write($"Visual {file} wasn't imported", reason);
        }

        if (result.Added > 0)
        {
            // They're in the picker from now on; show the first one.
            _host?.ReloadPresets(result.First);
        }

        ShowStatus(result.Message);
        Toast.Show(owner, result.Message);
    }
}
