using System.Net;
using System.Net.Http;
using Velopack;
using Velopack.Sources;

namespace WinampInspiredVisualizer.Updates;

/// <summary>
/// Keeps an installed Lucent up to date from its releases on GitHub: the newest stable release (a
/// "Lucent 1.0" and so on), or, for people who choose every build, the "Latest build" release that
/// each change to main replaces. Until there's a stable release, everyone follows the latest build.
/// It checks shortly after startup and then every hour while Lucent runs, downloads a newer build in
/// the background, and installs it when Lucent closes, or straight away with a restart if asked. Lucent run from the zip isn't installed and never updates.
/// </summary>
public sealed class AppUpdater
{
    // The repository kept its old name when the app became Lucent. GitHub redirects the old name if the
    // repository is ever renamed, so this can stay as it is.
    private const string RepositoryUrl = "https://github.com/joshuadav86/Lucid";

    // GitHub answers this with the newest release that isn't a pre-release, or "not found" if there's none.
    private const string StableReleaseUrl = "https://api.github.com/repos/joshuadav86/Lucid/releases/latest";

    // Stable releases only, and every release: the "Latest build" release is a pre-release, so
    // following it means including pre-releases. Velopack never moves to an older version, so someone
    // who goes back to stable from a newer build stays on it until a stable release passes it.
    private readonly UpdateManager _stable = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private readonly UpdateManager _everyBuild = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true));
    private static readonly HttpClient Http = CreateHttpClient();
    private UpdateManager? _downloadedWith;
    private VelopackAsset? _downloaded;
    private bool _applying;

    /// <summary>Follow every build on main rather than stable releases only.</summary>
    public bool EveryBuild { get; set; }

    // GitHub allows 60 unsigned requests an hour from one address, and a check uses two or three.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Raised on a background thread once a newer build has downloaded and is ready to install. It's
    /// raised again if an even newer build arrives before that one is installed.
    /// </summary>
    public event Action<string>? UpdateReady;

    /// <summary>Checks for updates until Lucent closes. Returns at once if Lucent isn't installed.</summary>
    public async Task RunAsync()
    {
        if (!_stable.IsInstalled)
        {
            return;
        }

        // Let the first visuals compile before using the network and disk.
        await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
        while (!_applying)
        {
            await CheckOnceAsync().ConfigureAwait(false);
            await Task.Delay(CheckInterval).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// After switching between stable releases and every build: forgets a build already downloaded
    /// from the other one and checks again straight away.
    /// </summary>
    public void ChannelChanged()
    {
        if (_applying || !_stable.IsInstalled)
        {
            return;
        }

        _downloaded = null;
        _downloadedWith = null;
        _ = CheckOnceAsync();
    }

    private async Task CheckOnceAsync()
    {
        try
        {
            var manager = EveryBuild || !await HasStableReleaseAsync().ConfigureAwait(false) ? _everyBuild : _stable;
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null || _applying || update.TargetFullRelease.Version == _downloaded?.Version)
            {
                return;
            }

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            _downloaded = update.TargetFullRelease;
            _downloadedWith = manager;
            UpdateReady?.Invoke(_downloaded.Version.ToString());
        }
        catch (Exception)
        {
            // Offline, GitHub unreachable or mid-publish: try again at the next check.
        }
    }

    /// <summary>
    /// Hands the downloaded build to Velopack's updater, which waits for Lucent to exit, installs it
    /// and, if <paramref name="restart"/>, opens Lucent again. Does nothing if there's nothing to install.
    /// </summary>
    public void InstallOnExit(bool restart)
    {
        if (_downloaded is null || _downloadedWith is null || _applying)
        {
            return;
        }

        try
        {
            _downloadedWith.WaitExitThenApplyUpdates(_downloaded, silent: true, restart: restart);
            _applying = true;
        }
        catch (Exception)
        {
            // The update stays downloaded; Velopack installs it the next time Lucent starts.
        }
    }

    /// <summary>True when there's at least one stable release to follow.</summary>
    private static async Task<bool> HasStableReleaseAsync()
    {
        using var response = await Http.GetAsync(StableReleaseUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        // Anything else unexpected (rate limited, say) throws, and the check tries again next time.
        response.EnsureSuccessStatusCode();
        return true;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        // GitHub's API turns away requests that don't say what's asking.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Lucent");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
}
