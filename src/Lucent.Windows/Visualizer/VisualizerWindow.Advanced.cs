using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using Lucent.Visualizer;
using Lucent.Windows;

namespace Lucent;

/// <summary>
/// The visuals menu's Advanced submenu, for people writing shaders: show the visual's file, edit it, and recompile
/// it. Kept in its own file. What's shared with the Mac (which file, the editable copy) is in <see cref="ShaderEditing"/>.
/// </summary>
public partial class VisualizerWindow
{
    // The visual last recompiled, so if it didn't compile (and the visuals moved on) the menu still works on it.
    private string? _lastRecompiled;

    private MenuItem AdvancedMenu()
    {
        var parent = new MenuItem { Header = "Advanced", ToolTip = MenuTips.Advanced };
        var target = AdvancedTarget();
        var file = target is null ? null : ShaderEditing.FileOf(target);
        var editable = target is not null && ShaderEditing.CanEdit(target);

        var show = Item("Show in File Explorer", null, () => ShowShaderFile(target!), MenuTips.ShowShaderFile);
        show.IsEnabled = file is not null;
        parent.Items.Add(show);

        var edit = Item("Edit shader", null, () => EditShader(target!), editable ? MenuTips.EditShader : MenuTips.AvsNotEditable);
        edit.IsEnabled = editable;
        ToolTipService.SetShowOnDisabled(edit, true);
        parent.Items.Add(edit);

        // Named when it isn't the visual showing: the last one recompiled didn't compile and the visuals moved on.
        var showing = CurrentChoice()?.Name;
        var recompile = Item(target is not null && target.Name != showing ? $"Recompile {target.Name}" : "Recompile shader", null,
            () => RecompileShader(target!), editable ? MenuTips.RecompileShader : MenuTips.AvsNotEditable);
        recompile.IsEnabled = editable;
        ToolTipService.SetShowOnDisabled(recompile, true);
        parent.Items.Add(recompile);
        return parent;
    }

    private PresetSource? AdvancedTarget()
    {
        if (_host is null)
        {
            return null;
        }

        var name = ShaderEditing.Target(CurrentChoice()?.Name, _lastRecompiled, _host.HasFailed);
        return _host.Presets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowShaderFile(PresetSource source)
    {
        if (ShaderEditing.FileOf(source) is not { } file)
        {
            return;
        }

        // Explorer would open behind the full-screen visuals.
        LeaveFullscreenForDialog();
        try
        {
            Process.Start("explorer.exe", $"/select,\"{file}\"");
        }
        catch (Win32Exception ex)
        {
            ShowShaderProblem("Couldn't open File Explorer.", ex);
        }
    }

    private void EditShader(PresetSource source)
    {
        LeaveFullscreenForDialog();
        string file;
        try
        {
            file = ShaderEditing.EditableFile(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowShaderProblem($"Couldn't copy {source.Name} to your Presets folder.", ex);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            // Nothing is set to open .hlsl (or .milk) files, so fall back on Notepad, which every Windows has.
            try
            {
                Process.Start("notepad.exe", $"\"{file}\"");
            }
            catch (Win32Exception ex)
            {
                ShowShaderProblem("Couldn't open a text editor.", ex);
            }
        }
    }

    /// <summary>
    /// Loads the visuals again so the edited file is read and compiled, and carries on with this one. Unchanged
    /// visuals come straight from the shader cache, so it's quick. A compile error shows above the visuals.
    /// </summary>
    private void RecompileShader(PresetSource source)
    {
        _lastRecompiled = source.Name;
        VisualizerStatus.Text = string.Empty;
        VisualizerStatus.ToolTip = null;
        Toast.Show(this, $"Recompiling {source.Name}…");
        _host?.ReloadPresets(source.Name);
    }

    /// <summary>When the visual just recompiled reports it didn't compile, says so over the visuals (the bar above them is hidden in full screen).</summary>
    private void OnRecompileProblem(string message)
    {
        if (ShaderEditing.CompileError(message, _lastRecompiled) is { } error)
        {
            Toast.Show(this, $"{_lastRecompiled} didn't compile. {error}");
        }
    }

    private void ShowShaderProblem(string message, Exception ex)
    {
        ErrorLog.Write(message, ex);
        VisualizerStatus.Text = $"{message} {ex.Message}";
        VisualizerStatus.ToolTip = VisualizerStatus.Text;
    }
}
