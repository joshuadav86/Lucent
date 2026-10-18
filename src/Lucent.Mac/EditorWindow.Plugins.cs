using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Lucent.Mac.Interop;
using NAudio.Wave;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Plugins;

namespace Lucent.Mac;

/// <summary>
/// VST3 effects in the editor. Plugins open into a chain, each in its own helper process with its own window;
/// the bar above the processing buttons switches each one on or off, reorders them, previews the chain,
/// applies it as one step, and saves or opens chains (Lucent's own, or REAPER's .RfxChain files).
/// </summary>
public partial class EditorWindow
{
    /// <summary>Above this many effects, the menu groups them by maker.</summary>
    private const int FlatMenuLimit = 20;

    private readonly PluginChain _chain = new();
    private PluginCatalog? _catalog;
    private PluginPreviewProvider? _preview;
    /// <summary>What playback is going through (a plugin or a tool), so a click while playing carries on through it.</summary>
    private Func<ISampleProvider, ISampleProvider>? _previewThrough;

    // ---- Choosing a plugin ----

    private async void OnPlugins(object? sender, RoutedEventArgs e) => await ShowPluginMenuAsync(PluginsButton);

    private async void OnAddToChain(object? sender, RoutedEventArgs e) => await ShowPluginMenuAsync(ChainAddButton);

    private async Task ShowPluginMenuAsync(Control under)
    {
        if (await EnsureCatalogAsync() is not null)
        {
            ShowPluginMenu(under);
        }
    }

    /// <summary>The installed plugins, looking at new and changed plugin files first. Null if that was cancelled.</summary>
    private async Task<PluginCatalog?> EnsureCatalogAsync()
    {
        if (!PluginHost.IsAvailable)
        {
            await MessageWindow.ShowAsync(this, "Lucent's plugin helper (LucentPluginHost) is missing from the app, so plugins can't be used. " +
                                                "Reinstalling Lucent puts it back.", "Plugins");
            return null;
        }

        _catalog ??= await Task.Run(() => PluginCatalog.Load());
        var catalog = _catalog;
        var unscanned = await Task.Run(() => catalog.FindUnscanned());
        if (unscanned.Count > 0 && !await ScanPluginsAsync(everything: false))
        {
            return null;
        }

        return catalog;
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

    private void ShowPluginMenu(Control under)
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
        if (under == PluginsButton)
        {
            var chains = MenuEntry("Chains");
            AddChainItems(chains.Items, saveable: _chain.Count > 0);
            menu.Items.Add(chains);
        }

        var rescan = MenuEntry("Look for plugins again", tip: "After installing or updating plugins");
        rescan.Click += async (_, _) =>
        {
            if (await ScanPluginsAsync(everything: true))
            {
                ShowPluginMenu(under);
            }
        };
        menu.Items.Add(rescan);

        var addFolder = MenuEntry("Add a plugin folder…", tip: "If your plugins are somewhere other than the usual VST3 folders");
        addFolder.Click += async (_, _) => await AddPluginFolderAsync(under);
        menu.Items.Add(addFolder);

        var failures = catalog.Failures;
        if (failures.Count > 0)
        {
            var problems = MenuEntry($"Plugins that couldn't be loaded ({failures.Count})…");
            problems.Click += async (_, _) => await ShowPluginFailuresAsync(failures);
            menu.Items.Add(problems);
        }

        menu.Open(under);
    }

    private MenuItem PluginItem(PluginInfo plugin, string header)
    {
        var item = MenuEntry(header, tip: plugin.Kind is { } kind ? $"{kind}, version {plugin.Version}" : $"Version {plugin.Version}");
        item.Click += async (_, _) => await AddPluginAsync(plugin);
        return item;
    }

    private async Task AddPluginFolderAsync(Control under)
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
            ShowPluginMenu(under);
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

    // ---- The chain ----

    /// <summary>Opens a plugin and adds it to the end of the chain, with the settings it had last time.</summary>
    private async Task AddPluginAsync(PluginInfo plugin)
    {
        if (_session is not { } session)
        {
            return;
        }

        StopPreview();
        var audio = session.Audio;
        PluginHost? host = null;
        var opened = await RunBusyAsync($"Opening {plugin.Name}…", async (progress, token) =>
        {
            // Loading isn't interrupted part way; if Cancel was pressed, the plugin is closed once it's up.
            host = await Task.Run(() => OpenHost(plugin, PluginCatalog.LoadSettings(plugin), audio, out _));
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

        var slot = new ChainSlot(plugin, host);
        _chain.Add(slot);
        ShowChain();
        ShowStatus(_chain.Count == 1 ? $"{host.Name} is open." : $"{host.Name} is open, number {_chain.Count} in the chain.");
        if (host.HasWindow)
        {
            await ShowPluginWindowAsync(slot);
        }
    }

    /// <summary>
    /// Starts a plugin's helper and gives it its settings. Waits for the plugin, so call it off the UI thread.
    /// <paramref name="settingsRefused"/> is set when the plugin wouldn't take them (a different version of it, say).
    /// </summary>
    private static PluginHost OpenHost(PluginInfo plugin, byte[]? settings, AudioBuffer audio, out bool settingsRefused)
    {
        settingsRefused = false;
        var host = PluginHost.Open(plugin, audio.SampleRate, audio.ChannelCount);
        if (settings is not null)
        {
            try
            {
                host.SetSettings(settings);
            }
            catch (PluginException) when (!host.IsGone)
            {
                // Settings from an older version it doesn't understand: it starts as new.
                settingsRefused = true;
            }
        }

        return host;
    }

    /// <summary>Shows the chain's plugins in the bar, or hides the bar when there are none.</summary>
    private void ShowChain()
    {
        ChainPanel.Children.Clear();
        var slots = _chain.Slots;
        for (var i = 0; i < slots.Count; i++)
        {
            ChainPanel.Children.Add(ChainChip(slots[i], i, slots.Count));
        }

        PluginBar.IsVisible = slots.Count > 0;
        PluginBar.IsEnabled = !IsBusy;
        var on = slots.Count(s => !s.IsBypassed);
        PluginHint.Text = slots.Count switch
        {
            0 => string.Empty,
            1 when slots[0].Host.HasWindow =>
                "Set it up in its window (click its name to bring it back). Preview plays the selection through it, and changes are heard as you make them. " +
                "Add plugin chains another one after it. Apply changes the selection, or the whole sound.",
            1 => "This plugin has no window of its own, so it runs with its standard settings. Preview to hear it, Apply to use it, or add another plugin after it.",
            _ => $"The sound goes through them from left to right; {(on == slots.Count ? "all are on" : $"{on} of {slots.Count} are on")}. " +
                 "Switch one off to hear the chain without it, even while it plays. Apply runs the ones that are on, as one step.",
        };
    }

    private Control ChainChip(ChainSlot slot, int index, int count)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var on = new CheckBox
        {
            Theme = FindTheme("SwitchToggle"),
            IsChecked = !slot.IsBypassed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 2, 0),
        };
        ToolTip.SetTip(on, "Switch it off to hear the chain without it (even while the preview plays). Apply leaves out plugins that are off.");
        var name = new Button
        {
            Theme = FindTheme("ToolButton"),
            Height = 26,
            Padding = new Thickness(8, 0, 8, 0),
            Margin = new Thickness(0),
            Content = $"{index + 1}. {slot.Host.Name}",
            Opacity = slot.IsBypassed ? 0.5 : 1,
        };
        ToolTip.SetTip(name, slot.Host.HasWindow ? $"{slot.Title}: show its window" : $"{slot.Title} has no window of its own");
        name.Click += async (_, _) => await ShowPluginWindowAsync(slot);
        on.Click += (_, _) =>
        {
            slot.IsBypassed = on.IsChecked != true;
            ShowChain();
            ShowStatus(slot.IsBypassed ? $"{slot.Host.Name} is off." : $"{slot.Host.Name} is on.");
        };
        row.Children.Add(on);
        row.Children.Add(name);
        row.Children.Add(ChipButton("ChevronLeftIcon", "Move it earlier in the chain", index > 0, () => MoveInChain(index, index - 1)));
        row.Children.Add(ChipButton("ChevronRightIcon", "Move it later in the chain", index < count - 1, () => MoveInChain(index, index + 1)));
        row.Children.Add(ChipButton("CloseIcon", "Take it out of the chain (its settings are remembered)", true, async () => await RemoveFromChainAsync(slot)));
        return new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(6, 3, 4, 3),
            Margin = new Thickness(0, 0, 8, 6),
            Background = ThemeBrush("SurfaceRaisedBrush"),
            BorderBrush = ThemeBrush("HairlineBrush"),
            BorderThickness = new Thickness(1),
        };
    }

    private Button ChipButton(string icon, string tip, bool enabled, Action click)
    {
        var button = new Button
        {
            Theme = FindTheme("ChipButton"),
            IsEnabled = enabled,
            Content = new Avalonia.Controls.Shapes.Path { Theme = FindTheme("LineIcon"), Data = ThemeIcon(icon) },
        };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private void MoveInChain(int from, int to)
    {
        StopPreview();
        _chain.Move(from, to);
        ShowChain();
    }

    private async Task RemoveFromChainAsync(ChainSlot slot)
    {
        StopPreview();
        var index = _chain.Slots.ToList().IndexOf(slot);
        if (index < 0)
        {
            return;
        }

        _chain.RemoveAt(index);
        ShowChain();
        await CloseSlotsAsync(new[] { slot });
    }

    private async Task ShowPluginWindowAsync(ChainSlot slot)
    {
        if (!slot.Host.HasWindow || slot.Host.IsGone)
        {
            return;
        }

        // The helper is another process, so it's told this window's number, which it opens the plugin's window above.
        var owner = (IntPtr)WindowNumber();
        try
        {
            // Off the UI thread: while the plugin's window opens it may need this window to answer.
            await Task.Run(() => slot.Host.ShowWindow(owner, $"{slot.Host.Name} - Lucent"));
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

        if (_chain.Count == 0 || _session is not { } session || IsBusy)
        {
            return;
        }

        var selection = Selection;
        var (from, to) = selection.IsEmpty
            ? (Waveform.CursorFrame >= session.Audio.Frames - 1 ? 0 : Waveform.CursorFrame, session.Audio.Frames)
            : (selection.Start, selection.End);
        StopPlayback();
        var slots = _chain.Slots.ToList();
        try
        {
            // Starting again clears what's left in each plugin from before (an echo still ringing).
            var stages = await Task.Run(() => slots.Select(slot =>
                new PreviewStage(slot.Host, slot.Host.Start(offline: false).Generation, () => slot.IsBypassed)).ToList());
            // The listening volume is applied after the meters, so the plugins get the audio at its own level.
            PlayFrom(from, to, source => _preview = new PluginPreviewProvider(source, stages, 1f));
        }
        catch (PluginException ex)
        {
            ShowPluginProblem(ex);
        }
    }

    private async void OnApplyPlugin(object? sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var hosts = _chain.ActiveHosts;
        if (hosts.Count == 0)
        {
            ShowStatus("Every plugin in the chain is switched off, so there's nothing to apply.");
            return;
        }

        // The tail rings on into the audio after the selection, so the effect gets that too.
        var selection = Selection;
        var audio = session.Audio;
        var start = selection.IsEmpty ? 0 : selection.Start;
        var length = selection.IsEmpty ? audio.Frames : selection.Length;
        var maxTail = (long)(PluginEffect.MaxTail.TotalSeconds * audio.SampleRate);
        var range = new FrameRange(start, (int)Math.Min(length + maxTail, audio.Frames - start));
        await ApplyAsync(new PluginEffect(hosts, length), range, selection);
        await SavePluginSettingsAsync(_chain.Slots.ToList());
    }

    private async void OnClosePlugin(object? sender, RoutedEventArgs e) => await ClosePluginAsync();

    /// <summary>Remembers each plugin's settings for next time.</summary>
    private async Task SavePluginSettingsAsync(IReadOnlyList<ChainSlot> slots)
    {
        foreach (var slot in slots.Where(s => !s.Host.IsGone))
        {
            try
            {
                var settings = await Task.Run(() => slot.Host.GetSettings());
                PluginCatalog.SaveSettings(slot.Plugin, settings);
            }
            catch (PluginException ex)
            {
                ShowPluginProblem(ex);
            }
        }
    }

    /// <summary>Closes every plugin in the chain, remembering their settings.</summary>
    private async Task ClosePluginAsync()
    {
        if (_chain.Count == 0)
        {
            return;
        }

        StopPreview();
        var slots = _chain.Clear();
        ShowChain();
        await CloseSlotsAsync(slots);
    }

    private async Task CloseSlotsAsync(IReadOnlyList<ChainSlot> slots)
    {
        await SavePluginSettingsAsync(slots);
        await Task.Run(() =>
        {
            foreach (var slot in slots)
            {
                slot.Host.Dispose();
            }
        });
    }

    /// <summary>When the editor closes: saves the plugins' settings and closes them without holding the window up.</summary>
    private void ClosePluginInBackground()
    {
        var slots = _chain.Clear();
        _ = Task.Run(() =>
        {
            foreach (var slot in slots)
            {
                try
                {
                    if (!slot.Host.IsGone)
                    {
                        PluginCatalog.SaveSettings(slot.Plugin, slot.Host.GetSettings());
                    }
                }
                catch (PluginException)
                {
                    // Closing anyway.
                }

                slot.Host.Dispose();
            }
        });
    }

    /// <summary>Takes plugins that crashed or were stopped (after a failed Apply, say) out of the chain.</summary>
    private void ClosePluginIfGone()
    {
        var gone = _chain.RemoveGone();
        if (gone.Count > 0)
        {
            ShowChain();
            foreach (var slot in gone)
            {
                slot.Host.Dispose();
            }
        }
    }

    /// <summary>Explains a plugin problem, and takes the plugin out if it has stopped.</summary>
    private void ShowPluginProblem(PluginException problem)
    {
        ShowStatus(problem.Message);
        if (_chain.Slots.Any(s => s.Host.IsGone))
        {
            ClosePluginIfGone();
            _ = MessageWindow.ShowAsync(this, problem.Message, "Plugins");
        }
    }

    private void StopPreview()
    {
        if (_preview is not null)
        {
            StopPlayback();
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

    // ---- Saving and opening chains ----

    private void OnChainMenu(object? sender, RoutedEventArgs e)
    {
        var menu = MenuAbove();
        AddChainItems(menu.Items, saveable: _chain.Count > 0);
        menu.Open(ChainMenuButton);
    }

    private void AddChainItems(ItemCollection items, bool saveable)
    {
        var save = MenuEntry("Save this chain…", enabled: saveable,
            tip: "The plugins, their order, which are on, and all their settings, to open again any time");
        save.Click += async (_, _) => await SaveChainAsync();
        items.Add(save);

        var saved = ChainPreset.Saved();
        if (saved.Count > 0)
        {
            items.Add(new Separator());
            foreach (var (name, path) in saved)
            {
                var item = MenuEntry(name, tip: "Open this chain in place of the plugins open now");
                item.Click += async (_, _) => await OpenChainAsync(path);
                items.Add(item);
            }
        }

        items.Add(new Separator());
        var open = MenuEntry("Open a chain file…", tip: "A Lucent chain (.lucentchain) or a REAPER FX chain (.RfxChain)");
        open.Click += async (_, _) => await PickChainAsync(reaper: false);
        items.Add(open);
        var reaper = MenuEntry("Import a REAPER FX chain…",
            tip: "Brings in a chain saved in REAPER (.RfxChain): its VST3 plugins with their settings, in order");
        reaper.Click += async (_, _) => await PickChainAsync(reaper: true);
        items.Add(reaper);
        var folder = MenuEntry("Show the chains folder");
        folder.Click += (_, _) => ShowChainsFolder();
        items.Add(folder);
    }

    private async Task SaveChainAsync()
    {
        if (_chain.Count == 0)
        {
            return;
        }

        Directory.CreateDirectory(ChainPreset.Folder);
        IStorageFile? file;
        try
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the plugin chain",
                SuggestedFileName = string.Concat(string.Join(" + ", _chain.Slots.Select(s => s.Host.Name).Take(3))
                    .Where(c => !Path.GetInvalidFileNameChars().Contains(c))) + ChainPreset.Extension,
                DefaultExtension = ChainPreset.Extension,
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(ChainPreset.Folder),
                FileTypeChoices = new[] { new FilePickerFileType("Lucent plugin chain") { Patterns = new[] { "*" + ChainPreset.Extension } } },
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't ask where to save the chain ({ex.Message}).");
            return;
        }

        if (file?.TryGetLocalPath() is not { } path)
        {
            return;
        }

        var slots = _chain.Slots.ToList();
        var name = Path.GetFileNameWithoutExtension(path);
        if (await RunBusyAsync("Saving the chain…", async (_, _) =>
                await Task.Run(() => PluginChain.ToPreset(name, slots).Save(path))))
        {
            ShowStatus($"Saved the chain \"{name}\". Open it again from Chains.");
        }
    }

    private async Task PickChainAsync(bool reaper)
    {
        var reaperFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support",
            "REAPER", "FXChains");
        Directory.CreateDirectory(ChainPreset.Folder);
        var chosen = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = reaper ? "Choose a REAPER FX chain" : "Choose a plugin chain",
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                reaper && Directory.Exists(reaperFolder) ? reaperFolder : ChainPreset.Folder),
            FileTypeFilter = reaper
                ? new[] { new FilePickerFileType("REAPER FX chains") { Patterns = new[] { "*" + ReaperChain.Extension } } }
                : new[]
                {
                    new FilePickerFileType("Plugin chains") { Patterns = new[] { "*" + ChainPreset.Extension, "*" + ReaperChain.Extension } },
                    FilePickerFileTypes.All,
                },
        });
        if (chosen.Count > 0 && chosen[0].TryGetLocalPath() is { } path)
        {
            await OpenChainAsync(path);
        }
    }

    /// <summary>Opens a chain from a file in place of the plugins open now, and says what couldn't come over.</summary>
    private async Task OpenChainAsync(string path)
    {
        if (_session is not { } session)
        {
            return;
        }

        ChainPreset preset;
        try
        {
            preset = ChainPreset.Load(path);
        }
        catch (PluginException ex)
        {
            await MessageWindow.ShowAsync(this, ex.Message, "Plugin chains");
            return;
        }

        if (await EnsureCatalogAsync() is not { } catalog)
        {
            return;
        }

        var resolved = preset.Resolve(catalog.Plugins);
        var missing = resolved.Where(r => r.Plugin is null).Select(r => r.Wanted).ToList();
        if (missing.Count == resolved.Count)
        {
            await MessageWindow.ShowAsync(this,
                $"None of the plugins in \"{preset.Name}\" are installed on this Mac:\n\n" + Bullets(missing.Select(Named)), "Plugin chains");
            return;
        }

        await ClosePluginAsync();
        var audio = session.Audio;
        var opened = new List<ChainSlot>();
        var notes = new List<string>();
        await RunBusyAsync($"Opening \"{preset.Name}\"…", async (progress, token) =>
        {
            var found = resolved.Where(r => r.Plugin is not null).ToList();
            for (var i = 0; i < found.Count; i++)
            {
                token.ThrowIfCancellationRequested();
                var (wanted, plugin) = (found[i].Wanted, found[i].Plugin!);
                progress.Report((double)i / found.Count);
                try
                {
                    var refused = false;
                    var host = await Task.Run(() => OpenHost(plugin, wanted.SettingsBytes() ?? PluginCatalog.LoadSettings(plugin), audio, out refused));
                    opened.Add(new ChainSlot(plugin, host) { IsBypassed = wanted.Bypassed });
                    if (refused)
                    {
                        notes.Add($"{wanted.Name}: it didn't take the saved settings (a different version?), so it starts as new.");
                    }
                }
                catch (PluginException ex)
                {
                    notes.Add($"{wanted.Name}: {ex.Message}");
                }

                if (wanted.Note is { } note)
                {
                    notes.Add($"{wanted.Name}: {note}");
                }
            }
        });

        // Whatever opened stays open, even if Cancel was pressed part way.
        foreach (var slot in opened)
        {
            _chain.Add(slot);
        }

        ShowChain();
        if (opened.Count == 0)
        {
            return;
        }

        ShowStatus($"Opened \"{preset.Name}\": {opened.Count} plugin{(opened.Count == 1 ? string.Empty : "s")}.");
        if (preset.Note is { } left)
        {
            notes.Add(left);
        }

        if (missing.Count > 0)
        {
            notes.Insert(0, "Not installed here, so left out: " + string.Join(", ", missing.Select(Named)) + ".");
        }

        if (notes.Count > 0)
        {
            await MessageWindow.ShowAsync(this,
                $"\"{preset.Name}\" is open, with {opened.Count} of its {resolved.Count} plugins.\n\n" + Bullets(notes), "Plugin chains");
        }
    }

    private static string Named(ChainPresetPlugin plugin) =>
        string.IsNullOrWhiteSpace(plugin.Vendor) ? plugin.Name : $"{plugin.Name} ({plugin.Vendor})";

    private static string Bullets(IEnumerable<string> lines) => string.Join("\n", lines.Select(l => $"• {l}"));

    private void ShowChainsFolder()
    {
        try
        {
            Directory.CreateDirectory(ChainPreset.Folder);
            Process.Start(new ProcessStartInfo("open") { ArgumentList = { ChainPreset.Folder }, UseShellExecute = false });
        }
        catch (Exception)
        {
            ShowStatus($"The chains are in {ChainPreset.Folder}.");
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
