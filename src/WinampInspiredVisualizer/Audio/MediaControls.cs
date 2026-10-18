using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Windows.Media;
using Windows.Storage.Streams;
using WinampInspiredVisualizer.Library;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer.Audio;

/// <summary>The buttons a keyboard's media keys, a headset or Windows' media controls can press.</summary>
public enum MediaCommand
{
    PlayPause,
    Play,
    Pause,
    Stop,
    Next,
    Previous,
}

/// <summary>What the player is doing, as shown in Windows' media controls.</summary>
public enum MediaState
{
    /// <summary>Nothing to play: Lucent leaves Windows' media controls.</summary>
    Closed,
    Stopped,
    Paused,
    Playing,
}

/// <summary>
/// Puts Lucent in Windows' media controls (the media panel by the volume flyout, and the lock screen),
/// with the playing track's title, artist, album, cover and position, and makes the keyboard's media keys
/// work even when Lucent isn't the window in front. Windows sends media keys to whichever app is in
/// its media controls, so one does both. On a Windows that can't do that, the media keys are taken
/// as system-wide hotkeys instead. Nothing here throws: if Windows says no, the keys just work
/// while Lucent is in front, as before.
/// Make it once the window has a handle, and call it from the UI thread.
/// </summary>
public sealed class MediaControls : IDisposable
{
    // Big enough for the lock screen, small enough to hand Windows quickly.
    private const int CoverPixels = 512;

    private const int WM_HOTKEY = 0x0312;
    private const int WM_APPCOMMAND = 0x0319;
    private const uint MOD_NOREPEAT = 0x4000;

    // Media key codes, with the hotkey id each one is registered under (its own code).
    private static readonly (int Key, MediaCommand Command)[] MediaKeys =
    {
        (0xB3, MediaCommand.PlayPause), // VK_MEDIA_PLAY_PAUSE
        (0xB2, MediaCommand.Stop), // VK_MEDIA_STOP
        (0xB0, MediaCommand.Next), // VK_MEDIA_NEXT_TRACK
        (0xB1, MediaCommand.Previous), // VK_MEDIA_PREV_TRACK
    };

    private readonly Dispatcher _dispatcher;
    private readonly HwndSource _source;
    private readonly SystemMediaTransportControls? _controls;
    private readonly List<int> _hotkeys = new();
    private Track? _track;
    private CancellationTokenSource? _loadingCover;
    private bool _loggedFailure;
    private bool _disposed;

    public MediaControls(HwndSource source)
    {
        _source = source;
        _dispatcher = source.Dispatcher;
        _source.AddHook(WndProc);

        _controls = TryGetControls(source.Handle);
        if (_controls is null)
        {
            RegisterHotkeys();
        }
    }

    /// <summary>Raised on the UI thread when a media key or one of Windows' media buttons is pressed.</summary>
    public event EventHandler<MediaCommand>? CommandReceived;

    /// <summary>Raised on the UI thread when the position is dragged in Windows' media controls.</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    /// <summary>True when Lucent is in Windows' media controls (false on a Windows too old for it).</summary>
    public bool IsAvailable => _controls is not null;

    /// <summary>Shows a track's details and cover, or clears them for null. Does nothing if it's already showing.</summary>
    public void ShowTrack(Track? track)
    {
        // A refreshed Track (after editing its info) is a new object, so this shows the new info too.
        if (_controls is null || _disposed || ReferenceEquals(track, _track))
        {
            return;
        }

        _track = track;
        _loadingCover?.Cancel();
        _loadingCover = null;
        Try(() =>
        {
            var updater = _controls.DisplayUpdater;
            if (track is null)
            {
                updater.ClearAll();
                updater.Update();
                return;
            }

            updater.Type = MediaPlaybackType.Music;
            updater.MusicProperties.Title = track.Title;
            updater.MusicProperties.Artist = track.Artist;
            updater.MusicProperties.AlbumTitle = track.Album;
            // The last track's cover would be wrong; the new one follows once it's read.
            updater.Thumbnail = null;
            updater.Update();
        });

        if (track is not null)
        {
            LoadCover(track);
        }
    }

    /// <summary>Shows whether music is playing, and whether there's anything to skip to.</summary>
    public void ShowState(MediaState state, bool canSkip)
    {
        if (_controls is null || _disposed)
        {
            return;
        }

        Try(() =>
        {
            _controls.PlaybackStatus = state switch
            {
                MediaState.Playing => MediaPlaybackStatus.Playing,
                MediaState.Paused => MediaPlaybackStatus.Paused,
                MediaState.Stopped => MediaPlaybackStatus.Stopped,
                _ => MediaPlaybackStatus.Closed,
            };
            _controls.IsPlayEnabled = state != MediaState.Closed;
            _controls.IsPauseEnabled = state == MediaState.Playing;
            _controls.IsStopEnabled = state is MediaState.Playing or MediaState.Paused;
            _controls.IsNextEnabled = canSkip;
            _controls.IsPreviousEnabled = canSkip;
        });
    }

    /// <summary>Shows how far into the track the music is. Windows moves it along by itself while playing.</summary>
    public void ShowPosition(TimeSpan position, TimeSpan duration)
    {
        if (_controls is null || _disposed)
        {
            return;
        }

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        position = position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;
        Try(() => _controls.UpdateTimelineProperties(new SystemMediaTransportControlsTimelineProperties
        {
            StartTime = TimeSpan.Zero,
            MinSeekTime = TimeSpan.Zero,
            Position = position,
            MaxSeekTime = duration,
            EndTime = duration,
        }));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadingCover?.Cancel();
        // Usually the window is gone by now, which frees its hotkeys anyway.
        Try(() =>
        {
            _source.RemoveHook(WndProc);
            foreach (var id in _hotkeys)
            {
                UnregisterHotKey(_source.Handle, id);
            }
        });

        _hotkeys.Clear();
        if (_controls is not null)
        {
            Try(() =>
            {
                _controls.ButtonPressed -= OnButtonPressed;
                _controls.PlaybackPositionChangeRequested -= OnPositionChangeRequested;
                _controls.DisplayUpdater.ClearAll();
                _controls.DisplayUpdater.Update();
                _controls.PlaybackStatus = MediaPlaybackStatus.Closed;
                _controls.IsEnabled = false;
            });
        }
    }

    /// <summary>Windows' media controls for this window, switched on; null when this Windows can't provide them.</summary>
    private SystemMediaTransportControls? TryGetControls(IntPtr hwnd)
    {
        try
        {
            var controls = SystemMediaTransportControlsInterop.GetForWindow(hwnd);
            controls.IsEnabled = true;
            controls.IsPlayEnabled = false;
            controls.IsPauseEnabled = false;
            controls.IsStopEnabled = false;
            controls.IsNextEnabled = false;
            controls.IsPreviousEnabled = false;
            controls.PlaybackStatus = MediaPlaybackStatus.Closed;
            controls.ButtonPressed += OnButtonPressed;
            controls.PlaybackPositionChangeRequested += OnPositionChangeRequested;
            return controls;
        }
        catch (Exception ex)
        {
            // Old Windows, or WinRT missing or broken: fall back to hotkeys.
            ErrorLog.Write("Windows media controls", ex);
            return null;
        }
    }

    private void RegisterHotkeys()
    {
        foreach (var (key, _) in MediaKeys)
        {
            // Fails when another app already has the key; it then still works while Lucent is in front.
            if (RegisterHotKey(_source.Handle, key, MOD_NOREPEAT, (uint)key))
            {
                _hotkeys.Add(key);
            }
        }
    }

    // Windows' media buttons and keys arrive on a background thread.
    private void OnButtonPressed(SystemMediaTransportControls sender, SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        MediaCommand? command = args.Button switch
        {
            SystemMediaTransportControlsButton.Play => MediaCommand.Play,
            SystemMediaTransportControlsButton.Pause => MediaCommand.Pause,
            SystemMediaTransportControlsButton.Stop => MediaCommand.Stop,
            SystemMediaTransportControlsButton.Next => MediaCommand.Next,
            SystemMediaTransportControlsButton.Previous => MediaCommand.Previous,
            _ => null,
        };
        if (command is { } pressed)
        {
            _dispatcher.InvokeAsync(() => Raise(pressed));
        }
    }

    private void OnPositionChangeRequested(SystemMediaTransportControls sender, PlaybackPositionChangeRequestedEventArgs args)
    {
        var position = args.RequestedPlaybackPosition;
        _dispatcher.InvokeAsync(() =>
        {
            if (!_disposed)
            {
                SeekRequested?.Invoke(this, position);
            }
        });
    }

    private void Raise(MediaCommand command)
    {
        if (!_disposed)
        {
            CommandReceived?.Invoke(this, command);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case WM_HOTKEY when _hotkeys.Contains(wParam.ToInt32()):
                foreach (var (key, command) in MediaKeys)
                {
                    if (key == wParam.ToInt32())
                    {
                        Raise(command);
                    }
                }

                handled = true;
                break;
            case WM_APPCOMMAND when AppCommand(lParam) is { } command:
                // Media buttons that come as app commands (some keyboards and remotes) rather than keys.
                Raise(command);
                handled = true;
                // Telling Windows it was dealt with, so it doesn't pass it on too.
                return new IntPtr(1);
        }

        return IntPtr.Zero;
    }

    private static MediaCommand? AppCommand(IntPtr lParam)
    {
        // GET_APPCOMMAND_LPARAM: the high word, without the bits that say which device it came from.
        var code = (int)((lParam.ToInt64() >> 16) & 0x0FFF);
        return code switch
        {
            11 => MediaCommand.Next, // APPCOMMAND_MEDIA_NEXTTRACK
            12 => MediaCommand.Previous, // APPCOMMAND_MEDIA_PREVIOUSTRACK
            13 => MediaCommand.Stop, // APPCOMMAND_MEDIA_STOP
            14 => MediaCommand.PlayPause, // APPCOMMAND_MEDIA_PLAY_PAUSE
            46 => MediaCommand.Play, // APPCOMMAND_MEDIA_PLAY
            47 => MediaCommand.Pause, // APPCOMMAND_MEDIA_PAUSE
            _ => null,
        };
    }

    private async void LoadCover(Track track)
    {
        var loading = new CancellationTokenSource();
        _loadingCover = loading;

        IRandomAccessStream? stream;
        try
        {
            stream = await Task.Run(() => CoverStream(track.Path, loading.Token), loading.Token);
        }
        catch (Exception)
        {
            // Cancelled, or a cover that couldn't be read: the controls just go without one.
            return;
        }

        if (stream is null || loading.IsCancellationRequested || _disposed || _controls is null)
        {
            stream?.Dispose();
            return;
        }

        Try(() =>
        {
            _controls.DisplayUpdater.Thumbnail = RandomAccessStreamReference.CreateFromStream(stream);
            _controls.DisplayUpdater.Update();
        });
    }

    /// <summary>The track's cover as a small JPEG in memory, for Windows to read; null when it has none.</summary>
    private static IRandomAccessStream? CoverStream(string path, CancellationToken token)
    {
        var cover = CoverArt.LoadForTrack(path, CoverPixels, token);
        if (cover is null)
        {
            return null;
        }

        // Re-encoded, so a huge or unusual picture doesn't go to Windows as it is.
        var jpeg = CoverArt.EncodeJpeg(cover.Image);
        token.ThrowIfCancellationRequested();

        // Windows' own in-memory stream, because Windows copies it each time it shows the cover.
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream))
        {
            writer.WriteBytes(jpeg);
            writer.StoreAsync().AsTask(token).GetAwaiter().GetResult();
            writer.DetachStream();
        }

        stream.Seek(0);
        return stream;
    }

    /// <summary>Runs a call to Windows' media controls. If one fails, the music carries on regardless.</summary>
    private void Try(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Once is enough: the position is shown every second, and the log mustn't fill with the same error.
            if (!_loggedFailure)
            {
                _loggedFailure = true;
                ErrorLog.Write("Windows media controls", ex);
            }
        }
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
}
