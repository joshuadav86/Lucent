using Lucent.Library;
using Lucent.Settings;

namespace Lucent.Streaming;

/// <summary>
/// Keeps up with what another app (an <see cref="IRemotePlayer"/>) is playing, and presses its buttons
/// for the player window. Make it on the UI thread: it reads the app every second or few, and
/// <see cref="Changed"/> and everything it reports happen on that thread.
/// </summary>
public sealed class RemoteControl : IDisposable
{
    // The other app takes a moment to act on a button, so it's read again after this long to see what it did.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(400);

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _readNow = new(0);
    private readonly SharperCover? _sharperCover;

    // The song the artwork is for, and its ID then (an app can give the same song another ID a few seconds later).
    private string? _artworkFor;
    private RemoteTrack? _artworkTrack;

    // The song whose cover is being fetched now, and until when a song still without one is asked again: an app can name
    // a new song a moment before it has its cover (Spotify tells Windows the title first).
    private string? _artworkLoading;
    private DateTime _artworkAskedUntil;
    private static readonly TimeSpan ArtworkAskTime = TimeSpan.FromSeconds(5);

    // What the list was last read for: the list's ID, and the song for a list that follows the song.
    private (string? List, string? Song)? _listFor;
    private int _listReads;
    private bool _disposed;

    // The volume asked for and not yet sent (sent one at a time, the newest winning, as a dragged slider asks
    // many times a second), and until when reads keep showing it rather than what the device said before.
    private double? _volumeWanted;
    private bool _sendingVolume;
    private DateTime _volumeHeldUntil;

    // Until when a button's problem stays shown, though the app reads fine again.
    private DateTime _problemHeldUntil;

    // Shuffle and repeat just asked for, and until when reads keep showing them, as the app (Spotify's Web API
    // especially) can take a moment to report them.
    private bool? _shuffleWanted;
    private RepeatMode? _repeatWanted;
    private DateTime _modesHeldUntil;

    // The thumb just given, the song it was given to, and until when reads keep showing it, as the app can take a
    // moment to light its thumb (or, on Windows, may not say at all).
    private (string Song, SongRating Rating)? _ratingWanted;
    private DateTime _ratingHeldUntil;

    // Until when reads keep the position just sought to, rather than where the app was before it caught up.
    private DateTime _positionHeldUntil;

    // What the queue window shows for an app that can't share its list: no songs, and a note saying why.
    private readonly RemoteList? _limitList;
    private RemoteList? _list;

    // Whether the app was running here when last asked (see IRemoteOpener), the button that failed while it wasn't,
    // pressed again once the Open button has opened it, and whether it's being opened now.
    private bool _appOpen = true;
    private Func<CancellationToken, Task>? _afterOpen;
    private bool _opening;

    // How long an app gets to start after Open, and to be ready for the button pressed before it was open.
    private static readonly TimeSpan OpenTime = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan ReadyTime = TimeSpan.FromSeconds(12);

    /// <param name="sharperCover">Finds a sharper cover when the app's is small, or null to show the app's as it is.</param>
    public RemoteControl(IRemotePlayer player, SharperCover? sharperCover = null)
    {
        Player = player;
        _sharperCover = sharperCover;
        _limitList = player is not IRemoteLists && player.ListLimit is { } why ? new RemoteList(null, "Up next", Array.Empty<RemoteTrack>(), why) : null;
        if (player is IRemoteNotifier notifier)
        {
            notifier.Changed += OnPlayerChanged;
        }

        _ = ReadLoopAsync();
    }

    /// <summary>Raised when what's playing, where it's up to, its artwork or a problem changes.</summary>
    public event EventHandler? Changed;

    public IRemotePlayer Player { get; }

    /// <summary>What was playing when last read, or null when nothing is (or it hasn't been read yet).</summary>
    public RemoteState? State { get; private set; }

    /// <summary>The playing song's cover, as an image file's bytes, or null.</summary>
    public byte[]? Artwork { get; private set; }

    /// <summary>Why the app can't be shown or controlled right now, worded for the user, or null when it's fine.</summary>
    public string? Problem { get; private set; }

    /// <summary>
    /// The songs the app is playing through (its playlist, album or what's up next), or null when the app can't
    /// say or it hasn't been read yet. For an app that can never say (<see cref="IRemotePlayer.ListLimit"/>), an
    /// empty list whose note says why.
    /// </summary>
    public RemoteList? List
    {
        get => _list ?? _limitList;
        private set => _list = value;
    }

    public bool IsPlaying => State is { IsPlaying: true };

    /// <summary>
    /// True when the app isn't open on this computer and nothing is playing anywhere, so the player offers to open it
    /// (<see cref="OpenAppAsync"/>) rather than leaving the user to.
    /// </summary>
    public bool NeedsOpening => Player is IRemoteOpener && !_appOpen && !IsPlaying && !_disposed;

    /// <summary>True while <see cref="OpenAppAsync"/> waits for the app to start.</summary>
    public bool IsOpening => _opening;

    /// <summary>
    /// Opens the app (or its web player), waits for it to start, then presses again whatever button didn't work
    /// because it wasn't open (Play, or a playlist chosen in the library).
    /// </summary>
    public async Task OpenAppAsync()
    {
        if (_disposed || _opening || Player is not IRemoteOpener opener)
        {
            return;
        }

        var retry = _afterOpen;
        _afterOpen = null;
        _opening = true;
        Problem = null;
        _problemHeldUntil = default;
        Changed?.Invoke(this, EventArgs.Empty);
        try
        {
            await opener.OpenAsync(_stop.Token);

            // A web player is only seen open once a read finds its tab, so reads come every couple of seconds meanwhile.
            var startedBy = DateTime.UtcNow + OpenTime;
            var readAt = DateTime.UtcNow;
            while (!opener.IsOpen)
            {
                if (DateTime.UtcNow >= startedBy)
                {
                    throw new RemoteProblem($"{Player.Name} didn't open. Try opening it yourself.");
                }

                if (DateTime.UtcNow >= readAt && _readNow.CurrentCount == 0)
                {
                    readAt = DateTime.UtcNow + TimeSpan.FromSeconds(2);
                    _readNow.Release();
                }

                await Task.Delay(500, _stop.Token);
            }

            _appOpen = true;
            if (retry is not null)
            {
                // Running isn't ready: the app takes a few seconds to load its last song (and Spotify to tell its Web
                // API it's here), so the button is tried until it works.
                var readyBy = DateTime.UtcNow + ReadyTime;
                await Task.Delay(1000, _stop.Token);
                while (true)
                {
                    try
                    {
                        await retry(_stop.Token);
                        break;
                    }
                    catch (RemoteProblem) when (DateTime.UtcNow < readyBy)
                    {
                        await Task.Delay(1500, _stop.Token);
                    }
                }

                await Task.Delay(SettleTime, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            Report(ex, fromButton: true);
        }
        finally
        {
            _opening = false;
        }

        if (!_disposed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
            if (_readNow.CurrentCount == 0)
            {
                _readNow.Release();
            }
        }
    }

    public Task PlayPauseAsync() => IsPlaying
        ? RunAsync(Player.PauseAsync, state => state with { IsPlaying = false, Position = state.PositionNow, ReadAt = DateTime.UtcNow })
        : RunAsync(Player.PlayAsync, state => state with { IsPlaying = true, Position = state.PositionNow, ReadAt = DateTime.UtcNow });

    public Task PauseAsync() => IsPlaying ? PlayPauseAsync() : Task.CompletedTask;

    /// <summary>
    /// Stop: pauses the app and goes back to the start of the song. The pause is sent even when the app last read
    /// as paused, as that can be a moment out of date (or the play button's guess), and Stop has to stop the music.
    /// </summary>
    public Task StopAsync()
    {
        var wasPlaying = State is not { IsPlaying: false };
        return RunAsync(
            async cancel =>
            {
                try
                {
                    await Player.PauseAsync(cancel);
                }
                catch (Exception) when (!wasPlaying)
                {
                    // Already paused: some apps refuse a pause then (Spotify says the command isn't allowed).
                }

                try
                {
                    await Player.SeekAsync(TimeSpan.Zero, cancel);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    // The music has stopped, which is what matters: some apps can't rewind (or have nothing to rewind,
                    // like Spotify with no device playing), and the next read shows where the song really is.
                }
            },
            state => state with { IsPlaying = false, Position = TimeSpan.Zero, ReadAt = DateTime.UtcNow });
    }

    public Task NextAsync() => RunAsync(Player.NextAsync, null);

    public Task PreviousAsync() => RunAsync(Player.PreviousAsync, null);

    /// <summary>Plays one of <see cref="List"/>'s songs.</summary>
    public Task PlayFromListAsync(RemoteTrack track) => Player is IRemoteLists lists && _list is { } list
        ? RunAsync(cancel => lists.PlayFromListAsync(list, track, cancel), null)
        : Task.CompletedTask;

    /// <summary>Plays one of the songs of <paramref name="list"/>, a playlist looked through in the library, carrying on through it.</summary>
    public Task PlayFromListAsync(RemoteList list, RemoteTrack track) => Player is IRemoteLists lists
        ? RunAsync(cancel => lists.PlayFromListAsync(list, track, cancel), null)
        : Task.CompletedTask;

    /// <summary>Plays one of the account's playlists (<see cref="IRemotePlaylists"/>) in the app.</summary>
    public Task PlayPlaylistAsync(RemotePlaylist playlist) => Player is IRemotePlaylists playlists
        ? RunAsync(cancel => playlists.PlayPlaylistAsync(playlist, cancel), null)
        : Task.CompletedTask;

    /// <summary>
    /// Why the visuals can't follow the music, worded for the user, while the app plays on another device (Spotify on
    /// a TV, say), or null when it plays here.
    /// </summary>
    public string? ElsewhereNote => State is { IsPlaying: true, IsElsewhere: true } state
        ? $"{Player.Name} is playing on {state.Device ?? "another device"}, so the sound doesn't pass through this computer and the visuals can't hear it."
        : null;

    /// <summary>True when the app can be moved to this computer (<see cref="PlayHereAsync"/>).</summary>
    public bool CanPlayHere => Player is IRemoteDevices && State is { IsElsewhere: true };

    /// <summary>Moves the app's playback to this computer, so the visuals hear it.</summary>
    public Task PlayHereAsync() => Player is IRemoteDevices devices
        ? RunAsync(devices.PlayHereAsync, state => state with { IsElsewhere = false, Device = null })
        : Task.CompletedTask;

    /// <summary>True when the app says its volume and Lucent can change it (<see cref="RemoteState.Volume"/>).</summary>
    public bool HasVolume => Player is IRemoteVolume && State?.Volume is not null;

    /// <summary>Changes the app's volume on the device it's playing on, from 0 to 1.</summary>
    public async void SetVolume(double volume)
    {
        if (_disposed || Player is not IRemoteVolume remote || State is not { Volume: not null } state)
        {
            return;
        }

        volume = Math.Clamp(volume, 0, 1);
        State = state with { Volume = volume };
        _volumeWanted = volume;
        _volumeHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(3);
        if (_sendingVolume)
        {
            return;
        }

        _sendingVolume = true;
        try
        {
            // A moment's wait gathers a drag's many steps into one request.
            await Task.Delay(150, _stop.Token);
            while (_volumeWanted is { } wanted && !_disposed)
            {
                _volumeWanted = null;
                await remote.SetVolumeAsync(wanted, _stop.Token);
            }
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            // Lucent stopped controlling the app.
        }
        catch (Exception ex)
        {
            _volumeWanted = null;
            Report(ex, fromButton: true);
        }
        finally
        {
            _sendingVolume = false;
            _volumeHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        }
    }

    /// <summary>True when the app says whether it's shuffling and Lucent can change it (<see cref="RemoteState.Shuffle"/>).</summary>
    public bool HasShuffle => Player is IRemoteModes && State?.Shuffle is not null;

    /// <summary>True when the app says what it repeats and Lucent can change it (<see cref="RemoteState.Repeat"/>).</summary>
    public bool HasRepeat => Player is IRemoteModes && State?.Repeat is not null;

    /// <summary>Turns the app's shuffle on or off.</summary>
    public Task ToggleShuffleAsync()
    {
        if (Player is not IRemoteModes modes || State?.Shuffle is not { } shuffle)
        {
            return CantChange("shuffle");
        }

        var on = !shuffle;
        _shuffleWanted = on;
        _modesHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        return RunAsync(
            async cancel =>
            {
                try
                {
                    await modes.SetShuffleAsync(on, cancel);
                }
                catch (Exception)
                {
                    _shuffleWanted = null;
                    throw;
                }
            },
            state => state with { Shuffle = on });
    }

    /// <summary>Moves the app's repeat on, as the player's own does: off, the whole list, this song, off again.</summary>
    public Task CycleRepeatAsync()
    {
        if (Player is not IRemoteModes modes || State?.Repeat is not { } repeat)
        {
            return CantChange("repeat");
        }

        var next = repeat switch
        {
            RepeatMode.Off when !modes.RepeatsAll(State!) => RepeatMode.One,
            RepeatMode.Off => RepeatMode.All,
            RepeatMode.All => RepeatMode.One,
            _ => RepeatMode.Off,
        };
        _repeatWanted = next;
        _modesHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        return RunAsync(
            async cancel =>
            {
                try
                {
                    await modes.SetRepeatAsync(next, cancel);
                }
                catch (Exception)
                {
                    _repeatWanted = null;
                    throw;
                }
            },
            state => state with { Repeat = next });
    }

    /// <summary>The shuffle button's tip, while it's the app's shuffle.</summary>
    public string ShuffleTip => !HasShuffle ? $"{Player.Name} doesn't let Lucent change its shuffle"
        : State!.Shuffle == true ? $"{Player.Name} is shuffling. Click to turn it off (S)"
        : $"Shuffle {Player.Name} (S)";

    /// <summary>The repeat button's tip, while it's the app's repeat.</summary>
    public string RepeatTip => !HasRepeat ? $"{Player.Name} doesn't let Lucent change its repeat" : State!.Repeat switch
    {
        RepeatMode.All => $"{Player.Name} repeats the whole playlist. Click to repeat just this song (R)",
        RepeatMode.One => $"{Player.Name} repeats this song. Click to stop repeating (R)",
        _ when Player is IRemoteModes modes && !modes.RepeatsAll(State!) => $"Repeat is off in {Player.Name}. Click to repeat this song (R)",
        _ => $"Repeat is off in {Player.Name}. Click to repeat the whole playlist (R)",
    };

    /// <summary>True when the app has thumbs up and down Lucent can press (<see cref="RemoteState.Rating"/>).</summary>
    public bool HasRating => Player is IRemoteRating && State is { Rating: not null, Track: not null };

    /// <summary>Gives the song a thumbs up, or takes it back when it has one.</summary>
    public Task ThumbUpAsync() => RateAsync(SongRating.Up);

    /// <summary>Gives the song a thumbs down (which skips it, on Pandora), or takes it back when it has one.</summary>
    public Task ThumbDownAsync() => RateAsync(SongRating.Down);

    private Task RateAsync(SongRating thumb)
    {
        if (Player is not IRemoteRating rating || State is not { Rating: { } shown, Track: { } song })
        {
            return CantChange("thumbs");
        }

        // Pressing a lit thumb takes it back, as in the app.
        var wanted = shown == thumb ? SongRating.None : thumb;
        _ratingWanted = (song.Id, wanted);
        _ratingHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        return RunAsync(
            async cancel =>
            {
                try
                {
                    await rating.RateAsync(wanted, cancel);
                }
                catch (Exception)
                {
                    _ratingWanted = null;
                    throw;
                }
            },
            state => state with { Rating = wanted });
    }

    /// <summary>The thumbs up button's tip, while the app has thumbs.</summary>
    public string ThumbUpTip => State?.Rating == SongRating.Up
        ? $"You gave this a thumbs up in {Player.Name}. Click to take it back (T)"
        : $"Thumbs up: {Player.Name} plays more like this (T)";

    /// <summary>The thumbs down button's tip, while the app has thumbs.</summary>
    public string ThumbDownTip => State?.Rating == SongRating.Down
        ? $"You gave this a thumbs down in {Player.Name}. Click to take it back (Shift+T)"
        : $"Thumbs down: {Player.Name} skips it and plays less like it (Shift+T)";

    private Task CantChange(string what)
    {
        Report(new RemoteProblem(State is null
            ? $"Wait a moment for {Player.Name} to connect, then try {what} again."
            : $"{Player.Name} doesn't let Lucent change its {what}. Change it in {Player.Name} itself."), fromButton: true);
        return Task.CompletedTask;
    }

    public Task SeekAsync(TimeSpan position)
    {
        // The app can take a moment to report the new place (Spotify especially), so the bar doesn't jump back meanwhile.
        _positionHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        return RunAsync(cancel => Player.SeekAsync(position, cancel), state => state with { Position = position, ReadAt = DateTime.UtcNow });
    }

    // Presses one of the app's buttons. The guess shows the result straight away (the play button
    // turning to pause, the bar jumping), until the app is read again a moment later.
    private async Task RunAsync(Func<CancellationToken, Task> command, Func<RemoteState, RemoteState>? guess)
    {
        if (_disposed)
        {
            return;
        }

        if (guess is not null && State is { } state)
        {
            State = guess(state);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        try
        {
            await command(_stop.Token);
            await Task.Delay(SettleTime, _stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Pressed again once the app is open, when the Open button opens it.
            if (Player is IRemoteOpener { IsOpen: false })
            {
                _appOpen = false;
                _afterOpen = command;
            }

            Report(ex, fromButton: true);
        }

        _readNow.Release();
    }

    private async Task ReadLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            await ReadOnceAsync();
            try
            {
                await _readNow.WaitAsync(Player.ReadAgainAfter(State), _stop.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // The app says its song or play state just changed, so it's read now rather than at the next read.
    private void OnPlayerChanged(object? sender, EventArgs e)
    {
        if (_disposed || _readNow.CurrentCount > 0)
        {
            return;
        }

        try
        {
            _readNow.Release();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReadOnceAsync()
    {
        RemoteState? state;
        try
        {
            state = await Player.ReadAsync(_stop.Token);
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            NoteWhetherOpen();
            Report(ex);
            return;
        }

        if (_disposed)
        {
            return;
        }

        NoteWhetherOpen();

        // The device takes a moment to report a volume just set, so what was asked for shows until then.
        if (state is { Volume: not null } && State is { Volume: { } asked } && (_sendingVolume || DateTime.UtcNow < _volumeHeldUntil))
        {
            state = state with { Volume = asked };
        }

        if (DateTime.UtcNow < _modesHeldUntil)
        {
            if (state is { Shuffle: not null } && _shuffleWanted is { } shuffle)
            {
                state = state with { Shuffle = shuffle };
            }

            if (state is { Repeat: not null } && _repeatWanted is { } repeat)
            {
                state = state with { Repeat = repeat };
            }
        }
        else
        {
            _shuffleWanted = null;
            _repeatWanted = null;
        }

        // Only for the song it was given to: a thumbs down skips to the next one, which starts with neither.
        if (state is { Rating: not null, Track: { } rated } && _ratingWanted is { } given && given.Song == rated.Id && DateTime.UtcNow < _ratingHeldUntil)
        {
            state = state with { Rating = given.Rating };
        }

        if (state is { Track: { } song } && State is { Track: { } shown } && song.Id == shown.Id && DateTime.UtcNow < _positionHeldUntil)
        {
            state = state with { Position = State.PositionNow, ReadAt = DateTime.UtcNow };
        }

        State = state;
        if (DateTime.UtcNow >= _problemHeldUntil)
        {
            Problem = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        ReadListIfChanged(state);

        ShowArtwork(state?.Track);
    }

    // The song's cover, fetched beside the reads, so a slow one never holds up the next song's title or a pause.
    private async void ShowArtwork(RemoteTrack? track)
    {
        var again = track is not null && track.Id == _artworkFor;
        if (again && (Artwork is not null || _artworkLoading == track!.Id || DateTime.UtcNow >= _artworkAskedUntil))
        {
            return;
        }

        if (!again)
        {
            if (track?.Id == _artworkFor)
            {
                return;
            }

            // The same song under another ID (Spotify's app here names it one way, its Web API another a few seconds
            // later) keeps its cover, unless that's still small and the new read says where the app keeps a larger one.
            var sameSong = track is not null && Artwork is not null && track.IsSameSong(_artworkTrack);
            var before = _artworkTrack;
            _artworkFor = track?.Id;
            _artworkTrack = track;
            _artworkAskedUntil = DateTime.UtcNow + ArtworkAskTime;
            if (sameSong && (track!.ArtworkUrl is null || track.ArtworkUrl == before!.ArtworkUrl || SharperCover.IsSharp(Artwork)))
            {
                return;
            }

            if (!sameSong)
            {
                Artwork = null;
            }
        }

        if (track is null)
        {
            return;
        }

        var found = false;
        _artworkLoading = track.Id;
        try
        {
            // The queue window's list usually has the song already, with its cover's address, which is fetched first:
            // the app here can name a new song a moment before it hands over its cover.
            byte[]? artwork = null;
            if (!again && track.ArtworkUrl is null && ListedCover(track) is { } listed)
            {
                try
                {
                    artwork = await Player.ArtworkAsync(track with { ArtworkUrl = listed }, _stop.Token);
                }
                catch (Exception) when (!_stop.IsCancellationRequested)
                {
                    // The app's own, then.
                }
            }

            if (_artworkFor != track.Id)
            {
                return;
            }

            artwork ??= await Player.ArtworkAsync(track, _stop.Token);
            if (artwork is not null && !_disposed && _artworkFor == track.Id
                && (Artwork is null || ImageFormats.WidthOf(artwork) > ImageFormats.WidthOf(Artwork)))
            {
                Artwork = artwork;
                found = true;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // No cover, then (or Lucent stopped controlling the app). The song still shows.
        }
        finally
        {
            if (_artworkLoading == track.Id)
            {
                _artworkLoading = null;
            }
        }

        // Looked for once a song, unless the app's own cover came only when asked again.
        if (!again || found)
        {
            await ShowSharperCoverAsync(track);
        }
    }

    // The cover's address for the song in the list the app plays through, or null when it isn't there.
    private string? ListedCover(RemoteTrack track)
    {
        if (List is not { Tracks: { Count: > 0 } tracks })
        {
            return null;
        }

        foreach (var listed in tracks)
        {
            if (listed.ArtworkUrl is { } url && listed.IsSameSong(track))
            {
                return url;
            }
        }

        return null;
    }

    // The app's cover shows straight away; a sharper one, when it's small and one can be found, replaces it.
    private async Task ShowSharperCoverAsync(RemoteTrack track)
    {
        if (_sharperCover is null || _disposed || _artworkFor != track.Id)
        {
            return;
        }

        var shown = Artwork;
        try
        {
            var sharper = await _sharperCover.FindAsync(track, shown, _stop.Token);
            if (sharper is not null && !_disposed && _artworkFor == track.Id && ReferenceEquals(Artwork, shown))
            {
                Artwork = sharper;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // The app's cover stays.
        }
    }

    // Asks whether the app is running here, after each read, for NeedsOpening. A button that failed while it wasn't is
    // forgotten once it's open again by other means, so the Open button never presses an old one.
    private void NoteWhetherOpen()
    {
        if (Player is not IRemoteOpener opener)
        {
            return;
        }

        try
        {
            _appOpen = opener.IsOpen;
        }
        catch (Exception)
        {
            _appOpen = true;
        }

        if (_appOpen && !_opening)
        {
            _afterOpen = null;
        }
    }

    // Reads the list again when the song moves to another playlist or album (or to the next song, for what's up
    // next). It runs beside the once-a-second reads, as a long playlist takes a few requests.
    private async void ReadListIfChanged(RemoteState? state)
    {
        if (Player is not IRemoteLists lists || state?.Track is null)
        {
            return;
        }

        // The song by its title, as its ID changes a few seconds into it (the app here names it one way, then the Web
        // API's reading of the same song another), and reading again then would swap the list under a click.
        var wanted = (state.ListId, List is { FollowsSong: true } || List is null ? state.Track.Title.Trim().ToLowerInvariant() : null);
        if (_listFor == wanted)
        {
            return;
        }

        _listFor = wanted;
        var read = ++_listReads;
        RemoteList? list;
        try
        {
            list = await lists.ListAsync(state, _stop.Token);
        }
        catch (Exception)
        {
            // Nothing to show, then; the player still shows and controls the song. It's tried again with the next song.
            list = null;
        }

        if (_disposed || read != _listReads)
        {
            return;
        }

        // A list that doesn't follow the song needs reading again only when the playlist changes.
        if (list is { FollowsSong: false })
        {
            _listFor = (state.ListId, null);
        }

        List = list;
        Changed?.Invoke(this, EventArgs.Empty);

        // A long playlist shows its start straight away, and the whole of it once that's read.
        if (list?.Rest is { } rest)
        {
            RemoteList whole;
            try
            {
                whole = await rest(_stop.Token);
            }
            catch (Exception)
            {
                // The start stays, then; the whole playlist is read again when it next plays.
                return;
            }

            if (_disposed || read != _listReads)
            {
                return;
            }

            List = whole;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Report(Exception ex, bool fromButton = false)
    {
        if (_disposed)
        {
            return;
        }

        // Why a button didn't work stays up long enough to read, rather than going with the next read a second later.
        if (fromButton)
        {
            _problemHeldUntil = DateTime.UtcNow + TimeSpan.FromSeconds(6);
        }

        Problem = ex is RemoteProblem ? ex.Message : $"Can't reach {Player.Name} right now.";
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (Player is IRemoteNotifier notifier)
        {
            notifier.Changed -= OnPlayerChanged;
        }

        _stop.Cancel();
        Player.Dispose();
    }
}
