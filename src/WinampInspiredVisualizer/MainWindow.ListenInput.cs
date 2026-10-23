using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using WinampInspiredVisualizer.Audio;

namespace WinampInspiredVisualizer;

// Visuals from an input: instead of every app's sound, the visuals, meter and analyzer follow one of the
// audio interface's inputs. It's for apps that skip Windows' mixer, which loopback can't hear: a DAW like
// Ableton playing through its ASIO driver. The interface's own mixer sends what the DAW plays back to an
// input (TotalMix's Loopback), and Lucent listens there. Chosen under the "Visuals for every app's sound" switch.
public partial class MainWindow
{
    private LiveInput? _inputListening;
    private InputFeed? _inputFeed;

    // The input and channel being listened to ("id#channel"), and the one that couldn't be, which isn't tried
    // again until it's chosen again.
    private string? _listeningToInput;
    private string? _inputFailed;

    private string? WantedInput() =>
        _settings.ListenToOtherApps && _settings.ListenInput is { } id ? $"{id}#{_settings.ListenInputChannel}" : null;

    /// <summary>
    /// Starts or stops listening to the input chosen for the visuals. True while one is listened to, so every
    /// app's sound isn't listened to as well.
    /// </summary>
    private bool UpdateInputListening()
    {
        var wanted = WantedInput();
        if (wanted is not null && wanted == _inputFailed)
        {
            wanted = null;
        }

        if (wanted == _listeningToInput)
        {
            return _inputListening is not null;
        }

        StopInputListening();
        if (wanted is null)
        {
            return false;
        }

        // Every app's sound and the input both feed the visuals, so one goes before the other starts.
        _systemAudio?.Dispose();
        _systemAudio = null;
        _listeningTo = null;
        InputFeed? feed = null;
        try
        {
            var input = LiveInputs.ListForVisuals().FirstOrDefault(candidate => candidate.Id == _settings.ListenInput)
                ?? throw new IOException("The input isn't connected.");
            var details = LiveInputs.Describe(input);
            feed = new InputFeed(_player.Capture, details.NativeRate ?? 48000);
            var live = LiveInput.Start(input, feed, Math.Clamp(_settings.ListenInputChannel, 0, Math.Max(0, details.Channels - 1)));
            live.Stopped += (_, _) => Dispatcher.BeginInvoke(() =>
            {
                // Unplugged, say: back to every app's sound until it's chosen again.
                if (_inputListening == live)
                {
                    StopInputListening();
                    _inputFailed = wanted;
                    UpdateListening();
                }
            });
            _inputListening = live;
            _inputFeed = feed;
            _listeningToInput = wanted;
            return true;
        }
        catch (Exception ex)
        {
            feed?.Dispose();
            ErrorLog.Write("Visuals input", ex);
            Trace.WriteLine($"Couldn't listen to the input for the visuals: {ex}");
            _inputFailed = wanted;
            return false;
        }
    }

    private void StopInputListening()
    {
        _inputListening?.Dispose();
        _inputListening = null;
        _inputFeed?.Dispose();
        _inputFeed = null;
        _listeningToInput = null;
    }

    /// <summary>Shows what the visuals listen to under the switch, and how to get a DAW on ASIO into them.</summary>
    private void UpdateListenSource()
    {
        ListenSourceButton.Visibility = _settings.ListenToOtherApps ? Visibility.Visible : Visibility.Collapsed;
        if (!_settings.ListenToOtherApps)
        {
            return;
        }

        var output = SystemAudio.DefaultOutputName() ?? "this PC";
        if (_settings.ListenInput is null)
        {
            ListenSourceName.Text = "Every app's sound";
            ListenHint.Text = $"The visuals, meter, and analyzer react to whatever is playing on {output}: Spotify, a browser, a game. "
                + "A DAW on ASIO (Ableton, say) skips Windows' mixer, so it isn't heard: loop it back to an input in your interface's mixer and pick that input above.";
            return;
        }

        var name = ListenInputName();
        ListenSourceName.Text = name;
        ListenHint.Text = _inputFailed is not null && _inputFailed == WantedInput()
            ? $"Lucent couldn't listen to {name}. Is it plugged in? The visuals hear every app's sound on {output} for now; pick the input again to retry."
            : $"The visuals, meter, and analyzer follow {name}. For Ableton on an RME interface, turn on Loopback for the output Ableton plays to in TotalMix, "
                + "and pick that output's input here.";
    }

    // The chosen input's name, with its channels when it has more than two.
    private string ListenInputName()
    {
        var input = LiveInputs.ListForVisuals().FirstOrDefault(candidate => candidate.Id == _settings.ListenInput);
        if (input is null)
        {
            return "the chosen input";
        }

        return InputChannels(input) > 2 ? $"{input.Name}: {PairName(_settings.ListenInputChannel)}" : input.Name;
    }

    private static int InputChannels(RecordingInput input)
    {
        try
        {
            return LiveInputs.Describe(input).Channels;
        }
        catch (Exception)
        {
            return 2;
        }
    }

    private static string PairName(int first) => $"inputs {first + 1}–{first + 2}";

    private void OnChooseListenSource(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ListenSourceButton };
        var every = new MenuItem
        {
            Header = $"Every app's sound on {SystemAudio.DefaultOutputName() ?? "this PC"}",
            IsChecked = _settings.ListenInput is null,
        };
        every.Click += (_, _) => UseListenSource(null, 0);
        menu.Items.Add(every);

        var inputs = LiveInputs.ListForVisuals();
        if (inputs.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var input in inputs)
        {
            var chosen = _settings.ListenInput == input.Id;
            var channels = InputChannels(input);
            var item = new MenuItem { Header = input.Name, IsChecked = chosen };
            if (channels <= 2)
            {
                item.Click += (_, _) => UseListenSource(input.Id, 0);
            }
            else
            {
                // One choice per pair of channels: the loopback lands on the pair of the output it was turned on for.
                for (var first = 0; first + 1 < channels; first += 2)
                {
                    var pair = first;
                    var pairItem = new MenuItem { Header = PairName(pair), IsChecked = chosen && _settings.ListenInputChannel == pair };
                    pairItem.Click += (_, _) => UseListenSource(input.Id, pair);
                    item.Items.Add(pairItem);
                }
            }

            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var sound = new MenuItem { Header = "Windows' Sound settings…" };
        sound.Click += (_, _) => Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true });
        menu.Items.Add(sound);
        menu.IsOpen = true;
    }

    private void UseListenSource(string? id, int firstChannel)
    {
        _settings.ListenInput = id;
        _settings.ListenInputChannel = firstChannel;
        _settings.Save();
        _inputFailed = null;
        UpdateListening();
        UpdateSourcesPanel();
    }
}
