using Avalonia.Controls;
using Lucent.Mac.Audio;

namespace Lucent.Mac;

// The volume bar with "Visuals for every app's sound" on: the visuals hear the whole mix, so Lucent's own volume
// alone would do nothing for a browser or game, and would turn its own music down before the analyzer hears it.
// So the bar sets the Mac's volume (applied after the mix is heard), and Lucent's own music goes into the mix at full
// level, so the analyzer reads each song's true loudness whatever the volume. Lucent's own volume comes back when the
// switch is turned off. With the output to itself, Lucent's music isn't in the mix, so its own volume stays in charge.
public partial class MainWindow
{
    private int _computerVolumeTicks;

    // Whether Lucent's own music plays at full level into the mix, with the Mac's volume turning it up and down.
    private bool UsesComputerVolume => _settings.ListenToOtherApps && !_settings.ExclusiveOutput;

    // Whether the volume bar is the Mac's volume: a controlled app's own volume comes first.
    private bool BarIsComputerVolume => _remote is null && UsesComputerVolume;

    /// <summary>Gives Lucent's music its own volume, or full level while the Mac's volume is in charge.</summary>
    private void ApplyOwnVolume() => _player.Volume = UsesComputerVolume ? 1f : (float)Math.Clamp(_settings.Volume, 0, 1);

    /// <summary>Called by the position timer: follows the Mac's volume about once a second, when it's changed elsewhere (the volume keys, say).</summary>
    private void FollowComputerVolume()
    {
        if (BarIsComputerVolume && ++_computerVolumeTicks % 5 == 0)
        {
            ShowVolume();
        }
    }

    /// <summary>Shows the Mac's volume on the bar; false when the output has no volume to show.</summary>
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
        var output = SystemAudio.DefaultOutputName() ?? "this Mac";
        ToolTip.SetTip(VolumeSlider, $"The Mac's volume for {output}, which every app plays through. The analyzer reads the music before it. "
            + "(Scrolling over the player changes it too.)");
        return true;
    }
}
