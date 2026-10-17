using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Visualizer;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>The visuals, in their own window with the visual picker and Auto controls underneath.</summary>
public partial class VisualizerWindow : ToolWindow
{
    // How often Auto can change visual, offered in the menu next to the AUTO pill (and in Options).
    internal static readonly int[] AutoCycleChoices = { 10, 15, 25, 45, 90, 180 };

    private VisualizerHost? _host;
    private AppSettings _settings = new();
    private IReadOnlyList<string> _presetNames = Array.Empty<string>();
    private List<VisualChoice> _choices = new();
    private List<VisualChoice> _shown = new();
    private DateTime _presetPopupClosedAt;
    private bool _isFullscreen;
    private WindowState _stateBeforeFullscreen;
    private Rect _boundsBeforeFullscreen;

    public VisualizerWindow()
    {
        InitializeComponent();
        PlacementName = "Visuals";
    }

    public bool IsFullscreen => _isFullscreen;

    /// <summary>Puts the visualizer in this window. Called once, before the window is first shown.</summary>
    public void Attach(VisualizerHost host, AppSettings settings)
    {
        _host = host;
        _settings = settings;
        host.PresetsLoaded += (_, names) =>
        {
            _presetNames = names;
            BuildChoices();
        };
        host.ProblemReported += (_, message) =>
        {
            VisualizerStatus.Text = message;
            VisualizerStatus.ToolTip = message;
        };
        host.DoubleClicked += (_, _) => ToggleFullscreen();
        host.PresetChanged += (_, _) => ShowPreset();
        host.FrameRateMeasured += (_, stats) =>
            FrameRateText.Text = $"{stats.FramesPerSecond:0} fps · {stats.Width} × {stats.Height}" + (stats.Hdr ? " · HDR" : string.Empty);
        VisualizerFrame.Child = host;
        UpdateAutoCycleButton();
    }

    /// <summary>Shows or hides the frame rate readout, following the setting in Options.</summary>
    public void ShowFrameRate(bool show) => FrameRateText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

    // ---- Picking visuals ----

    private void OnNextPreset(object sender, RoutedEventArgs e) => ChangePreset(+1);

    private void OnPreviousPreset(object sender, RoutedEventArgs e) => ChangePreset(-1);

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
        PresetName.Text = current.Name;
        PresetList.SelectedItem = _shown.Contains(current) ? current : null;
        _settings.LastVisual = current.Name;
        UpdateFavoriteButton();
    }

    private void OnTogglePresetPicker(object sender, RoutedEventArgs e) => TogglePresetPicker();

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
            Show();
        }

        PresetPopup.IsOpen = true;
        if (PresetList.SelectedItem is not null)
        {
            PresetList.ScrollIntoView(PresetList.SelectedItem);
        }

        PresetList.Focus();
    }

    private void OnPresetPopupClosed(object? sender, EventArgs e) => _presetPopupClosedAt = DateTime.UtcNow;

    private void OnPresetListClick(object sender, MouseButtonEventArgs e)
    {
        // A click on a row's star stars it and leaves the picker open.
        if (e.OriginalSource is not DependencyObject source || IsInsideButton(source))
        {
            return;
        }

        if (ItemsControl.ContainerFromElement(PresetList, source) is ListBoxItem { DataContext: VisualChoice choice })
        {
            PickPreset(choice);
        }
    }

    private bool IsInsideButton(DependencyObject source)
    {
        for (var element = source; element is not null && element != PresetList; element = VisualTreeHelper.GetParent(element))
        {
            if (element is ButtonBase)
            {
                return true;
            }
        }

        return false;
    }

    private void OnPresetListKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && PresetList.SelectedItem is VisualChoice choice)
        {
            PickPreset(choice);
            e.Handled = true;
        }
    }

    private void PickPreset(VisualChoice choice)
    {
        if (_host is not null)
        {
            _host.PresetIndex = choice.Index;
            ShowPreset();
        }

        PresetPopup.IsOpen = false;
    }

    // ---- Favourites and lists of visuals ----

    private void BuildChoices()
    {
        var favorites = new HashSet<string>(_settings.FavoriteVisuals, StringComparer.OrdinalIgnoreCase);
        _choices = _presetNames.Select((name, index) => new VisualChoice(name, index) { IsFavorite = favorites.Contains(name) }).ToList();
        ShowActiveList();
    }

    /// <summary>The visuals in a list (by name), or null for "All visuals".</summary>
    private List<string>? NamesInList(string? list) =>
        list is null ? null
        : list == AppSettings.FavoritesListName ? _settings.FavoriteVisuals
        : _settings.VisualLists.TryGetValue(list, out var names) ? names
        : null;

    /// <summary>Fills the picker with the chosen list and has Auto play from it.</summary>
    private void ShowActiveList()
    {
        var list = _settings.ActiveVisualList;
        if (list is not null && list != AppSettings.FavoritesListName && !_settings.VisualLists.ContainsKey(list))
        {
            // The list was deleted (or the settings were edited by hand).
            list = _settings.ActiveVisualList = null;
        }

        var names = NamesInList(list);
        if (names is null)
        {
            _shown = _choices.ToList();
        }
        else
        {
            var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            _shown = _choices.Where(choice => wanted.Contains(choice.Name)).ToList();
        }

        PresetList.ItemsSource = _shown;
        ListPickerLabel.Text = list is null ? "All visuals" : $"{list} ({_shown.Count})";

        // An empty list can't drive Auto, so it plays from everything until something is added.
        if (_host is not null)
        {
            _host.AutoCycleNames = names is null || _shown.Count == 0 ? null : _shown.Select(choice => choice.Name).ToList();
        }

        ListHint.Text = list is null
            ? string.Empty
            : _shown.Count == 0
                ? list == AppSettings.FavoritesListName
                    ? "No favourites yet. Click a visual's star to add it here."
                    : "This list is empty. Right-click any visual in All visuals to add it."
                : "Auto plays from this list, and N and B step through it.";
        ListHint.Visibility = list is null ? Visibility.Collapsed : Visibility.Visible;
        ShowPreset();
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
        FavoriteButton.ToolTip = favorite ? "Remove this visual from your favourites (F)" : "Make this visual a favourite (F)";
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

    private void OnToggleFavorite(object sender, RoutedEventArgs e) => ToggleFavorite();

    /// <summary>Stars the visual that's showing, or unstars it.</summary>
    public void ToggleFavorite()
    {
        if (CurrentChoice() is { } current)
        {
            SetFavorite(current, !current.IsFavorite);
        }
    }

    private void OnStarClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: VisualChoice choice })
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
                _host!.AutoCycleNames = _settings.FavoriteVisuals.Count == 0 ? null : _settings.FavoriteVisuals.ToList();
            }
            else
            {
                ShowActiveList();
            }
        }

        UpdateFavoriteButton();
    }

    private void OnListMenu(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ListPickerButton, Placement = PlacementMode.Bottom };
        var active = _settings.ActiveVisualList;

        menu.Items.Add(ListMenuItem("All visuals", null, _choices.Count));
        menu.Items.Add(ListMenuItem(AppSettings.FavoritesListName, AppSettings.FavoritesListName, _settings.FavoriteVisuals.Count));
        foreach (var (name, names) in _settings.VisualLists.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            menu.Items.Add(ListMenuItem(name, name, names.Count));
        }

        menu.Items.Add(new Separator());
        var create = new MenuItem { Header = "New list…" };
        create.Click += (_, _) =>
        {
            if (AskForListName("New list of visuals", "Give it a name, then right-click visuals to add them.", "Create") is { } name)
            {
                _settings.VisualLists[name] = new List<string>();
                UseList(name);
            }
        };
        menu.Items.Add(create);

        if (active is not null && active != AppSettings.FavoritesListName)
        {
            var rename = new MenuItem { Header = $"Rename {active}…" };
            rename.Click += (_, _) =>
            {
                if (AskForListName("Rename list", "Choose a new name.", "Rename", active) is { } name && name != active)
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

        // The picker closes when focus leaves it, so keep it open while the menu is up.
        PresetPopup.StaysOpen = true;
        menu.Closed += (_, _) => PresetPopup.StaysOpen = false;
        menu.IsOpen = true;
    }

    private MenuItem ListMenuItem(string label, string? list, int count)
    {
        var item = new MenuItem
        {
            Header = $"{label}  ({count})",
            IsCheckable = true,
            IsChecked = list == _settings.ActiveVisualList,
        };
        item.Click += (_, _) => UseList(list);
        return item;
    }

    /// <summary>Asks for a name no other list (or the favourites) has. Null if cancelled.</summary>
    private string? AskForListName(string heading, string hint, string action, string initial = "")
    {
        PresetPopup.StaysOpen = true;
        try
        {
            return PromptWindow.Ask(this, heading, hint, action, initial, name =>
                string.Equals(name, initial, StringComparison.OrdinalIgnoreCase) ? null
                : string.Equals(name, AppSettings.FavoritesListName, StringComparison.OrdinalIgnoreCase)
                  || _settings.VisualLists.Keys.Any(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                    ? "There's already a list with that name."
                    : null);
        }
        finally
        {
            PresetPopup.StaysOpen = false;
        }
    }

    // Right-click a visual in the picker: favourite it, or add it to (or take it out of) a list.
    private void OnPresetListMenu(object sender, ContextMenuEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(PresetList, source) is not ListBoxItem { DataContext: VisualChoice choice })
        {
            e.Handled = true;
            return;
        }

        var menu = PresetList.ContextMenu!;
        menu.Items.Clear();

        var play = new MenuItem { Header = "Show it now" };
        play.Click += (_, _) => PickPreset(choice);
        menu.Items.Add(play);

        var favorite = new MenuItem { Header = choice.IsFavorite ? "Remove from favourites" : "Add to favourites" };
        favorite.Click += (_, _) => SetFavorite(choice, !choice.IsFavorite);
        menu.Items.Add(favorite);

        var addTo = new MenuItem { Header = "Add to list" };
        foreach (var (name, names) in _settings.VisualLists.OrderBy(pair => pair.Key, StringComparer.CurrentCultureIgnoreCase))
        {
            var inList = names.Contains(choice.Name, StringComparer.OrdinalIgnoreCase);
            var item = new MenuItem { Header = name, IsChecked = inList, IsEnabled = !inList };
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
        create.Click += (_, _) =>
        {
            if (AskForListName("New list of visuals", $"It starts with {choice.Name}.", "Create") is { } name)
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

        PresetPopup.StaysOpen = true;
        menu.Closed -= OnPresetMenuClosed;
        menu.Closed += OnPresetMenuClosed;
    }

    private void OnPresetMenuClosed(object sender, RoutedEventArgs e) => PresetPopup.StaysOpen = false;

    // ---- Auto ----

    private void OnToggleAutoCycle(object sender, RoutedEventArgs e) => ToggleAutoCycle();

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
        AutoCycleButton.Content = $"AUTO · {ShortIntervalText(seconds)}";
        AutoCycleButton.SetResourceReference(ForegroundProperty, _settings.AutoCycle ? "AccentBrush" : "FaintTextBrush");
        AutoCycleButton.ToolTip = _settings.AutoCycle
            ? $"Moving to a new visual on the beat every {IntervalText(seconds)} or so. Click to stop (A), right-click to change how often"
            : $"Auto is off. Click to move to a new visual on the beat every {IntervalText(seconds)} or so (A), right-click to change how often";
    }

    // "25 seconds" / "3 minutes" for menus and tooltips, "25s" / "3m" for the pill.
    internal static string IntervalText(int seconds) =>
        seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60} minutes" : $"{seconds} seconds";

    private static string ShortIntervalText(int seconds) =>
        seconds >= 120 && seconds % 60 == 0 ? $"{seconds / 60}m" : $"{seconds}s";

    private void OnAutoCycleIntervalMenu(object sender, RoutedEventArgs e) => ShowAutoCycleMenu();

    private void OnAutoCycleRightClick(object sender, MouseButtonEventArgs e)
    {
        ShowAutoCycleMenu();
        e.Handled = true;
    }

    /// <summary>The menu above the AUTO pill for choosing how often the visuals change.</summary>
    private void ShowAutoCycleMenu()
    {
        var menu = new ContextMenu { PlacementTarget = AutoCycleButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        menu.Items.Add(new MenuItem { Header = "Change visual about every", IsEnabled = false });
        foreach (var seconds in AutoCycleChoices)
        {
            var item = new MenuItem { Header = IntervalText(seconds), IsChecked = seconds == _settings.AutoCycleSeconds };
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

        menu.IsOpen = true;
    }

    // ---- Window ----

    private void OnToggleFullscreen(object sender, RoutedEventArgs e) => ToggleFullscreen();

    /// <summary>Fills the screen with the visuals (Esc or double-click to leave).</summary>
    public void ToggleFullscreen()
    {
        if (!_isFullscreen && !IsVisible)
        {
            Show();
        }

        _isFullscreen = !_isFullscreen;
        var chrome = _isFullscreen ? Visibility.Collapsed : Visibility.Visible;
        TitleBar.Visibility = chrome;
        BottomBar.Visibility = chrome;
        Frame.BorderThickness = new Thickness(_isFullscreen ? 0 : 1);
        PresetPopup.IsOpen = false;

        if (_isFullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            _boundsBeforeFullscreen = RestoreBounds;
            // Leave Maximized first so Windows recalculates the size without the title bar.
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            Activate();
        }
        else
        {
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;
            WindowState = _stateBeforeFullscreen;
            if (_stateBeforeFullscreen == WindowState.Normal && !_boundsBeforeFullscreen.IsEmpty)
            {
                Left = _boundsBeforeFullscreen.Left;
                Top = _boundsBeforeFullscreen.Top;
                Width = _boundsBeforeFullscreen.Width;
                Height = _boundsBeforeFullscreen.Height;
            }
        }

        OnWindowStateChanged(this, EventArgs.Empty);
    }

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = (Geometry)FindResource(maximized ? "RestoreIcon" : "MaximizeIcon");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";

        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = maximized && !_isFullscreen ? new Thickness(7) : new Thickness(0);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _isFullscreen)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }
}
