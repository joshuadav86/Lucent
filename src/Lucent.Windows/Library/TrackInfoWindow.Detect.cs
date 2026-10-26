using System.Windows;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Dsp;
using WinampInspiredVisualizer.Library;

namespace WinampInspiredVisualizer;

/// <summary>
/// Detect, beside the BPM and key: listens to the whole song for its tempo and key and fills them in, ready to save
/// into the file, where DJ apps and other players read them.
/// </summary>
public partial class TrackInfoWindow
{
    private CancellationTokenSource? _detecting;

    private async void OnDetectTempoAndKey(object sender, RoutedEventArgs e)
    {
        if (_tracks.Count != 1 || _detecting is not null)
        {
            return;
        }

        var path = _tracks[0].Path;
        var detecting = new CancellationTokenSource();
        _detecting = detecting;
        DetectButton.IsEnabled = false;
        DetectButton.Content = "Listening…";
        ShowStatus("Listening for the tempo and key…");
        try
        {
            var music = await Task.Run(() =>
            {
                var (audio, _, _) = AudioFileLoader.Load(path, null, detecting.Token);
                return MusicAnalysis.Analyze(audio, detecting.Token);
            }, detecting.Token);

            if (_tracks.Count != 1 || _tracks[0].Path != path)
            {
                return;
            }

            if (music.Grid is { } grid)
            {
                BpmBox.Text = TagFields.NumberText((uint)Math.Round(grid.Tempo));
            }

            if (music.Key is { } key)
            {
                KeyBox.Text = key.TagText;
            }

            ShowStatus(music.Summary is { } summary
                ? $"Lucent hears {summary}. Save to keep them in the file."
                : "Lucent couldn't hear a steady beat or a key in this one.");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            ShowStatus("Lucent couldn't listen to this file.", warning: true);
        }
        finally
        {
            _detecting = null;
            detecting.Dispose();
            DetectButton.Content = "Detect";
            DetectButton.IsEnabled = _tracks.Count == 1;
        }
    }
}
