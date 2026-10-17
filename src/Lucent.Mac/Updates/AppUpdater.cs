using Velopack;
using Velopack.Sources;

namespace Lucent.Mac.Updates;

/// <summary>
/// Keeps an installed Lucent up to date from the "Latest build" release on GitHub. It checks shortly
/// after startup and then every hour while Lucent runs, downloads a newer build in the background,
/// and installs it when Lucent closes, or straight away with a restart if asked. The Mac builds are
/// their own Velopack channels (osx-arm64 and osx-x64, set when CI packs them), so each Mac gets the
/// build for its own chip. Lucent.app from the plain zip isn't installed and never updates.
/// </summary>
public sealed class AppUpdater
{
    // The repository kept its old name when the app became Lucent. GitHub redirects the old name if the
    // repository is ever renamed, so this can stay as it is.
    private const string RepositoryUrl = "https://github.com/joshuadav86/Lucid";

    // The "Latest build" release is a pre-release, so pre-releases have to be included.
    private readonly UpdateManager _manager = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true));
    private VelopackAsset? _downloaded;
    private bool _applying;

    // GitHub allows 60 unsigned requests an hour from one address, and a check uses one or two.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Raised on a background thread once a newer build has downloaded and is ready to install. It's
    /// raised again if an even newer build arrives before that one is installed.
    /// </summary>
    public event Action<string>? UpdateReady;

    /// <summary>Checks for updates until Lucent closes. Returns at once if Lucent isn't installed.</summary>
    public async Task RunAsync()
    {
        if (!_manager.IsInstalled)
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

    private async Task CheckOnceAsync()
    {
        try
        {
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null || _applying || update.TargetFullRelease.Version == _downloaded?.Version)
            {
                return;
            }

            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            _downloaded = update.TargetFullRelease;
            UpdateReady?.Invoke(_downloaded.Version.ToString());
        }
        catch (Exception)
        {
            // Offline, GitHub unreachable or mid-publish: try again at the next check.
        }
    }

    /// <summary>
    /// Hands the downloaded build to Velopack's updater (UpdateMac, inside Lucent.app), which waits for Lucent to exit, replaces the app with it
    /// and, if <paramref name="restart"/>, opens Lucent again. Does nothing if there's nothing to install.
    /// </summary>
    public void InstallOnExit(bool restart)
    {
        if (_downloaded is null || _applying)
        {
            return;
        }

        try
        {
            _manager.WaitExitThenApplyUpdates(_downloaded, silent: true, restart: restart);
            _applying = true;
        }
        catch (Exception)
        {
            // The update stays downloaded; Velopack installs it the next time Lucent starts.
        }
    }
}
