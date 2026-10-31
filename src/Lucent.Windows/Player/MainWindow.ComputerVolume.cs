using Lucent.Audio;

namespace Lucent;

// The volume bar with "Visuals for every app's sound" on: the visuals hear the whole mix, so Lucent's own volume
// alone would do nothing for a browser or game, and would turn its own music down before the analyzer hears it.
// So the bar sets Windows' volume (applied after the mix is heard), and Lucent's own music goes into the mix at full
// level, so the analyzer reads each song's true loudness whatever the volume. Lucent's own volume comes back when the
// switch is turned off. In exclusive mode Lucent's music skips the mix, so its own volume stays in charge there.
public partial class MainWindow
{
    private int _computerVolumeTicks;

    // Whether Lucent's own music plays at full level into the mix, with Windows' volume turning it up and down.
    private bool UsesComputerVolume => _settings.ListenToOtherApps && !_settings.ExclusiveOutput;

    // Whether the volume bar is Windows' volume: a controlled app's own volume comes first.
    private bool BarIsComputerVolume => _remote is null && UsesComputerVolume;

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
        VolumeSlider.ToolTip = $"Windows' volume for {output}, which every app plays through. The analyzer reads the music before it. "
            + "(Mouse wheel over the player changes it too.)";
        return true;
    }
}
