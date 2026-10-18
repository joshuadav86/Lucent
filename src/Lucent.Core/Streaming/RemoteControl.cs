namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Keeps up with what another app (an <see cref="IRemotePlayer"/>) is playing, and presses its buttons
/// for the player window. Make it on the UI thread: it reads the app about once a second, and
/// <see cref="Changed"/> and everything it reports happen on that thread.
/// </summary>
public sealed class RemoteControl : IDisposable
{
    private static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(1);

    // The other app takes a moment to act on a button, so it's read again after this long to see what it did.
    private static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(400);

    private readonly CancellationTokenSource _stop = new();
    private readonly SemaphoreSlim _readNow = new(0);
    private string? _artworkFor;
    private bool _disposed;

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

    public bool IsPlaying => State is { IsPlaying: true };

    public Task PlayPauseAsync() => IsPlaying
        ? RunAsync(Player.PauseAsync, state => state with { IsPlaying = false, Position = state.PositionNow, ReadAt = DateTime.UtcNow })
        : RunAsync(Player.PlayAsync, state => state with { IsPlaying = true, Position = state.PositionNow, ReadAt = DateTime.UtcNow });

    public Task PauseAsync() => IsPlaying ? PlayPauseAsync() : Task.CompletedTask;

    public Task NextAsync() => RunAsync(Player.NextAsync, null);

    public Task PreviousAsync() => RunAsync(Player.PreviousAsync, null);

    public Task SeekAsync(TimeSpan position) =>
        RunAsync(cancel => Player.SeekAsync(position, cancel), state => state with { Position = position, ReadAt = DateTime.UtcNow });

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
            Report(ex);
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
                await _readNow.WaitAsync(ReadEvery, _stop.Token);
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

        State = state;
        Problem = null;
        Changed?.Invoke(this, EventArgs.Empty);

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

    private void Report(Exception ex)
    {
        if (_disposed)
        {
            return;
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
