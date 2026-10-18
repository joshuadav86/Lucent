using System.Runtime.InteropServices;
using Avalonia.Threading;
using Lucent.Mac.Interop;
using Lucent.Mac.Library;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace Lucent.Mac.Audio;

/// <summary>The buttons a keyboard's media keys, headphones or macOS's Now Playing controls can press.</summary>
public enum MediaCommand
{
    PlayPause,
    Play,
    Pause,
    Stop,
    Next,
    Previous,
}

/// <summary>What the player is doing, as shown in macOS's Now Playing controls.</summary>
public enum MediaState
{
    /// <summary>Nothing to play: Lucent leaves macOS's Now Playing controls.</summary>
    Closed,
    Stopped,
    Paused,
    Playing,
}

/// <summary>
/// Puts Lucent in macOS's Now Playing controls (Control Center, the menu bar, and the lock screen),
/// with the playing track's title, artist, album, cover and position, and makes the keyboard's media keys
/// and headphone buttons (AirPods, say) work even when Lucent isn't the app in front. macOS sends media
/// keys to whichever app is Now Playing, so one does both. Nothing here throws: if macOS says no, the
/// keys just work while Lucent is in front, as before.
/// Make it once the app is running, and call it from the UI thread.
/// </summary>
public sealed partial class MediaControls : IDisposable
{
    // Big enough for the lock screen, small enough to hand macOS quickly.
    private const int CoverPixels = 512;

    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Framework = "/System/Library/Frameworks/MediaPlayer.framework/MediaPlayer";

    // MPRemoteCommandHandlerStatus.
    private const nint HandlerSuccess = 0;
    private const nint HandlerCommandFailed = 200;

    // MPNowPlayingPlaybackState.
    private const ulong StatePlaying = 1;
    private const ulong StatePaused = 2;
    private const ulong StateStopped = 3;

    // MPNowPlayingInfoMediaTypeAudio.
    private const nint MediaTypeAudio = 1;

    // A command handler's Objective-C type: MPRemoteCommandHandlerStatus (NSInteger) for (id self, SEL, MPRemoteCommandEvent *).
    private const string HandlerTypes = "q@:@";

    // How far the shown position may drift from where macOS has moved it along to before it's sent again.
    private static readonly TimeSpan PositionSlack = TimeSpan.FromSeconds(1);

    // The one set of controls the remote command callbacks report to (macOS has one Now Playing app per process).
    private static MediaControls? _current;

    // The cover macOS's artwork block hands out, which it may ask for from any thread.
    private static readonly object CoverLock = new();
    private static IntPtr _coverImage;
    private static IntPtr _coverBlock;

    private readonly IntPtr _infoCenter;
    private readonly IntPtr _target;
    private readonly List<IntPtr> _commands = new();
    private readonly Dictionary<MediaCommand, IntPtr> _commandFor = new();
    private readonly IntPtr _seekCommand;
    private readonly Keys? _keys;
    private Track? _track;
    private MediaState _state = MediaState.Closed;
    private TimeSpan _position;
    private TimeSpan _duration;
    private DateTime _publishedAt;
    private IntPtr _artwork;
    private CancellationTokenSource? _loadingCover;
    private bool _loggedFailure;
    private bool _disposed;

    public unsafe MediaControls()
    {
        try
        {
            if (!OperatingSystem.IsMacOS() || !NativeLibrary.TryLoad(Framework, out var library))
            {
                return;
            }

            _keys = new Keys(library);
            _infoCenter = ObjC.Send(ObjC.Class("MPNowPlayingInfoCenter"), "defaultCenter");
            var commandCenter = ObjC.Send(ObjC.Class("MPRemoteCommandCenter"), "sharedCommandCenter");
            if (_infoCenter == IntPtr.Zero || commandCenter == IntPtr.Zero)
            {
                _infoCenter = IntPtr.Zero;
                return;
            }

            // An object of Lucent's own for the commands to call, with one method per button.
            var targetClass = WebKit.DefineClass("LucentMediaCommandTarget",
                ("lucentPlayPause:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnPlayPause, HandlerTypes),
                ("lucentPlay:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnPlay, HandlerTypes),
                ("lucentPause:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnPause, HandlerTypes),
                ("lucentStop:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnStop, HandlerTypes),
                ("lucentNext:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnNext, HandlerTypes),
                ("lucentPrevious:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnPrevious, HandlerTypes),
                ("lucentSeek:", (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, IntPtr, nint>)&OnSeek, HandlerTypes));
            _target = ObjC.Send(ObjC.Send(targetClass, "alloc"), "init");

            _current = this;
            AddCommand(commandCenter, "togglePlayPauseCommand", "lucentPlayPause:", MediaCommand.PlayPause);
            AddCommand(commandCenter, "playCommand", "lucentPlay:", MediaCommand.Play);
            AddCommand(commandCenter, "pauseCommand", "lucentPause:", MediaCommand.Pause);
            AddCommand(commandCenter, "stopCommand", "lucentStop:", MediaCommand.Stop);
            AddCommand(commandCenter, "nextTrackCommand", "lucentNext:", MediaCommand.Next);
            AddCommand(commandCenter, "previousTrackCommand", "lucentPrevious:", MediaCommand.Previous);
            _seekCommand = AddCommand(commandCenter, "changePlaybackPositionCommand", "lucentSeek:", null);
            Publish();
        }
        catch (Exception ex)
        {
            // MediaPlayer missing or broken: the media keys still work while Lucent is in front.
            ErrorLog.Write("Mac media controls", ex);
            _infoCenter = IntPtr.Zero;
            if (ReferenceEquals(_current, this))
            {
                _current = null;
            }
        }
    }

    /// <summary>Raised on the UI thread when a media key or one of macOS's Now Playing buttons is pressed.</summary>
    public event EventHandler<MediaCommand>? CommandReceived;

    /// <summary>Raised on the UI thread when the position is dragged in macOS's Now Playing controls.</summary>
    public event EventHandler<TimeSpan>? SeekRequested;

    /// <summary>True when Lucent is in macOS's Now Playing controls (false when not on a Mac, or MediaPlayer is missing).</summary>
    public bool IsAvailable => _infoCenter != IntPtr.Zero;

    /// <summary>Shows a track's details and cover, or clears them for null. Does nothing if it's already showing.</summary>
    public void ShowTrack(Track? track)
    {
        // A refreshed Track (after editing its info) is a new object, so this shows the new info too.
        if (!IsAvailable || _disposed || ReferenceEquals(track, _track))
        {
            return;
        }

        _track = track;
        _loadingCover?.Cancel();
        _loadingCover = null;
        // The last track's cover would be wrong; the new one follows once it's read.
        Try(() => SetCover(IntPtr.Zero, default));
        // A new track starts from the beginning; the player's own length for it follows with its position.
        _position = TimeSpan.Zero;
        _duration = track?.Duration ?? TimeSpan.Zero;

        Try(Publish);
        if (track is not null)
        {
            LoadCover(track);
        }
    }

    /// <summary>Shows whether music is playing, and whether there's anything to skip to.</summary>
    public void ShowState(MediaState state, bool canSkip)
    {
        if (!IsAvailable || _disposed)
        {
            return;
        }

        // Where macOS has moved the position along to, kept as the music stops or starts.
        _position = ShownPosition();
        _state = state;
        Try(() =>
        {
            Enable(MediaCommand.PlayPause, state != MediaState.Closed);
            Enable(MediaCommand.Play, state != MediaState.Closed);
            Enable(MediaCommand.Pause, state == MediaState.Playing);
            Enable(MediaCommand.Stop, state is MediaState.Playing or MediaState.Paused);
            Enable(MediaCommand.Next, canSkip);
            Enable(MediaCommand.Previous, canSkip);
            if (_seekCommand != IntPtr.Zero)
            {
                ObjC.SendVoid(_seekCommand, "setEnabled:", state is MediaState.Playing or MediaState.Paused);
            }

            Publish();
        });
    }

    /// <summary>Shows how far into the track the music is. macOS moves it along by itself while playing.</summary>
    public void ShowPosition(TimeSpan position, TimeSpan duration)
    {
        if (!IsAvailable || _disposed)
        {
            return;
        }

        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        position = position < TimeSpan.Zero ? TimeSpan.Zero : position > duration ? duration : position;

        // Only a seek (or a slip) needs telling: macOS moves the position along by itself while playing.
        if (duration == _duration && (position - ShownPosition()).Duration() < PositionSlack)
        {
            return;
        }

        _position = position;
        _duration = duration;
        Try(Publish);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _loadingCover?.Cancel();
        if (!IsAvailable)
        {
            return;
        }

        Try(() =>
        {
            foreach (var command in _commands)
            {
                ObjC.SendVoid(command, "removeTarget:", _target);
            }

            ObjC.SendVoid(_infoCenter, "setNowPlayingInfo:", IntPtr.Zero);
            ObjC.SendVoid(_infoCenter, "setPlaybackState:", StateStopped);
            SetCover(IntPtr.Zero, default);
            ObjC.Release(_target);
        });

        _commands.Clear();
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }
    }

    /// <summary>Shows a cover from JPEG (or any other picture) bytes. Used once a track's cover is read.</summary>
    internal unsafe void ShowCover(byte[] picture)
    {
        if (!IsAvailable || _disposed)
        {
            return;
        }

        Try(() =>
        {
            using var pool = AutoreleasePool.Begin();
            IntPtr image;
            fixed (byte* bytes = picture)
            {
                var data = IntPtr_msgSend_IntPtr_nuint(ObjC.Class("NSData"), ObjC.Selector("dataWithBytes:length:"), (IntPtr)bytes, (nuint)picture.Length);
                image = ObjC.Send(ObjC.Send(ObjC.Class("NSImage"), "alloc"), "initWithData:", data);
            }

            if (image == IntPtr.Zero)
            {
                return;
            }

            var size = ObjC.ReadValue<ObjC.CGSize>(image, "size");
            if (size.Width <= 0 || size.Height <= 0)
            {
                size = new ObjC.CGSize { Width = CoverPixels, Height = CoverPixels };
            }

            SetCover(image, size);
            ObjC.Release(image);
            Publish();
        });
    }

    /// <summary>The MPRemoteCommand a command is on, with Lucent's method added to it, switched off until there's music.</summary>
    private IntPtr AddCommand(IntPtr commandCenter, string property, string action, MediaCommand? command)
    {
        var remote = ObjC.Send(commandCenter, property);
        if (remote == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        void_msgSend_IntPtr_IntPtr(remote, ObjC.Selector("addTarget:action:"), _target, ObjC.Selector(action));
        ObjC.SendVoid(remote, "setEnabled:", false);
        _commands.Add(remote);
        if (command is { } known)
        {
            _commandFor[known] = remote;
        }

        return remote;
    }

    private void Enable(MediaCommand command, bool enabled)
    {
        if (_commandFor.TryGetValue(command, out var remote))
        {
            ObjC.SendVoid(remote, "setEnabled:", enabled);
        }
    }

    /// <summary>Where macOS will be showing the position now, having moved it along since it was last told.</summary>
    private TimeSpan ShownPosition()
    {
        var position = _position;
        if (_state == MediaState.Playing)
        {
            position += DateTime.UtcNow - _publishedAt;
        }

        return _duration > TimeSpan.Zero && position > _duration ? _duration : position;
    }

    /// <summary>
    /// Hands macOS the whole of what's playing at once, as it wants it: the track's details, cover,
    /// length, position and whether it's moving, and whether Lucent is playing, paused or stopped.
    /// </summary>
    private void Publish()
    {
        if (_keys is not { } keys)
        {
            return;
        }

        using var pool = AutoreleasePool.Begin();
        _publishedAt = DateTime.UtcNow;
        ObjC.SendVoid(_infoCenter, "setPlaybackState:", _state switch
        {
            MediaState.Playing => StatePlaying,
            MediaState.Paused => StatePaused,
            _ => StateStopped,
        });

        if (_track is not { } track || _state == MediaState.Closed)
        {
            ObjC.SendVoid(_infoCenter, "setNowPlayingInfo:", IntPtr.Zero);
            return;
        }

        var info = ObjC.Send(ObjC.Class("NSMutableDictionary"), "dictionary");
        void Set(IntPtr key, IntPtr value)
        {
            if (key != IntPtr.Zero && value != IntPtr.Zero)
            {
                void_msgSend_IntPtr_IntPtr(info, ObjC.Selector("setObject:forKey:"), value, key);
            }
        }

        Set(keys.Title, WebKit.String(track.Title));
        if (!string.IsNullOrEmpty(track.Artist))
        {
            Set(keys.Artist, WebKit.String(track.Artist));
        }

        if (!string.IsNullOrEmpty(track.Album))
        {
            Set(keys.Album, WebKit.String(track.Album));
        }

        if (_duration > TimeSpan.Zero)
        {
            Set(keys.Duration, Number(_duration.TotalSeconds));
        }

        Set(keys.Elapsed, Number(_position.TotalSeconds));
        Set(keys.Rate, Number(_state == MediaState.Playing ? 1.0 : 0.0));
        Set(keys.DefaultRate, Number(1.0));
        Set(keys.MediaType, ObjC.Send(ObjC.Class("NSNumber"), "numberWithInteger:", MediaTypeAudio));
        Set(keys.Artwork, _artwork);
        ObjC.SendVoid(_infoCenter, "setNowPlayingInfo:", info);
    }

    /// <summary>Swaps the cover macOS shows for this NSImage (or none for zero), as an MPMediaItemArtwork.</summary>
    private unsafe void SetCover(IntPtr image, ObjC.CGSize size)
    {
        lock (CoverLock)
        {
            ObjC.Release(_coverImage);
            _coverImage = image;
            if (image != IntPtr.Zero)
            {
                ObjC.Send(image, "retain");
            }
        }

        ObjC.Release(_artwork);
        _artwork = IntPtr.Zero;
        if (image == IntPtr.Zero)
        {
            return;
        }

        if (_coverBlock == IntPtr.Zero)
        {
            // NSImage *(^)(CGSize size): the block macOS calls for the cover at the size it's drawing it.
            _coverBlock = WebKit.GlobalBlock((IntPtr)(delegate* unmanaged<IntPtr, ObjC.CGSize, IntPtr>)&OnCoverRequested, "@24@?0{CGSize=dd}8");
        }

        _artwork = IntPtr_msgSend_CGSize_IntPtr(ObjC.Send(ObjC.Class("MPMediaItemArtwork"), "alloc"),
            ObjC.Selector("initWithBoundsSize:requestHandler:"), size, _coverBlock);
    }

    private async void LoadCover(Track track)
    {
        var loading = new CancellationTokenSource();
        _loadingCover = loading;

        byte[]? picture;
        try
        {
            picture = await Task.Run(() => CoverPicture(track.Path, loading.Token), loading.Token);
        }
        catch (Exception)
        {
            // Cancelled, or a cover that couldn't be read: the controls just go without one.
            return;
        }

        if (picture is null || loading.IsCancellationRequested || _disposed)
        {
            return;
        }

        ShowCover(picture);
    }

    /// <summary>The track's cover as a small JPEG, for macOS to read; null when it has none.</summary>
    private static byte[]? CoverPicture(string path, CancellationToken token)
    {
        var cover = CoverArt.LoadForTrack(path, CoverPixels, token);
        if (cover is null)
        {
            return null;
        }

        // Re-encoded, so a huge or unusual picture doesn't go to macOS as it is.
        var jpeg = CoverArt.EncodeJpeg(cover.Image);
        token.ThrowIfCancellationRequested();
        return jpeg;
    }

    private static IntPtr Number(double value) =>
        IntPtr_msgSend_Double(ObjC.Class("NSNumber"), ObjC.Selector("numberWithDouble:"), value);

    /// <summary>Runs a call to macOS's Now Playing controls. If one fails, the music carries on regardless.</summary>
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
                ErrorLog.Write("Mac media controls", ex);
            }
        }
    }

    // macOS's Now Playing buttons, media keys and headphone buttons arrive on the main thread, inside
    // MediaPlayer's own call. They're passed on to the UI thread's queue rather than run there and then.
    private static nint Receive(MediaCommand command)
    {
        if (_current is not { } controls)
        {
            return HandlerCommandFailed;
        }

        try
        {
            Dispatcher.UIThread.Post(() => controls.Raise(command));
        }
        catch (Exception)
        {
            // An exception mustn't cross back into MediaPlayer.
            return HandlerCommandFailed;
        }

        return HandlerSuccess;
    }

    private void Raise(MediaCommand command)
    {
        if (!_disposed)
        {
            CommandReceived?.Invoke(this, command);
        }
    }

    [UnmanagedCallersOnly]
    private static nint OnPlayPause(IntPtr self, IntPtr selector, IntPtr commandEvent) => Receive(MediaCommand.PlayPause);

    [UnmanagedCallersOnly]
    private static nint OnPlay(IntPtr self, IntPtr selector, IntPtr commandEvent) => Receive(MediaCommand.Play);

    [UnmanagedCallersOnly]
    private static nint OnPause(IntPtr self, IntPtr selector, IntPtr commandEvent) => Receive(MediaCommand.Pause);

    [UnmanagedCallersOnly]
    private static nint OnStop(IntPtr self, IntPtr selector, IntPtr commandEvent) => Receive(MediaCommand.Stop);

    [UnmanagedCallersOnly]
    private static nint OnNext(IntPtr self, IntPtr selector, IntPtr commandEvent) => Receive(MediaCommand.Next);

    [UnmanagedCallersOnly]
    private static nint OnPrevious(IntPtr self, IntPtr selector, IntPtr commandEvent) => Receive(MediaCommand.Previous);

    /// <summary>changePlaybackPositionCommand: the event is an MPChangePlaybackPositionCommandEvent, with the new place in seconds.</summary>
    [UnmanagedCallersOnly]
    private static nint OnSeek(IntPtr self, IntPtr selector, IntPtr commandEvent)
    {
        if (_current is not { } controls)
        {
            return HandlerCommandFailed;
        }

        try
        {
            if (commandEvent == IntPtr.Zero || !ObjC.SendBool(commandEvent, "respondsToSelector:", ObjC.Selector("positionTime")))
            {
                return HandlerCommandFailed;
            }

            var seconds = ObjC.SendDouble(commandEvent, "positionTime");
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
            {
                return HandlerCommandFailed;
            }

            var position = TimeSpan.FromSeconds(Math.Max(0, seconds));
            Dispatcher.UIThread.Post(() =>
            {
                if (!controls._disposed)
                {
                    controls.SeekRequested?.Invoke(controls, position);
                }
            });
        }
        catch (Exception)
        {
            // An exception mustn't cross back into MediaPlayer.
            return HandlerCommandFailed;
        }

        return HandlerSuccess;
    }

    /// <summary>The artwork block: the cover, whatever size macOS asks for (it scales it itself). May run on any thread.</summary>
    [UnmanagedCallersOnly]
    private static IntPtr OnCoverRequested(IntPtr block, ObjC.CGSize size)
    {
        try
        {
            IntPtr image;
            lock (CoverLock)
            {
                image = _coverImage;
                if (image != IntPtr.Zero)
                {
                    ObjC.Send(image, "retain");
                }
            }

            // Handed back autoreleased, as a block returning an object does.
            return image == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(image, "autorelease");
        }
        catch (Exception)
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>MediaPlayer's NSString keys for the now-playing details, read from the framework.</summary>
    private sealed class Keys
    {
        public Keys(IntPtr library)
        {
            Title = Read(library, "MPMediaItemPropertyTitle");
            Artist = Read(library, "MPMediaItemPropertyArtist");
            Album = Read(library, "MPMediaItemPropertyAlbumTitle");
            Duration = Read(library, "MPMediaItemPropertyPlaybackDuration");
            Artwork = Read(library, "MPMediaItemPropertyArtwork");
            Elapsed = Read(library, "MPNowPlayingInfoPropertyElapsedPlaybackTime");
            Rate = Read(library, "MPNowPlayingInfoPropertyPlaybackRate");
            DefaultRate = Read(library, "MPNowPlayingInfoPropertyDefaultPlaybackRate");
            MediaType = Read(library, "MPNowPlayingInfoPropertyMediaType");
        }

        public IntPtr Title { get; }

        public IntPtr Artist { get; }

        public IntPtr Album { get; }

        public IntPtr Duration { get; }

        public IntPtr Artwork { get; }

        public IntPtr Elapsed { get; }

        public IntPtr Rate { get; }

        public IntPtr DefaultRate { get; }

        public IntPtr MediaType { get; }

        // Each constant is an NSString * the framework exports; zero for one this macOS doesn't have.
        private static IntPtr Read(IntPtr library, string name) =>
            NativeLibrary.TryGetExport(library, name, out var address) ? Marshal.ReadIntPtr(address) : IntPtr.Zero;
    }

    /// <summary>
    /// For the self-test: the title macOS has now, read back from MPNowPlayingInfoCenter, whether every
    /// command has Lucent's method on it, and whether the cover (if any) comes back from the artwork block.
    /// </summary>
    internal (string? Title, bool CommandsReady, bool? CoverReturned) ReadBack()
    {
        if (!IsAvailable || _keys is not { } keys)
        {
            return (null, false, null);
        }

        using var pool = AutoreleasePool.Begin();
        var info = ObjC.Send(_infoCenter, "nowPlayingInfo");
        var title = info == IntPtr.Zero ? null : WebKit.ReadString(ObjC.Send(info, "objectForKey:", keys.Title));
        var commandsReady = _commands.Count == 7 && _commands.TrueForAll(command => command != IntPtr.Zero);
        foreach (var action in new[] { "lucentPlayPause:", "lucentPlay:", "lucentPause:", "lucentStop:", "lucentNext:", "lucentPrevious:", "lucentSeek:" })
        {
            commandsReady &= ObjC.SendBool(_target, "respondsToSelector:", ObjC.Selector(action));
        }

        bool? coverReturned = null;
        var artwork = info == IntPtr.Zero || keys.Artwork == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(info, "objectForKey:", keys.Artwork);
        if (artwork != IntPtr.Zero)
        {
            var image = IntPtr_msgSend_CGSize(artwork, ObjC.Selector("imageWithSize:"), new ObjC.CGSize { Width = 64, Height = 64 });
            coverReturned = image != IntPtr.Zero;
        }

        return (title, commandsReady, coverReturned);
    }

    // objc_msgSend in the shapes MediaPlayer needs. Doubles and the CGSize (two doubles) go in floating-point
    // registers on both arm64 and x64, which a typed objc_msgSend gets right on each.
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_Double(IntPtr receiver, IntPtr selector, double argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_IntPtr_nuint(IntPtr receiver, IntPtr selector, IntPtr bytes, nuint length);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_CGSize_IntPtr(IntPtr receiver, IntPtr selector, ObjC.CGSize size, IntPtr block);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial IntPtr IntPtr_msgSend_CGSize(IntPtr receiver, IntPtr selector, ObjC.CGSize size);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    private static partial void void_msgSend_IntPtr_IntPtr(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);
}
