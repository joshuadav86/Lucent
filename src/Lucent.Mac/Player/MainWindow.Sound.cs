using Avalonia.Controls;
using Avalonia.Interactivity;
using Lucent.Mac.Audio;
using Lucent.Playlist;
using Lucent.Settings;

namespace Lucent.Mac;

/// <summary>
/// Gapless playback and the Sound options: which output the music plays through, exclusive
/// (bit-perfect) mode, crossfading, and lining up the next track so it follows without a gap. Kept in its own file.
/// </summary>
public partial class MainWindow
{
    // How long before a track ends the next one is opened and lined up behind it (sooner for a long crossfade).
    private TimeSpan LineUpLead => TimeSpan.FromSeconds(Math.Max(10, CrossfadeSeconds + 5));

    // The crossfade lengths offered, in seconds.
    private static readonly int[] CrossfadeChoices = { 1, 2, 3, 5, 8, 10, 12 };

    // How long tracks overlap, or 0 when crossfading is off.
    private int CrossfadeSeconds => _settings.Crossfade ? Math.Clamp(_settings.CrossfadeSeconds, 1, 12) : 0;

    // The track chosen to play after this one, once it's been chosen (null if the queue ends here).
    private UpNext? _upNext;
    private bool _upNextChosen;
    // True when the chosen track couldn't be lined up to follow seamlessly: it plays the usual way.
    private bool _upNextRejected;

    /// <summary>
    /// What plays after the current track: the entry, whether a shuffle starts a fresh round with it,
    /// and whether it's the same track again (repeat one).
    /// </summary>
    private sealed record UpNext(PlaylistEntry Entry, bool StartsNewRound, bool RepeatsCurrent);

    /// <summary>
    /// Picks what follows the current track, without playing it or changing anything: null when the
    /// queue ends here and <paramref name="wrap"/> is off.
    /// </summary>
    private UpNext? ChooseNext(bool wrap)
    {
        if (_playlist.Count == 0)
        {
            return null;
        }

        if (_settings.Shuffle)
        {
            var unplayed = _playlist.Where(entry => !_shuffleHistory.Contains(entry)).ToList();
            var newRound = false;
            if (unplayed.Count == 0)
            {
                if (!wrap)
                {
                    return null;
                }

                // Everything has played: start a fresh round, avoiding an instant repeat of this track.
                newRound = true;
                unplayed = _playlist.Where(entry => entry != _current || _playlist.Count == 1).ToList();
            }

            return new UpNext(unplayed[_random.Next(unplayed.Count)], newRound, false);
        }

        var index = _current is null ? -1 : _playlist.IndexOf(_current);
        var next = index + 1;
        if (next >= _playlist.Count)
        {
            if (!wrap)
            {
                return null;
            }

            next = 0;
        }

        return new UpNext(_playlist[next], false, false);
    }

    /// <summary>
    /// Near the end of a track, opens the next one and hands it to the player to follow without a gap.
    /// Runs with the position timer.
    /// </summary>
    private void LineUpNext()
    {
        if ((!_settings.Gapless && !_settings.Crossfade) || !_player.HasTrack || _current is null || _player.Duration - _player.Position > LineUpLead)
        {
            return;
        }

        if (!_upNextChosen)
        {
            _upNext = _settings.Repeat == RepeatMode.One
                ? new UpNext(_current, false, true)
                : ChooseNext(wrap: _settings.Repeat == RepeatMode.All);
            _upNextChosen = true;
        }

        // Lined up again if the player let go of it (its file was released to save tags, say).
        if (_upNext is not null && !_upNextRejected && _player.QueuedPath is null)
        {
            _upNextRejected = !_player.QueueNext(_upNext.Entry.Track.Path);
        }
    }

    /// <summary>Drops the track lined up next, after anything that could change what should follow.</summary>
    private void ForgetUpNext()
    {
        _upNext = null;
        _upNextChosen = false;
        _upNextRejected = false;
        _player.QueueNext(null);
    }

    /// <summary>The player has run straight on into the track that was lined up.</summary>
    private void OnTrackAdvanced()
    {
        var upNext = _upNext;
        _upNext = null;
        _upNextChosen = false;
        _upNextRejected = false;

        // Normally the lined-up entry; if the queue changed in the moment between, the entry for that file.
        var entry = upNext is not null && _player.IsCurrentFile(upNext.Entry.Track.Path)
            ? upNext.Entry
            : _playlist.FirstOrDefault(candidate => _player.IsCurrentFile(candidate.Track.Path));
        if (entry is null)
        {
            return;
        }

        if (upNext is { StartsNewRound: true })
        {
            _shuffleHistory.Clear();
        }

        ShowEntryPlaying(entry);
    }

    /// <summary>Hands the saved sound settings to the player.</summary>
    private void ApplySoundSettings()
    {
        _player.Gapless = _settings.Gapless;
        _player.CrossfadeSeconds = CrossfadeSeconds;
        _player.OutputDeviceId = _settings.OutputDevice;
        _player.Exclusive = _settings.ExclusiveOutput;
        ApplyOwnVolume();
    }

    /// <summary>Marks the current sound settings in the options panel.</summary>
    private void ShowSoundValues()
    {
        ExclusiveSwitch.IsChecked = _settings.ExclusiveOutput;
        GaplessSwitch.IsChecked = _settings.Gapless;
        CrossfadeSwitch.IsChecked = _settings.Crossfade;
        CrossfadeLengthName.Text = $"{Math.Clamp(_settings.CrossfadeSeconds, 1, 12)} seconds  ▾";
        OutputDeviceName.Text = _settings.OutputDevice is null
            ? "Mac default output  ▾"
            : (OutputDevices.List().FirstOrDefault(device => device.Id == _settings.OutputDevice)?.Name ?? "Output not connected") + "  ▾";
        OutputStatus.Text = DescribeOutput();
    }

    /// <summary>What's reaching the output now, in the terms a DAC's display would use.</summary>
    private string DescribeOutput()
    {
        if (_player.Output is not { } output)
        {
            return "";
        }

        if (output.Problem is not null)
        {
            return output.Problem;
        }

        var rate = output.SampleRate % 1000 == 0
            ? $"{output.SampleRate / 1000} kHz"
            : $"{output.SampleRate / 1000.0:0.0} kHz";
        // Always floating point on a Mac, exclusive or not: it carries 16- and 24-bit samples exactly.
        var depth = "32-bit float";
        var mode = output.Exclusive ? "exclusive" : "through macOS's mixer";
        var verdict = _player.IsBitPerfect ? " · bit-perfect"
            : !output.Exclusive ? ""
            : output.Resampled ? " · resampled: your output can't take this rate"
            : output.SourceBits == 0 ? " · lossy file"
            : _player.Volume < 1f ? " · not bit-perfect below 100% volume"
            : !_player.Equalizer.Design.IsBypass ? " · not bit-perfect with the equalizer on"
            : "";
        return $"Now: {rate} · {depth} · {mode}{verdict}";
    }

    private void OnChooseOutputDevice(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = OutputDeviceButton, Placement = PlacementMode.Bottom };
        void Add(string label, string? id)
        {
            var item = new MenuItem { Header = label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.OutputDevice == id };
            item.Click += (_, _) => ChangeSound(() => _settings.OutputDevice = id);
            menu.Items.Add(item);
        }

        Add("Mac default output", null);
        var devices = OutputDevices.List();
        if (devices.Count > 0)
        {
            menu.Items.Add(new Separator());
        }

        foreach (var device in devices)
        {
            Add(device.Name, device.Id);
        }

        menu.Open(OutputDeviceButton);
    }

    private void OnSoundSwitched(object? sender, RoutedEventArgs e)
    {
        var on = (sender as CheckBox)?.IsChecked == true;
        if (sender == ExclusiveSwitch)
        {
            ChangeSound(() => _settings.ExclusiveOutput = on);
        }
        else if (sender == GaplessSwitch)
        {
            _settings.Gapless = on;
            _player.Gapless = on;
            ForgetUpNext();
            _settings.Save();
        }
        else if (sender == CrossfadeSwitch)
        {
            _settings.Crossfade = on;
            ChangeCrossfade();
        }
    }

    private void OnChooseCrossfadeLength(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = CrossfadeLengthButton, Placement = PlacementMode.Bottom };
        foreach (var seconds in CrossfadeChoices)
        {
            var item = new MenuItem { Header = $"{seconds} seconds", ToggleType = MenuItemToggleType.CheckBox, IsChecked = _settings.CrossfadeSeconds == seconds };
            item.Click += (_, _) =>
            {
                _settings.CrossfadeSeconds = seconds;
                _settings.Crossfade = true;
                ChangeCrossfade();
            };
            menu.Items.Add(item);
        }

        menu.Open(CrossfadeLengthButton);
    }

    // Hands a new crossfade to the player, lining the next track up again so it starts at the right moment.
    private void ChangeCrossfade()
    {
        _player.CrossfadeSeconds = CrossfadeSeconds;
        ForgetUpNext();
        _settings.Save();
        ShowSoundValues();
    }

    /// <summary>
    /// Changes how the music reaches the output, and moves the track that's playing over to it
    /// straight away, at the same place.
    /// </summary>
    private void ChangeSound(Action change)
    {
        change();
        ApplySoundSettings();
        _settings.Save();

        if (_player.HasTrack && _player.CurrentPath is { } path)
        {
            var position = _player.Position;
            var playing = _player.IsPlaying;
            try
            {
                _player.Load(path);
                _player.Position = position;
                if (playing)
                {
                    _player.Play();
                }
            }
            catch (Exception ex)
            {
                ShowError("Couldn't switch the music over to that output.", ex);
            }

            ForgetUpNext();
            UpdatePlayPauseButton();
        }

        ShowSoundValues();
    }
}
