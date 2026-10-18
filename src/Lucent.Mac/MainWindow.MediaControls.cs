using Avalonia.Input;
using Avalonia.Threading;
using Lucent.Mac.Audio;

namespace Lucent.Mac;

// The keyboard's media keys, and Lucent in macOS's Now Playing controls (Control Center, the menu bar,
// the lock screen, and headphone buttons). See MediaControls.
public partial class MainWindow
{
    // One press of a media key could reach Lucent twice: as a key press in the window in front (when a
    // keyboard sends it as one), and from macOS's Now Playing controls. The second one inside this long
    // is the same press, so it's ignored.
    private static readonly TimeSpan MediaKeyEcho = TimeSpan.FromMilliseconds(400);

    private MediaControls? _mediaControls;
    private DispatcherTimer? _mediaPositionTimer;
    private MediaCommand? _lastMediaCommand;
    private bool _lastMediaCommandWasKey;
    private DateTime _lastMediaCommandAt;
    private TimeSpan _shownMediaPosition = TimeSpan.MinValue;

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Set up once, when the player first opens, so the commands go to a window that's there.
        if (_mediaControls is not null)
        {
            return;
        }

        var controls = new MediaControls();
        _mediaControls = controls;
        controls.CommandReceived += (_, command) => RunMediaCommand(command, fromKey: false);
        controls.SeekRequested += (_, position) =>
        {
            if (_player.HasTrack)
            {
                _player.Position = position;
                UpdatePosition();
                UpdateMediaControls();
            }
        };

        if (controls.IsAvailable)
        {
            // Keeps the position in macOS's Now Playing controls in step with seeking and pausing here.
            _mediaPositionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _mediaPositionTimer.Tick += (_, _) => ShowMediaPosition();
            _mediaPositionTimer.Start();
        }

        Closed += (_, _) =>
        {
            _mediaPositionTimer?.Stop();
            controls.Dispose();
        };

        UpdateMediaControls();
    }

    /// <summary>The player command for a media key on the keyboard, or null for any other key.</summary>
    private static MediaCommand? MediaCommandFor(Key key) => key switch
    {
        Key.MediaPlayPause => MediaCommand.PlayPause,
        Key.MediaStop => MediaCommand.Stop,
        Key.MediaNextTrack => MediaCommand.Next,
        Key.MediaPreviousTrack => MediaCommand.Previous,
        _ => null,
    };

    /// <summary>Does what a media key or one of macOS's Now Playing buttons asks, unless it's a repeat of the same press.</summary>
    private void RunMediaCommand(MediaCommand command, bool fromKey)
    {
        var now = DateTime.UtcNow;
        if (command == _lastMediaCommand && fromKey != _lastMediaCommandWasKey && now - _lastMediaCommandAt < MediaKeyEcho)
        {
            return;
        }

        _lastMediaCommand = command;
        _lastMediaCommandWasKey = fromKey;
        _lastMediaCommandAt = now;

        switch (command)
        {
            case MediaCommand.PlayPause:
                TogglePlayPause();
                break;
            case MediaCommand.Play when !ShownIsPlaying:
                TogglePlayPause();
                break;
            case MediaCommand.Pause when ShownIsPlaying:
                TogglePlayPause();
                break;
            case MediaCommand.Stop:
                StopPlayback();
                break;
            case MediaCommand.Next:
                PlayNext(wrap: true);
                break;
            case MediaCommand.Previous:
                PlayPrevious();
                break;
        }

        UpdateMediaControls();
    }

    /// <summary>Shows the playing track, whether it's playing, and where it's up to in macOS's Now Playing controls.</summary>
    private void UpdateMediaControls()
    {
        if (_mediaControls is not { IsAvailable: true } controls)
        {
            return;
        }

        if (_remote is not null)
        {
            // The app being controlled is in macOS's Now Playing itself, so the media keys go straight to it.
            controls.ShowTrack(null);
            controls.ShowState(MediaState.Closed, canSkip: false);
            return;
        }

        controls.ShowTrack(_current?.Track);
        // Stopped rather than closed while there's a queue, so the media keys still come to Lucent to start it.
        var state = _player.IsPlaying ? MediaState.Playing
            : _player.HasTrack && _player.Position > TimeSpan.Zero ? MediaState.Paused
            : _player.HasTrack || _playlist.Count > 0 ? MediaState.Stopped
            : MediaState.Closed;
        controls.ShowState(state, canSkip: _playlist.Count > 0);
        _shownMediaPosition = TimeSpan.MinValue;
        ShowMediaPosition();
    }

    private void ShowMediaPosition()
    {
        if (_mediaControls is null)
        {
            return;
        }

        // macOS only needs telling when the position moves: while playing, or after a seek.
        var position = _player.Position;
        if (position == _shownMediaPosition)
        {
            return;
        }

        _shownMediaPosition = position;
        _mediaControls.ShowPosition(position, _player.Duration);
    }
}
