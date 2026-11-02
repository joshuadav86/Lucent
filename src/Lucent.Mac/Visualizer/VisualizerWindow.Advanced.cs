using System.ComponentModel;
using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Lucent.Mac.Windows;
using Lucent.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// The visuals menu's Advanced submenu, for people writing shaders: show the visual's file, edit it, and recompile
/// it. Kept in its own file. What's shared with Windows (which file, the editable copy) is in <see cref="ShaderEditing"/>.
/// </summary>
public partial class VisualizerWindow
{
    // The visual last recompiled, so if it didn't compile (and the visuals moved on) the menu still works on it.
    private string? _lastRecompiled;

    private MenuItem AdvancedMenu()
    {
        var parent = new MenuItem { Header = "Advanced" };
        ToolTip.SetTip(parent, MenuTips.Advanced);
        var target = AdvancedTarget();
        var file = target is null ? null : ShaderEditing.FileOf(target);
        var editable = target is not null && ShaderEditing.CanEdit(target);

        var show = Item("Show in Finder", null, () => ShowShaderFile(target!), MenuTips.ShowShaderFile);
        show.IsEnabled = file is not null;
        parent.Items.Add(show);

        var edit = Item("Edit shader", null, () => EditShader(target!), editable ? MenuTips.EditShader : MenuTips.AvsNotEditable);
        edit.IsEnabled = editable;
        ToolTip.SetShowOnDisabled(edit, true);
        parent.Items.Add(edit);

        // Named when it isn't the visual showing: the last one recompiled didn't compile and the visuals moved on.
        var showing = CurrentChoice()?.Name;
        var recompile = Item(target is not null && target.Name != showing ? $"Recompile {target.Name}" : "Recompile shader", null,
            () => RecompileShader(target!), editable ? MenuTips.RecompileShader : MenuTips.AvsNotEditable);
        recompile.IsEnabled = editable;
        ToolTip.SetShowOnDisabled(recompile, true);
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

        // Finder would open on another Space from the full-screen visuals.
        LeaveFullscreenForDialog();
        try
        {
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", file }, UseShellExecute = false })?.Dispose();
        }
        catch (Win32Exception ex)
        {
            ShowShaderProblem("Couldn't open Finder.", ex);
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

        // The app set to open .hlsl (or .milk) files, or the default text editor when there isn't one.
        // open waits for Launch Services to answer, so it runs off the UI thread.
        _ = Task.Run(() =>
        {
            try
            {
                if (Open(file) != 0 && Open("-t", file) != 0)
                {
                    Dispatcher.UIThread.Post(() => ShowShaderProblem("Couldn't open a text editor.", new Win32Exception("open didn't find an app.")));
                }
            }
            catch (Win32Exception ex)
            {
                Dispatcher.UIThread.Post(() => ShowShaderProblem("Couldn't open a text editor.", ex));
            }
        });
    }

    private static int Open(params string[] arguments)
    {
        var start = new ProcessStartInfo("open") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var open = Process.Start(start);
        if (open is null)
        {
            return -1;
        }

        open.StandardError.ReadToEnd();
        return open.WaitForExit(10_000) ? open.ExitCode : -1;
    }

    /// <summary>
    /// Loads the visuals again so the edited file is read and compiled, and carries on with this one. Unchanged
    /// visuals come straight from the shader cache, so it's quick. A compile error shows above the visuals.
    /// </summary>
    private void RecompileShader(PresetSource source)
    {
        _lastRecompiled = source.Name;
        VisualizerStatus.Text = string.Empty;
        ToolTip.SetTip(VisualizerStatus, null);
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
        ToolTip.SetTip(VisualizerStatus, VisualizerStatus.Text);
    }
}
