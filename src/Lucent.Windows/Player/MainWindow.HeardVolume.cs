namespace Lucent;

// The volume bar only ever turns Lucent's own music, or the controlled app's own volume (Spotify's, say), up and down.
// It never touches the computer's volume, so every other app on this PC keeps its own level.
// Both of those volumes are applied before Lucent hears the sound while it listens to other apps, so the capture is
// told how far the sound was turned down and turns it back up for the visuals and analyzer (AudioCapture.SetExternalVolume):
// they see each song at its true loudness whatever the bar says.
public partial class MainWindow
{
    /// <summary>Gives Lucent's music its own volume.</summary>
    private void ApplyOwnVolume()
    {
        _player.Volume = (float)Math.Clamp(_settings.Volume, 0, 1);
        FollowHeardVolume();
    }

    /// <summary>
    /// How far the sound Lucent listens to was already turned down: the controlled app's own volume while it plays here,
    /// or Lucent's own volume while its music plays into every app's sound. Full otherwise.
    /// </summary>
    private double HeardVolume
    {
        get
        {
            if (_systemAudio is null)
            {
                return 1;
            }

            if (_remote is { } remote)
            {
                return remote is { HasVolume: true, State: { IsElsewhere: false, Volume: { } level } } ? level : 1;
            }

            return _settings.ListenToOtherApps && !_settings.ExclusiveOutput && _player.IsPlaying
                ? Math.Clamp(_settings.Volume, 0, 1)
                : 1;
        }
    }

    /// <summary>Tells the capture how far what it hears was turned down. Called by the position timer and on each volume change.</summary>
    private void FollowHeardVolume() => _player.Capture.SetExternalVolume(HeardVolume);
}
