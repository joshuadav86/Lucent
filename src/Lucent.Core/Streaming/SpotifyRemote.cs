using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Lucent.Settings;

namespace Lucent.Streaming;

/// <summary>
/// Spotify, through its Web API: what's playing on whichever device the account is playing on (the
/// Spotify app on this computer, a phone, a speaker), and its play, pause, skip and seek. Spotify only
/// takes those buttons from Premium accounts; on a free account Lucent can still show what's playing.
/// It also reads the playlist, album or Liked Songs the song is playing from, for the queue window, its shuffle and
/// repeat, which the player's own buttons change, and lists the account's playlists for the library.
/// </summary>
public sealed class SpotifyRemote : IRemotePlayer, IRemoteLists, IRemotePlaylists, IRemoteLinks, IRemoteVolume, IRemoteDevices, IRemoteModes, IRemoteOpener, IRemoteNotifier
{
    private const string Root = "https://api.spotify.com/v1";
    private const string Api = Root + "/me/player";

    // A playlist's first thousand songs are read straight away, in 20 requests, and show; the rest (up to Spotify's
    // limit of 10,000 a playlist) are read after, a page a second, so a huge playlist or Liked Songs doesn't spend
    // Spotify's patience with all of Lucent's users in one go. Read whole, it's kept (see RemoteListStore), so it's
    // read again only once it changes.
    private const int MostSongs = 1000;
    private const int MostSongsInAll = 10_000;
    private static readonly TimeSpan RestPagePause = TimeSpan.FromSeconds(1);

    // More playlists than anyone scrolls through in the library, in at most 10 requests.
    private const int MostPlaylists = 500;

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly Action<string> _saveRefreshToken;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string _refreshToken;
    private SpotifySignIn.Tokens? _tokens;

    // Since when no device has been playing for the account, for asking less often the longer it stays that way.
    private DateTime? _idleSince;

    // The last song a device was playing (or paused on), shown paused while no device is playing for the account.
    private RemoteState? _last;

    // The Spotify app on this computer, read and pressed directly (Windows' media controls, or AppleScript on a Mac),
    // or null. While it's playing, it says what's playing and takes the buttons, so the Web API is only asked now and
    // then, for what only it knows: the playlist, the device and its volume.
    private readonly IRemotePlayer? _app;
    private bool _usingApp;
    private RemoteState? _web;
    private DateTime _webReadAt;

    // Playlists (and Liked Songs) Spotify wouldn't share, with why, so they aren't asked for again.
    private readonly ConcurrentDictionary<string, string> _refused = new();

    // What the app here said last time it was read, or null when it couldn't be.
    private RemoteState? _appRead;

    // How often the Web API is asked while the app on this computer says what's playing, and how soon after a new
    // song (for its playlist), as Spotify limits how often all of Lucent's users together may ask.
    private static readonly TimeSpan WebEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan WebAfterNewSong = TimeSpan.FromSeconds(5);

    /// <param name="refreshToken">From <see cref="SpotifySignIn.SignInAsync"/>, or saved from before.</param>
    /// <param name="saveRefreshToken">Called when Spotify hands out a new refresh token, which replaces the saved one.</param>
    /// <param name="app">The Spotify app on this computer, read and pressed directly, or null.</param>
    public SpotifyRemote(HttpClient http, string refreshToken, Action<string> saveRefreshToken, IRemotePlayer? app = null)
    {
        _http = http;
        _clientId = SpotifySignIn.ClientId ?? throw new RemoteProblem("This copy of Lucent wasn't built with a Spotify app to sign in with.");
        _refreshToken = refreshToken;
        _saveRefreshToken = saveRefreshToken;
        _app = app;
    }

    public string Name => "Spotify";

    /// <summary>Raised when the Spotify app on this computer says its song or play state changed.</summary>
    public event EventHandler? Changed
    {
        add
        {
            if (_app is IRemoteNotifier app)
            {
                app.Changed += value;
            }
        }

        remove
        {
            if (_app is IRemoteNotifier app)
            {
                app.Changed -= value;
            }
        }
    }

    /// <summary>
    /// True when the Spotify app is running on this computer, or when there's no app here to read (a connection made
    /// just to list playlists), which then has nothing to open.
    /// </summary>
    public bool IsOpen => _app is not IRemoteOpener app || app.IsOpen;

    /// <summary>Starts the Spotify app on this computer.</summary>
    public Task OpenAsync(CancellationToken cancel) => _app is IRemoteOpener app ? app.OpenAsync(cancel) : Task.CompletedTask;

    /// <summary>
    /// What's playing: from the Spotify app on this computer while it plays (with the Web API's playlist, device and
    /// volume added, read every <see cref="WebEvery"/>), otherwise from the Web API, which also sees phones and
    /// speakers. When the Web API can't be asked (Spotify has asked Lucent to wait), the app's song shows instead.
    /// </summary>
    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var app = await ReadAppAsync(cancel);
        var newSong = !SameSong(_appRead?.Track, app?.Track);
        _appRead = app;
        if (app is { IsPlaying: true, Track: not null })
        {
            _usingApp = true;
            var now = DateTime.UtcNow;
            var due = _web is null || now - _webReadAt > WebEvery || (!SameSong(_web.Track, app.Track) && now - _webReadAt > WebAfterNewSong);
            // A new song shows the moment the app names it; the Web API is asked about it on the next read, which also
            // gives Spotify's servers a moment to catch up with the app.
            if (due && !newSong && !SpotifyWait.IsWaiting)
            {
                _webReadAt = now;
                try
                {
                    _web = await ReadWebAsync(cancel);
                }
                catch (RemoteProblem)
                {
                    // The app still says what's playing.
                }
            }

            return Combine(app, _web);
        }

        try
        {
            var web = await ReadWebAsync(cancel);
            _usingApp = false;
            return web;
        }
        catch (RemoteProblem) when (app?.Track is not null)
        {
            // Spotify asked Lucent to wait (or can't be reached), but the app here has the song.
            _usingApp = true;
            return Combine(app, _web);
        }
    }

    private async Task<RemoteState?> ReadAppAsync(CancellationToken cancel)
    {
        if (_app is null)
        {
            return null;
        }

        try
        {
            return await _app.ReadAsync(cancel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Not open on this computer (or not allowed to be asked): the Web API it is.
            return null;
        }
    }

    // The app's song and where it's up to, with what the Web API knows about the same song: its Spotify ID (so the
    // queue window marks it), cover, playlist, device and volume. Shuffle and repeat are the account's, whatever the
    // song: the app's when it says (it's read more often), otherwise the Web API's.
    private static RemoteState Combine(RemoteState app, RemoteState? web)
    {
        app = app with { Shuffle = app.Shuffle ?? web?.Shuffle, Repeat = CombineRepeat(app.Repeat, web?.Repeat) };
        var volume = CombineVolume(app.Volume, web);
        if (web?.Track is not { } webTrack || !SameSong(webTrack, app.Track))
        {
            // A new song, until the Web API is next read, is most likely from the same playlist, and on the same device
            // at the same volume.
            return app with { Volume = volume, ListId = web?.ListId };
        }

        var track = webTrack with { Duration = app.Track!.Duration > TimeSpan.Zero ? app.Track.Duration : webTrack.Duration };
        return app with { Track = track, ListId = web.ListId, Device = web.Device, Volume = volume, IsElsewhere = web.IsElsewhere };
    }

    // The volume of the device playing, which is the app here: Spotify's own slider as the Web API last read it, unless
    // Spotify has asked Lucent to wait (when a change could only be made through the app here), or it hasn't been read
    // yet; then the app's (on a Mac, the same slider; on Windows, Spotify's level in the volume mixer). So the volume
    // bar stays through a wait, and a new song before the next Web API read.
    private static double? CombineVolume(double? app, RemoteState? web)
    {
        var webVolume = web is { IsElsewhere: false } ? web.Volume : null;
        return SpotifyWait.IsWaiting ? app ?? webVolume : webVolume ?? app;
    }

    // The Mac app only says whether it repeats at all, so repeating one song comes from the Web API.
    private static RepeatMode? CombineRepeat(RepeatMode? app, RepeatMode? web) =>
        app is RepeatMode.All && web is RepeatMode.One ? RepeatMode.One : app ?? web;

    // The app and the Web API name a song the same way, or by the same spotify:track: URI on a Mac.
    private static bool SameSong(RemoteTrack? a, RemoteTrack? b) =>
        a is not null && b is not null
        && ((a.Uri is not null && a.Uri == b.Uri) || string.Equals(a.Title.Trim(), b.Title.Trim(), StringComparison.OrdinalIgnoreCase));

    private async Task<RemoteState?> ReadWebAsync(CancellationToken cancel)
    {
        using var response = await SendAsync(HttpMethod.Get, Api, cancel);
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            // No device is playing for the account: Spotify was just opened, was quit and opened again, or has sat
            // paused long enough to let go. The song read last stays, paused, as Spotify's own app still shows it,
            // and Play picks it up again (see PlayOnSomeDeviceAsync).
            return _last is { } last ? last with { IsPlaying = false, ReadAt = DateTime.UtcNow, Device = null, Volume = null, IsElsewhere = false } : null;
        }

        await ThrowIfFailedAsync(response, cancel);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
        var state = ParseState(json.RootElement, DateTime.UtcNow);
        if (state?.Track is not null)
        {
            _last = state;
        }

        return state;
    }

    // Spotify limits how often all of Lucent's users together may ask, and asking every few seconds all day (paused
    // overnight, say) ran into a wait of hours. While another device plays, every 5 seconds, or as the song ends if
    // that's sooner, so the next song shows on time. While nothing plays, every 5 seconds for a minute, then every 15,
    // then once a minute after ten minutes: the app here says the moment it starts playing, and a press of Play in
    // Lucent reads straight away, so only a phone or speaker starting on its own is noticed later.
    public TimeSpan ReadAgainAfter(RemoteState? state)
    {
        // The app on this computer can be asked every second; the Web API part keeps its own pace.
        if (_usingApp)
        {
            _idleSince = null;
            return TimeSpan.FromSeconds(1);
        }

        if (state is not { IsPlaying: true })
        {
            return IdleReadEvery(DateTime.UtcNow - (_idleSince ??= DateTime.UtcNow));
        }

        _idleSince = null;
        var left = state.Track is { Duration.Ticks: > 0 } track ? track.Duration - state.PositionNow + TimeSpan.FromSeconds(0.5) : TimeSpan.MaxValue;
        return left < TimeSpan.FromSeconds(5) ? TimeSpan.FromSeconds(Math.Max(1, left.TotalSeconds)) : TimeSpan.FromSeconds(5);
    }

    /// <summary>How often the Web API is asked while nothing plays, by how long nothing has.</summary>
    internal static TimeSpan IdleReadEvery(TimeSpan idle) =>
        idle < TimeSpan.FromMinutes(1) ? TimeSpan.FromSeconds(5)
        : idle < TimeSpan.FromMinutes(10) ? TimeSpan.FromSeconds(15)
        : TimeSpan.FromMinutes(1);

    // While the app here says what's playing, its own buttons are pressed (which needs no Premium and no Web API).
    public Task PlayAsync(CancellationToken cancel) => _usingApp && _app is { } app ? app.PlayAsync(cancel) : PlayOnSomeDeviceAsync(null, cancel);

    public Task PauseAsync(CancellationToken cancel) => _usingApp && _app is { } app ? app.PauseAsync(cancel) : CommandAsync(HttpMethod.Put, "/pause", cancel);

    public Task NextAsync(CancellationToken cancel) => _usingApp && _app is { } app ? app.NextAsync(cancel) : CommandAsync(HttpMethod.Post, "/next", cancel);

    public Task PreviousAsync(CancellationToken cancel) =>
        _usingApp && _app is { } app ? app.PreviousAsync(cancel) : CommandAsync(HttpMethod.Post, "/previous", cancel);

    public Task SeekAsync(TimeSpan position, CancellationToken cancel) => _usingApp && _app is { } app
        ? app.SeekAsync(position, cancel)
        : CommandAsync(HttpMethod.Put, $"/seek?position_ms={(long)Math.Max(0, position.TotalMilliseconds)}", cancel);

    public async Task<byte[]?> ArtworkAsync(RemoteTrack track, CancellationToken cancel) =>
        track.ArtworkUrl is { } url ? await _http.GetByteArrayAsync(url, cancel)
        : _app is { } app ? await app.ArtworkAsync(track, cancel)
        : null;

    /// <summary>
    /// The volume of the device Spotify plays on, through the Web API (which needs Premium). When that can't be asked
    /// (Spotify has asked Lucent to wait, or the account isn't Premium) and Spotify plays here, the app here sets it.
    /// </summary>
    public async Task SetVolumeAsync(double volume, CancellationToken cancel)
    {
        if (SpotifyWait.IsWaiting && AppHereSetsVolume is { } here)
        {
            await here.SetVolumeAsync(volume, cancel);
            return;
        }

        try
        {
            await CommandAsync(HttpMethod.Put, $"/volume?volume_percent={SpotifyVolume.ToSpotifyPercent(volume)}", cancel);
        }
        catch (RemoteProblem) when (AppHereSetsVolume is { } local)
        {
            await local.SetVolumeAsync(volume, cancel);
            return;
        }

        Remember(state => state with { Volume = volume });
    }

    // The app here, when it plays for the account and can say and set its volume.
    private IRemoteVolume? AppHereSetsVolume =>
        _app is IRemoteVolume here && _appRead is { Track: not null, Volume: not null } && (_web ?? _last) is not { IsElsewhere: true } ? here : null;

    /// <summary>
    /// Shuffle on or off: through the app here when it can (no Premium needed), otherwise through the Web API.
    /// </summary>
    public async Task SetShuffleAsync(bool on, CancellationToken cancel)
    {
        if (_appRead?.Shuffle is null || !await AppSetsAsync(app => app.SetShuffleAsync(on, cancel)))
        {
            await CommandAsync(HttpMethod.Put, $"/shuffle?state={(on ? "true" : "false")}", cancel);
        }

        Remember(state => state with { Shuffle = on });
    }

    /// <summary>Repeat: through the app here when it can, otherwise (or for one song on a Mac) through the Web API.</summary>
    public async Task SetRepeatAsync(RepeatMode mode, CancellationToken cancel)
    {
        if (_appRead?.Repeat is null || !await AppSetsAsync(app => app.SetRepeatAsync(mode, cancel)))
        {
            var state = mode switch
            {
                RepeatMode.One => "track",
                RepeatMode.All => "context",
                _ => "off",
            };
            await CommandAsync(HttpMethod.Put, $"/repeat?state={state}", cancel);
        }

        Remember(state => state with { Repeat = mode });
    }

    // The app here sets it, when it's the device Spotify plays on (a phone or speaker keeps its own).
    private async Task<bool> AppSetsAsync(Func<IRemoteModes, Task> set)
    {
        if (_app is not IRemoteModes modes || (_web ?? _last) is { IsElsewhere: true })
        {
            return false;
        }

        try
        {
            await set(modes);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // It can't (repeat one song on a Mac) or didn't take it: the Web API, then.
            return false;
        }
    }

    // A change just made shows in the Web API's last reading too, which may not be read again for half a minute.
    private void Remember(Func<RemoteState, RemoteState> change)
    {
        _web = _web is { } web ? change(web) : null;
        _last = _last is { } last ? change(last) : null;
    }

    // The Web API's playback state (GET /me/player).
    private static RemoteState? ParseState(JsonElement root, DateTime readAt)
    {
        var playing = root.TryGetProperty("is_playing", out var isPlaying) && isPlaying.ValueKind == JsonValueKind.True;
        var position = root.TryGetProperty("progress_ms", out var progress) && progress.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(progress.GetInt64())
            : TimeSpan.Zero;
        // The playlist, album, artist or Liked Songs it's playing from, as a spotify: URI.
        var context = root.TryGetProperty("context", out var contextJson) && contextJson.ValueKind == JsonValueKind.Object
            && Text(contextJson, "uri") is { Length: > 0 } uri
                ? uri
                : null;
        // No item: an advert, or a device that's on but has nothing loaded.
        var track = root.TryGetProperty("item", out var item) ? ParseTrack(item, null) : null;
        // The Spotify Connect device it's playing on: this computer, a phone, a speaker or a TV. Some can't have
        // their volume changed from elsewhere (supports_volume false).
        string? device = null;
        double? volume = null;
        var elsewhere = false;
        if (root.TryGetProperty("device", out var deviceJson) && deviceJson.ValueKind == JsonValueKind.Object)
        {
            device = Text(deviceJson, "name") is { Length: > 0 } name ? name : null;
            elsewhere = !IsThisComputer(deviceJson, Environment.MachineName);
            var canSet = !deviceJson.TryGetProperty("supports_volume", out var supports) || supports.ValueKind != JsonValueKind.False;
            if (canSet && deviceJson.TryGetProperty("volume_percent", out var percent) && percent.ValueKind == JsonValueKind.Number)
            {
                // Spotify's slider, which is much steeper than Lucent's bar (SpotifyVolume).
                volume = SpotifyVolume.FromSpotify(percent.GetDouble() / 100);
            }
        }

        bool? shuffle = root.TryGetProperty("shuffle_state", out var shuffling) && shuffling.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? shuffling.GetBoolean()
            : null;
        RepeatMode? repeat = Text(root, "repeat_state") switch
        {
            "off" => RepeatMode.Off,
            "context" => RepeatMode.All,
            "track" => RepeatMode.One,
            _ => null,
        };
        return new RemoteState(track, playing, position, readAt, context, device, volume, elsewhere, shuffle, repeat);
    }

    // A track or podcast episode. An album's own list of tracks leaves the album out, so it's passed in.
    internal static RemoteTrack? ParseTrack(JsonElement item, JsonElement? fromAlbum)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = Text(item, "name");
        var duration = item.TryGetProperty("duration_ms", out var length) && length.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(length.GetInt64())
            : TimeSpan.Zero;
        var artist = Artists(item);
        var album = string.Empty;
        var albumArtist = string.Empty;
        uint year = 0;
        string? artwork = null;
        string? thumbnail = null;
        var albumObject = fromAlbum ?? (item.TryGetProperty("album", out var albumJson) ? albumJson : default);
        if (albumObject.ValueKind == JsonValueKind.Object)
        {
            album = Text(albumObject, "name");
            albumArtist = Artists(albumObject);
            artwork = LargestImage(albumObject);
            thumbnail = SmallestImage(albumObject);

            // "1998-04-20", "1998-04" or "1998", by how much the label told Spotify.
            var released = Text(albumObject, "release_date");
            year = released.Length >= 4 && uint.TryParse(released[..4], NumberStyles.None, CultureInfo.InvariantCulture, out var found) ? found : 0;
        }
        else if (item.TryGetProperty("show", out var show) && show.ValueKind == JsonValueKind.Object)
        {
            // A podcast episode: the show stands in for the artist and album.
            artist = Text(show, "publisher");
            album = Text(show, "name");
            artwork = LargestImage(show);
            thumbnail = SmallestImage(show);
        }

        var id = Text(item, "id") is { Length: > 0 } value ? value : $"{artist}|{title}";
        var uri = Text(item, "uri") is { Length: > 0 } itemUri ? itemUri : null;
        return new RemoteTrack(id, title, artist, album, duration, artwork, uri, thumbnail)
        {
            AlbumArtist = albumArtist,
            Year = year,
            TrackNumber = Number(item, "track_number"),
            Disc = Number(item, "disc_number"),
        };
    }

    // Every artist of a song or album, "Ulrich Schnauss, Mark Peters".
    private static string Artists(JsonElement owner) =>
        owner.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artists.EnumerateArray().Select(entry => Text(entry, "name")).Where(name => name.Length > 0))
            : string.Empty;

    private static uint Number(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetUInt32(out var number) ? number : 0;

    public async Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel)
    {
        // A playlist Spotify wouldn't share isn't asked for again with every song, only what's up next.
        if (state.ListId is { } refusedList && _refused.TryGetValue(refusedList, out var refusedNote))
        {
            return await UpNextAsync(state.ListId, refusedNote, cancel);
        }

        string? note = null;
        try
        {
            var list = state.ListId?.Split(':') switch
            {
                ["spotify", "playlist", var id] => await PlaylistAsync(state.ListId, id, cancel),
                ["spotify", "album", var id] => await AlbumAsync(state.ListId, id, cancel),
                [.., "collection"] => await LikedSongsAsync(state.ListId, cancel),
                _ => null,
            };
            if (list is not null)
            {
                return list;
            }
        }
        catch (Refused refused)
        {
            note = refused.Note;
            _refused[state.ListId!] = note;
        }

        // An artist, a radio station, a mix Spotify doesn't share: show what's up next instead.
        return await UpNextAsync(state.ListId, note, cancel);
    }

    public async Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel)
    {
        var song = track.Uri ?? $"spotify:track:{track.Id}";
        var justTheSong = JsonSerializer.Serialize(new Dictionary<string, object> { ["uris"] = new[] { song } });
        if (list.Id is not { } context)
        {
            await PlayOnSomeDeviceAsync(justTheSong, cancel);
            return;
        }

        // From a playlist or album, Spotify carries on through it, so it's played from there.
        var fromList = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["context_uri"] = context,
            ["offset"] = new Dictionary<string, string> { ["uri"] = song },
        });
        if (!list.FollowsSong)
        {
            await PlayOnSomeDeviceAsync(fromList, cancel);
            return;
        }

        // What's up next can hold songs queued from elsewhere, which aren't in the playlist, and Spotify refuses those
        // from it: they play on their own.
        try
        {
            await PlayOnSomeDeviceAsync(fromList, cancel);
        }
        catch (RemoteProblem) when (!SpotifyWait.IsWaiting)
        {
            await PlayOnSomeDeviceAsync(justTheSong, cancel);
        }
    }

    /// <summary>
    /// The account's Liked Songs, then its playlists as Spotify lists them (GET /me/playlists): the ones it made, follows
    /// and collaborates on. Spotify's own mixes (Discover Weekly, Release Radar) aren't among them, as it doesn't share
    /// those with other apps.
    /// </summary>
    public async Task<IReadOnlyList<RemotePlaylist>> PlaylistsAsync(CancellationToken cancel)
    {
        // While Spotify has asked Lucent to wait (or can't be reached), the playlists listed last time show.
        if (SpotifyWait.IsWaiting && await RemoteListStore.LoadPlaylistsAsync(Name) is { } kept)
        {
            return kept;
        }

        IReadOnlyList<RemotePlaylist> playlists;
        try
        {
            playlists = await ReadPlaylistsAsync(cancel);
        }
        catch (Exception ex) when (Unreachable(ex))
        {
            if (await RemoteListStore.LoadPlaylistsAsync(Name) is { } listedBefore)
            {
                return listedBefore;
            }

            throw;
        }

        RemoteListStore.SavePlaylists(Name, playlists);
        return playlists;
    }

    private async Task<IReadOnlyList<RemotePlaylist>> ReadPlaylistsAsync(CancellationToken cancel)
    {
        try
        {
            await RequireScopeAsync("playlist-read-private", PlaylistsSignInNote, cancel);
        }
        catch (Refused refused)
        {
            throw new RemoteProblem(refused.Note);
        }

        string me;
        using (var account = await GetJsonAsync($"{Root}/me", null, cancel))
        {
            me = Text(account.RootElement, "id");
        }

        var playlists = new List<RemotePlaylist>();
        if (me.Length > 0 && _tokens?.Scope?.Split(' ').Contains("user-library-read") != false)
        {
            playlists.Add(new RemotePlaylist($"spotify:user:{me}:collection", "Liked Songs", CanAdd: true));
        }

        for (string? page = $"{Root}/me/playlists?limit=50"; page is not null && playlists.Count < MostPlaylists;)
        {
            using var json = await GetJsonAsync(page, null, cancel);
            if (json.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                playlists.AddRange(items.EnumerateArray().Select(item => ParsePlaylist(item, me)).OfType<RemotePlaylist>());
            }

            page = Text(json.RootElement, "next") is { Length: > 0 } next ? next : null;
        }

        return playlists;
    }

    /// <summary>A playlist from GET /me/playlists, or null for an empty slot (one since deleted).</summary>
    internal static RemotePlaylist? ParsePlaylist(JsonElement item, string me)
    {
        if (item.ValueKind != JsonValueKind.Object || Text(item, "uri") is not { Length: > 0 } uri)
        {
            return null;
        }

        // The count is under "items" since February 2026's changes ("tracks" before).
        int? songs = null;
        foreach (var name in new[] { "items", "tracks" })
        {
            if (item.TryGetProperty(name, out var count) && count.ValueKind == JsonValueKind.Object
                && count.TryGetProperty("total", out var total) && total.ValueKind == JsonValueKind.Number)
            {
                songs = total.GetInt32();
                break;
            }
        }

        string? owner = null;
        if (item.TryGetProperty("owner", out var by) && by.ValueKind == JsonValueKind.Object && Text(by, "id") != me)
        {
            owner = Text(by, "display_name") is { Length: > 0 } shown ? shown : Text(by, "id") is { Length: > 0 } id ? id : null;
        }

        // Spotify only lets other apps add to playlists the account made or collaborates on.
        var collaborative = item.TryGetProperty("collaborative", out var shared) && shared.ValueKind == JsonValueKind.True;
        var title = Text(item, "name");
        return new RemotePlaylist(uri, title.Length > 0 ? title : "Untitled playlist", songs, owner, owner is null || collaborative);
    }

    public async Task<RemoteList> SongsAsync(RemotePlaylist playlist, CancellationToken cancel)
    {
        try
        {
            return playlist.Uri.Split(':') switch
            {
                ["spotify", "playlist", var id] => await PlaylistAsync(playlist.Uri, id, cancel),
                [.., "collection"] => await LikedSongsAsync(playlist.Uri, cancel),
                _ => new RemoteList(playlist.Uri, playlist.Name, Array.Empty<RemoteTrack>()),
            };
        }
        catch (Refused refused)
        {
            // Other people's playlists: Spotify plays them, but won't say what's in them.
            return new RemoteList(playlist.Uri, playlist.Name, Array.Empty<RemoteTrack>(), refused.Note == PlaylistNote
                ? PreviewRefusedNote
                : refused.Note.Replace(" Until then, this is what's up next.", string.Empty, StringComparison.Ordinal));
        }
    }

    public Task PlayPlaylistAsync(RemotePlaylist playlist, CancellationToken cancel) =>
        PlayOnSomeDeviceAsync(JsonSerializer.Serialize(new Dictionary<string, object> { ["context_uri"] = playlist.Uri }), cancel);

    /// <summary>
    /// Adds the song to the end of a playlist (POST /playlists/{id}/items), or saves it to Liked Songs (PUT /me/library).
    /// A sign-in from before Lucent added songs doesn't allow either, so it says to sign in again rather than ask.
    /// </summary>
    /// <summary>
    /// False when the sign-in is from before Lucent added songs to playlists, so it needs signing in again first. Asks
    /// Spotify nothing but a fresh sign-in token, when the one held has run out.
    /// </summary>
    public async Task<bool> CanAddSongsAsync(CancellationToken cancel)
    {
        await AccessTokenAsync(forceRefresh: false, cancel);
        return _tokens?.Scope is not { } granted || granted.Split(' ').Contains("playlist-modify-private");
    }

    public async Task AddToPlaylistAsync(RemotePlaylist playlist, RemoteTrack track, CancellationToken cancel)
    {
        var song = track.Uri ?? $"spotify:track:{track.Id}";
        var liked = playlist.IsLiked;
        try
        {
            await RequireScopeAsync(liked ? "user-library-modify" : "playlist-modify-private", AddSignInNote, cancel);
        }
        catch (Refused refused)
        {
            throw new RemoteProblem(refused.Note);
        }

        using var response = liked
            ? await SendAsync(HttpMethod.Put, $"{Root}/me/library?uris={Uri.EscapeDataString(song)}", cancel)
            : await SendAsync(HttpMethod.Post, $"{Root}/playlists/{playlist.Uri.Split(':')[^1]}/items", cancel,
                JsonSerializer.Serialize(new Dictionary<string, object> { ["uris"] = new[] { song } }));
        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            throw new RemoteProblem($"Spotify wouldn't let Lucent add songs to {playlist.Name}. It only allows playlists you made or collaborate on.");
        }

        await ThrowIfFailedAsync(response, cancel);
    }

    /// <summary>
    /// The song's open.spotify.com link, which opens it in Spotify or a browser for whoever it's sent to, or null when
    /// Spotify didn't say which song it is.
    /// </summary>
    public static string? LinkOf(RemoteTrack track) =>
        (track.Uri ?? $"spotify:track:{track.Id}").Split(':') is ["spotify", var kind and ("track" or "episode"), var id]
            && id.Length > 0 && id.All(char.IsLetterOrDigit)
            ? $"https://open.spotify.com/{kind}/{id}"
            : null;

    string? IRemoteLinks.WebLink(RemoteTrack track) => LinkOf(track);

    /// <summary>
    /// Whether the song is already saved in Liked Songs (GET /me/library/contains), or already in the playlist, among its
    /// first thousand songs (read 50 at a time, as Spotify has no way to ask about one song).
    /// </summary>
    public async Task<bool> ContainsAsync(RemotePlaylist playlist, RemoteTrack track, CancellationToken cancel)
    {
        var song = track.Uri ?? $"spotify:track:{track.Id}";
        if (playlist.IsLiked)
        {
            using var saved = await GetJsonAsync($"{Root}/me/library/contains?uris={Uri.EscapeDataString(song)}", null, cancel);
            return saved.RootElement.ValueKind == JsonValueKind.Array && saved.RootElement.EnumerateArray().FirstOrDefault().ValueKind == JsonValueKind.True;
        }

        var (songs, _, _) = await PagesAsync($"{Root}/playlists/{playlist.Uri.Split(':')[^1]}/items?limit=50&additional_types=track,episode", null,
            entry => ParseTrack(entry.TryGetProperty("item", out var item) ? item : entry.TryGetProperty("track", out var each) ? each : default, null),
            MostSongs, TimeSpan.Zero, cancel);
        return songs.Any(each => each.Uri == song || each.Id == track.Id);
    }

    /// <summary>
    /// Plays (what's loaded, or <paramref name="json"/>'s songs). With no device playing for the account, Spotify
    /// refuses that (404, NO_ACTIVE_DEVICE), so the play goes to one of the account's devices that's on: this
    /// computer's Spotify first, then the only one there is, then any computer. Spotify carries on there from
    /// where the account left off.
    /// </summary>
    private async Task PlayOnSomeDeviceAsync(string? json, CancellationToken cancel)
    {
        using (var response = await SendAsync(HttpMethod.Put, Api + "/play", cancel, json))
        {
            if (response.StatusCode != HttpStatusCode.NotFound)
            {
                await ThrowIfFailedAsync(response, cancel);
                return;
            }
        }

        // The Spotify app here, when it has a song loaded, plays it itself.
        if (json is null && await ReadAppAsync(cancel) is { Track: not null } && _app is { } app)
        {
            await app.PlayAsync(cancel);
            return;
        }

        string? device;
        using (var devices = await GetJsonAsync(Api + "/devices", null, cancel))
        {
            device = PickDevice(devices.RootElement, Environment.MachineName);
        }

        if (device is null)
        {
            throw new RemoteProblem("Spotify isn't open anywhere. Open Spotify on this computer (or a phone or speaker), then press play.");
        }

        if (json is null)
        {
            // Moving the account's playback to the device, playing, carries on where it left off.
            await CommandAsync(HttpMethod.Put, string.Empty, cancel,
                JsonSerializer.Serialize(new Dictionary<string, object> { ["device_ids"] = new[] { device }, ["play"] = true }));
        }
        else
        {
            await CommandAsync(HttpMethod.Put, $"/play?device_id={Uri.EscapeDataString(device)}", cancel, json);
        }
    }

    /// <summary>
    /// Moves the account's playback to this computer's Spotify (from a TV or speaker, say), playing, so the sound comes
    /// through this computer and the visuals hear it.
    /// </summary>
    public async Task PlayHereAsync(CancellationToken cancel)
    {
        string? device;
        using (var devices = await GetJsonAsync(Api + "/devices", null, cancel))
        {
            device = ThisComputer(devices.RootElement, Environment.MachineName);
        }

        if (device is null)
        {
            throw new RemoteProblem("Spotify isn't open on this computer. Open it here, then press Play here again.");
        }

        await CommandAsync(HttpMethod.Put, string.Empty, cancel,
            JsonSerializer.Serialize(new Dictionary<string, object> { ["device_ids"] = new[] { device }, ["play"] = true }));
    }

    /// <summary>The ID of this computer's Spotify, from GET /me/player/devices, or null when it isn't open here.</summary>
    internal static string? ThisComputer(JsonElement root, string machineName) =>
        root.TryGetProperty("devices", out var list) && list.ValueKind == JsonValueKind.Array
            && list.EnumerateArray().FirstOrDefault(device => Text(device, "id").Length > 0 && IsThisComputer(device, machineName)) is
                { ValueKind: JsonValueKind.Object } mine
            ? Text(mine, "id")
            : null;

    /// <summary>
    /// Whether a Spotify Connect device is this computer. Spotify names a computer's device after the computer, which macOS
    /// and Windows write with spaces and apostrophes ("Joshua's MacBook Pro") where the machine name has hyphens
    /// ("Joshuas-MacBook-Pro").
    /// </summary>
    internal static bool IsThisComputer(JsonElement device, string machineName) =>
        Text(device, "type").Equals("Computer", StringComparison.OrdinalIgnoreCase) && Plain(Text(device, "name")) == Plain(machineName);

    private static string Plain(string text) => new(text.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    /// <summary>The ID of the device to play on, from GET /me/player/devices, or null when none is on.</summary>
    internal static string? PickDevice(JsonElement root, string machineName)
    {
        if (!root.TryGetProperty("devices", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var devices = list.EnumerateArray()
            .Where(device => Text(device, "id").Length > 0
                && !(device.TryGetProperty("is_restricted", out var restricted) && restricted.ValueKind == JsonValueKind.True))
            .ToList();
        var computers = devices.Where(device => Text(device, "type").Equals("Computer", StringComparison.OrdinalIgnoreCase)).ToList();
        var mine = computers.Where(device => IsThisComputer(device, machineName)).ToList();
        var pick = mine.Count > 0 ? mine[0] : devices.Count == 1 ? devices[0] : computers.Count > 0 ? computers[0] : devices.FirstOrDefault();
        return pick.ValueKind == JsonValueKind.Object ? Text(pick, "id") : null;
    }

    // Spotify only lists the songs of playlists the account made or collaborates on (GET /playlists/{id}/items).
    private Task<RemoteList> PlaylistAsync(string context, string id, CancellationToken cancel) =>
        OrKeptAsync(context, () => ReadPlaylistAsync(context, id, cancel));

    private Task<RemoteList> LikedSongsAsync(string context, CancellationToken cancel) =>
        OrKeptAsync(context, () => ReadLikedSongsAsync(context, cancel));

    /// <summary>
    /// The list, read; or, while Spotify has asked Lucent to wait (or can't be reached), the copy kept from when it was
    /// last read whole, with a note saying so. With no copy kept, why it can't be read.
    /// </summary>
    private static async Task<RemoteList> OrKeptAsync(string context, Func<Task<RemoteList>> read)
    {
        Exception problem;
        if (SpotifyWait.IsWaiting)
        {
            problem = new RemoteProblem(SlowDown());
        }
        else
        {
            try
            {
                return await read();
            }
            catch (Exception ex) when (Unreachable(ex))
            {
                problem = ex;
            }
        }

        if (await RemoteListStore.LoadAnyAsync(context) is { } kept)
        {
            return kept.List with { Note = KeptNote(kept.ReadAt, DateTime.Now) };
        }

        throw problem;
    }

    // Spotify asked Lucent to wait, or there's no internet: a kept copy is better than nothing.
    private static bool Unreachable(Exception ex) =>
        SpotifyWait.IsWaiting || ex is HttpRequestException || (ex is TaskCanceledException { InnerException: TimeoutException });

    /// <summary>"Spotify asked Lucent to wait about 8 hours, so these are its songs as of yesterday."</summary>
    internal static string KeptNote(DateTime readAtUtc, DateTime nowLocal)
    {
        var read = readAtUtc.ToLocalTime();
        var when = read.Date == nowLocal.Date ? $"today at {read.ToString("h:mm tt", CultureInfo.InvariantCulture)}"
            : read.Date == nowLocal.Date.AddDays(-1) ? "yesterday"
            : read.ToString("MMM d", CultureInfo.InvariantCulture);
        return SpotifyWait.IsWaiting
            ? $"Spotify asked Lucent to wait {SpotifyWait.LeftText()} before asking again, so these are its songs as of {when}."
            : $"Lucent couldn't reach Spotify, so these are its songs as of {when}.";
    }

    private async Task<RemoteList> ReadPlaylistAsync(string context, string id, CancellationToken cancel)
    {
        await RequireScopeAsync("playlist-read-private", PlaylistSignInNote, cancel);
        string name, snapshot;
        using (var playlist = await GetJsonAsync($"{Root}/playlists/{id}?fields=name,snapshot_id", PlaylistNote, cancel))
        {
            name = Text(playlist.RootElement, "name");
            snapshot = Text(playlist.RootElement, "snapshot_id");
        }

        return await LongListAsync(context, name, snapshot.Length > 0 ? snapshot : null,
            $"{Root}/playlists/{id}/items?limit=50&additional_types=track,episode", PlaylistNote,
            // Each entry holds its song as "item" (as "track" before February 2026's changes).
            entry => Added(entry, ParseTrack(entry.TryGetProperty("item", out var item) ? item : entry.TryGetProperty("track", out var track) ? track : default, null)),
            cancel);
    }

    private async Task<RemoteList> AlbumAsync(string context, string id, CancellationToken cancel)
    {
        using var album = await GetJsonAsync($"{Root}/albums/{id}", null, cancel);
        var root = album.RootElement.Clone();
        var (tracks, _, _) = await PagesAsync($"{Root}/albums/{id}/tracks?limit=50", null, entry => ParseTrack(entry, root), MostSongs,
            TimeSpan.Zero, cancel);
        var artist = root.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artists.EnumerateArray().Select(entry => Text(entry, "name")).Where(name => name.Length > 0))
            : string.Empty;
        var name = Text(root, "name");
        return new RemoteList(context, artist.Length > 0 ? $"{artist} – {name}" : name, tracks);
    }

    private async Task<RemoteList> ReadLikedSongsAsync(string context, CancellationToken cancel)
    {
        await RequireScopeAsync("user-library-read", SignInAgainNote, cancel);
        return await LongListAsync(context, "Liked Songs", null, $"{Root}/me/tracks?limit=50", SignInAgainNote,
            entry => Added(entry, ParseTrack(entry.TryGetProperty("track", out var track) ? track : default, null)), cancel);
    }

    /// <summary>
    /// A playlist or Liked Songs: its first thousand songs, with the rest to read slowly after (see
    /// <see cref="RemoteList.Rest"/>), or the whole of it when it was read before and hasn't changed since.
    /// </summary>
    /// <param name="version">The playlist's snapshot ID, which changes with it, or null to tell by its first page.</param>
    private async Task<RemoteList> LongListAsync(string context, string name, string? version, string url, string? refusedNote,
        Func<JsonElement, RemoteTrack?> parse, CancellationToken cancel)
    {
        if (version is not null && await WholeListAsync(context, version) is { } known)
        {
            return known with { Name = name };
        }

        var (tracks, next, total) = await PagesAsync(url, refusedNote, parse, 1, TimeSpan.Zero, cancel);

        // Liked Songs has no snapshot, but lists the newest first: the same count and newest song, and it's the same.
        var key = version ?? $"{total}|{tracks.FirstOrDefault()?.Id}|{tracks.FirstOrDefault()?.AddedAt:O}";
        if (version is null && await WholeListAsync(context, key) is { } same)
        {
            return same with { Name = name };
        }

        if (next is not null)
        {
            var (more, after, _) = await PagesAsync(next, refusedNote, parse, MostSongs - tracks.Count, TimeSpan.Zero, cancel);
            tracks.AddRange(more);
            next = after;
        }

        if (next is null)
        {
            return KeepWhole(context, key, new RemoteList(context, name, tracks));
        }

        var start = tracks;
        return new RemoteList(context, name, start, Total: total, Rest: async restCancel =>
        {
            var (rest, _, _) = await PagesAsync(next, refusedNote, parse, MostSongsInAll - start.Count, RestPagePause, restCancel);
            return KeepWhole(context, key, new RemoteList(context, name, [.. start, .. rest]));
        });
    }

    // Kept since it was read, this run or an earlier one (see RemoteListStore).
    private static Task<RemoteList?> WholeListAsync(string context, string version) => RemoteListStore.LoadAsync(context, version);

    private static RemoteList KeepWhole(string context, string version, RemoteList list)
    {
        RemoteListStore.Save(context, version, list);
        return list;
    }

    // When a playlist entry (or a liked song) was added, from its "added_at".
    private static RemoteTrack? Added(JsonElement entry, RemoteTrack? track) =>
        track is not null && Text(entry, "added_at") is { Length: > 0 } added
            && DateTime.TryParse(added, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var at)
            ? track with { AddedAt = at }
            : track;

    // What Spotify plays next: the song now, then its queue (GET /me/player/queue). From a playlist or album, its songs
    // are played from it (see PlayFromListAsync), so Spotify carries on through it rather than stopping after one.
    private async Task<RemoteList> UpNextAsync(string? context, string? note, CancellationToken cancel)
    {
        using var queue = await GetJsonAsync($"{Api}/queue", null, cancel);
        var tracks = new List<RemoteTrack>();
        if (queue.RootElement.TryGetProperty("currently_playing", out var now) && ParseTrack(now, null) is { } playing)
        {
            tracks.Add(playing);
        }

        if (queue.RootElement.TryGetProperty("queue", out var next) && next.ValueKind == JsonValueKind.Array)
        {
            tracks.AddRange(next.EnumerateArray().Select(item => ParseTrack(item, null)).OfType<RemoteTrack>());
        }

        var playable = context?.Split(':') is ["spotify", "playlist" or "album", _];
        return new RemoteList(playable ? context : null, "Up next", tracks, note, FollowsSong: true);
    }

    private const string PlaylistNote = "Spotify only shares the songs of playlists you made or collaborate on, so this is what's up next.";

    private const string PreviewRefusedNote =
        "Spotify only shares the songs of playlists you made or collaborate on. Press play to hear this one in Spotify.";

    private const string PlaylistSignInNote =
        "To show your playlists here, sign out of Spotify and back in from the Add music menu. Until then, this is what's up next.";

    private const string AddSignInNote =
        "To add songs to your Spotify playlists, sign out of Spotify and back in from the Add music menu.";

    private const string PlaylistsSignInNote =
        "To see your Spotify playlists here, sign out of Spotify and back in from the Add music menu.";

    private const string SignInAgainNote =
        "To show your Liked Songs here, sign out of Spotify and back in from the Add music menu. Until then, this is what's up next.";

    // A sign-in from before Lucent read playlists doesn't allow it, and Spotify would only refuse.
    private async Task RequireScopeAsync(string scope, string note, CancellationToken cancel)
    {
        await AccessTokenAsync(forceRefresh: false, cancel);
        if (_tokens?.Scope is { } granted && !granted.Split(' ').Contains(scope))
        {
            throw new Refused(note);
        }
    }

    // Follows a list's "next" links, up to most songs, pausing between pages for as long as asked. Returns the songs,
    // the page after them (null at the end of the list), and how many songs Spotify says the whole list has.
    private async Task<(List<RemoteTrack> Tracks, string? Next, int? Total)> PagesAsync(string url, string? refusedNote,
        Func<JsonElement, RemoteTrack?> parse, int most, TimeSpan pause, CancellationToken cancel)
    {
        var tracks = new List<RemoteTrack>();
        int? total = null;
        string? page = url;
        for (var first = true; page is not null && tracks.Count < most; first = false)
        {
            if (!first && pause > TimeSpan.Zero)
            {
                await Task.Delay(pause, cancel);
            }

            using var json = await GetJsonAsync(page, refusedNote, cancel);
            if (json.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                // Songs no longer on Spotify come back empty, and are left out.
                tracks.AddRange(items.EnumerateArray().Select(parse).OfType<RemoteTrack>());
            }

            if (total is null && json.RootElement.TryGetProperty("total", out var count) && count.ValueKind == JsonValueKind.Number)
            {
                total = count.GetInt32();
            }

            page = Text(json.RootElement, "next") is { Length: > 0 } next ? next : null;
        }

        return (tracks, page, total);
    }

    // Spotify saying no (403) or that it doesn't know the list (404, as for its own mixes) means showing what's up next instead.
    private async Task<JsonDocument> GetJsonAsync(string url, string? refusedNote, CancellationToken cancel)
    {
        using var response = await SendAsync(HttpMethod.Get, url, cancel);
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound && refusedNote is not null)
        {
            throw new Refused(refusedNote);
        }

        await ThrowIfFailedAsync(response, cancel);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancel));
    }

    private sealed class Refused(string note) : Exception(note)
    {
        public string Note { get; } = note;
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;

    // The biggest cover Spotify lists (they come largest first, 640 pixels, but that isn't promised).
    private static string? LargestImage(JsonElement owner)
    {
        if (!owner.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return images.EnumerateArray()
            .Select(image => (Url: Text(image, "url"), Width: image.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number ? width.GetInt32() : 0))
            .Where(image => image.Url.Length > 0)
            .OrderByDescending(image => image.Width)
            .Select(image => image.Url)
            .FirstOrDefault();
    }

    // The smallest cover Spotify lists that's still sharp beside a song in a list (they're usually 640, 300 and 64 pixels).
    private static string? SmallestImage(JsonElement owner, int atLeast = 64)
    {
        if (!owner.TryGetProperty("images", out var images) || images.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var sized = images.EnumerateArray()
            .Select(image => (Url: Text(image, "url"), Width: image.TryGetProperty("width", out var width) && width.ValueKind == JsonValueKind.Number ? width.GetInt32() : 0))
            .Where(image => image.Url.Length > 0)
            .ToList();
        return sized.Where(image => image.Width >= atLeast).OrderBy(image => image.Width).Select(image => image.Url).FirstOrDefault()
            ?? sized.OrderByDescending(image => image.Width).Select(image => image.Url).FirstOrDefault();
    }

    private async Task CommandAsync(HttpMethod method, string path, CancellationToken cancel, string? json = null)
    {
        using var response = await SendAsync(method, Api + path, cancel, json);
        await ThrowIfFailedAsync(response, cancel);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, CancellationToken cancel, string? json = null)
    {
        // Spotify asked Lucent to wait: asking anyway would only make it wait longer.
        if (SpotifyWait.IsWaiting)
        {
            throw new RemoteProblem(SlowDown());
        }

        for (var attempt = 0; ; attempt++)
        {
            var token = await AccessTokenAsync(forceRefresh: attempt > 0, cancel);
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (json is not null)
            {
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            }
            else if (method != HttpMethod.Get)
            {
                request.Content = new ByteArrayContent(Array.Empty<byte>());
            }

            var response = await _http.SendAsync(request, cancel);
            // A token taken back early: get a new one and try once more.
            if (response.StatusCode == HttpStatusCode.Unauthorized && attempt == 0)
            {
                response.Dispose();
                continue;
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                SpotifyWait.WaitFor(RetryAfter(response.Headers.RetryAfter, DateTimeOffset.UtcNow));
            }

            return response;
        }
    }

    /// <summary>
    /// How long a 429 says to wait: Retry-After in seconds, or (rarely) as a date. Without one, 5 seconds.
    /// </summary>
    internal static TimeSpan RetryAfter(RetryConditionHeaderValue? header, DateTimeOffset now) =>
        header?.Delta is { } delta ? delta
        : header?.Date is { } date && date > now ? date - now
        : TimeSpan.FromSeconds(5);

    // Spotify says how long to wait, which is usually seconds but can be much longer after a lot of asking.
    private static string SlowDown() => SpotifyWait.Left > TimeSpan.FromMinutes(1.5)
        ? $"Spotify asked Lucent to wait {SpotifyWait.LeftText()} before asking again. Spotify itself still plays."
        : "Spotify asked Lucent to slow down. It'll carry on in a moment.";

    private async Task ThrowIfFailedAsync(HttpResponseMessage response, CancellationToken cancel)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancel);
        throw response.StatusCode switch
        {
            HttpStatusCode.Forbidden when body.Contains("PREMIUM_REQUIRED", StringComparison.Ordinal) =>
                new RemoteProblem("Spotify only lets other apps play, pause and skip with a Premium account."),
            // The device or what's playing doesn't allow it just now (some speakers and TVs, an advert).
            HttpStatusCode.Forbidden when body.Contains("Restriction violated", StringComparison.OrdinalIgnoreCase) =>
                new RemoteProblem("Spotify won't let other apps do that on the device it's playing on right now."),
            HttpStatusCode.Forbidden =>
                new RemoteProblem("Spotify wouldn't let Lucent do that. If this Spotify app is still in development mode, your account needs adding to it on developer.spotify.com."),
            HttpStatusCode.NotFound =>
                new RemoteProblem("Start playing something in Spotify first, then Lucent can control it."),
            HttpStatusCode.TooManyRequests => new RemoteProblem(SlowDown()),
            HttpStatusCode.Unauthorized =>
                new RemoteProblem("Spotify signed Lucent out. Sign in again from the Add music menu."),
            _ => new RemoteProblem($"Spotify had a problem ({(int)response.StatusCode})."),
        };
    }

    private async Task<string> AccessTokenAsync(bool forceRefresh, CancellationToken cancel)
    {
        await _tokenGate.WaitAsync(cancel);
        try
        {
            if (!forceRefresh && _tokens is { } current && DateTime.UtcNow < current.ExpiresAt)
            {
                return current.AccessToken;
            }

            var tokens = await SpotifySignIn.PostTokenAsync(_http, new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _refreshToken,
                ["client_id"] = _clientId,
            }, cancel);
            _tokens = tokens;
            // With PKCE, Spotify hands out a new refresh token each time and the old one stops working.
            if (tokens.RefreshToken is { Length: > 0 } refresh && refresh != _refreshToken)
            {
                _refreshToken = refresh;
                _saveRefreshToken(refresh);
            }

            return tokens.AccessToken;
        }
        finally
        {
            _tokenGate.Release();
        }
    }

    public void Dispose()
    {
        // The HttpClient is the app's, and a request still on its way is cancelled by its caller.
        _app?.Dispose();
    }
}
