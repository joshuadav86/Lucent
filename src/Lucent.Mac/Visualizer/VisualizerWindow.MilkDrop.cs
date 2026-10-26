using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lucent.Mac.Visuals;
using WinampInspiredVisualizer.Visualizer.MilkDrop;

namespace Lucent.Mac;

/// <summary>MilkDrop presets: switching them on or off, their look, and bringing in more. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    /// <summary>
    /// A "MilkDrop presets" submenu: switch them on or off, choose between the modern look and MilkDrop's own,
    /// import files or a whole pack's folder, or open the folder they're kept in.
    /// </summary>
    private MenuItem MilkDropMenu()
    {
        var parent = new MenuItem { Header = "MilkDrop presets" };
        var shown = new MenuItem { Header = "Show MilkDrop presets", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.MilkDropVisuals };
        shown.Click += (_, _) =>
        {
            ShowMilkDrop(!_settings.MilkDropVisuals);
            Player?.ShowOptionsState();
        };
        parent.Items.Add(shown);
        var modern = new MenuItem { Header = "Modern look", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.ModernMilkDrop };
        modern.Click += (_, _) =>
        {
            UseModernMilkDrop(!_settings.ModernMilkDrop);
            Player?.ShowOptionsState();
        };
        parent.Items.Add(modern);
        parent.Items.Add(new Separator());
        parent.Items.Add(MenuItemFor("Import MilkDrop presets…", ImportMilkDropFiles));
        parent.Items.Add(MenuItemFor("Import a folder of presets…", ImportMilkDropFolder));
        parent.Items.Add(new Separator());
        parent.Items.Add(MenuItemFor("Show the MilkDrop folder in Finder", OpenMilkDropFolder));
        return parent;
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

    /// <summary>
    /// Shows MilkDrop presets in Lucent's light (its glow, tone-mapping and HDR highlights) or as MilkDrop showed
    /// them, from the next frame.
    /// </summary>
    public void UseModernMilkDrop(bool modern)
    {
        _settings.ModernMilkDrop = modern;
        _settings.Save();
        MilkDropLook.Modern = modern;
    }

    /// <summary>The Import button on the picker's MilkDrop folder: presets one by one, or a whole pack's folder.</summary>
    private void OnImportIntoMilkDropFolder(object? sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (sender is not Control button)
        {
            return;
        }

        var menu = new ContextMenu();
        menu.Items.Add(MenuItemFor("Import MilkDrop presets…", ImportMilkDropFiles));
        menu.Items.Add(MenuItemFor("Import a folder of presets…", ImportMilkDropFolder));
        menu.Open(button);
    }

    private async void ImportMilkDropFiles()
    {
        LeaveFullscreenForDialog();
        var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Import MilkDrop presets",
            AllowMultiple = true,
            FileTypeFilter = new[] { new FilePickerFileType("MilkDrop presets") { Patterns = new[] { "*.milk" } } },
        });
        var paths = chosen.Select(file => file.TryGetLocalPath()).OfType<string>().ToList();
        if (paths.Count > 0)
        {
            ShowImport(MilkDropImport.ImportFiles(paths, new MetalShaderFiles().MilkDropFolder));
        }
    }

    private async void ImportMilkDropFolder()
    {
        LeaveFullscreenForDialog();
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Import a folder of MilkDrop presets",
            AllowMultiple = false,
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }

        try
        {
            ShowImport(MilkDropImport.ImportFolder(folder, new MetalShaderFiles().MilkDropFolder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus($"Couldn't read that folder: {ex.Message}");
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
        foreach (var (file, reason) in result.Skipped)
        {
            ErrorLog.Write($"MilkDrop preset {file} wasn't imported", reason);
        }

        var skipped = result.Skipped.Count;
        if (skipped > 0)
        {
            message += $" {skipped} file{(skipped == 1 ? " wasn't a preset and was" : "s weren't presets and were")} left out.";
        }

        if (result.Added > 0)
        {
            // They're in the MilkDrop folder of the picker from now on; show the first one.
            _host?.ReloadPresets(result.First);
        }

        ShowStatus(message);
    }

    private void ShowStatus(string message)
    {
        VisualizerStatus.Text = message;
        ToolTip.SetTip(VisualizerStatus, message);
    }

    private static void OpenMilkDropFolder()
    {
        var folder = new MetalShaderFiles().MilkDropFolder;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { folder }, UseShellExecute = false });
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

    private static MenuItem MenuItemFor(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }
}
