using System.Diagnostics;
using Lucent.Settings;

namespace Lucent.Mac;

/// <summary>After an update to a new stable release: asks once whether to see what's new on the website (see ReleaseNotes).</summary>
public partial class MainWindow
{
    private async void OfferReleaseNotes()
    {
        // The screenshots and CI's launch check have no one to answer.
        if (_previewing || LayoutCheck.IsAsked
            || ReleaseNotes.Due(_settings, AppInfo.Version, firstStart: _settings.AddStarterMusic) is not { } release)
        {
            return;
        }

        _settings.Save();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var answer = await MessageWindow.ShowAsync(this,
            $"Lucent is now {release} Beta. Would you like to see what's new and what's fixed on the Lucent website?",
            $"Lucent {release} Beta", MessageButtons.YesNo);
        if (answer != MessageResult.Yes)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(ReleaseNotes.PageFor(release)) { UseShellExecute = true });
        }
        catch (Exception)
        {
            // No browser to open it in: the page is still there on the website.
        }
    }
}
