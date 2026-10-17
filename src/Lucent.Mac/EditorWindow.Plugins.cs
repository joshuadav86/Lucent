using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Lucent.Mac.Interop;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Plugins;

namespace Lucent.Mac;

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
    /// <summary>What playback is going through (a plugin or a tool), so a click while playing carries on through it.</summary>
    private Func<ISampleProvider, ISampleProvider>? _previewThrough;

    // ---- Choosing a plugin ----

    private async void OnPlugins(object? sender, RoutedEventArgs e)
    {
        if (!PluginHost.IsAvailable)
        {
            await MessageWindow.ShowAsync(this, "Lucent's plugin helper (LucentPluginHost) is missing from the app, so plugins can't be used. " +
                                                "Reinstalling Lucent puts it back.", "Plugins");
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

        var menu = MenuAbove();
        var plugins = catalog.Plugins;
        if (plugins.Count == 0)
        {
            menu.Items.Add(MenuEntry("No VST3 effects found", enabled: false));
            menu.Items.Add(MenuEntry($"Lucent looks in {string.Join(" and ", PluginCatalog.StandardFolders)}", enabled: false));
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
                var group = MenuEntry(maker.Key);
                foreach (var plugin in maker)
                {
                    group.Items.Add(PluginItem(plugin, plugin.Name));
                }

                menu.Items.Add(group);
            }
        }

        menu.Items.Add(new Separator());
        var rescan = MenuEntry("Look for plugins again", tip: "After installing or updating plugins");
        rescan.Click += async (_, _) =>
        {
            if (await ScanPluginsAsync(everything: true))
            {
                ShowPluginMenu();
            }
        };
        menu.Items.Add(rescan);

        var addFolder = MenuEntry("Add a plugin folder…", tip: "If your plugins are somewhere other than the usual VST3 folders");
        addFolder.Click += async (_, _) => await AddPluginFolderAsync();
        menu.Items.Add(addFolder);

        var failures = catalog.Failures;
        if (failures.Count > 0)
        {
            var problems = MenuEntry($"Plugins that couldn't be loaded ({failures.Count})…");
            problems.Click += async (_, _) => await ShowPluginFailuresAsync(failures);
            menu.Items.Add(problems);
        }

        menu.Open(PluginsButton);
    }

    private MenuItem PluginItem(PluginInfo plugin, string header)
    {
        var item = MenuEntry(header, tip: plugin.Kind is { } kind ? $"{kind}, version {plugin.Version}" : $"Version {plugin.Version}");
        item.Click += async (_, _) => await OpenPluginAsync(plugin);
        return item;
    }

    private async Task AddPluginFolderAsync()
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Choose a folder with VST3 plugins in it",
        });
        if (folders.Count == 0 || folders[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }

        catalog.AddFolder(folder);
        catalog.Save();
        if (await ScanPluginsAsync(everything: false))
        {
            ShowPluginMenu();
        }
    }

    private async Task ShowPluginFailuresAsync(IReadOnlyList<(string Path, string Error)> failures)
    {
        const int shown = 12;
        var lines = failures.Take(shown).Select(f => $"• {Path.GetFileName(f.Path)}: {f.Error}");
        var more = failures.Count > shown ? $"\n…and {failures.Count - shown} more." : string.Empty;
        await MessageWindow.ShowAsync(this,
            "These plugin files couldn't be loaded. Instruments, and plugins built only for another kind of Mac " +
            "(Intel or Apple silicon), can't be used in the editor; others may need updating.\n\n" + string.Join("\n", lines) + more,
            "Plugins");
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
        PluginWindowButton.IsVisible = host.HasWindow;
        PluginHint.Text = host.HasWindow
            ? "Set it up in its window. Preview plays the selection through it, and changes are heard as you make them. Apply changes the selection, or the whole sound."
            : "This plugin has no window of its own, so it runs with its standard settings. Preview to hear it, Apply to use it.";
        PluginBar.IsVisible = true;
        PluginBar.IsEnabled = true;
        ShowStatus($"{host.Name} is open.");
        if (host.HasWindow)
        {
            await ShowPluginWindowAsync();
        }
    }

    private async void OnShowPluginWindow(object? sender, RoutedEventArgs e) => await ShowPluginWindowAsync();

    private async Task ShowPluginWindowAsync()
    {
        if (_plugin is not { } host)
        {
            return;
        }

        // The helper is another process, so it's told this window's number, which it opens the plugin's window above.
        var owner = (IntPtr)WindowNumber();
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

    private async void OnPreviewPlugin(object? sender, RoutedEventArgs e)
    {
        if (_preview is not null && _player.IsPlaying)
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
            // The listening volume is applied after the meters, so the plugin gets the audio at its own level.
            PlayFrom(from, to, source => _preview = new PluginPreviewProvider(source, host, generation, 1f));
        }
        catch (PluginException ex)
        {
            ShowPluginProblem(ex);
        }
    }

    private async void OnApplyPlugin(object? sender, RoutedEventArgs e)
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

    private async void OnClosePlugin(object? sender, RoutedEventArgs e) => await ClosePluginAsync();

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

        if (_preview is not null)
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
        PluginBar.IsVisible = false;
        PluginName.Text = string.Empty;
    }

    /// <summary>Explains a plugin problem, and closes the plugin if it has stopped.</summary>
    private void ShowPluginProblem(PluginException problem)
    {
        ShowStatus(problem.Message);
        if (_plugin?.IsGone == true)
        {
            ClosePluginIfGone();
            _ = MessageWindow.ShowAsync(this, problem.Message, "Plugins");
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

    /// <summary>This window's number in the Mac's window server (its NSWindow's windowNumber), or 0 if it has none.</summary>
    private long WindowNumber()
    {
        try
        {
            if (OperatingSystem.IsMacOS() && TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: var window } && window != IntPtr.Zero)
            {
                return (long)ObjC.Send(window, "windowNumber");
            }
        }
        catch (Exception)
        {
            // The plugin's window then just opens on its own, centred.
        }

        return 0;
    }
}
