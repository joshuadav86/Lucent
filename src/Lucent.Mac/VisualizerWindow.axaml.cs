using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Visualizer;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Library;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac;

/// <summary>The visuals, in their own window with the visual picker and Auto controls underneath.</summary>
public partial class VisualizerWindow : ToolWindow
{
    // How often Auto can change visual, offered in the menu next to the AUTO pill (and in Options).
    internal static readonly int[] AutoCycleChoices = { 10, 15, 25, 45, 90, 180 };

    /// <summary>A tag no visual in the chosen list has is drawn faint.</summary>
    public static readonly IValueConverter FaintWhenNone = new FuncValueConverter<int, double>(count => count == 0 ? 0.45 : 1.0);

    /// <summary>The tooltip on a visual's star in the picker.</summary>
    public static readonly IValueConverter StarTip = new FuncValueConverter<bool, string>(favorite => favorite ? "Remove from favorites" : "Favorite");

    private readonly ObservableCollection<object> _rows = new();
    private readonly Dictionary<string, VisualFamilyFolder> _folders = new(StringComparer.OrdinalIgnoreCase);
    private VisualizerHost? _host;
    private AppSettings _settings = new();
    private IReadOnlyList<string> _presetNames = Array.Empty<string>();
    private List<VisualChoice> _choices = new();
    private List<VisualChoice> _shown = new();
    private List<VisualTag> _tags = new();
    private HashSet<string> _tickedTags = new(StringComparer.OrdinalIgnoreCase);
    private HashSet<string> _collapsedFamilies = new(StringComparer.OrdinalIgnoreCase);
    private string[] _searchWords = Array.Empty<string>();
    private DateTime _presetPopupClosedAt;
    private bool _isFullscreen;
    private WindowState _stateBeforeFullscreen;
    private PixelPoint _positionBeforeFullscreen;
    private Size _sizeBeforeFullscreen;
    private readonly VisualHistory _history = new();

    public VisualizerWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        PlacementName = "Visuals";
        PresetList.ItemsSource = _rows;
        PresetList.ContainerPrepared += OnPresetRowPrepared;
        PresetList.SelectionChanged += OnPresetListSelectionChanged;
        PresetList.AddHandler(PointerReleasedEvent, OnPresetListClick, RoutingStrategies.Bubble, handledEventsToo: true);
        PresetList.ContextRequested += OnPresetListMenu;
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        AutoCycleButton.AddHandler(PointerReleasedEvent, OnAutoCycleRightClick, RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowStateProperty)
            {
                OnWindowStateChanged();
            }
        };
    }

    public bool IsFullscreen => _isFullscreen;

    /// <summary>Puts the visualizer in this window. Called once, before the window is first shown.</summary>
    public void Attach(VisualizerHost host, AppSettings settings)
    {
        _host = host;
        _settings = settings;
        _collapsedFamilies = new HashSet<string>(settings.CollapsedVisualFamilies, StringComparer.OrdinalIgnoreCase);
        host.PresetsLoaded += (_, names) =>
        {
            _presetNames = names;
            BuildChoices();
        };
        host.ProblemReported += (_, message) =>
        {
            VisualizerStatus.Text = message;
            ToolTip.SetTip(VisualizerStatus, message);
        };
        host.DoubleClicked += (_, _) => OnVisualsDoubleClicked();
        host.RightClicked += (_, _) => ShowVisualsMenu();
        host.PresetChanged += (_, _) => ShowPreset();
        host.FrameRateMeasured += (_, stats) =>
            FrameRateText.Text = $"{stats.FramesPerSecond:0} fps · {stats.Width} × {stats.Height}" + (stats.Hdr ? " · HDR" : string.Empty);
        VisualizerFrame.Child = host;
        UpdateAutoCycleButton();
        UpdatePreviousButton();
        UpdateGroupFamiliesButton();
        SortLabel.Text = VisualSorts.Label(settings.VisualSort);
    }

    /// <summary>Shows or hides the frame rate readout, following the setting in Options.</summary>
    public void ShowFrameRate(bool show) => FrameRateText.IsVisible = show;

    /// <summary>Shows the window, keeping it with the player it belongs to.</summary>
    private void ShowWindow()
    {
        if (Owner is Window owner)
        {
            Show(owner);
        }
        else if (Player is { IsVisible: true } player)
        {
            Show(player);
        }
        else
        {
            Show();
        }
    }

    // ---- Picking visuals ----

    private void OnNextPreset(object? sender, RoutedEventArgs e) => ChangePreset(+1);

    // Why the visuals hear nothing (the controlled app playing on another device), or null.
    private string? _elsewhere;

    /// <summary>Raised when "Play on this Mac" is pressed, to move the controlled app's playback here.</summary>
    public event EventHandler? PlayHereRequested;

    /// <summary>
    /// Says, under the picture, why the visuals hear nothing: the controlled app is playing on another device
    /// (<paramref name="note"/>), with a button to move it here when <paramref name="canPlayHere"/>. Null hides it.
    /// </summary>
    public void ShowElsewhere(string? note, bool canPlayHere)
    {
        _elsewhere = note;
        ElsewhereText.Text = note ?? string.Empty;
        PlayHereButton.IsVisible = canPlayHere;
        UpdateElsewhereBar();
    }

    // Hidden in full screen, with the rest of the window's bars.
    private void UpdateElsewhereBar() => ElsewhereBar.IsVisible = _elsewhere is not null && !_isFullscreen;

    private void OnPlayHere(object? sender, RoutedEventArgs e) => PlayHereRequested?.Invoke(this, EventArgs.Empty);

    private void OnPreviousPreset(object? sender, RoutedEventArgs e) => PreviousPreset();

    /// <summary>Previous visual (B): back to the one shown before, or up the list, as picked in Options.</summary>
    public void PreviousPreset() => PreviousPreset(_settings.PreviousVisual);

    public void PreviousPreset(PreviousVisualMode mode)
    {
        if (_host is null || _presetNames.Count == 0)
        {
            return;
        }

        // With nothing further back to go to, it steps up the list instead.
        if (mode == PreviousVisualMode.LastShown && _history.Back(name => FindChoice(name) is not null) is { } name)
        {
            _host.PresetIndex = FindChoice(name)!.Index;
            ShowPreset();
            return;
        }

        ChangePreset(-1);
    }

    private VisualChoice? FindChoice(string name) =>
        _choices.Find(choice => string.Equals(choice.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Says on the ← button which way it goes.</summary>
    public void UpdatePreviousButton() =>
        ToolTip.SetTip(PreviousButton, _settings.PreviousVisual == PreviousVisualMode.LastShown
            ? "Back to the visual shown before this one (B). Options > Visuals can make it go up the list instead"
            : "Previous visual in the list (B). Options > Visuals can make it go back to the one shown before instead");

    /// <summary>Steps to the next or previous visual in the list the picker is showing.</summary>
    public void ChangePreset(int step)
    {
        if (_host is null || _presetNames.Count == 0)
        {
            return;
        }

        var shown = _shown.Count > 0 ? _shown : _choices;
        var position = shown.FindIndex(choice => choice.Index == _host.PresetIndex);
        position = position < 0
            ? (step > 0 ? 0 : shown.Count - 1)
            : ((position + step) % shown.Count + shown.Count) % shown.Count;
        _host.PresetIndex = shown[position].Index;
        ShowPreset();
    }

    private void ShowPreset()
    {
        if (_host is null || _presetNames.Count == 0)
        {
            PresetName.Text = "No visuals found";
            return;
        }

        var index = Math.Clamp(_host.PresetIndex, 0, _presetNames.Count - 1);
        var current = _choices[index];
        PresetName.Text = current.Label;
        PresetList.SelectedItem = _shown.Contains(current) && _rows.Contains(current) ? current : null;
        _settings.LastVisual = current.Name;
        _history.Shown(current.Name);
        UpdateFavoriteButton();
    }

    private void OnTogglePresetPicker(object? sender, RoutedEventArgs e) => TogglePresetPicker();

    public void TogglePresetPicker()
    {
        // Clicking the name while the picker is open closes it (the click outside already did),
        // so don't immediately reopen it.
        if (PresetPopup.IsOpen || DateTime.UtcNow - _presetPopupClosedAt < TimeSpan.FromMilliseconds(250))
        {
            PresetPopup.IsOpen = false;
            return;
        }

        if (_presetNames.Count == 0)
        {
            return;
        }

        if (!IsVisible)
        {
            ShowWindow();
        }

        // Typing goes to the window that's active, so make it this one (V may have been pressed in the player).
        if (!IsActive)
        {
            Activate();
        }

        // Visuals picked or starred since it was last open move up those orders. (Not while it's open,
        // so a row doesn't jump away from under the pointer.)
        if (_settings.VisualSort is VisualSort.RecentlyUsed or VisualSort.FavoritesFirst)
        {
            ShowActiveList();
        }

        PresetPopup.IsOpen = true;
        if (PresetList.SelectedItem is { } selected)
        {
            PresetList.ScrollIntoView(selected);
        }

        // Ready to type a search straight away, once the picker is on screen. Any earlier search is
        // selected, so typing replaces it.
        Dispatcher.UIThread.Post(() =>
        {
            if (PresetPopup.IsOpen)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        }, DispatcherPriority.Input);
    }

    private void OnPresetPopupClosed(object? sender, EventArgs e) => _presetPopupClosedAt = DateTime.UtcNow;

    /// <summary>Keeps the picker open while a menu or question that belongs to it is up (it would close on the click otherwise).</summary>
    private void KeepPickerOpen(bool keep)
    {
        PresetPopup.IsLightDismissEnabled = !keep;
    }

    private void OnPresetListClick(object? sender, PointerReleasedEventArgs e)
    {
        // A click on a row's star (or a folder's heading) is that button's, and leaves the picker open.
        if (e.InitialPressMouseButton != MouseButton.Left || e.KeyModifiers.HasFlag(KeyModifiers.Control)
            || e.Source is not Visual source || IsInsideButton(source))
        {
            return;
        }

        if (source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is { DataContext: VisualChoice choice })
        {
            PickPreset(choice);
        }
    }

    private bool IsInsideButton(Visual source)
    {
        for (var element = source; element is not null && element != PresetList; element = element.GetVisualParent())
        {
            if (element is Button or ToggleButton)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPresetListKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PresetList.SelectedItem is VisualChoice choice)
        {
            PickPreset(choice);
            e.Handled = true;
        }
    }

    /// <summary>Folder headings are rows of the list too, but they can't be picked: the arrow keys step over them.</summary>
    private void OnPresetListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (PresetList.SelectedItem is not VisualFamilyFolder)
        {
            return;
        }

        var from = e.RemovedItems.Count > 0 ? _rows.IndexOf(e.RemovedItems[0]!) : -1;
        var at = PresetList.SelectedIndex;
        var step = at < from ? -1 : 1;
        for (var index = at + step; index >= 0 && index < _rows.Count; index += step)
        {
            if (_rows[index] is VisualChoice)
            {
                PresetList.SelectedIndex = index;
                PresetList.ScrollIntoView(index);
                return;
            }
        }

        PresetList.SelectedIndex = from;
    }

    private void OnPresetRowPrepared(object? sender, ContainerPreparedEventArgs e)
    {
        e.Container.Classes.Set("folder", e.Container.DataContext is VisualFamilyFolder || _rows.ElementAtOrDefault(e.Index) is VisualFamilyFolder);
    }

    private void PickPreset(VisualChoice choice)
    {
        if (_host is not null)
        {
            _host.PresetIndex = choice.Index;
            ShowPreset();
        }

        VisualSorts.Remember(_settings.RecentVisuals, choice.Name);

        PresetPopup.IsOpen = false;
    }

    // ---- Favourites and lists of visuals ----

    private void BuildChoices()
    {
        var favorites = new HashSet<string>(_settings.FavoriteVisuals, StringComparer.OrdinalIgnoreCase);
        var presets = _host?.Presets ?? Array.Empty<PresetSource>();
        _choices = _presetNames.Select((name, index) =>
        {
            // Presets and names come from the same load, so they line up; the fallback is just in case.
            var preset = index < presets.Count ? presets[index] : null;
            var tags = (preset?.Tags ?? Array.Empty<string>()).OrderBy(VisualStyles.TagOrder).ThenBy(tag => tag, StringComparer.OrdinalIgnoreCase).ToList();
            return new VisualChoice(name, index, preset?.Family ?? VisualStyles.OtherFamily, tags, preset?.Author)
            {
                IsFavorite = favorites.Contains(name),
                Added = preset?.Added,
            };
        }).ToList();

        // One tick box for every tag some visual uses. Ticks saved for tags no visual has any more are dropped,
        // so they can't quietly hide everything.
        var ticked = new HashSet<string>(_settings.VisualTagFilter, StringComparer.OrdinalIgnoreCase);
        _tags = _choices.SelectMany(choice => choice.Tags)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(VisualStyles.TagOrder).ThenBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(tag => new VisualTag(tag) { IsChecked = ticked.Contains(tag) })
            .ToList();
        TagList.ItemsSource = _tags;
        _tickedTags = TickedTags();
        ShowActiveList();
    }

    /// <summary>The visuals in a list (by name), or null for "All visuals".</summary>
    private List<string>? NamesInList(string? list) =>
        list is null ? null
        : list == AppSettings.FavoritesListName ? _settings.FavoriteVisuals
        : _settings.VisualLists.TryGetValue(list, out var names) ? names
        : null;

    /// <summary>
    /// Fills the picker with the chosen list, narrowed to the ticked tags and sorted into family folders,
    /// and has Auto (and N and B) play from exactly what it shows.
    /// </summary>
    private void ShowActiveList()
    {
        var list = _settings.ActiveVisualList;
        if (list is not null && list != AppSettings.FavoritesListName && !_settings.VisualLists.ContainsKey(list))
        {
            // The list was deleted (or the settings were edited by hand).
            list = _settings.ActiveVisualList = null;
        }

        var names = NamesInList(list);

        // With MilkDrop or AVS presets switched off, they're left out of every list, and so out of Auto.
        var choices = _choices.Where(IsSwitchedOn).ToList();
        var presetsHidden = choices.Count < _choices.Count;
        List<VisualChoice> inList;
        if (names is null)
        {
            inList = choices;
        }
        else
        {
            var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            inList = choices.Where(choice => wanted.Contains(choice.Name)).ToList();
        }

        // Each tick box counts the visuals in this list (and matching the search) that have its tag.
        var searched = inList.Where(MatchesSearch).ToList();
        foreach (var tag in _tags)
        {
            tag.Count = searched.Count(choice => choice.Tags.Contains(tag.Name, StringComparer.OrdinalIgnoreCase));
        }

        _shown = InPickerOrder(searched.Where(MatchesTags));
        FillRows();
        ListPickerLabel.Text = list is null ? "All visuals" : $"{list} ({inList.Count})";
        VisualCount.Text = VisualChoice.CountText(_shown.Count, inList.Count);

        // Narrowed by a list, tags or a search, Auto plays from what's shown. If nothing is shown it plays from everything.
        var narrowed = names is not null || presetsHidden || _tickedTags.Count > 0 || _searchWords.Length > 0;
        if (_host is not null)
        {
            _host.AutoCycleNames = !narrowed || _shown.Count == 0 ? null : _shown.Select(choice => choice.Name).ToList();
        }

        ShowListHint(list, inList.Count, narrowed);
        ShowPreset();
    }

    /// <summary>
    /// The picker's rows: the shown visuals, each family's under a folder heading when they're grouped.
    /// A closed folder's visuals aren't rows until it's opened again.
    /// </summary>
    private void FillRows()
    {
        foreach (var folder in _folders.Values)
        {
            folder.PropertyChanged -= OnFolderChanged;
        }

        _folders.Clear();
        var rows = new List<object>();
        if (_settings.GroupVisualsByFamily)
        {
            foreach (var family in _shown.GroupBy(choice => choice.Family, StringComparer.OrdinalIgnoreCase))
            {
                var members = family.ToList();
                // While searching every folder is open, so no match is hidden in a closed one.
                var folder = new VisualFamilyFolder(family.Key, members,
                    _searchWords.Length > 0 || !_collapsedFamilies.Contains(family.Key));
                folder.PropertyChanged += OnFolderChanged;
                _folders[family.Key] = folder;
                rows.Add(folder);
                if (folder.IsExpanded)
                {
                    rows.AddRange(members);
                }
            }
        }
        else
        {
            rows.AddRange(_shown);
        }

        _rows.Clear();
        foreach (var row in rows)
        {
            _rows.Add(row);
        }
    }

    // Family folders remember whether they were left open or closed.
    private void OnFolderChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not VisualFamilyFolder folder || e.PropertyName != nameof(VisualFamilyFolder.IsExpanded))
        {
            return;
        }

        var at = _rows.IndexOf(folder);
        if (at < 0)
        {
            return;
        }

        if (folder.IsExpanded)
        {
            for (var i = 0; i < folder.Members.Count; i++)
            {
                _rows.Insert(at + 1 + i, folder.Members[i]);
            }
        }
        else
        {
            while (at + 1 < _rows.Count && _rows[at + 1] is VisualChoice)
            {
                _rows.RemoveAt(at + 1);
            }
        }

        if (_searchWords.Length == 0
            && (folder.IsExpanded ? _collapsedFamilies.Remove(folder.Name) : _collapsedFamilies.Add(folder.Name)))
        {
            SaveCollapsedFamilies();
        }

        if (folder.IsExpanded && _host is not null && folder.Members.FirstOrDefault(choice => choice.Index == _host.PresetIndex) is { } current)
        {
            PresetList.SelectedItem = current;
        }
    }

    /// <summary>
    /// The visuals in the order the picker lists them, which is also the order N and B step through them:
    /// folder by folder when grouped, in the chosen order within each.
    /// </summary>
    private List<VisualChoice> InPickerOrder(IEnumerable<VisualChoice> choices) =>
        VisualSorts.Order(choices, _settings.VisualSort, _settings.GroupVisualsByFamily, _settings.RecentVisuals);

    /// <summary>Whether a visual has any of the ticked tags (every visual does when none are ticked).</summary>
    private bool MatchesTags(VisualChoice choice) =>
        _tickedTags.Count == 0 || choice.Tags.Any(_tickedTags.Contains);

    /// <summary>Whether a visual has every word typed in the search box in its name, family or tags.</summary>
    private bool MatchesSearch(VisualChoice choice) => SearchText.Matches(choice.SearchKey, _searchWords);

    private HashSet<string> TickedTags() =>
        new(_tags.Where(tag => tag.IsChecked).Select(tag => tag.Name), StringComparer.OrdinalIgnoreCase);

    /// <summary>The line under the picker's heading saying what it's showing, and the tag panel's controls.</summary>
    private void ShowListHint(string? list, int inListCount, bool narrowed)
    {
        var tagNames = _tags.Where(tag => tag.IsChecked).Select(tag => tag.Name).ToList();
        var tagText = string.Join(" or ", tagNames);
        var search = (SearchBox.Text ?? string.Empty).Trim();
        var searching = _searchWords.Length > 0;

        // "tagged Calm or Space and matching "blue"": what the tags and the search narrow it to.
        var filters = new List<string>();
        if (tagNames.Count > 0)
        {
            filters.Add($"tagged {tagText}");
        }

        if (searching)
        {
            filters.Add($"matching “{search}”");
        }

        var filterText = string.Join(" and ", filters);

        ListHint.Text = list is not null && inListCount == 0
            ? list == AppSettings.FavoritesListName
                ? "No favorites yet. Click a visual's star to add it here."
                : "This list is empty. Right-click any visual in All visuals to add it."
            : _shown.Count == 0
                ? searching
                    ? $"No visuals {(tagNames.Count > 0 ? $"tagged {tagText} " : string.Empty)}match “{search}”. Try other words, or press Esc to clear the search."
                    : $"None of these visuals are tagged {tagText}. Tick other tags, or click Clear."
                : filters.Count > 0
                    ? $"Showing {_shown.Count} of {inListCount}, {filterText}. Auto plays from these, and N and B step through them."
                    : "Auto plays from this list, and N and B step through it.";
        ListHint.IsVisible = narrowed;

        ClearTagsButton.IsVisible = tagNames.Count > 0;
        TagHint.IsVisible = _tags.Count != 0;

        // A reminder on the visual's name in the bar, since the picker is closed most of the time.
        ToolTip.SetTip(PresetPickerButton, filters.Count > 0
            ? $"Choose a visual (V). Showing only visuals {filterText}."
            : "Choose a visual (V)");
    }

    private void UseList(string? list)
    {
        _settings.ActiveVisualList = list;
        _settings.Save();
        ShowActiveList();
    }

    private void UpdateFavoriteButton()
    {
        var favorite = CurrentChoice()?.IsFavorite == true;
        FavoriteButton.IsChecked = favorite;
        ToolTip.SetTip(FavoriteButton, favorite ? "Remove this visual from your favorites (F)" : "Make this visual a favorite (F)");
        if (favorite)
        {
            FavoriteIcon.SetResourceReference(Shape.FillProperty, "AccentBrush");
        }
        else
        {
            FavoriteIcon.Fill = Brushes.Transparent;
        }
    }

    private VisualChoice? CurrentChoice() =>
        _host is not null && _host.PresetIndex >= 0 && _host.PresetIndex < _choices.Count ? _choices[_host.PresetIndex] : null;

    private void OnToggleFavorite(object? sender, RoutedEventArgs e) => ToggleFavorite();

    /// <summary>Stars the visual that's showing, or unstars it.</summary>
    public void ToggleFavorite()
    {
        if (CurrentChoice() is { } current)
        {
            SetFavorite(current, !current.IsFavorite);
        }
        else
        {
            UpdateFavoriteButton();
        }
    }

    private void OnStarClick(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { DataContext: VisualChoice choice })
        {
            SetFavorite(choice, !choice.IsFavorite);
        }

        e.Handled = true;
    }

    private void SetFavorite(VisualChoice choice, bool favorite)
    {
        choice.IsFavorite = favorite;
        _settings.FavoriteVisuals.RemoveAll(name => string.Equals(name, choice.Name, StringComparison.OrdinalIgnoreCase));
        if (favorite)
        {
            _settings.FavoriteVisuals.Add(choice.Name);
        }

        _settings.Save();
        if (_settings.ActiveVisualList == AppSettings.FavoritesListName)
        {
            // Keep the row in place while the picker is open, so unstarring by mistake can be undone.
            if (PresetPopup.IsOpen && !favorite)
            {
                ListPickerLabel.Text = $"{AppSettings.FavoritesListName} ({_settings.FavoriteVisuals.Count})";
                var playable = _choices.Where(other => other.IsFavorite && IsSwitchedOn(other)
                    && MatchesTags(other) && MatchesSearch(other))
                    .Select(other => other.Name).ToList();
                _host!.AutoCycleNames = playable.Count == 0 ? null : playable;
            }
            else
            {
                ShowActiveList();
            }
        }

        UpdateFavoriteButton();
    }

    private void OnListMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ListPickerButton, Placement = PlacementMode.BottomEdgeAlignedLeft };
        var active = _settings.ActiveVisualList;

        menu.Items.Add(ListMenuItem("All visuals", null, _choices.Count));
        menu.Items.Add(ListMenuItem(AppSettings.FavoritesListName, AppSettings.FavoritesListName, _settings.FavoriteVisuals.Count));
        foreach (var (name, names) in _settings.VisualLists.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(ListMenuItem(name, name, names.Count));
        }

        menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New list…" };
        create.Click += async (_, _) =>
        {
            if (await AskForListName("New list of visuals", "Give it a name, then right-click visuals to add them.", "Create") is { } name)
            {
                _settings.VisualLists[name] = new List<string>();
                UseList(name);
            }
        };
        menu.Items.Add(create);

        if (active is not null && active != AppSettings.FavoritesListName)
        {
            var rename = new MenuItem { Header = $"Rename {active}…" };
            rename.Click += async (_, _) =>
            {
                if (await AskForListName("Rename list", "Choose a new name.", "Rename", active) is { } name && name != active)
                {
                    _settings.VisualLists[name] = _settings.VisualLists[active];
                    _settings.VisualLists.Remove(active);
                    UseList(name);
                }
            };
            menu.Items.Add(rename);

            var delete = new MenuItem { Header = $"Delete {active}" };
            delete.Click += (_, _) =>
            {
                _settings.VisualLists.Remove(active);
                UseList(null);
            };
            menu.Items.Add(delete);
        }

        // The picker closes on a click outside it, so keep it open while the menu is up.
        KeepPickerOpen(true);
        menu.Closed += (_, _) => KeepPickerOpenUnlessAsking();
        menu.Open(ListPickerButton);
    }

    private MenuItem ListMenuItem(string label, string? list, int count)
    {
        var item = new MenuItem
        {
            Header = $"{label}  ({count})",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = list == _settings.ActiveVisualList,
        };
        item.Click += (_, _) => UseList(list);
        return item;
    }

    private int _asking;

    /// <summary>A menu that belongs to the picker closed: the picker closes on outside clicks again, unless a question it asked is still up.</summary>
    private void KeepPickerOpenUnlessAsking()
    {
        if (_asking == 0)
        {
            KeepPickerOpen(false);
        }
    }

    /// <summary>Asks for a name no other list (or the favourites) has. Null if cancelled.</summary>
    private async Task<string?> AskForListName(string heading, string hint, string action, string initial = "")
    {
        _asking++;
        KeepPickerOpen(true);
        try
        {
            return await PromptWindow.AskAsync(this, heading, hint, action, initial, name =>
                string.Equals(name, initial, StringComparison.OrdinalIgnoreCase) ? null
                : string.Equals(name, AppSettings.FavoritesListName, StringComparison.OrdinalIgnoreCase)
                  || _settings.VisualLists.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    ? "There's already a list with that name."
                    : null);
        }
        finally
        {
            _asking--;
            KeepPickerOpen(false);
        }
    }

    // Right-click a visual in the picker: favourite it, or add it to (or take it out of) a list.
    private void OnPresetListMenu(object? sender, ContextRequestedEventArgs e)
    {
        e.Handled = true;
        if (e.Source is not Visual source
            || source.FindAncestorOfType<ListBoxItem>(includeSelf: true) is not { DataContext: VisualChoice choice } row)
        {
            return;
        }

        var menu = new ContextMenu();

        var play = new MenuItem { Header = "Show it now" };
        play.Click += (_, _) => PickPreset(choice);
        menu.Items.Add(play);

        var favorite = new MenuItem { Header = choice.IsFavorite ? "Remove from favorites" : "Add to favorites" };
        favorite.Click += (_, _) => SetFavorite(choice, !choice.IsFavorite);
        menu.Items.Add(favorite);

        var addTo = new MenuItem { Header = "Add to list" };
        foreach (var (name, names) in _settings.VisualLists.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var inList = names.Contains(choice.Name, StringComparer.OrdinalIgnoreCase);
            var item = new MenuItem { Header = name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = inList, IsEnabled = !inList };
            item.Click += (_, _) =>
            {
                names.Add(choice.Name);
                _settings.Save();
                ShowActiveList();
            };
            addTo.Items.Add(item);
        }

        if (addTo.Items.Count > 0)
        {
            addTo.Items.Add(new Separator());
        }

        var create = new MenuItem { Header = "New list…" };
        create.Click += async (_, _) =>
        {
            if (await AskForListName("New list of visuals", $"It starts with {choice.Name}.", "Create") is { } name)
            {
                _settings.VisualLists[name] = new List<string> { choice.Name };
                _settings.Save();
                ShowActiveList();
            }
        };
        addTo.Items.Add(create);
        menu.Items.Add(addTo);

        var active = _settings.ActiveVisualList;
        if (active is not null && active != AppSettings.FavoritesListName && _settings.VisualLists.TryGetValue(active, out var current))
        {
            var remove = new MenuItem { Header = $"Remove from {active}" };
            remove.Click += (_, _) =>
            {
                current.RemoveAll(name => string.Equals(name, choice.Name, StringComparison.OrdinalIgnoreCase));
                _settings.Save();
                ShowActiveList();
            };
            menu.Items.Add(remove);
        }

        KeepPickerOpen(true);
        menu.Closed += (_, _) => KeepPickerOpenUnlessAsking();
        menu.Open(row);
    }

    // ---- Family folders and tags ----

    private void OnToggleGroupFamilies(object? sender, RoutedEventArgs e)
    {
        _settings.GroupVisualsByFamily = !_settings.GroupVisualsByFamily;
        _settings.Save();
        UpdateGroupFamiliesButton();
        ShowActiveList();
        if (PresetList.SelectedItem is { } selected)
        {
            PresetList.ScrollIntoView(selected);
        }
    }

    private void UpdateGroupFamiliesButton()
    {
        GroupFamiliesButton.SetResourceReference(ForegroundProperty, _settings.GroupVisualsByFamily ? "AccentBrush" : "FaintTextBrush");

        // Without folders there's nothing to open or close.
        CollapseAllButton.IsVisible = _settings.GroupVisualsByFamily;
        ExpandAllButton.IsVisible = _settings.GroupVisualsByFamily;
    }

    private void OnSortMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SortButton, Placement = PlacementMode.BottomEdgeAlignedLeft };
        foreach (var sort in VisualSorts.All)
        {
            var item = new MenuItem
            {
                Header = VisualSorts.Label(sort),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = sort == _settings.VisualSort,
            };
            item.Click += (_, _) =>
            {
                _settings.VisualSort = sort;
                _settings.Save();
                SortLabel.Text = VisualSorts.Label(sort);
                ShowActiveList();
                if (PresetList.SelectedItem is { } selected)
                {
                    PresetList.ScrollIntoView(selected);
                }
            };
            menu.Items.Add(item);
        }

        // The picker closes on a click outside it, so keep it open while the menu is up.
        KeepPickerOpen(true);
        menu.Closed += (_, _) => KeepPickerOpenUnlessAsking();
        menu.Open(SortButton);
    }

    private void OnCollapseAllFamilies(object? sender, RoutedEventArgs e) => SetAllFamiliesOpen(false);

    private void OnExpandAllFamilies(object? sender, RoutedEventArgs e) => SetAllFamiliesOpen(true);

    /// <summary>
    /// Closes or opens every family folder, and remembers it. Closing all of them during a search only
    /// takes effect once the search is cleared, since a search keeps every folder open.
    /// </summary>
    private void SetAllFamiliesOpen(bool open)
    {
        _collapsedFamilies.Clear();
        if (!open)
        {
            _collapsedFamilies.UnionWith(_choices.Select(choice => choice.Family));
        }

        SaveCollapsedFamilies();
        if (_searchWords.Length > 0)
        {
            return;
        }

        foreach (var folder in _folders.Values)
        {
            folder.IsExpanded = open;
        }

        if (open && PresetList.SelectedItem is { } selected)
        {
            PresetList.ScrollIntoView(selected);
        }
    }

    private void SaveCollapsedFamilies()
    {
        _settings.CollapsedVisualFamilies = _collapsedFamilies.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();
        _settings.Save();
    }

    private void OnTagClick(object? sender, RoutedEventArgs e) => TagsChanged();

    private void OnClearTags(object? sender, RoutedEventArgs e)
    {
        foreach (var tag in _tags)
        {
            tag.IsChecked = false;
        }

        TagsChanged();
    }

    /// <summary>Saves the ticked tags and shows (and has Auto play) only the visuals with any of them.</summary>
    private void TagsChanged()
    {
        _tickedTags = TickedTags();
        _settings.VisualTagFilter = _tags.Where(tag => tag.IsChecked).Select(tag => tag.Name).ToList();
        _settings.Save();
        ShowActiveList();
    }

    // ---- Search ----

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        var text = SearchBox.Text ?? string.Empty;
        ClearSearchButton.IsVisible = text.Length > 0;
        _searchWords = SearchText.Words(text);
        ShowActiveList();
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Shows the first match, like clicking it.
                if (_shown.Count > 0)
                {
                    PickPreset(_shown[0]);
                }

                e.Handled = true;
                break;
            case Key.Escape when !string.IsNullOrEmpty(SearchBox.Text):
                SearchBox.Clear();
                e.Handled = true;
                break;
            case Key.Escape:
                PresetPopup.IsOpen = false;
                e.Handled = true;
                break;
            case Key.Down when _shown.Count > 0:
                // Into the list, to pick with the arrow keys and Return.
                var target = PresetList.SelectedItem as VisualChoice ?? _rows.OfType<VisualChoice>().FirstOrDefault();
                if (target is not null)
                {
                    ListRows.Focus(PresetList, target);
                }

                e.Handled = true;
                break;
        }
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    // ---- Auto ----

    private void OnToggleAutoCycle(object? sender, RoutedEventArgs e) => ToggleAutoCycle();

    public void ToggleAutoCycle()
    {
        _settings.AutoCycle = !_settings.AutoCycle;
        Player?.ApplyVisualizerSettings();
        UpdateAutoCycleButton();
        _settings.Save();
    }

    public void UpdateAutoCycleButton()
    {
        var seconds = _settings.AutoCycleSeconds;
        AutoCycleText.Text = $"AUTO · {ShortIntervalText(seconds)}";
        AutoCycleButton.SetResourceReference(ForegroundProperty, _settings.AutoCycle ? "AccentBrush" : "FaintTextBrush");
        ToolTip.SetTip(AutoCycleButton, _settings.AutoCycle
            ? $"Auto is on: a new visual comes on by itself every {IntervalText(seconds)} or so, on the beat. Click to keep this one (A), right-click to change how often"
            : $"Auto is off: this visual stays until you change it. Click to get a new one every {IntervalText(seconds)} or so, on the beat (A), right-click to change how often");
        FitBottomBar();
    }

    // ---- Fitting the visuals bar ----

    private const double PickerWidest = 220;

    private void OnBottomBarSizeChanged(object? sender, SizeChangedEventArgs e) => FitBottomBar();

    /// <summary>
    /// Keeps everything in the visuals bar in sight however narrow the window. The picker stays centered
    /// while there's room; then the AUTO pill drops its words (leaving its circling arrow), the picker
    /// narrows, and at the narrowest the picker moves left of centre to make room for the buttons on the right.
    /// </summary>
    private void FitBottomBar()
    {
        var width = BottomBar.Bounds.Width;
        if (width <= 0)
        {
            return;
        }

        const double arrows = 2 * 32 + 8;
        var compact = width - 2 * RightControlsWidth(compact: false) - arrows < PickerWidest;
        var right = RightControlsWidth(compact);
        var centered = width - 2 * right - arrows;
        var star = new GridLength(1, GridUnitType.Star);
        var fits = centered >= 150;
        BottomBar.ColumnDefinitions[0].Width = fits ? star : new GridLength(0);
        BottomBar.ColumnDefinitions[1].Width = fits ? GridLength.Auto : star;
        BottomBar.ColumnDefinitions[2].Width = fits ? star : GridLength.Auto;
        PresetPickerButton.Width = fits ? Math.Min(PickerWidest, centered) : Math.Clamp(width - right - arrows - 8, 90, PickerWidest);
    }

    private double RightControlsWidth(bool compact)
    {
        AutoCycleText.IsVisible = !compact;
        BarRightControls.Measure(Size.Infinity);
        return BarRightControls.DesiredSize.Width;
    }

    // "25 seconds" / "3 minutes" for menus and tooltips, "25s" / "3m" for the pill.
    internal static string IntervalText(int seconds) =>
        seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60} minutes" : $"{seconds} seconds";

    private static string ShortIntervalText(int seconds) =>
        seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60}m" : $"{seconds}s";

    private void OnAutoCycleIntervalMenu(object? sender, RoutedEventArgs e) => ShowAutoCycleMenu();

    private void OnAutoCycleRightClick(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            ShowAutoCycleMenu();
            e.Handled = true;
        }
    }

    /// <summary>The menu above the AUTO pill for choosing how often the visuals change.</summary>
    private void ShowAutoCycleMenu()
    {
        var menu = new ContextMenu { PlacementTarget = AutoCycleButton, Placement = PlacementMode.TopEdgeAlignedLeft, VerticalOffset = -6 };
        menu.Items.Add(new MenuItem { Header = "Change visual about every", IsEnabled = false });
        foreach (var seconds in AutoCycleChoices)
        {
            var item = new MenuItem
            {
                Header = IntervalText(seconds),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = seconds == _settings.AutoCycleSeconds,
            };
            item.Click += (_, _) =>
            {
                // Choosing how often only makes sense with Auto on, so this turns it on too.
                _settings.AutoCycleSeconds = seconds;
                _settings.AutoCycle = true;
                Player?.ApplyVisualizerSettings();
                UpdateAutoCycleButton();
                _settings.Save();
            };
            menu.Items.Add(item);
        }

        menu.Open(AutoCycleButton);
    }

    // ---- Window ----

    private void OnMaximizeRestore(object? sender, RoutedEventArgs e) => MaximizeOrRestore();

    /// <summary>Double-clicking the title bar maximizes or restores the window, as the system title bar would.</summary>
    private void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (e.Source is Visual source && source.FindAncestorOfType<Button>(includeSelf: true) is null)
        {
            MaximizeOrRestore();
        }
    }

    private void MaximizeOrRestore() =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnWindowStateChanged()
    {
        var maximized = WindowState == WindowState.Maximized;
        if (this.TryFindResource(maximized ? "RestoreIcon" : "MaximizeIcon", out var icon) && icon is Geometry geometry)
        {
            MaximizeIcon.Data = geometry;
        }

        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");

        // (On Windows a maximized borderless window hangs slightly off the screen and is padded back in;
        // a Mac's zoomed window fits the screen exactly, so there's nothing to pad.)
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled)
        {
            return;
        }

        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
        }
        else if (e.Key == Key.F && e.KeyModifiers == (KeyModifiers.Control | KeyModifiers.Meta))
        {
            // ⌃⌘F, the Mac's fullscreen key (F11 on Windows).
            ToggleFullscreen();
            e.Handled = true;
        }
    }
}

/// <summary>A family's folder heading in the visual picker: its name, how many visuals it holds, and whether it's open.</summary>
public sealed class VisualFamilyFolder : INotifyPropertyChanged
{
    private bool _isExpanded;

    public VisualFamilyFolder(string name, IReadOnlyList<VisualChoice> members, bool isExpanded)
    {
        Name = name;
        Members = members;
        _isExpanded = isExpanded;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Name { get; }

    /// <summary>The visuals in the folder, in the picker's order.</summary>
    public IReadOnlyList<VisualChoice> Members { get; }

    public int ItemCount => Members.Count;

    /// <summary>The MilkDrop presets' folder, which has an Import button.</summary>
    public bool IsMilkDrop => Name == VisualStyles.MilkDropFamily;

    /// <summary>The Winamp AVS presets' folder, which has an Import button too.</summary>
    public bool IsAvs => Name == VisualStyles.AvsFamily;

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
            }
        }
    }

    public override string ToString() => Name;
}
