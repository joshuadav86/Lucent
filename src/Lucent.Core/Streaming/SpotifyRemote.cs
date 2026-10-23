using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Spotify, through its Web API: what's playing on whichever device the account is playing on (the
/// Spotify app on this computer, a phone, a speaker), and its play, pause, skip and seek. Spotify only
/// takes those buttons from Premium accounts; on a free account Lucent can still show what's playing.
/// It also reads the playlist, album or Liked Songs the song is playing from, for the queue window, its shuffle and
/// repeat, which the player's own buttons change, and lists the account's playlists for the library.
/// </summary>
public sealed class SpotifyRemote : IRemotePlayer, IRemoteLists, IRemotePlaylists, IRemoteVolume, IRemoteDevices, IRemoteModes
{
    private const string Root = "https://api.spotify.com/v1";
    private const string Api = Root + "/me/player";

    // Enough for any playlist someone plays through, without hundreds of requests for a huge Liked Songs.
    private const int MostSongs = 1000;

    // More playlists than anyone scrolls through in the library, in at most 10 requests.
    private const int MostPlaylists = 500;

    private readonly HttpClient _http;
    private readonly string _clientId;
    private readonly Action<string> _saveRefreshToken;
    private readonly SemaphoreSlim _tokenGate = new(1, 1);
    private string _refreshToken;
    private SpotifySignIn.Tokens? _tokens;
    private DateTime _waitUntil;

    // The last song a device was playing (or paused on), shown paused while no device is playing for the account.
    private RemoteState? _last;

    // The Spotify app on this computer, read and pressed directly (Windows' media controls, or AppleScript on a Mac),
    // or null. While it's playing, it says what's playing and takes the buttons, so the Web API is only asked now and
    // then, for what only it knows: the playlist, the device and its volume.
    private readonly IRemotePlayer? _app;
    private bool _usingApp;
    private RemoteState? _web;
    private DateTime _webReadAt;

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

    /// <summary>
    /// What's playing: from the Spotify app on this computer while it plays (with the Web API's playlist, device and
    /// volume added, read every <see cref="WebEvery"/>), otherwise from the Web API, which also sees phones and
    /// speakers. When the Web API can't be asked (Spotify has asked Lucent to wait), the app's song shows instead.
    /// </summary>
    public async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        var app = await ReadAppAsync(cancel);
        _appRead = app;
        if (app is { IsPlaying: true, Track: not null })
        {
            _usingApp = true;
            var now = DateTime.UtcNow;
            var due = _web is null || now - _webReadAt > WebEvery || (!SameSong(_web.Track, app.Track) && now - _webReadAt > WebAfterNewSong);
            if (due && now >= _waitUntil)
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
        if (web?.Track is not { } webTrack || !SameSong(webTrack, app.Track))
        {
            return app with { Volume = null };
        }

        var track = webTrack with { Duration = app.Track!.Duration > TimeSpan.Zero ? app.Track.Duration : webTrack.Duration };
        return app with { Track = track, ListId = web.ListId, Device = web.Device, Volume = web.Volume, IsElsewhere = web.IsElsewhere };
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

    // Spotify limits how often an app may ask, over each 30 seconds, and a reading a second (with the playlist and
    // queue reads beside it) ran into that. Every 3 seconds while playing, or as the song ends if that's sooner,
    // so the next song shows on time; every 5 while paused.
    public TimeSpan ReadAgainAfter(RemoteState? state)
    {
        // The app on this computer can be asked every second; the Web API part keeps its own pace.
        if (_usingApp)
        {
            return TimeSpan.FromSeconds(1);
        }

        if (state is not { IsPlaying: true })
        {
            return TimeSpan.FromSeconds(5);
        }

        var left = state.Track is { Duration.Ticks: > 0 } track ? track.Duration - state.PositionNow + TimeSpan.FromSeconds(0.5) : TimeSpan.MaxValue;
        return left < TimeSpan.FromSeconds(3) ? TimeSpan.FromSeconds(Math.Max(1, left.TotalSeconds)) : TimeSpan.FromSeconds(3);
    }

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

    // Like the other buttons, this needs Premium.
    public Task SetVolumeAsync(double volume, CancellationToken cancel) =>
        CommandAsync(HttpMethod.Put, $"/volume?volume_percent={(int)Math.Round(Math.Clamp(volume, 0, 1) * 100)}", cancel);

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
                volume = Math.Clamp(percent.GetDouble() / 100, 0, 1);
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
    private static RemoteTrack? ParseTrack(JsonElement item, JsonElement? fromAlbum)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var title = Text(item, "name");
        var duration = item.TryGetProperty("duration_ms", out var length) && length.ValueKind == JsonValueKind.Number
            ? TimeSpan.FromMilliseconds(length.GetInt64())
            : TimeSpan.Zero;
        var artist = string.Empty;
        var album = string.Empty;
        string? artwork = null;
        string? thumbnail = null;
        if (item.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array)
        {
            artist = string.Join(", ", artists.EnumerateArray().Select(entry => Text(entry, "name")).Where(name => name.Length > 0));
        }

        var albumObject = fromAlbum ?? (item.TryGetProperty("album", out var albumJson) ? albumJson : default);
        if (albumObject.ValueKind == JsonValueKind.Object)
        {
            album = Text(albumObject, "name");
            artwork = LargestImage(albumObject);
            thumbnail = SmallestImage(albumObject);
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
        return new RemoteTrack(id, title, artist, album, duration, artwork, uri, thumbnail);
    }

    public async Task<RemoteList?> ListAsync(RemoteState state, CancellationToken cancel)
    {
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
        }

        // An artist, a radio station, a mix Spotify doesn't share: show what's up next instead.
        return await UpNextAsync(note, cancel);
    }

    public Task PlayFromListAsync(RemoteList list, RemoteTrack track, CancellationToken cancel)
    {
        var song = track.Uri ?? $"spotify:track:{track.Id}";
        // From a playlist or album, Spotify carries on through it; from what's up next, it plays just that song.
        var body = list.Id is { } context && !list.FollowsSong
            ? JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["context_uri"] = context,
                ["offset"] = new Dictionary<string, string> { ["uri"] = song },
            })
            : JsonSerializer.Serialize(new Dictionary<string, object> { ["uris"] = new[] { song } });
        return PlayOnSomeDeviceAsync(body, cancel);
    }

    /// <summary>
    /// The account's Liked Songs, then its playlists as Spotify lists them (GET /me/playlists): the ones it made, follows
    /// and collaborates on. Spotify's own mixes (Discover Weekly, Release Radar) aren't among them, as it doesn't share
    /// those with other apps.
    /// </summary>
    public async Task<IReadOnlyList<RemotePlaylist>> PlaylistsAsync(CancellationToken cancel)
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

    public Task PlayPlaylistAsync(RemotePlaylist playlist, CancellationToken cancel) =>
        PlayOnSomeDeviceAsync(JsonSerializer.Serialize(new Dictionary<string, object> { ["context_uri"] = playlist.Uri }), cancel);

    /// <summary>
    /// Adds the song to the end of a playlist (POST /playlists/{id}/items), or saves it to Liked Songs (PUT /me/library).
    /// A sign-in from before Lucent added songs doesn't allow either, so it says to sign in again rather than ask.
    /// </summary>
    public async Task AddToPlaylistAsync(RemotePlaylist playlist, RemoteTrack track, CancellationToken cancel)
    {
        var song = track.Uri ?? $"spotify:track:{track.Id}";
        var liked = playlist.Uri.EndsWith(":collection", StringComparison.Ordinal);
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
    public static string? WebLink(RemoteTrack track) =>
        (track.Uri ?? $"spotify:track:{track.Id}").Split(':') is ["spotify", var kind and ("track" or "episode"), var id]
            && id.Length > 0 && id.All(char.IsLetterOrDigit)
            ? $"https://open.spotify.com/{kind}/{id}"
            : null;

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
    private async Task<RemoteList> PlaylistAsync(string context, string id, CancellationToken cancel)
    {
        await RequireScopeAsync("playlist-read-private", PlaylistSignInNote, cancel);
        string name;
        using (var playlist = await GetJsonAsync($"{Root}/playlists/{id}?fields=name", PlaylistNote, cancel))
        {
            name = Text(playlist.RootElement, "name");
        }

        var tracks = await PagesAsync($"{Root}/playlists/{id}/items?limit=50&additional_types=track,episode", PlaylistNote,
            // Each entry holds its song as "item" (as "track" before February 2026's changes).
            entry => ParseTrack(entry.TryGetProperty("item", out var item) ? item : entry.TryGetProperty("track", out var track) ? track : default, null),
            cancel);
        return new RemoteList(context, name, tracks);
    }

    private async Task<RemoteList> AlbumAsync(string context, string id, CancellationToken cancel)
    {
        using var album = await GetJsonAsync($"{Root}/albums/{id}", null, cancel);
        var root = album.RootElement.Clone();
        var tracks = await PagesAsync($"{Root}/albums/{id}/tracks?limit=50", null, entry => ParseTrack(entry, root), cancel);
        var artist = root.TryGetProperty("artists", out var artists) && artists.ValueKind == JsonValueKind.Array
            ? string.Join(", ", artists.EnumerateArray().Select(entry => Text(entry, "name")).Where(name => name.Length > 0))
            : string.Empty;
        var name = Text(root, "name");
        return new RemoteList(context, artist.Length > 0 ? $"{artist} – {name}" : name, tracks);
    }

    private async Task<RemoteList> LikedSongsAsync(string context, CancellationToken cancel)
    {
        await RequireScopeAsync("user-library-read", SignInAgainNote, cancel);
        var tracks = await PagesAsync($"{Root}/me/tracks?limit=50", SignInAgainNote,
            entry => ParseTrack(entry.TryGetProperty("track", out var track) ? track : default, null), cancel);
        return new RemoteList(context, "Liked Songs", tracks);
    }

    // What Spotify plays next: the song now, then its queue (GET /me/player/queue).
    private async Task<RemoteList> UpNextAsync(string? note, CancellationToken cancel)
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

        return new RemoteList(null, "Up next", tracks, note, FollowsSong: true);
    }

    private const string PlaylistNote = "Spotify only shares the songs of playlists you made or collaborate on, so this is what's up next.";

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

    // Follows a list's "next" links, up to MostSongs songs.
    private async Task<List<RemoteTrack>> PagesAsync(string url, string? refusedNote, Func<JsonElement, RemoteTrack?> parse, CancellationToken cancel)
    {
        var tracks = new List<RemoteTrack>();
        for (string? page = url; page is not null && tracks.Count < MostSongs;)
        {
            using var json = await GetJsonAsync(page, refusedNote, cancel);
            if (json.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
            {
                // Songs no longer on Spotify come back empty, and are left out.
                tracks.AddRange(items.EnumerateArray().Select(parse).OfType<RemoteTrack>());
            }

            page = Text(json.RootElement, "next") is { Length: > 0 } next ? next : null;
        }

        return tracks;
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
        if (DateTime.UtcNow < _waitUntil)
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
                var wait = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5);
                _waitUntil = DateTime.UtcNow + wait;
            }

            return response;
        }
    }

    // Spotify says how long to wait, which is usually seconds but can be much longer after a lot of asking.
    private string SlowDown()
    {
        var left = _waitUntil - DateTime.UtcNow;
        return left > TimeSpan.FromMinutes(1.5)
            ? $"Spotify asked Lucent to wait {(left > TimeSpan.FromHours(1.5) ? $"about {left.TotalHours:0} hours" : $"about {left.TotalMinutes:0} minutes")} before asking again. Spotify itself still plays."
            : "Spotify asked Lucent to slow down. It'll carry on in a moment.";
    }

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
