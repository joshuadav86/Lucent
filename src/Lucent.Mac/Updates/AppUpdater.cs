using System.Net;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace Lucent.Mac.Updates;

/// <summary>
/// Keeps an installed Lucent up to date from its releases on GitHub: the newest stable release (a
/// "Lucent 1.0" and so on), or, for people who choose every build, the "Latest build" release that
/// each change to main replaces. Until there's a stable release, everyone follows the latest build.
/// It checks shortly after startup and then every hour while Lucent runs, downloads a newer build in
/// the background, and installs it when Lucent closes, or straight away with a restart if asked. With
/// <see cref="InstallAutomatically"/> off it only installs with that restart. Options
/// can also check on demand with <see cref="CheckNowAsync"/>. The Mac builds are
/// their own Velopack channels (osx-arm64 and osx-x64, set when CI packs them), so each Mac gets the
/// build for its own chip. Lucent.app from the plain zip isn't installed and never updates.
/// </summary>
public sealed class AppUpdater
{
    // The repository kept its old name when the app became Lucent. GitHub redirects the old name if the
    // repository is ever renamed, so this can stay as it is.
    private const string RepositoryUrl = "https://github.com/joshuadav86/Lucid";

    // GitHub answers this with the newest release that isn't a pre-release, or "not found" if there's none.
    private const string StableReleaseUrl = "https://api.github.com/repos/joshuadav86/Lucid/releases/latest";

    // The "Latest build" release, which names the commit it was built from.
    private const string EveryBuildReleaseUrl = "https://api.github.com/repos/joshuadav86/Lucid/releases/tags/latest";

    // The changes between two commits.
    private const string CompareUrl = "https://api.github.com/repos/joshuadav86/Lucid/compare/";

    // Stable releases only, and every release: the "Latest build" release is a pre-release, so
    // following it means including pre-releases. Velopack never moves to an older version, so someone
    // who goes back to stable from a newer build stays on it until a stable release passes it.
    private readonly UpdateManager _stable = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false));
    private readonly UpdateManager _everyBuild = new(new GithubSource(RepositoryUrl, accessToken: null, prerelease: true));
    private static readonly HttpClient Http = CreateHttpClient();
    private UpdateManager? _downloadedWith;
    private VelopackAsset? _downloaded;

    // Where the downloaded build was made from (a commit or a release's tag), for what's new in it.
    private string? _downloadedFrom;
    private (string From, IReadOnlyList<string> Changes)? _whatsNew;
    private bool _applying;

    // The hourly check and one asked for in Options take turns, so they don't download the same build twice.
    private readonly SemaphoreSlim _checking = new(1, 1);

    /// <summary>Follow every build on main rather than stable releases only.</summary>
    public bool EveryBuild { get; set; }

    /// <summary>Install a downloaded build when Lucent closes. Off, only a restart asked for installs it.</summary>
    public bool InstallAutomatically { get; set; } = true;

    // GitHub allows 60 unsigned requests an hour from one address, and a check uses two or three.
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    /// <summary>
    /// Raised on a background thread once a newer build has downloaded and is ready to install. It's
    /// raised again if an even newer build arrives before that one is installed.
    /// </summary>
    public event Action<string>? UpdateReady;

    /// <summary>
    /// Raised on a background thread when a check asked for with <see cref="CheckNowAsync"/> finds a newer
    /// build and starts downloading it.
    /// </summary>
    public event Action<string>? Downloading;

    /// <summary>False for Lucent run from the zip, which can't update itself.</summary>
    public bool IsInstalled => _stable.IsInstalled;

    /// <summary>The version that's downloaded and waiting to install, if there is one.</summary>
    public string? ReadyVersion => _downloaded?.Version.ToString();

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
        _downloadedFrom = null;
        _ = CheckOnceAsync();
    }

    /// <summary>
    /// Checks for a newer build now, as the hourly check does, and downloads it if there is one. A
    /// download raises <see cref="UpdateReady"/> as usual, so the title bar's UPDATE button shows too.
    /// </summary>
    public async Task<UpdateCheckResult> CheckNowAsync()
    {
        if (!_stable.IsInstalled)
        {
            return new UpdateCheckResult(UpdateCheckOutcome.NotInstalled);
        }

        try
        {
            var ready = await CheckAndDownloadAsync(announceDownload: true).ConfigureAwait(false);
            return ready is null
                ? new UpdateCheckResult(UpdateCheckOutcome.UpToDate)
                : new UpdateCheckResult(UpdateCheckOutcome.Ready, ready);
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Checking for updates", ex);
            return new UpdateCheckResult(UpdateCheckOutcome.Failed);
        }
    }

    private async Task CheckOnceAsync()
    {
        try
        {
            await CheckAndDownloadAsync(announceDownload: false).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Offline, GitHub unreachable or mid-publish: try again at the next check.
        }
    }

    /// <summary>Returns the version that's downloaded and ready to install, or null if Lucent is up to date.</summary>
    private async Task<string?> CheckAndDownloadAsync(bool announceDownload)
    {
        await _checking.WaitAsync().ConfigureAwait(false);
        try
        {
            var manager = EveryBuild || !await HasStableReleaseAsync().ConfigureAwait(false) ? _everyBuild : _stable;
            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null || _applying || update.TargetFullRelease.Version == _downloaded?.Version)
            {
                return ReadyVersion;
            }

            var version = update.TargetFullRelease.Version.ToString();
            if (announceDownload)
            {
                Downloading?.Invoke(version);
            }

            await manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            _downloaded = update.TargetFullRelease;
            _downloadedWith = manager;
            _downloadedFrom = await ReleaseSourceAsync(manager == _everyBuild).ConfigureAwait(false);
            UpdateReady?.Invoke(version);
            return version;
        }
        finally
        {
            _checking.Release();
        }
    }

    /// <summary>
    /// Hands the downloaded build to Velopack's updater (UpdateMac, inside Lucent.app), which waits for Lucent to exit, replaces the app with it
    /// and, if <paramref name="restart"/>, opens Lucent again. Does nothing if there's nothing to install,
    /// or when updates don't install automatically and no restart was asked for.
    /// </summary>
    public void InstallOnExit(bool restart)
    {
        if (_downloaded is null || _downloadedWith is null || _applying || (!restart && !InstallAutomatically))
        {
            return;
        }

        try
        {
            _downloadedWith.WaitExitThenApplyUpdates(_downloaded, silent: true, restart: restart);
            _applying = true;
        }
        catch (Exception ex)
        {
            // The update stays downloaded; Velopack installs it the next time Lucent starts (unless
            // updates don't install automatically, when it waits to be asked again).
            ErrorLog.Write("Installing the update", ex);
        }
    }

    /// <summary>
    /// What's new in the downloaded build since this one: the title of each change merged in between,
    /// newest first. Empty when there's nothing downloaded, or GitHub can't say.
    /// </summary>
    public async Task<IReadOnlyList<string>> WhatsNewAsync()
    {
        if (_downloadedFrom is not { } from || AppInfo.Commit is not { } commit)
        {
            return [];
        }

        if (_whatsNew is { } known && known.From == from)
        {
            return known.Changes;
        }

        try
        {
            using var response = await Http.GetAsync(CompareUrl + commit + "..." + from).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            var changes = json.RootElement.GetProperty("commits").EnumerateArray()
                .Select(entry => entry.GetProperty("commit").GetProperty("message").GetString() ?? "")
                .Select(ChangeTitle)
                .OfType<string>()
                .Reverse()
                .Distinct()
                .ToList();
            _whatsNew = (from, changes);
            return changes;
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Finding what's new in an update", ex);
            return [];
        }
    }

    /// <summary>
    /// The title of a change merged into main ("Merge pull request #12 from …", a blank line, then the
    /// title), or null for any other commit: the commits inside a change say how, not what.
    /// </summary>
    private static string? ChangeTitle(string message)
    {
        if (!message.StartsWith("Merge pull request #", StringComparison.Ordinal))
        {
            return null;
        }

        var lines = message.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return lines.Length > 1 ? lines[1] : null;
    }

    /// <summary>
    /// The commit the "Latest build" release was made from, or the newest stable release's tag. Null if
    /// GitHub can't say; what's new is left out then.
    /// </summary>
    private static async Task<string?> ReleaseSourceAsync(bool everyBuild)
    {
        try
        {
            using var response = await Http.GetAsync(everyBuild ? EveryBuildReleaseUrl : StableReleaseUrl).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
            return json.RootElement.GetProperty(everyBuild ? "target_commitish" : "tag_name").GetString();
        }
        catch (Exception)
        {
            return null;
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

/// <summary>What a check for updates found.</summary>
public enum UpdateCheckOutcome
{
    /// <summary>The build in use is the newest one to follow.</summary>
    UpToDate,

    /// <summary>A newer build has downloaded and is waiting to install.</summary>
    Ready,

    /// <summary>Lucent isn't installed (it was run from the zip), so it can't update itself.</summary>
    NotInstalled,

    /// <summary>GitHub couldn't be reached, or the download failed.</summary>
    Failed,
}

/// <summary>The outcome of a check for updates, and the version waiting to install when there is one.</summary>
public sealed record UpdateCheckResult(UpdateCheckOutcome Outcome, string? Version = null);
