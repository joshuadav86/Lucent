using System.Windows;
using WinampInspiredVisualizer.Updates;

namespace WinampInspiredVisualizer;

// Checking for updates on demand, from the Updates tab in Options. The hourly check runs either way.
public partial class MainWindow
{
    private bool _checkingForUpdates;

    private void OnCheckForUpdates(object sender, RoutedEventArgs e) => CheckForUpdates();

    private async void CheckForUpdates()
    {
        if (_checkingForUpdates)
        {
            return;
        }

        _checkingForUpdates = true;
        CheckUpdatesButton.IsEnabled = false;
        ShowUpdateStatus("Checking for updates…", readyToInstall: false);
        var result = await _updater.CheckNowAsync();
        _checkingForUpdates = false;
        CheckUpdatesButton.IsEnabled = true;
        ShowUpdateStatus(UpdateStatus(result), readyToInstall: result.Outcome == UpdateCheckOutcome.Ready);
    }

    private static string UpdateStatus(UpdateCheckResult result) => result.Outcome switch
    {
        UpdateCheckOutcome.Ready => ReadyStatus(result.Version!),
        UpdateCheckOutcome.UpToDate => $"You're up to date: Lucent {AppInfo.Version} is the newest.",
        UpdateCheckOutcome.NotInstalled => "This copy of Lucent was run from the zip, so it can't update itself. Install it with the setup from Lucent's releases page to get updates.",
        _ => "Couldn't check for updates. Check your internet connection and try again.",
    };

    private static string ReadyStatus(string version) =>
        $"Lucent {version} is ready. Restart to install it now, or it installs when you close Lucent.";

    /// <summary>Shows what a check found under the Updates tab, or clears it when <paramref name="status"/> is null.</summary>
    private void ShowUpdateStatus(string? status, bool readyToInstall)
    {
        UpdateStatusText.Text = status;
        UpdateStatusText.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        RestartToUpdateButton.Visibility = readyToInstall ? Visibility.Visible : Visibility.Collapsed;
    }
}
