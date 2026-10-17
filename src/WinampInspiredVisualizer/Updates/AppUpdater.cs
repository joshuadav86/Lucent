using Velopack;
using Velopack.Sources;

namespace WinampInspiredVisualizer.Updates;

/// <summary>
/// Keeps an installed Lucent up to date from the "Latest build" release on GitHub. It checks once
/// at startup, downloads a newer build in the background, and installs it when Lucent closes, or
/// straight away with a restart if asked. Lucent run from the zip isn't installed and never updates.
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

    /// <summary>Raised on a background thread once a newer build has downloaded and is ready to install.</summary>
    public event Action<string>? UpdateReady;

    public async Task CheckAsync()
    {
        if (!_manager.IsInstalled)
        {
            return;
        }

        try
        {
            // Let the first visuals compile before using the network and disk.
            await Task.Delay(TimeSpan.FromSeconds(20)).ConfigureAwait(false);
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return;
            }

            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            _downloaded = update.TargetFullRelease;
            UpdateReady?.Invoke(_downloaded.Version.ToString());
        }
        catch (Exception)
        {
            // Offline, GitHub unreachable or mid-publish: try again next time Lucent starts.
        }
    }

    /// <summary>
    /// Hands the downloaded build to Velopack's updater, which waits for Lucent to exit, installs it
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
