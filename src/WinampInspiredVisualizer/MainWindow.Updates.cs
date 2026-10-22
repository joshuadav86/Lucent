using System.Windows;
using WinampInspiredVisualizer.Updates;

namespace WinampInspiredVisualizer;

// Checking for updates on demand, from the Updates tab in Options. The hourly check runs either way.
public partial class MainWindow
{
    private bool _checkingForUpdates;

    private void OnCheckForUpdates(object sender, RoutedEventArgs e) => CheckForUpdates();

    /// <summary>The title bar's UPDATE button: what's new in the update, and the button that restarts into it.</summary>
    private void OnShowUpdate(object sender, RoutedEventArgs e) => ShowUpdatesTab();

    /// <summary>Opens Options on its Updates tab.</summary>
    private void ShowUpdatesTab()
    {
        Activate();
        if (!OptionsPopup.IsOpen && !_visuals.IsFullscreen)
        {
            ShowOptions();
        }

        UpdatesTabButton.IsChecked = true;
        OnOptionsTab(UpdatesTabButton, new RoutedEventArgs());
    }

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

    private string UpdateStatus(UpdateCheckResult result) => result.Outcome switch
    {
        UpdateCheckOutcome.Ready => ReadyStatus(result.Version!),
        UpdateCheckOutcome.UpToDate => $"You're up to date: Lucent {AppInfo.Version} is the newest.",
        UpdateCheckOutcome.NotInstalled => "This copy of Lucent was run from the zip, so it can't update itself. Install it with the setup from Lucent's releases page to get updates.",
        _ => "Couldn't check for updates. Check your internet connection and try again.",
    };

    private string ReadyStatus(string version) => _settings.InstallUpdatesAutomatically
        ? $"Lucent {version} is ready. Restart to install it now, or it installs when you close Lucent."
        : $"Lucent {version} is ready. It installs only when you choose Restart and install.";

    /// <summary>The title bar's UPDATE button's tip, for the update that's ready.</summary>
    private string UpdateButtonTip(string version) => _settings.InstallUpdatesAutomatically
        ? $"Lucent {version} is ready. Click to see what's new and restart into it, or it installs when you close Lucent."
        : $"Lucent {version} is ready. Click to see what's new and restart into it when you choose.";

    /// <summary>Shows what a check found under the Updates tab, or clears it when <paramref name="status"/> is null.</summary>
    private void ShowUpdateStatus(string? status, bool readyToInstall)
    {
        UpdateStatusText.Text = status;
        UpdateStatusText.Visibility = status is null ? Visibility.Collapsed : Visibility.Visible;
        RestartToUpdateButton.Visibility = readyToInstall ? Visibility.Visible : Visibility.Collapsed;
        if (readyToInstall)
        {
            ShowWhatsNew();
        }
        else
        {
            WhatsNewPanel.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>Lists what's changed since this build in the update that's ready, when GitHub can say.</summary>
    private async void ShowWhatsNew()
    {
        var changes = await _updater.WhatsNewAsync();
        if (RestartToUpdateButton.Visibility != Visibility.Visible)
        {
            return;
        }

        WhatsNewText.Text = string.Join("\n", changes.Select(change => "• " + change));
        WhatsNewPanel.Visibility = changes.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
