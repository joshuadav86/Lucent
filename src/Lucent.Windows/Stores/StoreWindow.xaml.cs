using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WinampInspiredVisualizer.Stores;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

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

    private StoreWindow(StoreService service, AppSettings settings)
    {
        InitializeComponent();
        _service = service;
        _settings = settings;
        foreach (var choice in FormatChoices.Children.OfType<RadioButton>())
        {
            choice.IsChecked = (string)choice.Tag == settings.BandcampFormat;
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

        _instance = new StoreWindow(service, settings) { Owner = owner };
        _instance.Show();
    }

    private void OnServiceChanged(object? sender, EventArgs e) => ShowState();

    private void ShowState()
    {
        BandcampImportSwitch.IsChecked = _settings.BandcampImport;
        BeatportImportSwitch.IsChecked = _settings.BeatportImport;
        WatchFolderText.Text = _service.WatchFolder;
        WatchFolderText.ToolTip = _service.WatchFolder;
        MusicFolderText.Text = _service.MusicFolder;
        MusicFolderText.ToolTip = _service.MusicFolder;

        var signedIn = _service.IsSignedIn;
        AccountText.Text = signedIn ? $"Signed in as {_service.Username ?? "a Bandcamp fan"}" : "Not signed in";
        SignInButton.Content = signedIn ? "SIGN OUT" : "SIGN IN";
        SyncOptions.Visibility = signedIn ? Visibility.Visible : Visibility.Collapsed;
        SyncSwitch.IsChecked = _settings.BandcampSync;

        var progress = _service.Progress;
        var syncing = _service.IsSyncing;
        SyncProgress.Visibility = syncing ? Visibility.Visible : Visibility.Collapsed;
        StopButton.Visibility = syncing ? Visibility.Visible : Visibility.Collapsed;
        SyncButton.IsEnabled = !syncing;
        FirstSyncChoice.Visibility = _firstSync is { Count: > 0 } && !syncing ? Visibility.Visible : Visibility.Collapsed;
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

        StatusText.Visibility = StatusText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnImportSwitched(object sender, RoutedEventArgs e)
    {
        _settings.BandcampImport = BandcampImportSwitch.IsChecked == true;
        _settings.BeatportImport = BeatportImportSwitch.IsChecked == true;
        ApplySettings();
    }

    private void OnChooseWatchFolder(object sender, RoutedEventArgs e)
    {
        if (ChooseFolder("Choose the folder your browser downloads to", _service.WatchFolder) is { } folder)
        {
            _settings.DownloadsWatchFolder = folder;
            ApplySettings();
        }
    }

    private void OnChooseMusicFolder(object sender, RoutedEventArgs e)
    {
        if (ChooseFolder("Choose where music from Bandcamp and Beatport goes", _service.MusicFolder) is { } folder)
        {
            _settings.StoreMusicFolder = folder;
            ApplySettings();
        }
    }

    private string? ChooseFolder(string title, string current)
    {
        var dialog = new OpenFolderDialog { Title = title, InitialDirectory = current };
        return dialog.ShowDialog(this) == true ? dialog.FolderName : null;
    }

    private void ApplySettings()
    {
        _settings.Save();
        _service.ApplySettings();
        ShowState();
    }

    private async void OnSignInOrOut(object sender, RoutedEventArgs e)
    {
        if (_service.IsSignedIn)
        {
            _firstSync = null;
            _service.SignOut();
            ShowState();
            return;
        }

        if (!_service.SignIn(this))
        {
            return;
        }

        ShowState();
        if (!_settings.BandcampSync)
        {
            await FindFirstSyncAsync();
        }
    }

    private void OnFormatChosen(object sender, RoutedEventArgs e)
    {
        _settings.BandcampFormat = (string)((RadioButton)sender).Tag;
        _settings.Save();
    }

    private async void OnSyncSwitched(object sender, RoutedEventArgs e)
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

    private async void OnSyncNow(object sender, RoutedEventArgs e)
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

        _firstSync = found;
        DownloadAllButton.Content = found.Count == 1 ? "Download it" : $"Download all {found.Count}";
        FirstSyncText.Text = found.Count == 1
            ? "1 purchase in your collection isn't in your library. Download it now, or only sync purchases you make from now on?"
            : $"{found.Count} purchases in your collection aren't in your library. A whole collection can take a lot of space and time. "
              + "Download them all now, or only sync purchases you make from now on?";
        ShowState();
    }

    private async void OnDownloadAll(object sender, RoutedEventArgs e)
    {
        if (_firstSync is not { } purchases)
        {
            return;
        }

        _firstSync = null;
        StartSyncing();
        await _service.DownloadAsync(purchases);
    }

    private void OnOnlyNew(object sender, RoutedEventArgs e)
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

    private void OnStop(object sender, RoutedEventArgs e) => _service.Cancel();

    private void OnCloseButton(object sender, RoutedEventArgs e) => Close();
}
