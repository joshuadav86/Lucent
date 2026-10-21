namespace WinampInspiredVisualizer.Streaming;

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
    private string? _artworkFor;

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

    // Until when reads keep the position just sought to, rather than where the app was before it caught up.
    private DateTime _positionHeldUntil;

    public RemoteControl(IRemotePlayer player)
    {
        Player = player;
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
    /// say or it hasn't been read yet.
    /// </summary>
    public RemoteList? List { get; private set; }

    public bool IsPlaying => State is { IsPlaying: true };

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
                catch (Exception) when (!wasPlaying)
                {
                    // Nothing to stop, and nowhere to rewind (Spotify with no device playing, say).
                }
            },
            state => state with { IsPlaying = false, Position = TimeSpan.Zero, ReadAt = DateTime.UtcNow });
    }

    public Task NextAsync() => RunAsync(Player.NextAsync, null);

    public Task PreviousAsync() => RunAsync(Player.PreviousAsync, null);

    /// <summary>Plays one of <see cref="List"/>'s songs.</summary>
    public Task PlayFromListAsync(RemoteTrack track) => Player is IRemoteLists lists && List is { } list
        ? RunAsync(cancel => lists.PlayFromListAsync(list, track, cancel), null)
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
            Report(ex);
            return;
        }

        if (_disposed)
        {
            return;
        }

        // The device takes a moment to report a volume just set, so what was asked for shows until then.
        if (state is { Volume: not null } && State is { Volume: { } asked } && (_sendingVolume || DateTime.UtcNow < _volumeHeldUntil))
        {
            state = state with { Volume = asked };
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

        var track = state?.Track;
        if (track?.Id == _artworkFor)
        {
            return;
        }

        _artworkFor = track?.Id;
        Artwork = null;
        if (track is null)
        {
            return;
        }

        try
        {
            var artwork = await Player.ArtworkAsync(track, _stop.Token);
            if (!_disposed && _artworkFor == track.Id)
            {
                Artwork = artwork;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // No cover, then (or Lucent stopped controlling the app). The song still shows.
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

        var wanted = (state.ListId, List is { FollowsSong: true } || List is null ? state.Track.Id : null);
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
        _stop.Cancel();
        Player.Dispose();
    }
}
