using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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
/// the channel strip beside the waveform shows them as insert slots, top to bottom in the order the sound goes
/// through them. Each slot switches its plugin on or off, moves it, or takes it out; empty slots add one. Play goes
/// through the chain (unless it's bypassed); the strip applies it as one step, and saves or opens chains (Lucent's own, or REAPER's .RfxChain files).
/// </summary>
public partial class EditorWindow
{
    /// <summary>The strip always shows at least this many slots, so it reads as a channel strip with room to add.</summary>
    private const int MinimumSlots = 6;

    private readonly PluginChain _chain = new();
    private PluginCatalog? _catalog;
    private PluginPreviewProvider? _preview;
    /// <summary>What playback is going through (a plugin or a tool), so a click while playing carries on through it.</summary>
    private Func<ISampleProvider, ISampleProvider>? _previewThrough;
    /// <summary>Where playback was asked to stop, so it can carry on to there after the chain changes.</summary>
    private int _playTo;
    private int _playRequests;
    /// <summary>Bypass in the strip: playback leaves out every plugin. Read on the audio thread.</summary>
    private volatile bool _chainBypassed;

    // ---- Choosing a plugin ----

    private async void OnPlugins(object? sender, RoutedEventArgs e) => await ShowPluginBrowserAsync(PluginsButton);

    private async Task ShowPluginBrowserAsync(Control under)
    {
        if (await EnsureCatalogAsync() is not null)
        {
            ShowPluginBrowser(under);
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
            ShowPluginBrowser(under);
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

        var wasAt = PauseForChain();
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
            ResumeAfterChain(wasAt);
            return;
        }

        var slot = new ChainSlot(plugin, host);
        _chain.Add(slot);
        ShowInserts(true);
        ResumeAfterChain(wasAt);
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

    private void OnInsertsToggle(object? sender, RoutedEventArgs e) => ShowInserts(!PluginBar.IsVisible);

    /// <summary>Shows or hides the channel strip, and remembers which for next time. The plugins stay open either way.</summary>
    private void ShowInserts(bool show)
    {
        if (_settings.ShowInserts != show)
        {
            _settings.ShowInserts = show;
            _settings.Save();
        }

        InsertsToggle.IsChecked = show;
        PluginBar.IsVisible = show;
        ShowChain();
    }

    /// <summary>Fills the channel strip: a slot for each plugin in the chain, then empty slots to add more.</summary>
    private void ShowChain()
    {
        ChainPanel.Children.Clear();
        var slots = _chain.Slots;
        for (var i = 0; i < slots.Count; i++)
        {
            ChainPanel.Children.Add(InsertSlot(slots[i], i, slots.Count));
        }

        for (var i = slots.Count; i < Math.Max(MinimumSlots, slots.Count + 1); i++)
        {
            ChainPanel.Children.Add(EmptySlot(i, first: i == slots.Count));
        }

        InsertsToggle.Content = slots.Count == 0 ? "Inserts" : $"Inserts ({slots.Count})";
        ChainBypassToggle.IsEnabled = slots.Count > 0;
        PluginApplyButton.IsEnabled = slots.Count > 0;
        var on = slots.Count(s => !s.IsBypassed);
        PluginHint.Text = slots.Count switch
        {
            0 => "Click an empty slot to add a VST3 plugin. Play then goes through the slots from the top down.",
            _ when _chainBypassed => "Bypassed: Play leaves the plugins out, to compare. Switch Bypass off to hear them again.",
            1 when slots[0].Host.HasWindow =>
                "Play goes through it, and changes in its window are heard as you make them (click its name to bring the window back). " +
                "Apply makes it part of the sound.",
            1 => "This plugin has no window of its own, so it runs with its standard settings. Play to hear it, Apply to make it part of the sound.",
            _ => $"Play goes through them from the top down; {(on == slots.Count ? "all are on" : $"{on} of {slots.Count} are on")}. " +
                 "Apply makes the ones that are on part of the sound, as one step.",
        };
    }

    private Control InsertSlot(ChainSlot slot, int index, int count)
    {
        var power = new ToggleButton { Theme = FindTheme("SlotPower"), IsChecked = !slot.IsBypassed };
        ToolTip.SetTip(power, "Switch it off to hear the chain without it (even while the preview plays). Apply leaves out plugins that are off.");
        power.Click += (_, _) =>
        {
            slot.IsBypassed = power.IsChecked != true;
            ShowChain();
            ShowStatus(slot.IsBypassed ? $"{slot.Host.Name} is off." : $"{slot.Host.Name} is on.");
        };
        var name = new Button
        {
            Theme = FindTheme("SlotName"),
            Content = new TextBlock
            {
                Text = slot.Host.Name,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Foreground = ThemeBrush("TextBrush"),
                FontSize = 12,
            },
            Opacity = slot.IsBypassed ? 0.45 : 1,
        };
        ToolTip.SetTip(name, slot.Host.HasWindow ? $"{slot.Title}: show its window" : $"{slot.Title} has no window of its own");
        name.Click += async (_, _) => await ShowPluginWindowAsync(slot);

        var number = SlotNumber(index);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        buttons.Children.Add(ChipButton("ChevronUpIcon", "Move it up, so the sound reaches it sooner", index > 0, () => MoveInChain(index, index - 1)));
        buttons.Children.Add(ChipButton("ChevronDownIcon", "Move it down, so the sound reaches it later", index < count - 1, () => MoveInChain(index, index + 1)));
        buttons.Children.Add(ChipButton("CloseIcon", "Take it out of the chain (its settings are remembered)", true, async () => await RemoveFromChainAsync(slot)));
        DockPanel.SetDock(number, Dock.Left);
        DockPanel.SetDock(power, Dock.Left);
        DockPanel.SetDock(buttons, Dock.Right);
        var row = new DockPanel { LastChildFill = true };
        row.Children.Add(number);
        row.Children.Add(power);
        row.Children.Add(buttons);
        row.Children.Add(name);
        return new Border
        {
            Child = row,
            Height = 34,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(4, 0, 2, 0),
            Margin = new Thickness(0, 0, 0, 4),
            Background = ThemeBrush("SurfaceRaisedBrush"),
            BorderBrush = ThemeBrush(slot.IsBypassed ? "HairlineBrush" : "AccentBrush"),
            BorderThickness = new Thickness(1),
        };
    }

    private Button EmptySlot(int index, bool first)
    {
        var content = new DockPanel();
        var number = SlotNumber(index);
        DockPanel.SetDock(number, Dock.Left);
        content.Children.Add(number);
        content.Children.Add(new TextBlock
        {
            Text = first ? "+  Add plugin" : string.Empty,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0),
        });
        var button = new Button { Theme = FindTheme("EmptySlot"), Content = content };
        ToolTip.SetTip(button, "Add a VST3 plugin at the end of the chain");
        button.Click += async (_, _) => await ShowPluginBrowserAsync(button);
        return button;
    }

    private TextBlock SlotNumber(int index) => new()
    {
        Text = (index + 1).ToString(),
        Width = 16,
        FontSize = 11,
        TextAlignment = TextAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = ThemeBrush("FaintTextBrush"),
    };

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
        var wasAt = PauseForChain();
        _chain.Move(from, to);
        ShowChain();
        ResumeAfterChain(wasAt);
    }

    private async Task RemoveFromChainAsync(ChainSlot slot)
    {
        var index = _chain.Slots.ToList().IndexOf(slot);
        if (index < 0)
        {
            return;
        }

        var wasAt = PauseForChain();
        _chain.RemoveAt(index);
        ShowChain();
        ResumeAfterChain(wasAt);
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

    /// <summary>
    /// Plays part of the sound through the plugins in the strip, so what's heard is what Apply would make; with no
    /// plugins open, or Bypass on, it plays as it is. Changes in the plugins' windows, and switching one off, are
    /// heard straight away.
    /// </summary>
    private async void Play(int from, int to)
    {
        if (_chain.Count == 0 || _toolPreview is not null)
        {
            PlayFrom(from, to);
            return;
        }

        if (_session is null || IsBusy)
        {
            return;
        }

        // Only the latest press plays, if Play is pressed again while the plugins are getting ready.
        var request = ++_playRequests;
        StopPlayback();
        var slots = _chain.Slots.ToList();
        try
        {
            // Starting again clears what's left in each plugin from before (an echo still ringing).
            var stages = await Task.Run(() => slots.Select(slot =>
                new PreviewStage(slot.Host, slot.Host.Start(offline: false).Generation, () => _chainBypassed || slot.IsBypassed)).ToList());
            if (request == _playRequests)
            {
                // The listening volume is applied after the meters, so the plugins get the audio at its own level.
                PlayFrom(from, to, source => _preview = new PluginPreviewProvider(source, stages, 1f));
            }
        }
        catch (PluginException ex)
        {
            ShowPluginProblem(ex);
        }
    }

    private void OnChainBypass(object? sender, RoutedEventArgs e) => SetChainBypassed(ChainBypassToggle.IsChecked == true);

    /// <summary>Plays without the plugins, to compare, or with them again; heard at once, even while playing.</summary>
    private void SetChainBypassed(bool bypassed)
    {
        _chainBypassed = bypassed;
        ChainBypassToggle.IsChecked = bypassed;
        ShowChain();
        if (_chain.Count > 0)
        {
            ShowStatus(bypassed ? "Bypassed: playing without the plugins." : "Playing through the plugins again.");
        }
    }

    /// <summary>Stops playback while the chain changes, saying where it was so it can carry on through the new chain.</summary>
    private int? PauseForChain()
    {
        if (!_player.IsPlaying || _toolPreview is not null)
        {
            StopPreview();
            return null;
        }

        var at = _player.PositionFrame;
        StopPlayback();
        return at;
    }

    private void ResumeAfterChain(int? at)
    {
        if (at is { } frame && _session is not null)
        {
            Play(frame, Math.Max(_playTo, frame));
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

    /// <summary>When playback stops: reports a plugin that failed while playing.</summary>
    private void ShowPreviewStopped()
    {
        var failed = _preview?.Error;
        _preview = null;
        _previewThrough = null;
        if (failed is PluginException problem)
        {
            ShowPluginProblem(problem);
        }
    }

    // ---- Saving and opening chains ----

    private void OnChainMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { Placement = PlacementMode.BottomEdgeAlignedRight, VerticalOffset = 2 };
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
        if (saveable)
        {
            items.Add(new Separator());
            var close = MenuEntry("Close every plugin", tip: "Empties the strip; each plugin's settings are remembered for next time");
            close.Click += async (_, _) => await ClosePluginAsync();
            items.Add(close);
        }
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

        ShowInserts(true);
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
