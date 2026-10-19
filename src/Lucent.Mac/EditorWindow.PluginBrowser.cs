using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using WinampInspiredVisualizer.Editor.Plugins;

namespace Lucent.Mac;

/// <summary>
/// The plugin browser, for adding a plugin to the chain. Typing finds plugins by name, maker, or type; the left
/// column narrows the list to the favorites or one type (EQ, Compressor, Reverb...); the list shows the plugins
/// under their types, or under their makers. The star beside a plugin makes it a favorite, and right-clicking one
/// gives it a different type. Clicking a plugin adds it to the end of the chain.
/// </summary>
public partial class EditorWindow
{
    private IReadOnlyList<PluginEntry> _pluginEntries = Array.Empty<PluginEntry>();
    /// <summary>The left column's choice: null for every plugin, <see cref="PluginBrowser.Favorites"/>, or a type.</summary>
    private string? _pluginSection;
    /// <summary>The rows made since the browser opened, by class ID, so a search only has to show or hide them.</summary>
    private readonly Dictionary<string, Button> _pluginRows = new();
    private DateTime _pluginPopupClosedAt;

    private void ShowPluginBrowser(Control under)
    {
        // Clicking the button that opened it closes it (the click outside already did), rather than opening it again.
        if (_catalog is null || (PluginPopup.PlacementTarget == under && DateTime.UtcNow - _pluginPopupClosedAt < TimeSpan.FromMilliseconds(250)))
        {
            return;
        }

        // From the Plugins button it opens above the button; from a slot in the strip, below the slot.
        PluginPopup.IsOpen = false;
        PluginPopup.PlacementTarget = under;
        PluginPopup.Placement = under == PluginsButton ? PlacementMode.TopEdgeAlignedLeft : PlacementMode.BottomEdgeAlignedLeft;
        PluginPopup.VerticalOffset = under == PluginsButton ? -6 : 2;
        ShowPluginActions(under);
        RefreshPluginBrowser();
        PluginScroller.ScrollToHome();
        PluginPopup.IsOpen = true;

        // Ready to type straight away. An earlier search is selected, so typing replaces it.
        Dispatcher.UIThread.Post(() =>
        {
            if (PluginPopup.IsOpen)
            {
                PluginSearchBox.Focus();
                PluginSearchBox.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void OnPluginPopupClosed(object? sender, EventArgs e) => _pluginPopupClosedAt = DateTime.UtcNow;

    /// <summary>Reads the plugins, stars, and types again, and shows them.</summary>
    private void RefreshPluginBrowser()
    {
        if (_catalog is not { } catalog)
        {
            return;
        }

        _pluginEntries = PluginBrowser.Entries(catalog.Plugins, _settings);
        _pluginRows.Clear();
        if (_pluginSection is not (null or PluginBrowser.Favorites) && !_pluginEntries.Any(e => e.Type == _pluginSection))
        {
            _pluginSection = null;
        }

        PluginGroupingButton.Content = _settings.PluginsByMaker ? "BY MAKER" : "BY TYPE";
        ToolTip.SetTip(PluginGroupingButton, _settings.PluginsByMaker
            ? "Listed under their makers. Click to list them under their types."
            : "Listed under their types. Click to list them under their makers.");
        ShowPluginSections();
        ShowPluginList();
    }

    private void ShowPluginSections()
    {
        PluginSections.Children.Clear();
        PluginSections.Children.Add(SectionButton("All plugins", null, _pluginEntries.Count));
        PluginSections.Children.Add(SectionButton(PluginBrowser.Favorites, PluginBrowser.Favorites, _pluginEntries.Count(e => e.IsFavorite)));
        PluginSections.Children.Add(new TextBlock
        {
            Text = "TYPES",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 12, 0, 4),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        foreach (var type in _pluginEntries.GroupBy(e => e.Type).OrderBy(g => PluginBrowser.Order(g.Key)))
        {
            PluginSections.Children.Add(SectionButton(type.Key, type.Key, type.Count()));
        }
    }

    private Button SectionButton(string label, string? section, int count)
    {
        var number = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("FaintTextBrush"),
        };
        DockPanel.SetDock(number, Dock.Right);
        var content = new DockPanel();
        content.Children.Add(number);
        content.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("TextBrush"),
        });
        var button = new Button { Theme = FindTheme("PluginRow"), Content = content, Focusable = false };
        if (section == _pluginSection)
        {
            button.Background = ThemeBrush("AccentSoftBrush");
        }

        button.Click += (_, _) =>
        {
            _pluginSection = section;
            ShowPluginSections();
            ShowPluginList();
            PluginScroller.ScrollToHome();
        };
        return button;
    }

    /// <summary>The plugins in the chosen section that match the search, under their headings.</summary>
    private void ShowPluginList()
    {
        var search = (PluginSearchBox.Text ?? string.Empty).Trim();
        ClearPluginSearchButton.IsVisible = search.Length > 0;
        var shown = _pluginEntries.Where(e => PluginBrowser.InSection(e, _pluginSection) && PluginBrowser.Matches(e, search)).ToList();
        PluginCount.Text = shown.Count == _pluginEntries.Count
            ? $"{_pluginEntries.Count} plugin{(_pluginEntries.Count == 1 ? string.Empty : "s")}"
            : $"{shown.Count} of {_pluginEntries.Count}";

        PluginRows.Children.Clear();
        if (shown.Count == 0)
        {
            PluginRows.Children.Add(new TextBlock
            {
                Theme = FindTheme("OptionHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 6, 8, 0),
                Text = _pluginEntries.Count == 0
                    ? $"No VST3 effects were found. Lucent looks in {string.Join(" and ", PluginCatalog.StandardFolders)}. " +
                      "If yours are somewhere else, add their folder below."
                    : _pluginSection == PluginBrowser.Favorites && search.Length == 0
                        ? "No favorites yet. Click the star beside a plugin to keep it here."
                        : "No plugins match. Try fewer words, or look in All plugins.",
            });
            return;
        }

        foreach (var (heading, entries) in PluginBrowser.Group(shown, _settings.PluginsByMaker))
        {
            PluginRows.Children.Add(PluginHeading(heading, entries.Count, first: PluginRows.Children.Count == 0));
            foreach (var entry in entries)
            {
                PluginRows.Children.Add(PluginRow(entry));
            }
        }
    }

    private DockPanel PluginHeading(string heading, int count, bool first)
    {
        var number = new TextBlock
        {
            Text = count.ToString(),
            FontSize = 11,
            Margin = new Thickness(8, 0, 0, 0),
            Foreground = ThemeBrush("FaintTextBrush"),
        };
        DockPanel.SetDock(number, Dock.Right);
        var row = new DockPanel { Margin = new Thickness(8, first ? 2 : 12, 8, 4) };
        row.Children.Add(number);
        row.Children.Add(new TextBlock
        {
            Text = heading,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        return row;
    }

    private Button PluginRow(PluginEntry entry)
    {
        var plugin = entry.Plugin;
        if (_pluginRows.TryGetValue(plugin.ClassId, out var made))
        {
            return made;
        }

        var star = new Button
        {
            Theme = FindTheme("IconButton"),
            Width = 24,
            Height = 24,
            Margin = new Thickness(6, -4, -4, -4),
            Focusable = false,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = ThemeIcon("StarIcon"),
                Stroke = ThemeBrush(entry.IsFavorite ? "AccentBrush" : "FaintTextBrush"),
                Fill = entry.IsFavorite ? ThemeBrush("AccentBrush") : Brushes.Transparent,
                StrokeThickness = 1.2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
            },
        };
        ToolTip.SetTip(star, entry.IsFavorite ? "Remove it from your favorites" : "Add it to your favorites");
        star.Click += (_, e) =>
        {
            // The row is a button too; the star's click is only the star's.
            e.Handled = true;
            SetPluginFavorite(entry, !entry.IsFavorite);
        };

        // Under a type, the row says who made it; under a maker, what type it is.
        var detail = new TextBlock
        {
            Text = _settings.PluginsByMaker ? entry.Type : entry.Maker,
            FontSize = 11,
            MaxWidth = 170,
            Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("FaintTextBrush"),
        };
        DockPanel.SetDock(star, Dock.Right);
        DockPanel.SetDock(detail, Dock.Right);
        var content = new DockPanel();
        content.Children.Add(star);
        content.Children.Add(detail);
        content.Children.Add(new TextBlock
        {
            Text = plugin.Name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("TextBrush"),
        });

        var row = new Button { Theme = FindTheme("PluginRow"), Content = content, Tag = entry };
        ToolTip.SetTip(row, $"{plugin.Name} by {entry.Maker}: {entry.Type}{(entry.TypeIsChosen ? " (your choice)" : string.Empty)}, version {plugin.Version}. " +
                            "Click to add it to the chain; right-click to change its type.");
        row.Click += async (_, _) => await AddFromBrowserAsync(plugin);
        row.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ShowPluginRowMenu(entry, row);
        };
        _pluginRows[plugin.ClassId] = row;
        return row;
    }

    private async Task AddFromBrowserAsync(PluginInfo plugin)
    {
        PluginPopup.IsOpen = false;
        await AddPluginAsync(plugin);
    }

    private void ShowPluginRowMenu(PluginEntry entry, Button row)
    {
        var plugin = entry.Plugin;
        var menu = new ContextMenu();

        var add = new MenuItem { Header = "Add to the chain" };
        add.Click += async (_, _) => await AddFromBrowserAsync(plugin);
        menu.Items.Add(add);

        var favorite = new MenuItem { Header = entry.IsFavorite ? "Remove from favorites" : "Add to favorites" };
        favorite.Click += (_, _) => SetPluginFavorite(entry, !entry.IsFavorite);
        menu.Items.Add(favorite);

        var type = new MenuItem { Header = "Type" };
        ToolTip.SetTip(type, "Where it's listed. Lucent works it out from the plugin; choose another if it's wrong.");
        var automatic = new MenuItem
        {
            Header = $"Automatic ({PluginBrowser.Guess(plugin)})",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = !entry.TypeIsChosen,
        };
        automatic.Click += (_, _) => SetPluginType(entry, null);
        type.Items.Add(automatic);
        type.Items.Add(new Separator());
        foreach (var choice in PluginBrowser.Types)
        {
            var item = new MenuItem { Header = choice, ToggleType = MenuItemToggleType.CheckBox, IsChecked = entry.TypeIsChosen && entry.Type == choice };
            item.Click += (_, _) => SetPluginType(entry, choice);
            type.Items.Add(item);
        }

        menu.Items.Add(type);

        // The browser would close on the click otherwise.
        PluginPopup.IsLightDismissEnabled = false;
        menu.Closed += (_, _) => PluginPopup.IsLightDismissEnabled = true;
        menu.Open(row);
    }

    private void SetPluginFavorite(PluginEntry entry, bool favorite)
    {
        PluginBrowser.SetFavorite(_settings, entry.Plugin, favorite);
        _settings.Save();
        ShowStatus(favorite ? $"{entry.Plugin.Name} is a favorite." : $"{entry.Plugin.Name} isn't a favorite any more.");
        RefreshKeepingPlace();
    }

    private void SetPluginType(PluginEntry entry, string? type)
    {
        PluginBrowser.SetType(_settings, entry.Plugin, type);
        _settings.Save();
        RefreshKeepingPlace();
    }

    /// <summary>Shows a change to a star or type without scrolling the list away from where it was.</summary>
    private void RefreshKeepingPlace()
    {
        var offset = PluginScroller.Offset;
        RefreshPluginBrowser();
        PluginScroller.UpdateLayout();
        PluginScroller.Offset = offset;
    }

    /// <summary>Along the bottom: the chains (from the Plugins button), looking for plugins again, and where they're looked for.</summary>
    private void ShowPluginActions(Control under)
    {
        PluginActions.Children.Clear();
        if (under == PluginsButton)
        {
            PluginActions.Children.Add(ActionPill("CHAINS", "Save this chain, or open a saved one (Lucent's own, or REAPER's .RfxChain files)", () =>
            {
                PluginPopup.IsOpen = false;
                var menu = MenuAbove();
                AddChainItems(menu.Items, saveable: _chain.Count > 0);
                menu.Open(PluginsButton);
            }));
        }

        PluginActions.Children.Add(ActionPill("LOOK AGAIN", "Look for plugins again, after installing or updating some", async () =>
        {
            PluginPopup.IsOpen = false;
            if (await ScanPluginsAsync(everything: true))
            {
                _pluginPopupClosedAt = DateTime.MinValue;
                ShowPluginBrowser(under);
            }
        }));
        PluginActions.Children.Add(ActionPill("ADD FOLDER…", "If your plugins are somewhere other than the usual VST3 folders", async () =>
        {
            PluginPopup.IsOpen = false;
            await AddPluginFolderAsync(under);
        }));

        var failures = _catalog?.Failures ?? Array.Empty<(string, string)>();
        if (failures.Count > 0)
        {
            PluginActions.Children.Add(ActionPill($"COULDN'T LOAD ({failures.Count})…", "Plugin files that couldn't be loaded, and why", async () =>
            {
                PluginPopup.IsOpen = false;
                await ShowPluginFailuresAsync(failures);
            }));
        }
    }

    private Button ActionPill(string label, string tip, Action click)
    {
        var button = new Button { Theme = FindTheme("PillButton"), Content = label, Margin = new Thickness(0, 0, 6, 0) };
        ToolTip.SetTip(button, tip);
        button.Click += (_, _) => click();
        return button;
    }

    private void OnPluginGrouping(object? sender, RoutedEventArgs e)
    {
        _settings.PluginsByMaker = !_settings.PluginsByMaker;
        _settings.Save();
        RefreshPluginBrowser();
        PluginScroller.ScrollToHome();
    }

    private void OnPluginSearchChanged(object? sender, TextChangedEventArgs e)
    {
        if (PluginPopup.IsOpen)
        {
            ShowPluginList();
            PluginScroller.ScrollToHome();
        }
    }

    private void OnClearPluginSearch(object? sender, RoutedEventArgs e)
    {
        PluginSearchBox.Clear();
        PluginSearchBox.Focus();
    }

    private IReadOnlyList<Button> ShownPluginRows => PluginRows.Children.OfType<Button>().ToList();

    private async void OnPluginSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Adds the first match, like clicking it.
                e.Handled = true;
                if (ShownPluginRows.FirstOrDefault()?.Tag is PluginEntry first)
                {
                    await AddFromBrowserAsync(first.Plugin);
                }

                break;
            case Key.Escape when !string.IsNullOrEmpty(PluginSearchBox.Text):
                PluginSearchBox.Clear();
                e.Handled = true;
                break;
            case Key.Down when ShownPluginRows.Count > 0:
                // Into the list, to choose with the arrow keys and Return.
                ShownPluginRows[0].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Up and Down move through the list (Up from the top goes back to the search); Esc closes the browser.</summary>
    private void OnPluginBrowserKeyDown(object? sender, KeyEventArgs e)
    {
        // (Esc in the search clears it first.)
        if (e.Key == Key.Escape && !(e.Source is TextBox { Text.Length: > 0 }))
        {
            PluginPopup.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down) || e.Source is not Button focused)
        {
            return;
        }

        var rows = ShownPluginRows.ToList();
        var index = rows.IndexOf(focused);
        if (index < 0)
        {
            return;
        }

        var next = index + (e.Key == Key.Down ? 1 : -1);
        if (next < 0)
        {
            PluginSearchBox.Focus();
        }
        else if (next < rows.Count)
        {
            rows[next].Focus(NavigationMethod.Directional);
            rows[next].BringIntoView();
        }

        e.Handled = true;
    }
}
