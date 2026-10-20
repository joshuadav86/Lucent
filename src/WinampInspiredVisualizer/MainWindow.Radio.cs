using System.Diagnostics;
using System.Windows;
using WinampInspiredVisualizer.Radio;
using WinampInspiredVisualizer.Streaming;
using Track = WinampInspiredVisualizer.Playlist.Track;

namespace WinampInspiredVisualizer;

// Online radio: a station picked in the radio browser (RadioWindow) plays through Lucent's own player, so the
// equalizer, the visuals, the meter and the analyzer get it like any song. While a station is on, the player's
// buttons work on stations: Next and Previous move through the list it was picked from, and stopping or pausing
// lets go of the stream (a live station can't be picked up where it was left), so Play tunes in again.
public partial class MainWindow
{
    private RadioDirectory? _radioDirectory;
    private RadioStation? _station;
    private RadioStream? _radioStream;
    private CancellationTokenSource? _tuning;
    private IReadOnlyList<RadioStation> _radioList = Array.Empty<RadioStation>();
    private string? _radioProblem;
    private byte[]? _radioLogo;
    private string? _shownStationArtwork;
    private Track? _stationTrack;

    /// <summary>Raised when the station on changes, or starts or stops playing, for the radio browser to show.</summary>
    public event EventHandler? StationChanged;

    /// <summary>The Radio Browser directory, shared by the radio browser and the player.</summary>
    public RadioDirectory RadioDirectory => _radioDirectory ??= new RadioDirectory(StreamingHttp, RadioUserAgent);

    /// <summary>The station on, playing or not, or null when the player is playing Lucent's own music.</summary>
    public RadioStation? CurrentStation => _station;

    /// <summary>True while the station on is playing (or tuning in).</summary>
    public bool IsStationPlaying => _station is not null && (_tuning is not null || _player.IsPlaying);

    private static string RadioUserAgent => $"Lucent/{AppInfo.Version}";

    /// <summary>Opens the radio browser, or brings it forward.</summary>
    public void ShowRadio() => RadioWindow.Open(this, _settings);

    /// <summary>
    /// Tunes in to <paramref name="station"/> and plays it, in place of whatever was playing. Next and Previous then
    /// move through <paramref name="list"/>, the stations it was picked from.
    /// </summary>
    public async void PlayStation(RadioStation station, IReadOnlyList<RadioStation> list)
    {
        StopControllingRemote(pause: true);
        _tuning?.Cancel();
        var tuning = _tuning = new CancellationTokenSource();

        // Lucent's own song stops; it's still in the queue for afterwards.
        _player.Close();
        _radioStream = null;
        if (_current is not null)
        {
            _current.IsCurrent = false;
            _current = null;
        }

        ForgetUpNext();
        if (_station?.Id != station.Id)
        {
            _radioLogo = null;
            LoadStationLogo(station);
        }

        _station = station;
        _radioList = list.Count > 0 ? list : new[] { station };
        _radioProblem = null;
        _stationTrack = null;
        ShowStationChange();

        RadioStream stream;
        try
        {
            stream = await TuneInAsync(station, tuning.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            if (_tuning == tuning)
            {
                _tuning = null;
                _radioProblem = ex is RadioProblem ? ex.Message : $"Lucent couldn't play the station. {ex.Message}";
                ErrorLog.Write("Radio", ex);
                ShowStationChange();
            }

            return;
        }

        if (_tuning != tuning || _station != station)
        {
            stream.Dispose();
            return;
        }

        _tuning = null;
        try
        {
            _player.Load(station.StreamUrl, stream);
        }
        catch (Exception ex)
        {
            _radioProblem = "Lucent couldn't play the station through your output.";
            ShowStationChange();
            ShowError(_radioProblem, ex);
            return;
        }

        _radioStream = stream;
        stream.TitleChanged += _ => Dispatcher.BeginInvoke(() =>
        {
            if (_radioStream == stream)
            {
                _stationTrack = null;
                ShowStation();
                UpdateMediaControls();
            }
        });
        stream.Lost += problem => Dispatcher.BeginInvoke(() =>
        {
            if (_radioStream == stream)
            {
                _radioProblem = "The station stopped sending. It may have gone off the air; press Play to try again.";
                if (problem is not null)
                {
                    ErrorLog.Write("Radio stream lost", problem);
                }

                ShowStation();
            }
        });
        _player.Play();
        RadioGuide.Remember(_settings, station);
        _settings.Save();
        _ = RadioDirectory.CountPlayAsync(station);
        ShowStationChange();
        OutputStatus.Text = DescribeOutput();
    }

    /// <summary>Opens the station's stream. A featured station whose address has moved is looked up in the directory.</summary>
    private async Task<RadioStream> TuneInAsync(RadioStation station, CancellationToken cancel)
    {
        try
        {
            return await RadioStream.OpenAsync(station.StreamUrl, RadioUserAgent, cancel, station.Codec);
        }
        catch (RadioProblem) when (!station.IsFromDirectory && station.Network is not null && !cancel.IsCancellationRequested)
        {
            var found = await RadioDirectory.SearchAsync(new RadioQuery(station.Name, null, null, 0, RadioSort.Popular, 20), cancel);
            var same = found.FirstOrDefault(candidate => string.Equals(candidate.Name, station.Name, StringComparison.OrdinalIgnoreCase)
                                                         && candidate.StreamUrl != station.StreamUrl);
            if (same is null)
            {
                throw;
            }

            return await RadioStream.OpenAsync(same.StreamUrl, RadioUserAgent, cancel, same.Codec);
        }
    }

    /// <summary>Stops the station, keeping it on the player so Play tunes in again.</summary>
    private void StopStation()
    {
        _tuning?.Cancel();
        _tuning = null;
        _radioStream = null;
        _player.Close();
        ShowStationChange();
    }

    /// <summary>Moves to the next or previous station in the list the station was picked from.</summary>
    private void StepStation(int step)
    {
        if (_station is not { } station || _radioList.Count < 2)
        {
            return;
        }

        var index = _radioList.ToList().FindIndex(candidate => candidate.Id == station.Id);
        var next = _radioList[((index < 0 ? 0 : index + step) % _radioList.Count + _radioList.Count) % _radioList.Count];
        PlayStation(next, _radioList);
    }

    /// <summary>The stream ran out (the station went off the air and didn't come back): it stays on the player, stopped.</summary>
    private void OnStationEnded()
    {
        _radioProblem ??= "The station stopped sending. It may have gone off the air; press Play to try again.";
        StopStation();
    }

    /// <summary>Back to Lucent's own music: forgets the station, if one was on. The player itself is left to the caller.</summary>
    private void LeaveRadio()
    {
        if (_station is null)
        {
            return;
        }

        _tuning?.Cancel();
        _tuning = null;
        _station = null;
        _radioStream = null;
        _radioProblem = null;
        _radioLogo = null;
        _shownStationArtwork = null;
        _stationTrack = null;
        StationChanged?.Invoke(this, EventArgs.Empty);
        UpdateTrackFavoriteButton();
    }

    public void SetStationFavorite(RadioStation station, bool favorite)
    {
        RadioGuide.SetFavorite(_settings, station, favorite);
        _settings.Save();
        UpdateTrackFavoriteButton();
        StationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Opens the station's website in the browser.</summary>
    public void OpenStationWebsite(RadioStation station)
    {
        if (station.Homepage is not { } homepage || !RadioGuide.IsStreamAddress(homepage))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(homepage) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open the station's website.", ex);
        }
    }

    private void ShowStationChange()
    {
        ShowStation();
        UpdatePlayPauseButton();
        UpdatePosition();
        UpdateTrackFavoriteButton();
        StationChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the station and its song in the title, details, window title and artwork window.</summary>
    private void ShowStation()
    {
        if (_station is not { } station)
        {
            return;
        }

        var song = _radioStream?.NowPlaying;
        var marquee = _tuning is not null ? $"Tuning in to {station.Name}…" : song ?? station.Name;
        // Set only when it changes, so a long title keeps scrolling smoothly.
        if (NowPlayingText.Text != marquee)
        {
            SetMarquee(marquee);
        }

        Title = $"{song ?? station.Name} - Lucent";
        TrackDetails.Text = StationDetails(station);
        TrackDetails.ToolTip = _radioProblem ?? $"{station.Name}, live: {station.StreamUrl}";

        var artwork = $"{station.Id}\n{song}\n{_radioLogo?.Length}";
        if (artwork != _shownStationArtwork)
        {
            _shownStationArtwork = artwork;
            var (artist, title) = SplitSong(song);
            ArtworkWindow.ShowRemote(new RemoteTrack(station.Id, title ?? station.Name, artist ?? string.Empty, station.Name, TimeSpan.Zero),
                _radioLogo);
        }
    }

    /// <summary>"Groove Salad · 128 kbps · live", or what's wrong.</summary>
    private string StationDetails(RadioStation station)
    {
        if (_radioProblem is { } problem)
        {
            return problem;
        }

        if (_radioStream is { IsBuffering: true })
        {
            return $"{station.Name} · buffering…";
        }

        var kbps = _radioStream?.Bitrate is > 0 and var bitrate ? bitrate : station.Bitrate;
        var playing = _player.IsPlaying ? "live" : _tuning is not null ? "connecting" : "stopped";
        var codec = _radioStream?.Codec is { } format ? $" {format}" : string.Empty;
        return kbps > 0 ? $"{station.Name} · {kbps} kbps{codec} · {playing}" : $"{station.Name} · {playing}";
    }

    /// <summary>With the position timer: shows the stream catching up, and catching up again.</summary>
    private void UpdateStationStatus()
    {
        if (_station is { } station && _remote is null)
        {
            var details = StationDetails(station);
            if (TrackDetails.Text != details)
            {
                TrackDetails.Text = details;
            }
        }
    }

    /// <summary>The station's song as a track, for the media controls.</summary>
    private Track? StationTrack()
    {
        if (_station is not { } station)
        {
            return null;
        }

        var (artist, title) = SplitSong(_radioStream?.NowPlaying);
        return _stationTrack ??= Track.ForStation(station.StreamUrl, title ?? station.Name, artist ?? string.Empty, station.Name, station.Bitrate);
    }

    /// <summary>"Artist - Title", as stations write it, split in two; a title alone when it has no dash.</summary>
    private static (string? Artist, string? Title) SplitSong(string? song)
    {
        if (song is null)
        {
            return (null, null);
        }

        var dash = song.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? (song[..dash].Trim(), song[(dash + 3)..].Trim()) : (null, song);
    }

    private async void LoadStationLogo(RadioStation station)
    {
        if (station.Logo is not { } logo || !RadioGuide.IsStreamAddress(logo))
        {
            return;
        }

        try
        {
            var bytes = await StreamingHttp.GetByteArrayAsync(logo);
            if (_station?.Id == station.Id && bytes.Length is > 0 and < 4_000_000)
            {
                _radioLogo = bytes;
                ShowStation();
            }
        }
        catch (Exception)
        {
            // No logo, that's all.
        }
    }

    private void StopRadio()
    {
        _tuning?.Cancel();
        _tuning = null;
        _radioStream = null;
    }
}
