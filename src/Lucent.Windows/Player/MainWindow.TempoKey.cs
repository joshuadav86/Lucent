using System.Windows;
using Lucent.Library;

namespace Lucent;

// Filling in missing BPM and key, from the Library tab in Options: songs without them are listened to as they play, and
// through the library if asked, and what's heard is written into their files. Library/TempoKeyFiller.cs does the work.
public partial class MainWindow
{
    private TempoKeyFiller? _tempoKey;

    private void SetUpTempoKey()
    {
        var filler = new TempoKeyFiller(
            path => _player.IsCurrentFile(path) || string.Equals(_player.QueuedPath, path, StringComparison.OrdinalIgnoreCase),
            _player.RunWithFileReleasedAsync,
            action => Dispatcher.InvokeAsync(action))
        {
            OnPlay = _settings.FillMissingTempoAndKey,
            ScanLibrary = _settings.FillTempoAndKeyInLibrary,
        };
        filler.Filled += OnTagsSaved;
        filler.StatusChanged += (_, _) => ShowTempoKeyState();
        _library.TagsLoaded += (_, _) => filler.SetLibrary(_library.Items);
        _tempoKey = filler;
    }

    private void OnTempoKeySwitched(object sender, RoutedEventArgs e)
    {
        _settings.FillMissingTempoAndKey = FillTempoKeySwitch.IsChecked == true;
        _settings.FillTempoAndKeyInLibrary = FillTempoKeyLibrarySwitch.IsChecked == true;
        _settings.Save();
        if (_tempoKey is not null)
        {
            _tempoKey.OnPlay = _settings.FillMissingTempoAndKey;
            _tempoKey.ScanLibrary = _settings.FillTempoAndKeyInLibrary;
            if (_settings.FillMissingTempoAndKey && _current is not null)
            {
                // The song playing now counts as just played.
                _tempoKey.Played(_current.Track.Path);
            }
        }

        ShowTempoKeyState();
    }

    /// <summary>Marks the switches on the Library tab, and says how far the library has got.</summary>
    private void ShowTempoKeyState()
    {
        FillTempoKeySwitch.IsChecked = _settings.FillMissingTempoAndKey;
        FillTempoKeyLibrarySwitch.IsChecked = _settings.FillTempoAndKeyInLibrary;
        FillTempoKeyLibrarySwitch.IsEnabled = _settings.FillMissingTempoAndKey;
        var status = _tempoKey?.Status ?? string.Empty;
        TempoKeyStatusText.Text = status;
        TempoKeyStatusText.Visibility = status.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
