using System.Diagnostics;
using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Import;
using WinampInspiredVisualizer.Settings;

namespace Lucent.Mac;

/// <summary>
/// Brings a library over from another player: Apple Music or iTunes, foobar2000 and other players'
/// playlists, ratings saved in the files, Plex, and YouTube Music playlists from Google Takeout. One
/// import runs at a time; the window can stay open beside the player while it does.
/// </summary>
public partial class ImportWindow : Window
{
    private static ImportWindow? _instance;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly MainWindow _player;
    private readonly AppSettings _settings;
    private readonly PlexClient _plex;
    private CancellationTokenSource? _running;
    private LibraryImportResult? _result;

    /// <summary>For Avalonia's XAML loader and designer only; Lucent opens the window with <see cref="Open"/>.</summary>
    public ImportWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _player = null!;
        _settings = null!;
        _plex = null!;
    }

    internal ImportWindow(MainWindow player, AppSettings settings)
        : this()
    {
        _player = player;
        _settings = settings;
        if (string.IsNullOrEmpty(settings.PlexClientId))
        {
            settings.PlexClientId = Guid.NewGuid().ToString("N");
            settings.Save();
        }

        _plex = new PlexClient(settings.PlexClientId);
        Closed += (_, _) =>
        {
            _running?.Cancel();
            _plex.Dispose();
            _instance = null;
        };
        ShowState();
    }

    /// <summary>Opens the window, or brings it forward if it's already open.</summary>
    public static void Open(MainWindow owner, AppSettings settings)
    {
        if (_instance is not null)
        {
            _instance.Activate();
            return;
        }

        _instance = new ImportWindow(owner, settings);
        _instance.Show(owner);
    }

    /// <summary>For the screenshot tool: shows what an import did, as if one had just finished.</summary>
    internal void PreviewResult(LibraryImportResult result, int newFavorites)
    {
        _result = result;
        ShowStatus(ImportRunner.Summary(result, newFavorites));
        ShowState();
    }

    private bool IsRunning => _running is not null;

    private void ShowState()
    {
        foreach (var button in Sources.Children.OfType<DockPanel>().SelectMany(panel => panel.Children.OfType<Button>()))
        {
            button.IsEnabled = !IsRunning;
        }

        FileTagsButton.IsEnabled = !IsRunning && _player.LibraryFileCount > 0;
        PlexButton.Content = _plex.IsSignedIn ? "IMPORT" : "SIGN IN";
        PlexText.Text = _plex.IsSignedIn
            ? $"Signed in. Lucent reads the music library and playlists on {(_plex.ServerName is { } name ? $"\"{name}\"" : "your server")}."
            : "Sign in on Plex's website, and Lucent reads the music library and playlists on your Plex Media Server.";
        StopButton.IsVisible = IsRunning;
        MissingButton.IsVisible = !IsRunning && _result is { Missing.Count: > 0 };
        var offer = !IsRunning && _result is not null ? ImportRunner.FolderOffer(_result) : null;
        FolderText.Text = offer ?? string.Empty;
        FolderText.IsVisible = offer is not null;
        LinkFolderButton.IsVisible = offer is not null;
    }

    private void ShowStatus(string text)
    {
        StatusText.Text = text;
        StatusText.IsVisible = text.Length != 0;
    }

    private async void OnAppleMusic(object? sender, RoutedEventArgs e)
    {
        var found = ITunesLibraryReader.FindLibraryFile(ImportSource.AppleMusic);
        var options = new FilePickerOpenOptions
        {
            Title = "Choose your Apple Music or iTunes library file",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Library files") { Patterns = new[] { "*.xml" } }, FilePickerFileTypes.All },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                found is not null ? Path.GetDirectoryName(found)! : ITunesLibraryReader.LikelyFolder(ImportSource.AppleMusic)),
        };
        var chosen = await StorageProvider.OpenFilePickerAsync(options);
        if (chosen.Count == 0 || chosen[0].TryGetLocalPath() is not { } file)
        {
            return;
        }

        await RunAsync(ImportSource.AppleMusic, _ => Task.FromResult(ITunesLibraryReader.Read(file)));
    }

    private async void OnPlaylistFiles(object? sender, RoutedEventArgs e)
    {
        var foobar = PlaylistFileReader.FoobarPlaylistFolders().FirstOrDefault();
        var options = new FilePickerOpenOptions
        {
            Title = "Choose playlists to import",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Playlists") { Patterns = PlaylistFileReader.Extensions.Select(ext => "*" + ext).ToList() },
                FilePickerFileTypes.All,
            },
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                foobar ?? Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)),
        };
        var files = (await StorageProvider.OpenFilePickerAsync(options)).Select(f => f.TryGetLocalPath()).OfType<string>().ToList();
        if (files.Count == 0)
        {
            return;
        }

        var source = foobar is not null && files.All(f => f.StartsWith(foobar, StringComparison.OrdinalIgnoreCase))
            ? ImportSource.Foobar2000
            : ImportSource.PlaylistFiles;
        await RunAsync(source, _ => Task.FromResult(PlaylistFileReader.ReadAll(files, source)), "Reading the playlists…");
    }

    private async void OnFileTags(object? sender, RoutedEventArgs e)
    {
        var (files, _) = _player.LibrarySnapshot();
        var progress = new Progress<int>(done => ShowStatus($"Reading ratings and play counts ({done:N0} of {files.Count:N0})…"));
        await RunAsync(ImportSource.FileTags, cancel => Task.FromResult(TagStatsReader.ReadAll(files, progress, cancel)),
            "Reading ratings and play counts…");
    }

    private async void OnPlex(object? sender, RoutedEventArgs e)
    {
        if (_plex.IsSignedIn)
        {
            var progress = new Progress<string>(ShowStatus);
            await RunAsync(ImportSource.Plex, cancel => _plex.ReadLibraryAsync(progress, cancel));
            return;
        }

        _running = new CancellationTokenSource();
        ShowState();
        try
        {
            ShowStatus("Opening Plex's sign-in page…");
            var pin = await _plex.StartSignInAsync(_running.Token);
            Process.Start(new ProcessStartInfo(pin.SignInUrl) { UseShellExecute = true });
            ShowStatus("Sign in on the Plex page that opened in your browser. Lucent carries on once you have.");
            await _plex.WaitForSignInAsync(pin, _running.Token);
            ShowStatus($"Signed in to Plex. Click IMPORT to bring your library over from {_plex.ServerName ?? "your server"}.");
        }
        catch (OperationCanceledException)
        {
            ShowStatus(string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException or System.Text.Json.JsonException or InvalidDataException)
        {
            _plex.SignOut();
            ShowStatus($"Couldn't sign in to Plex. {ex.Message}");
        }
        finally
        {
            _running?.Dispose();
            _running = null;
            ShowState();
        }
    }

    private async void OnYouTubeMusic(object? sender, RoutedEventArgs e)
    {
        var options = new FolderPickerOpenOptions
        {
            Title = "Choose your unzipped Google Takeout folder",
            AllowMultiple = false,
            SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")),
        };
        var chosen = await StorageProvider.OpenFolderPickerAsync(options);
        if (chosen.Count == 0 || chosen[0].TryGetLocalPath() is not { } folder)
        {
            return;
        }

        await RunAsync(ImportSource.YouTubeMusic, cancel => TakeoutReader.ReadAsync(folder, TakeoutReader.OEmbedLookup(Http), cancel),
            "Reading your YouTube Music playlists and looking up their songs…");
    }

    private async Task RunAsync(ImportSource source, Func<CancellationToken, Task<ImportedLibrary>> read, string? starting = null)
    {
        if (IsRunning)
        {
            return;
        }

        _running = new CancellationTokenSource();
        _result = null;
        ShowState();
        ShowStatus(starting ?? $"Reading your {ImportSources.Label(source)} library…");
        try
        {
            var (files, tags) = _player.LibrarySnapshot();
            var result = await ImportRunner.RunAsync(read, files, tags, new Progress<string>(ShowStatus), _running.Token);
            var favorites = _player.ImportFinished(result);
            _result = result;
            ShowStatus(ImportRunner.Summary(result, favorites));
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Stopped. Anything already brought over stays.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException
            or HttpRequestException or System.Text.Json.JsonException or InvalidOperationException)
        {
            ShowStatus($"Couldn't import from {ImportSources.Label(source)}. {ex.Message}");
        }
        finally
        {
            _running?.Dispose();
            _running = null;
            ShowState();
        }
    }

    private void OnShowMissing(object? sender, RoutedEventArgs e)
    {
        if (_result is null)
        {
            return;
        }

        try
        {
            var path = LibraryImporter.WriteMissingList(_result);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            ShowStatus($"Couldn't show the list. {ex.Message}");
        }
    }

    private void OnLinkFolder(object? sender, RoutedEventArgs e)
    {
        if (_result?.FolderToLink is { } folder)
        {
            _player.AddFolderToLibrary(folder);
            _result = _result with { FolderToLink = null };
            ShowStatus($"Added {folder} to your library.");
            ShowState();
        }
    }

    private void OnStop(object? sender, RoutedEventArgs e) => _running?.Cancel();

    private void OnCloseButton(object? sender, RoutedEventArgs e) => Close();
}
