using Lucent.Audio;
using Lucent.Streaming;

namespace Lucent;

// The volume bar as Windows' volume. Windows applies its volume after the sound Lucent listens to, so the analyzer
// reads each song's true loudness whatever the volume. That's the bar in two cases:
// - With "Visuals for every app's sound" on: the visuals hear the whole mix, so Lucent's own volume alone would do
//   nothing for a browser or game, and would turn its own music down before the analyzer hears it. Lucent's own music
//   goes into the mix at full level. Lucent's own volume comes back when the switch is turned off. In exclusive mode
//   Lucent's music skips the mix, so its own volume stays in charge there.
// - While a controlled app (Spotify, say) plays on this PC and Lucent hears it: the app's own volume is applied before
//   Lucent hears it, so turning that down turned the analyzer down too. The app's volume is handed to Windows' once
//   (turned up to full, with Windows' turned down to match), and the bar is Windows' volume from then on. While the
//   app plays on another device (a phone, a TV), the bar is that device's volume, as before.
public partial class MainWindow
{
    private int _computerVolumeTicks;

    // Whether Lucent's own music plays at full level into the mix, with Windows' volume turning it up and down.
    private bool UsesComputerVolume => _settings.ListenToOtherApps && !_settings.ExclusiveOutput;

    // Whether the volume bar should be Windows' volume.
    private bool BarIsComputerVolume => _remote is null ? UsesComputerVolume : AppHeardHere;

    // Whether the controlled app plays on this PC while the visuals listen to it (or to every app).
    private bool AppHeardHere => _remote is { State: { IsElsewhere: false } } && _systemAudio is not null;

    // Whether the bar showed Windows' volume when last shown, so a move of it sets Windows' volume.
    private bool _barShowsComputerVolume;

    // The controlled app whose own volume was handed to Windows', so it's done once each time it starts playing here.
    private RemoteControl? _appVolumeHanded;

    /// <summary>Gives Lucent's music its own volume, or full level while Windows' volume is in charge.</summary>
    private void ApplyOwnVolume() => _player.Volume = UsesComputerVolume ? 1f : (float)Math.Clamp(_settings.Volume, 0, 1);

    /// <summary>Called by the position timer: follows Windows' volume about once a second, when it's changed elsewhere.</summary>
    private void FollowComputerVolume()
    {
        if (BarIsComputerVolume && ++_computerVolumeTicks % 5 == 0)
        {
            ShowVolume();
        }
    }

    /// <summary>Shows Windows' volume on the bar; false when there's no output to show it for.</summary>
    private bool ShowComputerVolume()
    {
        if (ComputerVolume.Read() is not { } volume)
        {
            return false;
        }

        // Only when it's moved, so a drag in progress isn't nudged by rounding.
        if (Math.Abs(VolumeSlider.Value - volume.Level) > 0.004)
        {
            VolumeSlider.Value = volume.Level;
        }

        VolumeText.Text = volume.Muted ? "Muted" : $"{volume.Level * 100:0}%";
        var output = SystemAudio.DefaultOutputName() ?? "this PC";
        VolumeSlider.ToolTip = _remote is { } remote
            ? $"Windows' volume for {output}. {remote.Player.Name} plays at its full volume, and the analyzer reads it before Windows' volume. "
                + "(Mouse wheel over the player changes it too.)"
            : $"Windows' volume for {output}, which every app plays through. The analyzer reads the music before it. "
                + "(Mouse wheel over the player changes it too.)";
        return true;
    }

    /// <summary>
    /// Turns the controlled app's own volume up to full, so the analyzer hears the song at its true loudness, and
    /// Windows' volume down by as much, so it sounds no louder. Once each time the app starts playing here.
    /// </summary>
    private void HandAppVolumeToComputer(RemoteControl remote)
    {
        if (ReferenceEquals(_appVolumeHanded, remote) || !remote.HasVolume || remote.State?.Volume is not { } level)
        {
            return;
        }

        _appVolumeHanded = remote;
        if (level >= 0.995)
        {
            return;
        }

        // The app's volume is a straight multiplier, like the bar (Spotify's slider is converted, SpotifyVolume). Never
        // more than 30 dB, so an app that was turned right down doesn't leave every other app near silent.
        var down = Math.Min(-20 * Math.Log10(Math.Max(level, 1e-6)), 30);
        if (ComputerVolume.TurnDown(down))
        {
            remote.SetVolume(1);
        }
    }
}
