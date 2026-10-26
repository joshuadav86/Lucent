using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Mac.Audio;
using Lucent.Audio;

namespace Lucent.Mac;

// Visuals from an input: instead of every app's sound, the visuals, meter and analyzer follow one of the
// audio interface's inputs. The tap of every app's sound already hears a DAW playing through Core Audio
// (Ableton, say); this is for when it doesn't, as when the DAW plays to an interface other than the Mac's
// output. The interface's own mixer sends what the DAW plays back to an input (TotalMix's Loopback), or a
// loopback device like BlackHole carries it, and Lucent listens there. Chosen under the "Visuals for every
// app's sound" switch. Windows needs it for Ableton on ASIO, which skips Windows' mixer.
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
            var channels = LiveInputs.InputChannels(input.ObjectId);
            var rate = (int)Math.Round(ExclusiveDevice.ReadRate(input.ObjectId));
            // At the rate the interface runs at now, so it's left as the DAW set it.
            feed = new InputFeed(_player.Capture, rate > 0 ? rate : 48000);
            _inputListening = LiveInput.Start(input, feed, Math.Clamp(_settings.ListenInputChannel, 0, Math.Max(0, channels - 1)));
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

    /// <summary>Shows what the visuals listen to under the switch, and how to get a DAW's sound into them.</summary>
    private void UpdateListenSource()
    {
        ListenSourceButton.IsVisible = _settings.ListenToOtherApps;
        if (!_settings.ListenToOtherApps)
        {
            return;
        }

        var output = SystemAudio.DefaultOutputName() ?? "this Mac";
        if (_settings.ListenInput is null)
        {
            ListenSourceName.Text = "Every app's sound";
            if (SystemAudio.IsSupported)
            {
                ListenHint.Text = $"The visuals, meter, and analyzer react to whatever is playing on {output}: Spotify, a browser, Ableton, a game. "
                    + "macOS asks once whether Lucent may record the Mac's sound. If a DAW plays to another interface, loop it back to an input and pick that input above.";
            }

            return;
        }

        var name = ListenInputName();
        ListenSourceName.Text = name;
        ListenHint.Text = _inputFailed is not null && _inputFailed == WantedInput()
            ? $"Lucent couldn't listen to {name}. Is it plugged in, and is Lucent allowed to use inputs (System Settings, Privacy & Security, Microphone)? Pick the input again to retry."
            : $"The visuals, meter, and analyzer follow {name}. For a DAW on an RME interface, turn on Loopback for the output it plays to in TotalMix, "
                + "and pick that output's channels here. macOS asks once whether Lucent may use inputs.";
    }

    // The chosen input's name, with its channels when it has more than two.
    private string ListenInputName()
    {
        var input = LiveInputs.ListForVisuals().FirstOrDefault(candidate => candidate.Id == _settings.ListenInput);
        if (input is null)
        {
            return "the chosen input";
        }

        return LiveInputs.InputChannels(input.ObjectId) > 2 ? $"{input.Name}: {PairName(_settings.ListenInputChannel)}" : input.Name;
    }

    private static string PairName(int first) => $"inputs {first + 1}–{first + 2}";

    private void OnChooseListenSource(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ListenSourceButton, Placement = PlacementMode.Bottom };
        var every = new MenuItem
        {
            Header = $"Every app's sound on {SystemAudio.DefaultOutputName() ?? "this Mac"}",
            ToggleType = MenuItemToggleType.CheckBox,
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
            var channels = LiveInputs.InputChannels(input.ObjectId);
            var item = new MenuItem { Header = input.Name, ToggleType = MenuItemToggleType.CheckBox, IsChecked = chosen };
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
                    var pairItem = new MenuItem
                    {
                        Header = PairName(pair),
                        ToggleType = MenuItemToggleType.CheckBox,
                        IsChecked = chosen && _settings.ListenInputChannel == pair,
                    };
                    pairItem.Click += (_, _) => UseListenSource(input.Id, pair);
                    item.Items.Add(pairItem);
                }
            }

            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var midi = new MenuItem { Header = "Audio MIDI Setup…" };
        midi.Click += (_, _) => OpenAudioMidiSetup();
        menu.Items.Add(midi);
        menu.Open(ListenSourceButton);
    }

    private static void OpenAudioMidiSetup()
    {
        try
        {
            var open = new ProcessStartInfo("open") { UseShellExecute = false };
            open.ArgumentList.Add("/System/Applications/Utilities/Audio MIDI Setup.app");
            Process.Start(open)?.Dispose();
        }
        catch (Exception)
        {
            // Nothing more to do if macOS won't open it.
        }
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
