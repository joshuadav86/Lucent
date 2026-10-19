using System.Windows;

namespace WinampInspiredVisualizer;

// Lucent as the player that opens music from Explorer: songs it's started with or handed by
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

    private void OnMakeDefaultPlayer(object sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociations.OpenDefaultAppsSettings();
            ShowDefaultPlayerStatus("In the Settings window that opened, choose Lucent for your music files " +
                "(MP3, FLAC and the rest). Windows has you make this choice there yourself.");
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Opening Default apps", ex);
            ShowDefaultPlayerStatus("Couldn't open Settings. Go to Settings > Apps > Default apps and choose Lucent there.");
        }
    }

    private void ShowDefaultPlayerStatus(string status)
    {
        DefaultPlayerStatusText.Text = status;
        DefaultPlayerStatusText.Visibility = Visibility.Visible;
    }
}
