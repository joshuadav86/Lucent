using System.IO;
using System.Windows;
using System.Windows.Controls;
using Lucent.Audio;
using Lucent.Audio.Midi;
using Lucent.Playlist;
using Lucent.Windows;
using Microsoft.Win32;

namespace Lucent;

/// <summary>
/// Three smaller playback features from Options > Sound, kept in their own file: volume leveling, long files picking up
/// where they were left, and MIDI files' instruments (the SoundFont), which are downloaded the first time one plays.
/// Global shortcuts, from the same tab, are in MainWindow.Hotkeys.cs.
/// </summary>
public partial class MainWindow
{
    private readonly VolumeLeveling _leveling = new();

    // How often a long file's place is written to the settings while it plays (it's also kept on closing).
    private static readonly TimeSpan ResumeSaveEvery = TimeSpan.FromSeconds(30);
    private DateTime _resumeSavedAt = DateTime.MinValue;
    private bool _resumeChanged;
    private bool _gettingSoundFont;

    /// <summary>Hands the player its volume leveling, and gives a song its level once it's been measured.</summary>
    private void StartPlaybackExtras()
    {
        _player.Leveling = _leveling;
        // Global shortcuts belong to a window, so they wait until the player has one (see MainWindow.Hotkeys.cs).
        SourceInitialized += (_, _) => StartGlobalHotkeys();
        // Any song measured can change what plays now: it may be the one playing or lined up, or on the same album.
        _leveling.Measured += _ => Dispatcher.BeginInvoke(() => _player.RefreshLeveling());
        Closed += (_, _) =>
        {
            NoteResumePoint(force: true);
            _leveling.Save();
        };
    }

    /// <summary>The playback extras' settings, handed over with the rest of the sound settings.</summary>
    private void ApplyPlaybackExtras()
    {
        _leveling.Mode = _settings.Leveling;
        SoundFonts.Chosen = _settings.MidiSoundFont;
    }

    // ---- Volume leveling ----

    private void ChangeLeveling(LevelingMode mode)
    {
        _settings.Leveling = mode;
        _settings.Save();
        _leveling.Mode = mode;
        _player.RefreshLeveling();
    }

    // ---- Long files ----

    /// <summary>Moves a long file that's just been opened to where it was left, before it starts playing.</summary>
    private void ResumeWhereLeft(string path)
    {
        if (_settings.ResumeLongFiles && ResumePoints.Where(_settings.ResumePoints, path, _player.Duration) is { } place)
        {
            _player.Position = place;
            Toast.Show(this, $"Picking up where you left off, at {TimeFormat.Format(place)}");
        }
    }

    /// <summary>Keeps a long file's place as it plays (with the position timer), and writes it out now and then.</summary>
    private void NoteResumePoint(bool force = false)
    {
        if (_settings.ResumeLongFiles && _station is null && _player.HasTrack && _player.CurrentPath is { } path
            && ResumePoints.Note(_settings.ResumePoints, path, _player.Position, _player.Duration))
        {
            _resumeChanged = true;
        }

        if (_resumeChanged && (force || DateTime.UtcNow - _resumeSavedAt >= ResumeSaveEvery))
        {
            _resumeChanged = false;
            _resumeSavedAt = DateTime.UtcNow;
            _settings.Save();
        }
    }

    // ---- MIDI instruments ----

    /// <summary>A MIDI file was played before Lucent had any instruments: they're downloaded (once), then it plays.</summary>
    private async void GetSoundFontThenPlay(PlaylistEntry entry)
    {
        if (_gettingSoundFont)
        {
            return;
        }

        _gettingSoundFont = true;
        var busy = Toast.ShowBusy(this, $"Getting the instruments for MIDI files (about {SoundFonts.DownloadMegabytes} MB)…");
        var progress = new Progress<double>(fraction =>
            busy.Update($"Getting the instruments for MIDI files… {fraction * 100:0}%", fraction));
        try
        {
            await SoundFonts.DownloadAsync(((IProgress<double>)progress).Report, CancellationToken.None);
            busy.Close();
            ShowSoundFont();
            if (_playlist.Contains(entry))
            {
                PlayEntry(entry);
            }
        }
        catch (Exception ex)
        {
            busy.Close();
            ShowError("Couldn't download the instruments MIDI files play with. Check you're online and try again.", ex);
        }
        finally
        {
            _gettingSoundFont = false;
        }
    }

    /// <summary>Shows which SoundFont MIDI files play with in Options > Sound.</summary>
    private void ShowSoundFont()
    {
        var own = _settings.MidiSoundFont is not { Length: > 0 } chosen || !File.Exists(chosen);
        SoundFontName.Text = (own ? $"{SoundFonts.OwnName} (Lucent's own)" : Path.GetFileNameWithoutExtension(_settings.MidiSoundFont)) + "  ▾";
        SoundFontHint.Text = !own
            ? _settings.MidiSoundFont!
            : SoundFonts.IsOwnDownloaded
                ? "MIDI files (.mid) play through the equalizer and visuals like any song. Pick your own SoundFont (.sf2) for a different sound."
                : $"Downloaded the first time a MIDI file plays (about {SoundFonts.DownloadMegabytes} MB). Or pick your own SoundFont (.sf2).";
    }

    private void OnChooseSoundFont(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = SoundFontButton };
        var ownItem = new MenuItem { Header = $"{SoundFonts.OwnName} (Lucent's own)", IsChecked = _settings.MidiSoundFont is null };
        ownItem.Click += (_, _) => ChangeSoundFont(null);
        menu.Items.Add(ownItem);
        if (_settings.MidiSoundFont is { Length: > 0 } chosen)
        {
            menu.Items.Add(new MenuItem { Header = Path.GetFileName(chosen), IsChecked = true });
        }

        menu.Items.Add(new Separator());
        var pick = new MenuItem { Header = "Choose a SoundFont…" };
        pick.Click += (_, _) =>
        {
            var dialog = new OpenFileDialog { Title = "Choose a SoundFont for MIDI files", Filter = "SoundFonts (*.sf2)|*.sf2|All files|*.*" };
            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            if (SoundFonts.Check(dialog.FileName) is { } problem)
            {
                MessageBox.Show(this, $"That SoundFont can't be played with.\n\nDetails: {problem}", "Lucent", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ChangeSoundFont(dialog.FileName);
        };
        menu.Items.Add(pick);
        menu.IsOpen = true;
    }

    // Uses another SoundFont, and plays a MIDI file that's playing again with it, from the same place.
    private void ChangeSoundFont(string? path)
    {
        _settings.MidiSoundFont = path;
        SoundFonts.Chosen = path;
        if (_player.CurrentPath is { } playing && MidiSong.IsMidi(playing))
        {
            ChangeSound(() => { });
        }
        else
        {
            _settings.Save();
            ShowSoundFont();
        }
    }
}
