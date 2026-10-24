using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lucent.Mac.Visuals;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Visualizer.Avs;

namespace Lucent.Mac;

/// <summary>Winamp AVS presets: switching them on or off, their look, and bringing in more. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    /// <summary>
    /// A "Winamp AVS presets" submenu: switch them on or off, choose between the modern look and Winamp's own,
    /// import files or a whole pack's folder, or open the folder they're kept in.
    /// </summary>
    private MenuItem AvsMenu()
    {
        var parent = new MenuItem { Header = "Winamp AVS presets" };
        var shown = new MenuItem { Header = "Show AVS presets", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.AvsVisuals };
        shown.Click += (_, _) =>
        {
            ShowAvs(!_settings.AvsVisuals);
            Player?.ShowOptionsState();
        };
        parent.Items.Add(shown);
        var modern = new MenuItem { Header = "Modern look", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.ModernAvs };
        modern.Click += (_, _) =>
        {
            UseModernAvs(!_settings.ModernAvs);
            Player?.ShowOptionsState();
        };
        parent.Items.Add(modern);
        parent.Items.Add(new Separator());
        parent.Items.Add(MenuItemFor("Import AVS presets…", ImportAvsFiles));
        parent.Items.Add(MenuItemFor("Import a folder of presets…", ImportAvsFolder));
        parent.Items.Add(new Separator());
        parent.Items.Add(MenuItemFor("Show the AVS folder in Finder", OpenAvsFolder));
        return parent;
    }

    /// <summary>
    /// Lists AVS presets in the picker and lets Auto play them, or leaves them out of both. One on screen stays
    /// until the next visual.
    /// </summary>
    public void ShowAvs(bool shown)
    {
        _settings.AvsVisuals = shown;
        _settings.Save();
        ShowActiveList();
    }

    /// <summary>
    /// Whether a visual is in the picker and Auto: Lucent's originals, MilkDrop and AVS presets only while they're
    /// switched on.
    /// </summary>
    private bool IsSwitchedOn(VisualChoice choice) =>
        (_settings.LucentVisuals || !choice.IsLucentOriginal)
        && (_settings.MilkDropVisuals || !choice.IsMilkDrop) && (_settings.AvsVisuals || !choice.IsAvs);

    /// <summary>
    /// Lists Lucent's original shaders in the picker and lets Auto play them, or leaves them out of both, so only
    /// MilkDrop, AVS and the user's own visuals are left. Switched off while one is showing, it moves on to the
    /// first visual still listed.
    /// </summary>
    public void ShowLucentOriginals(bool shown)
    {
        _settings.LucentVisuals = shown;
        _settings.Save();
        ShowActiveList();
        if (_host is not null && CurrentChoice() is { } current && !IsSwitchedOn(current) && _shown.Count > 0)
        {
            _host.PresetIndex = _shown[0].Index;
            ShowPreset();
        }
    }

    /// <summary>
    /// Shows AVS presets modernised (sharper, smoothly enlarged, and lit by Lucent's glow and tone-mapping) or as
    /// Winamp drew them, from the next frame.
    /// </summary>
    public void UseModernAvs(bool modern)
    {
        _settings.ModernAvs = modern;
        _settings.Save();
        AvsVisual.ModernLook = modern;
    }

    /// <summary>The Import button on the picker's Winamp AVS folder: presets one by one, or a whole pack's folder.</summary>
    private void OnImportIntoAvsFolder(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control button)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MenuItemFor("Import AVS presets…", ImportAvsFiles));
        menu.Items.Add(MenuItemFor("Import a folder of presets…", ImportAvsFolder));
        menu.Open(button);
    }

    private async void ImportAvsFiles()
    {
        LeaveFullscreenForDialog();
        var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import AVS presets",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("Winamp AVS presets") { Patterns = new[] { "*.avs" } } },
        });
        var paths = chosen.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
        {
            ShowImport(AvsImport.ImportFiles(paths, new MetalShaderFiles().AvsFolder));
        }
    }

    private async void ImportAvsFolder()
    {
        LeaveFullscreenForDialog();
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import a folder of AVS presets",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }

        try
        {
            ShowImport(AvsImport.ImportFolder(folder, new MetalShaderFiles().AvsFolder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Couldn't read that folder: {ex.Message}");
        }
    }

    private void ShowImport(AvsImportResult result)
    {
        var message = result.Added switch
        {
            0 => "No AVS presets were found there.",
            1 => "Added 1 AVS preset.",
            _ => $"Added {result.Added} AVS presets.",
        };
        foreach (var (file, reason) in result.Skipped)
        {
            ErrorLog.Write($"AVS preset {file} wasn't imported", reason);
        }

        var skipped = result.Skipped.Count;
        if (skipped > 0)
        {
            message += $" {skipped} file{(skipped == 1 ? " wasn't a preset and was" : "s weren't presets and were")} left out.";
        }

        if (result.Added > 0)
        {
            // They're in the Winamp AVS folder of the picker from now on; show the first one.
            _host?.ReloadPresets(result.First);
        }

        ShowStatus(message);
    }

    private static void OpenAvsFolder()
    {
        var folder = new MetalShaderFiles().AvsFolder;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { folder }, UseShellExecute = false });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("AVS folder", ex);
        }
    }
}
