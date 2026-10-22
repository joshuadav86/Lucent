using System.Diagnostics;
using System.Windows;
using WinampInspiredVisualizer.Radio;
using WinampInspiredVisualizer.Streaming;
using WinampInspiredVisualizer.Video;
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
    private ILiveStream? _radioStream;
    private CancellationTokenSource? _tuning;
    private IReadOnlyList<RadioStation> _radioList = Array.Empty<RadioStation>();
    private string? _radioProblem;
    private byte[]? _radioLogo;
    private StationArt? _stationArt;
    private string? _songCoverFor;
    private byte[]? _songCover;
    private CancellationTokenSource? _songCoverLookup;
    private LiveShows? _liveShows;
    private LiveShow? _liveShow;
    private byte[]? _liveShowPicture;
    private DateTime _liveShowCheckedAt;
    private bool _liveShowChecking;
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
            _liveShow = null;
            _liveShowPicture = null;
            _liveShowCheckedAt = default;
            _radioLogo = null;
            LoadStationLogo(station);
        }

        _station = station;
        _radioList = list.Count > 0 ? list : new[] { station };
        _radioProblem = null;
        _stationTrack = null;
        ShowStationChange();

        ILiveStream stream;
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
            _player.Load(station.StreamUrl, stream.Sound);
        }
        catch (Exception ex)
        {
            _radioProblem = "Lucent couldn't play the station through your output.";
            ShowStationChange();
            ShowError(_radioProblem, ex);
            return;
        }

        _radioStream = stream;
        if (stream is VideoStream video)
        {
            // A video station's picture shows once it comes, in the visuals window, and it gets the camera from then on.
            video.PictureStarted += () => Dispatcher.BeginInvoke(() =>
            {
                if (_radioStream == stream && _station is { } on)
                {
                    OnVideoChanged();
                    _station = RadioGuide.MarkVideo(_settings, on);
                    _settings.Save();
                    StationChanged?.Invoke(this, EventArgs.Empty);
                }
            });
        }

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

    /// <summary>
    /// Opens the station's stream: through Lucent's own radio player for MP3 and AAC, and through the video player
    /// (libVLC) for video stations and the formats Lucent's own player doesn't read.
    /// </summary>
    private async Task<ILiveStream> TuneInAsync(RadioStation station, CancellationToken cancel)
    {
        if (RadioGuide.HasVideo(_settings, station))
        {
            return await OpenThroughVideoPlayerAsync(station.StreamUrl, cancel);
        }

        try
        {
            return await OpenRadioStreamAsync(station, cancel);
        }
        catch (RadioProblem problem) when (problem.Address is { } address && VideoEngine.MayWork && !cancel.IsCancellationRequested)
        {
            return await OpenThroughVideoPlayerAsync(address, cancel);
        }
    }

    /// <summary>Opens an MP3 or AAC stream. A featured station whose address has moved is looked up in the directory.</summary>
    private async Task<RadioStream> OpenRadioStreamAsync(RadioStation station, CancellationToken cancel)
    {
        try
        {
            return await RadioStream.OpenAsync(station.StreamUrl, RadioUserAgent, cancel, station.Codec);
        }
        catch (RadioProblem problem) when (problem.Address is null && !station.IsFromDirectory && station.Network is not null && !cancel.IsCancellationRequested)
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

    private static async Task<ILiveStream> OpenThroughVideoPlayerAsync(string address, CancellationToken cancel)
    {
        try
        {
            return await Task.Run(() => VideoStream.OpenStation(address, RadioUserAgent), cancel);
        }
        catch (NotSupportedException ex)
        {
            throw new RadioProblem("This station needs Lucent's video player (libVLC), which couldn't be loaded.", ex);
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
        _songCoverLookup?.Cancel();
        _songCoverFor = null;
        _songCover = null;
        _liveShow = null;
        _liveShowPicture = null;
        _liveShowCheckedAt = default;
        _stationTrack = null;
        StationChanged?.Invoke(this, EventArgs.Empty);
        UpdateTrackFavoriteButton();
    }

    /// <summary>Opens the page where listeners support the station (a donation page, say) in the browser. Lucent takes no payments itself.</summary>
    public void OpenStationSupport(RadioStation station)
    {
        if (FeaturedStations.SupportPage(station) is not { } page)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(page) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowError("Couldn't open the station's support page.", ex);
        }
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

        // The song the stream says is on, or else, on a live DJ station, the show that's on.
        var streamSong = _radioStream?.NowPlaying;
        var song = streamSong ?? _liveShow?.Title;
        var marquee = _tuning is not null ? $"Tuning in to {station.Name}…" : song ?? station.Name;
        // Set only when it changes, so a long title keeps scrolling smoothly.
        if (NowPlayingText.Text != marquee)
        {
            SetMarquee(marquee);
        }

        Title = $"{song ?? station.Name} - Lucent";
        TrackDetails.Text = StationDetails(station);
        TrackDetails.ToolTip = _radioProblem ?? $"{station.Name}, live: {station.StreamUrl}";

        if (streamSong != _songCoverFor)
        {
            LookUpSongCover(station, streamSong);
        }

        var artwork = $"{station.Id}\n{song}\n{_radioLogo?.Length}\n{_songCover?.Length}\n{_liveShowPicture?.Length}";
        if (artwork != _shownStationArtwork)
        {
            _shownStationArtwork = artwork;
            var (artist, title) = SplitSong(song);
            ArtworkWindow.ShowRemote(new RemoteTrack(station.Id, title ?? station.Name, artist ?? string.Empty, station.Name, TimeSpan.Zero),
                _songCover ?? _liveShowPicture ?? _radioLogo);
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

            CheckLiveShow(station);
        }
    }

    /// <summary>The station's song as a track, for the media controls.</summary>
    private Track? StationTrack()
    {
        if (_station is not { } station)
        {
            return null;
        }

        var (artist, title) = SplitSong(_radioStream?.NowPlaying ?? _liveShow?.Title);
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

    /// <summary>Song covers and station logos, for the artwork window.</summary>
    private StationArt StationArt => _stationArt ??= new StationArt(StreamingHttp);

    /// <summary>
    /// Shows the station again in the artwork window, for when it opens while a station is on (it opens showing
    /// the queue's song otherwise).
    /// </summary>
    private void ShowStationArtwork()
    {
        if (_station is not null)
        {
            _shownStationArtwork = null;
            ShowStation();
        }
    }

    private async void LoadStationLogo(RadioStation station)
    {
        try
        {
            var bytes = await StationArt.LogoAsync(station, CancellationToken.None);
            if (_station?.Id == station.Id && bytes is not null)
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

    /// <summary>Looks for the cover of the song the station says is on; the station's logo shows meanwhile, and if none is found.</summary>
    private async void LookUpSongCover(RadioStation station, string? song)
    {
        _songCoverLookup?.Cancel();
        _songCoverFor = song;
        _songCover = null;
        if (song is null)
        {
            return;
        }

        var lookup = _songCoverLookup = new CancellationTokenSource();
        var (artist, title) = SplitSong(song);
        try
        {
            var cover = await StationArt.SongCoverAsync(artist ?? string.Empty, title ?? song, lookup.Token);
            if (!lookup.IsCancellationRequested && _station?.Id == station.Id && _songCoverFor == song && cover is not null)
            {
                _songCover = cover;
                ShowStation();
            }
        }
        catch (Exception)
        {
            // No cover: the logo stays.
        }
    }

    /// <summary>
    /// With the position timer: on a live DJ station (The Lot, NTS) whose stream doesn't say who's on, finds the show
    /// that's on every couple of minutes, for the title and the artwork window.
    /// </summary>
    private async void CheckLiveShow(RadioStation station)
    {
        if (_liveShowChecking || DateTime.UtcNow - _liveShowCheckedAt < TimeSpan.FromMinutes(2) || !LiveShows.Knows(station))
        {
            return;
        }

        _liveShowChecking = true;
        _liveShowCheckedAt = DateTime.UtcNow;
        try
        {
            _liveShows ??= new LiveShows(StreamingHttp);
            var show = await _liveShows.NowAsync(station, CancellationToken.None);
            if (_station?.Id != station.Id || show == _liveShow)
            {
                return;
            }

            byte[]? picture = null;
            if (show?.Picture is { } address)
            {
                picture = await StationArt.PictureAsync(address, CancellationToken.None);
            }

            if (_station?.Id != station.Id)
            {
                return;
            }

            _liveShow = show;
            _liveShowPicture = picture;
            _stationTrack = null;
            ShowStation();
            UpdateMediaControls();
        }
        catch (Exception)
        {
            // Not known this time; it's tried again in a couple of minutes.
        }
        finally
        {
            _liveShowChecking = false;
        }
    }

    private void StopRadio()
    {
        _tuning?.Cancel();
        _tuning = null;
        _radioStream = null;
    }
}
