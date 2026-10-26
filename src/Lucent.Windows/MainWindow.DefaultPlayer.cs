using System.Windows;
using WinampInspiredVisualizer.Library;

namespace WinampInspiredVisualizer;

// Lucent as the player that opens music and videos from Explorer: songs it's started with or handed by
// another Lucent, and the Default player choice in Options > Updates.
public partial class MainWindow
{
    /// <summary>After the player opens: plays what Lucent was started with, and listens for more.</summary>
    private void StartOpeningFiles()
    {
        // An installed Lucent keeps Default apps pointing at itself; the install folder stays put
        // across updates, but this also lists copies installed before Lucent did this.
        if (_updater.IsInstalled)
        {
            Task.Run(() =>
            {
                try
                {
                    FileAssociations.Register();
                }
                catch (Exception ex)
                {
                    ErrorLog.Write("Listing Lucent in Default apps", ex);
                }
            });
        }

        if (App.StartupPaths.Count > 0)
        {
            OpenFiles(App.StartupPaths);
        }

        OpenRequests.Listen(paths => Dispatcher.BeginInvoke(() => OpenFiles(paths)));
    }

    /// <summary>Plays music opened from Explorer, in place of the queue, with the player in front.</summary>
    private void OpenFiles(IReadOnlyList<string> paths)
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        PlayDroppedPaths(paths);
    }

    /// <summary>Says whether double-clicked music and videos open in Lucent now, as Options shows the Updates tab.</summary>
    private void ShowDefaultPlayerState()
    {
        try
        {
            ShowDefaultPlayerStatus(DefaultPlayerStatus.Describe(FileAssociations.TypesElsewhere()));
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Checking the default player", ex);
            DefaultPlayerStatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void OnMakeDefaultPlayer(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociations.OpenDefaultAppsSettings();
            // Windows only lets people choose the default themselves, so this can't be reported as done
            // yet: Options checks again the next time it's opened.
            ShowDefaultPlayerStatus("In the Settings window that opened, click Set default at the top of Lucent's page " +
                "(on Windows 10, choose Lucent for each type, WAV, MP3, and MP4 among them). Then open Options again to check it took.");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Opening Default apps", ex);
            ShowDefaultPlayerStatus("Couldn't open Settings. Go to Settings > Apps > Default apps, find Lucent, and click Set default.");
        }
    }

    private void ShowDefaultPlayerStatus(string status)
    {
        DefaultPlayerStatusText.Text = status;
        DefaultPlayerStatusText.Visibility = Visibility.Visible;
    }
}
