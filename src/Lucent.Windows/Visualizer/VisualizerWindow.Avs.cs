using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Lucent.Visualizer;
using Lucent.Visualizer.Avs;

namespace Lucent;

/// <summary>Winamp AVS presets: switching them on or off, their look, and bringing in more. Kept in its own file.</summary>
public partial class VisualizerWindow
{
    /// <summary>
    /// An "AVS presets" submenu: switch them on or off, choose between the modern look and Winamp's own,
    /// import files or a whole pack's folder, or open the folder they're kept in.
    /// </summary>
    private MenuItem AvsMenu()
    {
        var parent = new MenuItem { Header = "AVS presets", ToolTip = MenuTips.Avs };
        var shown = Item("Show AVS presets", string.Empty, () =>
        {
            ShowAvs(!_settings.AvsVisuals);
            Player?.ShowOptionsState();
        }, MenuTips.ShowAvs);
        shown.IsChecked = _settings.AvsVisuals;
        parent.Items.Add(shown);
        var modern = Item("Modern look", string.Empty, () =>
        {
            UseModernAvs(!_settings.ModernAvs);
            Player?.ShowOptionsState();
        }, MenuTips.ModernAvs);
        modern.IsChecked = _settings.ModernAvs;
        parent.Items.Add(modern);
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Import AVS presets…", string.Empty, ImportAvsFiles, MenuTips.ImportAvs));
        parent.Items.Add(Item("Import a folder of presets…", string.Empty, ImportAvsFolder, MenuTips.ImportFolder));
        parent.Items.Add(new Separator());
        parent.Items.Add(Item("Open the AVS folder", string.Empty, OpenAvsFolder, MenuTips.PresetFolder));
        return parent;
    }

    /// <summary>The Import button on the picker's Winamp AVS folder: presets one by one, or a whole pack's folder.</summary>
    private void OnImportIntoAvsFolder(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = PlacementMode.Bottom };
        menu.Items.Add(Item("Import AVS presets…", string.Empty, ImportAvsFiles));
        menu.Items.Add(Item("Import a folder of presets…", string.Empty, ImportAvsFolder));
        menu.IsOpen = true;
        e.Handled = true;
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

    private void ImportAvsFiles()
    {
        LeaveFullscreenForDialog();
        var dialog = new OpenFileDialog
        {
            Title = "Import AVS presets",
            Filter = "AVS presets (*.avs)|*.avs",
            Multiselect = true,
        };
        if (dialog.ShowDialog(this) == true)
        {
            ShowImport(AvsImport.ImportFiles(dialog.FileNames, PresetLibrary.AvsFolder));
        }
    }

    private void ImportAvsFolder()
    {
        LeaveFullscreenForDialog();
        var dialog = new OpenFolderDialog { Title = "Import a folder of AVS presets" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            ShowImport(AvsImport.ImportFolder(dialog.FolderName, PresetLibrary.AvsFolder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            VisualizerStatus.Text = $"Couldn't read that folder: {ex.Message}";
            VisualizerStatus.ToolTip = VisualizerStatus.Text;
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

        VisualizerStatus.Text = message;
        VisualizerStatus.ToolTip = message;
    }

    private static void OpenAvsFolder()
    {
        try
        {
            Directory.CreateDirectory(PresetLibrary.AvsFolder);
            Process.Start("explorer.exe", $"\"{PresetLibrary.AvsFolder}\"");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ErrorLog.Write("AVS folder", ex);
        }
    }
}
