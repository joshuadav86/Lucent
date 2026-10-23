using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Radio;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// The radio browser, laid out like the plugin browser: typing finds stations by name, genre, or country; the left
/// column picks the featured networks (SomaFM, FRISKY, Radio Paradise, Nightride FM, The Lot Radio), the favorites, the stations
/// played lately, every station, or one genre; the sort and the quality and country filters sit beside the search.
/// The star beside a station makes it a favorite, and clicking a station plays it.
/// </summary>
/// <remarks>The featured stations, favorites and recently played are Lucent's own; everything else comes from Radio Browser as it's asked for.</remarks>
public partial class RadioWindow : Window
{
    private static RadioWindow? _instance;

    private readonly MainWindow _player;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _searchDelay;
    private List<RadioStation> _featured = FeaturedStations.Stations.ToList();
    private IReadOnlyDictionary<string, int> _genreCounts = new Dictionary<string, int>();
    private IReadOnlyList<RadioCountry> _countries = Array.Empty<RadioCountry>();
    private IReadOnlyList<RadioStation> _shown = Array.Empty<RadioStation>();
    private CancellationTokenSource? _searching;
    private string? _problem;

    /// <summary>For Avalonia's XAML loader and designer only; Lucent opens the window with <see cref="Open"/>.</summary>
    public RadioWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _player = null!;
        _settings = null!;
        _searchDelay = null!;
    }

    internal RadioWindow(MainWindow player, AppSettings settings)
        : this()
    {
        _player = player;
        _settings = settings;
        SearchBox.AddHandler(KeyDownEvent, OnSearchKeyDown, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        if (!Genres.All.Any(genre => genre.Name == settings.RadioSection) && !RadioGuide.IsLocal(settings.RadioSection))
        {
            settings.RadioSection = RadioGuide.Featured;
        }

        _searchDelay = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchDelay.Tick += (_, _) =>
        {
            _searchDelay.Stop();
            ShowStations();
        };
        _player.StationChanged += OnStationChanged;
        Closed += (_, _) =>
        {
            _player.StationChanged -= OnStationChanged;
            _searching?.Cancel();
            _searchDelay.Stop();
            _instance = null;
        };
        Loaded += (_, _) => SearchBox.Focus();

        ShowFilters();
        ShowSections();
        ShowStations();
        LoadDirectoryExtras();
    }

    /// <summary>The radio window, while it's open.</summary>
    public static Window? Current => _instance;

    /// <summary>Opens the window, or brings it forward if it's already open.</summary>
    public static void Open(MainWindow owner, AppSettings settings)
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        _instance = new RadioWindow(owner, settings);
        _instance.Show(owner);
    }

    /// <summary>For the screenshot tool: shows <paramref name="stations"/> as if the directory had sent them for a genre.</summary>
    internal void PreviewDirectory(string section, IReadOnlyList<RadioStation> stations)
    {
        _settings.RadioSection = section;
        _searching?.Cancel();
        _searching = null;
        ShowSections();
        ShowList(stations, 0);
    }

    private string Section => _settings.RadioSection;

    private string SearchText => (SearchBox.Text ?? string.Empty).Trim();

    /// <summary>FRISKY's stations, and the counts beside the genres, from the directory. The window works without them.</summary>
    private async void LoadDirectoryExtras()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            _genreCounts = await _player.RadioDirectory.GenreCountsAsync(cancel.Token);
            ShowSections();
        }
        catch (Exception)
        {
            // The genres just show without counts.
        }

        foreach (var (network, name) in FeaturedStations.LookedUp)
        {
            try
            {
                var found = await _player.RadioDirectory.SearchAsync(new RadioQuery(name, null, null, 0, RadioSort.Votes, 40), cancel.Token);
                var stations = FeaturedStations.BestStations(found, name, network);
                _featured = _featured.Where(station => station.Network != network).Concat(stations).ToList();
                ShowSections();
                if (Section == RadioGuide.Featured)
                {
                    ShowStations(keepPlace: true);
                }
            }
            catch (Exception)
            {
                // Listed again next time the window opens.
            }
        }
    }

    private void OnStationChanged(object? sender, EventArgs e)
    {
        ShowSections();
        ShowStations(keepPlace: true, again: false);
    }

    // ---- Sections ----

    private void ShowSections()
    {
        Sections.Children.Clear();
        Sections.Children.Add(SectionButton(RadioGuide.Featured, _featured.Count));
        Sections.Children.Add(SectionButton(RadioGuide.LiveAndVideo,
            RadioGuide.Local(RadioGuide.LiveAndVideo, _settings, _featured, string.Empty, _settings.RadioSort).Count));
        Sections.Children.Add(SectionButton(RadioGuide.Favorites, _settings.RadioFavorites.Count));
        Sections.Children.Add(SectionButton(RadioGuide.Recent, _settings.RadioRecent.Count));
        Sections.Children.Add(SectionButton(RadioGuide.AllStations, null));
        Sections.Children.Add(new TextBlock
        {
            Text = "GENRES",
            FontSize = 10,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(8, 12, 0, 4),
            Foreground = ThemeBrush("MutedTextBrush"),
        });
        foreach (var genre in Genres.All)
        {
            Sections.Children.Add(SectionButton(genre.Name, _genreCounts.TryGetValue(genre.Tag, out var count) ? count : null));
        }
    }

    private Button SectionButton(string section, int? count)
    {
        var content = new DockPanel();
        if (count is { } number)
        {
            var counted = new TextBlock
            {
                Text = Compact(number),
                FontSize = 11,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = ThemeBrush("FaintTextBrush"),
            };
            DockPanel.SetDock(counted, Dock.Right);
            content.Children.Add(counted);
        }

        content.Children.Add(new TextBlock
        {
            Text = section,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("TextBrush"),
        });
        var button = new Button { Theme = FindTheme("StationRow"), Content = content, Focusable = false };
        if (section == Section)
        {
            button.Background = ThemeBrush("AccentSoftBrush");
        }

        button.Click += (_, _) =>
        {
            _settings.RadioSection = section;
            _settings.Save();
            ShowSections();
            ShowStations();
        };
        return button;
    }

    // ---- Stations ----

    /// <summary>
    /// The section's stations that match the search and filters, under their headings. Lucent's own sections show
    /// straight away; the directory's are looked up (<paramref name="again"/> false shows the last answer again).
    /// </summary>
    private async void ShowStations(bool keepPlace = false, bool again = true)
    {
        var search = SearchText;
        ClearSearchButton.IsVisible = search.Length > 0;
        var offset = StationScroller.Offset.Y;
        if (RadioGuide.IsLocal(Section))
        {
            _searching?.Cancel();
            _problem = null;
            ShowList(RadioGuide.Local(Section, _settings, _featured, search, _settings.RadioSort), keepPlace ? offset : 0);
            return;
        }

        if (!again)
        {
            ShowList(_shown, offset);
            return;
        }

        _searching?.Cancel();
        var searching = _searching = new CancellationTokenSource();
        var genre = RadioGuide.GenreOf(Section);
        var query = new RadioQuery(search, genre?.Tag, _settings.RadioCountry, _settings.RadioMinBitrate, _settings.RadioSort, VideoOnly: _settings.RadioVideoOnly);
        StationCount.Text = "Looking…";
        try
        {
            var found = await _player.RadioDirectory.SearchAsync(query, searching.Token);
            if (_searching != searching)
            {
                return;
            }

            _problem = null;
            ShowList(found, keepPlace ? offset : 0);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            if (_searching == searching)
            {
                _problem = ex is RadioProblem ? ex.Message : $"The radio directory couldn't be searched. {ex.Message}";
                ShowList(Array.Empty<RadioStation>(), 0);
            }
        }
    }

    private void ShowList(IReadOnlyList<RadioStation> stations, double offset)
    {
        _shown = stations;
        StationCount.Text = stations.Count switch
        {
            0 => string.Empty,
            1 => "1 station",
            var count when !RadioGuide.IsLocal(Section) && count >= 150 => $"The top {count} stations",
            var count => $"{count} stations",
        };

        StationRows.Children.Clear();
        if (stations.Count == 0)
        {
            StationRows.Children.Add(new TextBlock
            {
                Theme = FindTheme("OptionHint"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8, 6, 8, 0),
                Text = _problem ?? EmptyText(),
            });
            return;
        }

        foreach (var (heading, group) in RadioGuide.Group(stations, Section))
        {
            StationRows.Children.Add(Heading(heading, group.Count, first: StationRows.Children.Count == 0));
            foreach (var station in group)
            {
                StationRows.Children.Add(StationRow(station));
            }
        }

        StationScroller.UpdateLayout();
        StationScroller.Offset = new Vector(0, offset);
    }

    private string EmptyText()
    {
        var filtered = _settings.RadioMinBitrate > 0 || _settings.RadioCountry is not null || _settings.RadioVideoOnly;
        return Section switch
        {
            RadioGuide.LiveAndVideo => "No live DJ or video stations match. To find more video stations, open All stations and switch the filter to VIDEO ONLY.",
            RadioGuide.Favorites when SearchText.Length == 0 && !filtered => "No favorites yet. Click the star beside a station to keep it here.",
            RadioGuide.Recent when SearchText.Length == 0 && !filtered => "Stations you play show up here.",
            _ when _settings.RadioVideoOnly => "No video stations match. Try fewer words, look in All stations, or turn off the video filter.",
            _ when filtered => "No stations match. Try fewer words, or loosen the quality or country filter.",
            _ => "No stations match. Try fewer words, or look in All stations.",
        };
    }

    private DockPanel Heading(string heading, int count, bool first)
    {
        var number = new TextBlock
        {
            Text = count.ToString(CultureInfo.CurrentCulture),
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

    private Button StationRow(RadioStation station)
    {
        var favorite = RadioGuide.IsFavorite(_settings, station);
        var playing = _player.CurrentStation?.Id == station.Id;
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
                Stroke = ThemeBrush(favorite ? "AccentBrush" : "FaintTextBrush"),
                Fill = favorite ? ThemeBrush("AccentBrush") : Brushes.Transparent,
                StrokeThickness = 1.2,
                StrokeLineCap = PenLineCap.Round,
                StrokeJoin = PenLineJoin.Round,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
            },
        };
        ToolTip.SetTip(star, favorite ? "Remove it from your favorites" : "Add it to your favorites");
        star.Click += (_, e) =>
        {
            // The row is a button too; the star's click is only the star's.
            e.Handled = true;
            _player.SetStationFavorite(station, !favorite);
        };

        var detail = new TextBlock
        {
            Text = station.Details(),
            FontSize = 11,
            MaxWidth = 300,
            Margin = new Thickness(10, 0, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush("FaintTextBrush"),
        };
        // The station's website, a click away; kept in its place when there's none, so the rows line up.
        var hasWebsite = station.Homepage is { } homepage && RadioGuide.IsStreamAddress(homepage);
        var website = new Button
        {
            Theme = FindTheme("IconButton"),
            Width = 24,
            Height = 24,
            Margin = new Thickness(6, -4, 0, -4),
            Focusable = false,
            Opacity = hasWebsite ? 1 : 0,
            IsHitTestVisible = hasWebsite,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = ThemeIcon("WebsiteIcon"),
                Stroke = ThemeBrush("FaintTextBrush"),
                StrokeThickness = 1.2,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
            },
        };
        if (hasWebsite)
        {
            ToolTip.SetTip(website, $"Open the station's website ({station.Homepage})");
        }

        website.Click += (_, e) =>
        {
            e.Handled = true;
            _player.OpenStationWebsite(station);
        };

        // Where listeners can support the station (a donation page), when Lucent knows it; kept in place like the website's.
        var supportPage = FeaturedStations.SupportPage(station);
        var support = new Button
        {
            Theme = FindTheme("IconButton"),
            Width = 24,
            Height = 24,
            Margin = new Thickness(6, -4, 0, -4),
            Focusable = false,
            Opacity = supportPage is not null ? 1 : 0,
            IsHitTestVisible = supportPage is not null,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = ThemeIcon("HeartIcon"),
                Stroke = ThemeBrush("FaintTextBrush"),
                StrokeThickness = 1.2,
                Width = 12,
                Height = 12,
                Stretch = Stretch.Uniform,
            },
        };
        if (supportPage is not null)
        {
            ToolTip.SetTip(support, $"Support the station ({supportPage})");
        }

        support.Click += (_, e) =>
        {
            e.Handled = true;
            _player.OpenStationSupport(station);
        };

        DockPanel.SetDock(star, Dock.Right);
        DockPanel.SetDock(website, Dock.Right);
        DockPanel.SetDock(support, Dock.Right);
        DockPanel.SetDock(detail, Dock.Right);
        var content = new DockPanel();
        content.Children.Add(star);
        content.Children.Add(website);
        content.Children.Add(support);
        content.Children.Add(detail);
        if (playing)
        {
            var speaker = new Avalonia.Controls.Shapes.Path
            {
                Data = ThemeIcon("SpeakerIcon"),
                Fill = ThemeBrush("AccentBrush"),
                Width = 10,
                Height = 10,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(speaker, Dock.Left);
            content.Children.Add(speaker);
        }

        // The camera: the station sends a picture, which shows in the visuals window.
        var video = RadioGuide.HasVideo(_settings, station);
        if (video)
        {
            var camera = new Avalonia.Controls.Shapes.Path
            {
                Data = ThemeIcon("VideoIcon"),
                Stroke = ThemeBrush(playing ? "AccentBrush" : "FaintTextBrush"),
                StrokeThickness = 1.2,
                Width = 13,
                Height = 13,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(camera, Dock.Left);
            content.Children.Add(camera);
        }

        content.Children.Add(new TextBlock
        {
            Text = station.Name,
            FontSize = 12,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = ThemeBrush(playing ? "AccentBrush" : "TextBrush"),
        });

        var row = new Button { Theme = FindTheme("StationRow"), Content = content, Tag = station };
        ToolTip.SetTip(row, Describe(station) + (video ? "\nA video station: its picture shows in the visuals window." : string.Empty) + (playing ? "\nPlaying now." : "\nClick to play it; right-click for more."));
        if (playing)
        {
            row.Background = ThemeBrush("AccentSoftBrush");
        }

        row.Click += (_, _) => Play(station);
        row.ContextRequested += (_, e) =>
        {
            e.Handled = true;
            ShowStationMenu(station, row);
        };
        return row;
    }

    private static string Describe(RadioStation station)
    {
        var lines = new List<string> { station.Name };
        if (station.Tags.Count > 0)
        {
            lines.Add(string.Join(", ", station.Tags.Take(6).Select(Genres.Display)));
        }

        var facts = new List<string>();
        if (station.Country.Length > 0)
        {
            facts.Add(station.Country);
        }

        if (station.Bitrate > 0)
        {
            facts.Add($"{station.Bitrate} kbps {station.Codec}".Trim());
        }

        if (station.Votes > 0)
        {
            facts.Add($"{station.Votes:N0} votes");
        }

        if (facts.Count > 0)
        {
            lines.Add(string.Join(" · ", facts));
        }

        return string.Join("\n", lines);
    }

    private void Play(RadioStation station) => _player.PlayStation(station, _shown);

    private void ShowStationMenu(RadioStation station, Button row)
    {
        var menu = new ContextMenu();
        var play = new MenuItem { Header = "Play" };
        play.Click += (_, _) => Play(station);
        menu.Items.Add(play);

        var favorite = RadioGuide.IsFavorite(_settings, station);
        var star = new MenuItem { Header = favorite ? "Remove from favorites" : "Add to favorites" };
        star.Click += (_, _) => _player.SetStationFavorite(station, !favorite);
        menu.Items.Add(star);

        if (Section == RadioGuide.Recent)
        {
            var forget = new MenuItem { Header = "Remove from recently played" };
            forget.Click += (_, _) =>
            {
                _settings.RadioRecent.RemoveAll(saved => saved.Id == station.Id);
                _settings.Save();
                ShowSections();
                ShowStations(keepPlace: true);
            };
            menu.Items.Add(forget);
        }

        menu.Items.Add(new Separator());
        var website = new MenuItem { Header = "Open the station's website", IsEnabled = station.Homepage is not null };
        website.Click += (_, _) => _player.OpenStationWebsite(station);
        menu.Items.Add(website);
        var supportItem = new MenuItem { Header = "Support the station…", IsEnabled = FeaturedStations.SupportPage(station) is not null };
        supportItem.Click += (_, _) => _player.OpenStationSupport(station);
        menu.Items.Add(supportItem);
        var copy = new MenuItem { Header = "Copy the stream's address" };
        copy.Click += async (_, _) =>
        {
            if (Clipboard is { } clipboard)
            {
                await clipboard.SetTextAsync(station.StreamUrl);
            }
        };
        menu.Items.Add(copy);
        Windows.LucentFullScreen.AddTo(menu);
        menu.Open(row);
    }

    // ---- Sort and filters ----

    private void ShowFilters()
    {
        SortButton.Content = RadioGuide.SortLabel(_settings.RadioSort) + "  ▾";
        ToolTip.SetTip(SortButton, $"Sorted: {RadioGuide.SortDescription(_settings.RadioSort)}. Click to sort another way.");
        QualityButton.Content = (_settings.RadioMinBitrate > 0 ? $"{_settings.RadioMinBitrate}K+" : "ANY QUALITY") + "  ▾";
        ToolTip.SetTip(QualityButton, $"{RadioGuide.QualityLabel(_settings.RadioMinBitrate)}. Click to choose the lowest bitrate to list.");
        var country = _countries.FirstOrDefault(candidate => candidate.Code == _settings.RadioCountry);
        CountryButton.Content = (_settings.RadioCountry is null ? "ANYWHERE" : (country?.Name ?? _settings.RadioCountry).ToUpperInvariant()) + "  ▾";
        ToolTip.SetTip(CountryButton, _settings.RadioCountry is null ? "Stations from every country. Click to choose one." : "Click to choose another country, or every country.");
        VideoButton.Content = _settings.RadioVideoOnly ? "VIDEO ONLY" : "AUDIO + VIDEO";
        ToolTip.SetTip(VideoButton, _settings.RadioVideoOnly
            ? "Only stations with video (the ones with the camera). Click to show every station."
            : "Every station, with and without video. Click to show only the ones with video.");
    }

    private void OnSortMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var sort in Enum.GetValues<RadioSort>())
        {
            var item = new MenuItem
            {
                Header = RadioGuide.SortDescription(sort),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = sort == _settings.RadioSort,
            };
            item.Click += (_, _) => ChangeFilter(() => _settings.RadioSort = sort);
            menu.Items.Add(item);
        }

        menu.Open(SortButton);
    }

    private void OnQualityMenu(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kbps in RadioGuide.Qualities)
        {
            var item = new MenuItem
            {
                Header = RadioGuide.QualityLabel(kbps),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = kbps == _settings.RadioMinBitrate,
            };
            item.Click += (_, _) => ChangeFilter(() => _settings.RadioMinBitrate = kbps);
            menu.Items.Add(item);
        }

        menu.Open(QualityButton);
    }

    private async void OnCountryMenu(object? sender, RoutedEventArgs e)
    {
        if (_countries.Count == 0)
        {
            try
            {
                CountryButton.IsEnabled = false;
                using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                _countries = await _player.RadioDirectory.CountriesAsync(cancel.Token);
            }
            catch (Exception ex)
            {
                await MessageWindow.ShowAsync(this, ex is RadioProblem ? ex.Message : $"The list of countries couldn't be had. {ex.Message}");
                return;
            }
            finally
            {
                CountryButton.IsEnabled = true;
            }
        }

        var menu = new ContextMenu();
        var anywhere = new MenuItem { Header = "Every country", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.RadioCountry is null };
        anywhere.Click += (_, _) => ChangeFilter(() => _settings.RadioCountry = null);
        menu.Items.Add(anywhere);
        menu.Items.Add(new Separator());

        // The countries with the most stations first, and the rest by name under More.
        MenuItem Country(RadioCountry country)
        {
            var item = new MenuItem
            {
                Header = $"{country.Name}  ({Compact(country.Stations)})",
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = country.Code == _settings.RadioCountry,
            };
            item.Click += (_, _) => ChangeFilter(() => _settings.RadioCountry = country.Code);
            return item;
        }

        foreach (var country in _countries.Take(20))
        {
            menu.Items.Add(Country(country));
        }

        if (_countries.Count > 20)
        {
            var more = new MenuItem { Header = "More countries" };
            foreach (var country in _countries.Skip(20).OrderBy(country => country.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                more.Items.Add(Country(country));
            }

            menu.Items.Add(more);
        }

        menu.Open(CountryButton);
    }

    private void OnVideoFilter(object? sender, RoutedEventArgs e) =>
        ChangeFilter(() => _settings.RadioVideoOnly = !_settings.RadioVideoOnly);

    private void ChangeFilter(Action change)
    {
        change();
        _settings.Save();
        ShowFilters();
        ShowStations();
    }

    // ---- Search and keys ----

    private void OnSearchChanged(object? sender, TextChangedEventArgs e)
    {
        ClearSearchButton.IsVisible = SearchText.Length > 0;
        if (_settings is null || !IsLoaded)
        {
            return;
        }

        // Lucent's own lists search as you type; the directory once you pause.
        _searchDelay.Stop();
        if (RadioGuide.IsLocal(Section))
        {
            ShowStations();
        }
        else
        {
            _searchDelay.Start();
        }
    }

    private void OnClearSearch(object? sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        SearchBox.Focus();
    }

    private IReadOnlyList<Button> ShownRows => StationRows.Children.OfType<Button>().ToList();

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Plays the first match, like clicking it. A search still on its way runs now.
                e.Handled = true;
                if (_searchDelay.IsEnabled)
                {
                    _searchDelay.Stop();
                    ShowStations();
                }
                else if (ShownRows.FirstOrDefault()?.Tag is RadioStation first)
                {
                    Play(first);
                }

                break;
            case Key.Escape when SearchText.Length > 0:
                SearchBox.Clear();
                e.Handled = true;
                break;
            case Key.Down when ShownRows.Count > 0:
                ShownRows[0].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
        }
    }

    /// <summary>Up and Down move through the list (Up from the top goes back to the search); Esc closes the window.</summary>
    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !(e.Source is TextBox { Text.Length: > 0 }))
        {
            Close();
            e.Handled = true;
            return;
        }

        if (e.Key is not (Key.Up or Key.Down) || e.Source is not Button focused)
        {
            return;
        }

        var rows = ShownRows.ToList();
        var index = rows.IndexOf(focused);
        if (index < 0)
        {
            return;
        }

        var next = index + (e.Key == Key.Down ? 1 : -1);
        if (next < 0)
        {
            SearchBox.Focus();
        }
        else if (next < rows.Count)
        {
            rows[next].Focus(NavigationMethod.Directional);
            rows[next].BringIntoView();
        }

        e.Handled = true;
    }

    private async void OnPlayAddress(object? sender, RoutedEventArgs e)
    {
        var address = await PromptWindow.AskAsync(this, "Play a station by its address",
            "The stream's address, or its .pls, .m3u or .m3u8 link, from the station's website. Audio and video stations both play.", "PLAY",
            check: text => RadioGuide.IsStreamAddress(text) ? null : "That doesn't look like a web address (http:// or https://).");
        if (address is not null)
        {
            Play(RadioGuide.FromAddress(address));
        }
    }

    private void OnCloseButton(object? sender, RoutedEventArgs e) => Close();

    private IBrush? ThemeBrush(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var brush) ? brush as IBrush : null;

    private Geometry? ThemeIcon(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var icon) ? icon as Geometry : null;

    private ControlTheme? FindTheme(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var theme) ? theme as ControlTheme : null;

    /// <summary>12,400 as "12.4k", so the counts fit beside the genres.</summary>
    private static string Compact(int count) => count switch
    {
        >= 10_000 => $"{count / 1000.0:0}k",
        >= 1_000 => $"{count / 1000.0:0.#}k",
        _ => count.ToString(CultureInfo.CurrentCulture),
    };
}
