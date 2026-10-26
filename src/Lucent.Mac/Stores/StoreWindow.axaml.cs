using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lucent.Mac.Stores;
using Lucent.Mac.Windows;
using Lucent.Settings;
using Lucent.Stores;

namespace Lucent.Mac;

/// <summary>
/// Bandcamp and Beatport in the library: importing their downloads as they arrive, and (experimental)
/// signing in to Bandcamp to sync the whole collection. Stays open beside the player while a sync runs.
/// </summary>
public partial class StoreWindow : Window
{
    private static StoreWindow? _instance;

    private readonly StoreService _service;
    private readonly AppSettings _settings;
    private IReadOnlyList<BandcampPurchase>? _firstSync;

    /// <summary>For Avalonia's XAML loader and designer only; Lucent opens the window with <see cref="Open"/>.</summary>
    public StoreWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _service = null!;
        _settings = null!;
    }

    internal StoreWindow(StoreService service, AppSettings settings)
        : this()
    {
        _service = service;
        _settings = settings;
        foreach (var choice in FormatChoices.Children.OfType<RadioButton>())
        {
            choice.IsChecked = (string?)choice.Tag == settings.BandcampFormat;
        }

        _service.Changed += OnServiceChanged;
        Closed += (_, _) =>
        {
            _service.Changed -= OnServiceChanged;
            _instance = null;
        };
        ShowState();
    }

    /// <summary>Opens the window, or brings it forward if it's already open.</summary>
    public static void Open(Window owner, StoreService service, AppSettings settings)
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        _instance = new StoreWindow(service, settings);
        _instance.Show(owner);
    }

    /// <summary>For the screenshot tool: shows the first-sync question as if the collection had just been looked at.</summary>
    internal void PreviewFirstSync(IReadOnlyList<BandcampPurchase> found) => AskAboutFirstSync(found);

    private void OnServiceChanged(object? sender, EventArgs e) => ShowState();

    private void ShowState()
    {
        BandcampImportSwitch.IsChecked = _settings.BandcampImport;
        BeatportImportSwitch.IsChecked = _settings.BeatportImport;
        WatchFolderText.Text = _service.WatchFolder;
        ToolTip.SetTip(WatchFolderText, _service.WatchFolder);
        MusicFolderText.Text = _service.MusicFolder;
        ToolTip.SetTip(MusicFolderText, _service.MusicFolder);

        var signedIn = _service.IsSignedIn;
        AccountText.Text = signedIn ? $"Signed in as {_service.Username ?? "a Bandcamp fan"}" : "Not signed in";
        SignInButton.Content = signedIn ? "SIGN OUT" : "SIGN IN";
        SyncOptions.IsVisible = signedIn;
        SyncSwitch.IsChecked = _settings.BandcampSync;

        var progress = _service.Progress;
        var syncing = _service.IsSyncing;
        SyncProgress.IsVisible = syncing;
        StopButton.IsVisible = syncing;
        SyncButton.IsEnabled = !syncing;
        FirstSyncChoice.IsVisible = _firstSync is { Count: > 0 } && !syncing;
        if (progress is not null)
        {
            SyncProgress.Value = progress.Total == 0 ? 0 : (progress.Done + progress.CurrentProgress) / progress.Total;
            StatusText.Text = progress.Current.Length == 0
                ? "Downloading…"
                : $"Downloading {progress.Done + 1} of {progress.Total}: {progress.Current}";
        }
        else
        {
            StatusText.Text = _service.Status;
        }

        StatusText.IsVisible = StatusText.Text.Length != 0;
    }

    private void OnImportSwitched(object? sender, RoutedEventArgs e)
    {
        _settings.BandcampImport = BandcampImportSwitch.IsChecked == true;
        _settings.BeatportImport = BeatportImportSwitch.IsChecked == true;
        ApplySettings();
    }

    private async void OnChooseWatchFolder(object? sender, RoutedEventArgs e)
    {
        if (await ChooseFolderAsync("Choose the folder your browser downloads to", _service.WatchFolder) is { } folder)
        {
            _settings.DownloadsWatchFolder = folder;
            ApplySettings();
        }
    }

    private async void OnChooseMusicFolder(object? sender, RoutedEventArgs e)
    {
        if (await ChooseFolderAsync("Choose where music from Bandcamp and Beatport goes", _service.MusicFolder) is { } folder)
        {
            _settings.StoreMusicFolder = folder;
            ApplySettings();
        }
    }

    private async Task<string?> ChooseFolderAsync(string title, string current)
    {
        var options = new FolderPickerOpenOptions { Title = title, AllowMultiple = false };
        if (Directory.Exists(current))
        {
            options.SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(current);
        }

        var chosen = await StorageProvider.OpenFolderPickerAsync(options);
        return chosen.Count > 0 ? chosen[0].TryGetLocalPath() : null;
    }

    private void ApplySettings()
    {
        _settings.Save();
        _service.ApplySettings();
        ShowState();
    }

    private async void OnSignInOrOut(object? sender, RoutedEventArgs e)
    {
        if (_service.IsSignedIn)
        {
            _firstSync = null;
            _service.SignOut();
            ShowState();
            return;
        }

        if (!await _service.SignInAsync(this))
        {
            return;
        }

        ShowState();
        if (!_settings.BandcampSync)
        {
            await FindFirstSyncAsync();
        }
    }

    private void OnFormatChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string format })
        {
            _settings.BandcampFormat = format;
            _settings.Save();
        }
    }

    private async void OnSyncSwitched(object? sender, RoutedEventArgs e)
    {
        if (SyncSwitch.IsChecked != true)
        {
            _settings.BandcampSync = false;
            _settings.Save();
            return;
        }

        // Turning it on for the first time asks about the purchases already in the collection first.
        SyncSwitch.IsChecked = false;
        await FindFirstSyncAsync();
    }

    private async void OnSyncNow(object? sender, RoutedEventArgs e)
    {
        if (_settings.BandcampSync)
        {
            await _service.SyncNewAsync();
        }
        else
        {
            await FindFirstSyncAsync();
        }
    }

    /// <summary>Before the first sync: counts what's not in the library yet and asks whether to download it all.</summary>
    private async Task FindFirstSyncAsync()
    {
        SyncButton.IsEnabled = false;
        var found = await _service.FindNewAsync();
        SyncButton.IsEnabled = true;
        if (found is null)
        {
            return;
        }

        if (found.Count == 0)
        {
            StartSyncing();
            return;
        }

        AskAboutFirstSync(found);
    }

    private void AskAboutFirstSync(IReadOnlyList<BandcampPurchase> found)
    {
        _firstSync = found;
        DownloadAllButton.Content = found.Count == 1 ? "Download it" : $"Download all {found.Count}";
        FirstSyncText.Text = found.Count == 1
            ? "1 purchase in your collection isn't in your library. Download it now, or only sync purchases you make from now on?"
            : $"{found.Count} purchases in your collection aren't in your library. A whole collection can take a lot of space and time. "
              + "Download them all now, or only sync purchases you make from now on?";
        ShowState();
    }

    private async void OnDownloadAll(object? sender, RoutedEventArgs e)
    {
        if (_firstSync is not { } purchases)
        {
            return;
        }

        _firstSync = null;
        StartSyncing();
        await _service.DownloadAsync(purchases);
    }

    private void OnOnlyNew(object? sender, RoutedEventArgs e)
    {
        if (_firstSync is { } purchases)
        {
            _service.Skip(purchases);
        }

        _firstSync = null;
        StartSyncing();
    }

    /// <summary>From here on, new purchases download whenever Lucent starts.</summary>
    private void StartSyncing()
    {
        _settings.BandcampSync = true;
        _settings.Save();
        _service.ApplySettings();
        ShowState();
    }

    private void OnStop(object? sender, RoutedEventArgs e) => _service.Cancel();

    private void OnCloseButton(object? sender, RoutedEventArgs e) => Close();
}
