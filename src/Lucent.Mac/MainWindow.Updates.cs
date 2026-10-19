using Avalonia.Interactivity;
using Lucent.Mac.Updates;

namespace Lucent.Mac;

// Checking for updates on demand, from the Updates tab in Options or Check for Updates in the menus.
// The hourly check runs either way.
public partial class MainWindow
{
    private bool _checkingForUpdates;

    private void OnCheckForUpdates(object? sender, RoutedEventArgs e) => CheckForUpdates();

    /// <summary>The title bar's UPDATE button: what's new in the update, and the button that restarts into it.</summary>
    private void OnShowUpdate(object? sender, RoutedEventArgs e) => ShowUpdatesTab();

    /// <summary>Check for Updates in the Lucent and Help menus: shows the Updates tab and checks there.</summary>
    private void CheckForUpdatesFromMenu()
    {
        ShowUpdatesTab();
        CheckForUpdates();
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

    private static string UpdateStatus(UpdateCheckResult result) => result.Outcome switch
    {
        UpdateCheckOutcome.Ready => ReadyStatus(result.Version!),
        UpdateCheckOutcome.UpToDate => $"You're up to date: Lucent {AppInfo.Version} is the newest.",
        UpdateCheckOutcome.NotInstalled => "This copy of Lucent came from the plain zip, so it can't update itself. Install it from Lucent's releases page to get updates.",
        _ => "Couldn't check for updates. Check your internet connection and try again.",
    };

    private static string ReadyStatus(string version) =>
        $"Lucent {version} is ready. Restart to install it now, or it installs when you close Lucent.";

    /// <summary>Shows what a check found under the Updates tab, or clears it when <paramref name="status"/> is null.</summary>
    private void ShowUpdateStatus(string? status, bool readyToInstall)
    {
        UpdateStatusText.Text = status;
        UpdateStatusText.IsVisible = status is not null;
        RestartToUpdateButton.IsVisible = readyToInstall;
        if (readyToInstall)
        {
            ShowWhatsNew();
        }
        else
        {
            WhatsNewPanel.IsVisible = false;
        }
    }

    /// <summary>Lists what's changed since this build in the update that's ready, when GitHub can say.</summary>
    private async void ShowWhatsNew()
    {
        var changes = await _updater.WhatsNewAsync();
        if (!RestartToUpdateButton.IsVisible)
        {
            return;
        }

        WhatsNewText.Text = string.Join("\n", changes.Select(change => "• " + change));
        WhatsNewPanel.IsVisible = changes.Count > 0;
    }
}
