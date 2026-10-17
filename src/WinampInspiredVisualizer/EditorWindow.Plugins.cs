using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Plugins;

namespace WinampInspiredVisualizer;

/// <summary>
/// VST3 effects in the editor. One plugin is open at a time, in its own helper process with its own window;
/// the bar above the processing buttons previews it, applies it, or closes it.
/// </summary>
public partial class EditorWindow
{
    /// <summary>Above this many effects, the menu groups them by maker.</summary>
    private const int FlatMenuLimit = 20;

    private PluginCatalog? _catalog;
    private PluginHost? _plugin;
    private PluginPreviewProvider? _preview;
    private Func<ISampleProvider, float, ISampleProvider>? _previewThrough;

    // ---- Choosing a plugin ----

    private async void OnPlugins(object sender, RoutedEventArgs e)
    {
        if (!PluginHost.IsAvailable)
        {
            MessageBox.Show(this, "Lucent's plugin helper (LucentPluginHost.exe) is missing from its folder, so plugins can't be used. " +
                                  "Reinstalling Lucent puts it back.", "Plugins", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _catalog ??= await Task.Run(() => PluginCatalog.Load());
        var catalog = _catalog;
        var unscanned = await Task.Run(() => catalog.FindUnscanned());
        if (unscanned.Count > 0 && !await ScanPluginsAsync(everything: false))
        {
            return;
        }

        ShowPluginMenu();
    }

    private async Task<bool> ScanPluginsAsync(bool everything)
    {
        if (_catalog is not { } catalog)
        {
            return false;
        }

        var done = await RunBusyAsync("Looking for VST3 plugins…", (progress, token) => catalog.ScanAsync(everything, progress, token));
        if (done)
        {
            var count = catalog.Plugins.Count;
            ShowStatus(count == 0 ? "No VST3 effects were found." : $"Found {count} VST3 effect{(count == 1 ? string.Empty : "s")}.");
        }

        return done;
    }

    private void ShowPluginMenu()
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = PluginsButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        var plugins = catalog.Plugins;
        if (plugins.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "No VST3 effects found", IsEnabled = false });
            menu.Items.Add(new MenuItem
            {
                Header = $"Lucent looks in {PluginCatalog.StandardFolders[0]}",
                IsEnabled = false,
            });
        }
        else if (plugins.Count <= FlatMenuLimit)
        {
            foreach (var plugin in plugins)
            {
                menu.Items.Add(PluginItem(plugin, $"{plugin.Name}  ·  {plugin.Vendor}"));
            }
        }
        else
        {
            foreach (var maker in plugins.GroupBy(p => string.IsNullOrWhiteSpace(p.Vendor) ? "Other" : p.Vendor)
                         .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase))
            {
                var group = new MenuItem { Header = maker.Key };
                foreach (var plugin in maker)
                {
                    group.Items.Add(PluginItem(plugin, plugin.Name));
                }

                menu.Items.Add(group);
            }
        }

        menu.Items.Add(new Separator());
        var rescan = new MenuItem { Header = "Look for plugins again", ToolTip = "After installing or updating plugins" };
        rescan.Click += async (_, _) =>
        {
            if (await ScanPluginsAsync(everything: true))
            {
                ShowPluginMenu();
            }
        };
        menu.Items.Add(rescan);

        var addFolder = new MenuItem { Header = "Add a plugin folder…", ToolTip = "If your plugins are somewhere other than the usual VST3 folder" };
        addFolder.Click += async (_, _) => await AddPluginFolderAsync();
        menu.Items.Add(addFolder);

        var failures = catalog.Failures;
        if (failures.Count > 0)
        {
            var problems = new MenuItem { Header = $"Plugins that couldn't be loaded ({failures.Count})…" };
            problems.Click += (_, _) => ShowPluginFailures(failures);
            menu.Items.Add(problems);
        }

        menu.IsOpen = true;
    }

    private MenuItem PluginItem(PluginInfo plugin, string header)
    {
        var item = new MenuItem { Header = header, ToolTip = plugin.Kind is { } kind ? $"{kind}, version {plugin.Version}" : $"Version {plugin.Version}" };
        item.Click += async (_, _) => await OpenPluginAsync(plugin);
        return item;
    }

    private async Task AddPluginFolderAsync()
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose a folder with VST3 plugins in it" };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        catalog.AddFolder(dialog.FolderName);
        catalog.Save();
        if (await ScanPluginsAsync(everything: false))
        {
            ShowPluginMenu();
        }
    }

    private void ShowPluginFailures(IReadOnlyList<(string Path, string Error)> failures)
    {
        const int shown = 12;
        var lines = failures.Take(shown).Select(f => $"• {Path.GetFileName(f.Path)}: {f.Error}");
        var more = failures.Count > shown ? $"\n…and {failures.Count - shown} more." : string.Empty;
        MessageBox.Show(this,
            "These plugin files couldn't be loaded. 32-bit plugins and instruments can't be used in the editor; " +
            "others may need updating.\n\n" + string.Join("\n", lines) + more,
            "Plugins", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ---- The open plugin ----

    private async Task OpenPluginAsync(PluginInfo plugin)
    {
        if (_session is not { } session)
        {
            return;
        }

        await ClosePluginAsync();
        var audio = session.Audio;
        PluginHost? host = null;
        var opened = await RunBusyAsync($"Opening {plugin.Name}…", async (progress, token) =>
        {
            // Loading isn't interrupted part way; if Cancel was pressed, the plugin is closed once it's up.
            host = await Task.Run(() =>
            {
                var opening = PluginHost.Open(plugin, audio.SampleRate, audio.ChannelCount);
                if (PluginCatalog.LoadSettings(plugin) is { } saved)
                {
                    try
                    {
                        opening.SetSettings(saved);
                    }
                    catch (PluginException) when (!opening.IsGone)
                    {
                        // Settings from an older version it doesn't understand: it starts as new.
                    }
                }

                return opening;
            });
            if (token.IsCancellationRequested)
            {
                var unwanted = host;
                host = null;
                _ = Task.Run(() => unwanted.Dispose());
                token.ThrowIfCancellationRequested();
            }
        });

        if (!opened || host is null)
        {
            return;
        }

        _plugin = host;
        PluginName.Text = string.IsNullOrWhiteSpace(plugin.Vendor) ? host.Name : $"{host.Name}  ·  {plugin.Vendor}";
        PluginWindowButton.Visibility = host.HasWindow ? Visibility.Visible : Visibility.Collapsed;
        PluginHint.Text = host.HasWindow
            ? "Set it up in its window. Preview plays the selection through it, and changes are heard as you make them. Apply changes the selection, or the whole sound."
            : "This plugin has no window of its own, so it runs with its standard settings. Preview to hear it, Apply to use it.";
        PluginBar.Visibility = Visibility.Visible;
        PluginBar.IsEnabled = true;
        ShowStatus($"{host.Name} is open.");
        if (host.HasWindow)
        {
            await ShowPluginWindowAsync();
        }
    }

    private async void OnShowPluginWindow(object sender, RoutedEventArgs e) => await ShowPluginWindowAsync();

    private async Task ShowPluginWindowAsync()
    {
        if (_plugin is not { } host)
        {
            return;
        }

        var owner = new WindowInteropHelper(this).Handle;
        // Lets the helper's window come to the front, which Windows otherwise only allows the active program.
        AllowSetForegroundWindow(host.ProcessId);
        try
        {
            // Off the UI thread: while the plugin's window opens it may need this window to answer.
            await Task.Run(() => host.ShowWindow(owner, $"{host.Name} - Lucent"));
        }
        catch (PluginException ex)
        {
            ShowPluginProblem(ex);
        }
    }

    private async void OnPreviewPlugin(object sender, RoutedEventArgs e)
    {
        if (_previewThrough is not null && _player.IsPlaying)
        {
            StopPlayback();
            return;
        }

        if (_plugin is not { } host || _session is not { } session || IsBusy)
        {
            return;
        }

        var selection = Selection;
        var (from, to) = selection.IsEmpty
            ? (Waveform.CursorFrame >= session.Audio.Frames - 1 ? 0 : Waveform.CursorFrame, session.Audio.Frames)
            : (selection.Start, selection.End);
        StopPlayback();
        try
        {
            // Starting again clears what's left in the plugin from before (an echo still ringing).
            var (_, _, generation) = await Task.Run(() => host.Start(offline: false));
            PlayFrom(from, to, (source, volume) => _preview = new PluginPreviewProvider(source, host, generation, volume));
        }
        catch (PluginException ex)
        {
            ShowPluginProblem(ex);
        }
    }

    private async void OnApplyPlugin(object sender, RoutedEventArgs e)
    {
        if (_plugin is not { } host || _session is not { } session)
        {
            return;
        }

        // The tail rings on into the audio after the selection, so the effect gets that too.
        var selection = Selection;
        var audio = session.Audio;
        var start = selection.IsEmpty ? 0 : selection.Start;
        var length = selection.IsEmpty ? audio.Frames : selection.Length;
        var maxTail = (long)(PluginEffect.MaxTail.TotalSeconds * audio.SampleRate);
        var range = new FrameRange(start, (int)Math.Min(length + maxTail, audio.Frames - start));
        await ApplyAsync(new PluginEffect(host, length), range, selection);
        await SavePluginSettingsAsync();
    }

    private async void OnClosePlugin(object sender, RoutedEventArgs e) => await ClosePluginAsync();

    /// <summary>Remembers the open plugin's settings for next time.</summary>
    private async Task SavePluginSettingsAsync()
    {
        if (_plugin is not { IsGone: false } host)
        {
            return;
        }

        try
        {
            var settings = await Task.Run(() => host.GetSettings());
            PluginCatalog.SaveSettings(host.Plugin, settings);
        }
        catch (PluginException ex)
        {
            ShowPluginProblem(ex);
        }
    }

    private async Task ClosePluginAsync()
    {
        if (_plugin is not { } host)
        {
            return;
        }

        if (_previewThrough is not null)
        {
            StopPlayback();
        }

        await SavePluginSettingsAsync();
        _plugin = null;
        HidePluginBar();
        await Task.Run(() => host.Dispose());
    }

    /// <summary>When the editor closes: saves the plugin's settings and closes it without holding the window up.</summary>
    private void ClosePluginInBackground()
    {
        if (_plugin is not { } host)
        {
            return;
        }

        _plugin = null;
        _ = Task.Run(() =>
        {
            try
            {
                if (!host.IsGone)
                {
                    PluginCatalog.SaveSettings(host.Plugin, host.GetSettings());
                }
            }
            catch (PluginException)
            {
                // Closing anyway.
            }

            host.Dispose();
        });
    }

    /// <summary>Takes the plugin bar away if the plugin crashed or was stopped (after a failed Apply, say).</summary>
    private void ClosePluginIfGone()
    {
        if (_plugin is { IsGone: true } host)
        {
            _plugin = null;
            HidePluginBar();
            host.Dispose();
        }
    }

    private void HidePluginBar()
    {
        PluginBar.Visibility = Visibility.Collapsed;
        PluginName.Text = string.Empty;
    }

    /// <summary>Explains a plugin problem, and closes the plugin if it has stopped.</summary>
    private void ShowPluginProblem(PluginException problem)
    {
        ShowStatus(problem.Message);
        if (_plugin?.IsGone == true)
        {
            ClosePluginIfGone();
            MessageBox.Show(this, problem.Message, "Plugins", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    /// <summary>Puts the Preview button back when playback stops, and reports a plugin that failed while previewing.</summary>
    private void ShowPreviewStopped()
    {
        var failed = _preview?.Error;
        _preview = null;
        _previewThrough = null;
        PluginPreviewButton.Content = "Preview";
        if (failed is PluginException problem)
        {
            ShowPluginProblem(problem);
        }
    }

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int processId);
}
