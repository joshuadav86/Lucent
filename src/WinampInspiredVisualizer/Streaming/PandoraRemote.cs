using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Pandora: its Windows app, or pandora.com in a browser. Lucent reads Pandora's own window with UI Automation (what
/// screen readers use): the song, its artist and album, where it's up to, and the play, skip, replay and thumbs
/// buttons, which it presses. When Pandora also tells Windows' media controls what's playing (a browser does), those
/// give the song and cover, and take play, pause and skip. No Pandora sign-in is needed; whoever is signed in to
/// Pandora is who's playing.
/// </summary>
/// <remarks>
/// Pandora has no public API, and its Windows app doesn't tell Windows' media controls anything, so its window is
/// the only place to read it. A browser only shows UI Automation the tab in front, so a Pandora tab is read while
/// it's the one showing in its window. When Pandora doesn't say whether a thumb is lit, the player shows the thumb
/// given from Lucent. A station can't go back a song or be sought through, and Pandora's free tier limits skips.
/// The Mac has no Pandora app, so it controls the web player in its tab (see the Mac's Streaming/PandoraRemote.cs).
/// </remarks>
public sealed class PandoraRemote : MediaControlsRemote, IRemotePlayer, IRemoteRating, IRemoteOpener
{
    // Reading Pandora's window is slow (it's a web page inside), so while it's all there is, it's read this often,
    // with the position moving on by itself between reads. With Windows' media controls too, only the thumbs come
    // from the window, this often, and straight away when the song changes.
    private static readonly TimeSpan ScreenReadEvery = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan ThumbsReadEvery = TimeSpan.FromSeconds(3);

    // Pandora's labels for its buttons ("Thumb Up", "Thumbs down", "Thumb up, selected", "Pause", "Skip", "Replay").
    private static readonly Regex UpLabel = Label(@"thumbs?[\s_-]*up");
    private static readonly Regex DownLabel = Label(@"thumbs?[\s_-]*down");
    private static readonly Regex PlayLabel = Label(@"(play|pause)\b");
    private static readonly Regex SkipLabel = Label(@"(skip|next)\b");
    private static readonly Regex ReplayLabel = Label(@"(replay|previous|back)\b");

    // A time as Pandora shows it: 1:18, or -2:57 for what's left.
    private static readonly Regex Clock = new(@"^-?\d{1,2}:\d{2}(:\d{2})?$", RegexOptions.CultureInvariant);

    // The links in Pandora's song details: a song's page ends …/TR…, its artist's …/AR… and its album's …/AL….
    private static readonly Regex PageKind = new(@"pandora\.com/.*/(TR|AR|AL)[A-Za-z0-9]+/?(?:[?#].*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private Screen? _screen;
    private DateTime _screenReadAt;
    private string? _screenFor;

    // True while the song comes from Pandora's window rather than Windows' media controls.
    private bool _fromScreen;
    private bool _reported;

    // False when the last read found Pandora nowhere: not its app, nor a browser tab.
    private bool _found = true;

    // The thumb last given from Lucent and the song it was given to, shown when Pandora doesn't say which is lit.
    private (string Song, SongRating Rating)? _given;
    private string? _song;

    public override string Name => "Pandora";

    public override string ListLimit => "Pandora's stations pick each song as they go, and Pandora doesn't say what's next.";

    public override string PlaylistsLimit => "Pandora doesn't let other apps see your stations or playlists.";

    protected override string NotOpen => "Open Pandora (its app, or pandora.com in your browser) and play a station";

    // The process Pandora's window belongs to (the app's, or the browser's), and the app's or browser's that tells
    // Windows' media controls, so the visuals hear it.
    public override IReadOnlyList<string> ProcessNames
    {
        get
        {
            var names = new List<string>();
            if (_screen?.Process is { } process)
            {
                names.Add(process);
            }

            if (!_fromScreen || names.Count == 0)
            {
                names.AddRange(SourceAppId is { } id && IsPandoraApp(id) ? ["Pandora"] : YouTubeRemote.BrowserProcessNames(SourceAppId));
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    TimeSpan IRemotePlayer.ReadAgainAfter(RemoteState? state) => _fromScreen ? ScreenReadEvery : TimeSpan.FromSeconds(1);

    protected override GlobalSystemMediaTransportControlsSession? Pick(IReadOnlyList<GlobalSystemMediaTransportControlsSession> sessions)
    {
        if (sessions.FirstOrDefault(session => IsPandoraApp(session.SourceAppUserModelId)) is { } app)
        {
            return app;
        }

        // Windows doesn't say which site a browser plays, so a browser counts when one of its windows shows Pandora.
        var browsers = PandoraWindows().Where(window => window.InBrowser).Select(window => window.Process).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var candidates = sessions.Where(session => YouTubeRemote.IsBrowser(session.SourceAppUserModelId)
            && YouTubeRemote.BrowserProcessNames(session.SourceAppUserModelId).Any(browsers.Contains)).ToList();
        return candidates.FirstOrDefault(session =>
                session.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
            ?? candidates.FirstOrDefault();
    }

    public override async Task<RemoteState?> ReadAsync(CancellationToken cancel)
    {
        RemoteState? media = null;
        RemoteProblem? problem = null;
        try
        {
            media = await base.ReadAsync(cancel);
        }
        catch (RemoteProblem ex)
        {
            // Pandora's app doesn't tell Windows' media controls, so its window is read instead.
            problem = ex;
        }

        var fromMedia = media?.Track is not null;
        if (!fromMedia || media!.Track!.Id != _screenFor || DateTime.UtcNow - _screenReadAt >= ThumbsReadEvery)
        {
            _screenFor = media?.Track?.Id;
            _screenReadAt = DateTime.UtcNow;
            _screen = await Task.Run(() => ReadScreen(_screen), cancel);
            if (!fromMedia && _screen?.Title is null)
            {
                Report(media);
            }
        }

        _fromScreen = !fromMedia && _screen?.Title is not null;
        RemoteState? state;
        if (fromMedia)
        {
            state = media;
        }
        else if (_screen is { Title: { } title } screen)
        {
            var id = screen.Link ?? $"{screen.Artist}|{screen.Album}|{title}";
            var track = new RemoteTrack(id, title, screen.Artist ?? string.Empty, screen.Album ?? string.Empty, screen.Length);
            var volume = await Task.Run(() => AppVolume(null), cancel);
            state = new RemoteState(track, screen.Playing, screen.Position, screen.ReadAt, Volume: volume);
        }
        else if (media is not null)
        {
            state = media;
        }
        else
        {
            _found = false;
            throw problem ?? new RemoteProblem($"{NotOpen}, then Lucent can show and control it.");
        }

        _found = true;

        if (state?.Track is not { } song)
        {
            return state;
        }

        _song = song.Id;

        // No thumbs on screen (an ad, or another tab in front): the buttons hide until they're back.
        if (_screen is not { Up: not null, Down: not null } thumbs)
        {
            return state;
        }

        var rating = thumbs.Lit ?? (_given is { } given && given.Song == song.Id ? given.Rating : SongRating.None);
        return state with { Rating = rating };
    }

    public bool IsOpen => _found;

    /// <summary>Starts Pandora's app when it's installed, and otherwise opens pandora.com in the web browser.</summary>
    public async Task OpenAsync(CancellationToken cancel)
    {
        if (!await AppOpener.TryStoreAppAsync("PandoraMediaInc."))
        {
            AppOpener.OpenWebsite("https://www.pandora.com");
        }
    }

    public override Task PlayAsync(CancellationToken cancel) =>
        _fromScreen ? PressAsync(screen => screen.Playing ? null : screen.PlayPause, cancel) : base.PlayAsync(cancel);

    public override Task PauseAsync(CancellationToken cancel) =>
        _fromScreen ? PressAsync(screen => screen.Playing ? screen.PlayPause : null, cancel) : base.PauseAsync(cancel);

    public override Task NextAsync(CancellationToken cancel) =>
        _fromScreen ? PressAsync(screen => screen.Skip ?? throw new RemoteProblem("Pandora has no skip button showing right now."), cancel) : base.NextAsync(cancel);

    // Pandora's replay button (Premium) plays the song before; a station on the free tier has none.
    public override Task PreviousAsync(CancellationToken cancel) =>
        _fromScreen ? PressAsync(screen => screen.Replay ?? throw new RemoteProblem("Pandora doesn't go back a song on this station."), cancel) : base.PreviousAsync(cancel);

    // A station can't be sought through, and Pandora's window has no way to seek from outside.
    public override Task SeekAsync(TimeSpan position, CancellationToken cancel) =>
        _fromScreen ? Task.CompletedTask : base.SeekAsync(position, cancel);

    public async Task RateAsync(SongRating rating, CancellationToken cancel)
    {
        var song = _song;
        var given = _given is { } before && before.Song == song ? before.Rating : SongRating.None;
        await PressAsync(
            screen =>
            {
                if (screen is not { Up: { } up, Down: { } down })
                {
                    throw new RemoteProblem("Lucent can't see Pandora's thumbs right now. Bring Pandora to the front (its tab, in a browser) and try again.");
                }

                // Pressing a lit thumb takes it back, as in Pandora.
                var now = screen.Lit ?? given;
                return rating switch
                {
                    SongRating.Up when now != SongRating.Up => up,
                    SongRating.Down when now != SongRating.Down => down,
                    SongRating.None when now == SongRating.Up => up,
                    SongRating.None when now == SongRating.Down => down,
                    _ => null,
                };
            },
            cancel);
        if (song is not null)
        {
            _given = (song, rating);
        }
    }

    /// <summary>Reads Pandora's window afresh and presses the button <paramref name="pick"/> picks from it (none, for null).</summary>
    private async Task PressAsync(Func<Screen, AutomationElement?> pick, CancellationToken cancel)
    {
        await Task.Run(
            () =>
            {
                var screen = ReadScreen(_screen)
                    ?? throw new RemoteProblem($"{NotOpen}, then Lucent can control it.");
                if (pick(screen) is { } button && !Press(button))
                {
                    throw new RemoteProblem("Pandora didn't take that button just now.");
                }
            },
            cancel);

        // Read the window again with the next read, to see what the button did.
        _screenReadAt = default;
    }

    /// <summary>
    /// What Pandora's window shows: the song, where it's up to, and its buttons, or null when no window of Pandora's
    /// has its player showing. <paramref name="last"/>'s window is looked at first.
    /// </summary>
    private static Screen? ReadScreen(Screen? last)
    {
        var windows = PandoraWindows().OrderBy(window => window.Handle != last?.Window).ThenBy(window => window.InBrowser).ToList();
        foreach (var window in windows)
        {
            try
            {
                if (ReadWindow(window) is { } screen)
                {
                    return screen;
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
            {
                // The window closed or changed while it was read.
            }
        }

        return null;
    }

    private static Screen? ReadWindow(PandoraWindow window)
    {
        // Everything in the window in one go (asking for each element's name one at a time takes seconds).
        var cache = new CacheRequest { TreeScope = TreeScope.Element };
        cache.Add(AutomationElement.NameProperty);
        cache.Add(AutomationElement.ClassNameProperty);
        cache.Add(AutomationElement.ControlTypeProperty);
        cache.Add(ValuePattern.ValueProperty);
        cache.Add(TogglePattern.ToggleStateProperty);
        cache.Add(SelectionItemPattern.IsSelectedProperty);
        AutomationElement[] all;
        using (cache.Activate())
        {
            var found = AutomationElement.FromHandle(window.Handle).FindAll(TreeScope.Descendants, Condition.TrueCondition);
            all = new AutomationElement[found.Count];
            found.CopyTo(all, 0);
        }

        var items = all.Select(element => new Item(element, Text(element, AutomationElement.NameProperty), Text(element, AutomationElement.ClassNameProperty),
            element.GetCachedPropertyValue(AutomationElement.ControlTypeProperty) as ControlType)).ToList();
        bool IsButton(Item item) => item.Type == ControlType.Button || item.Type == ControlType.CheckBox;

        // The player along the bottom runs thumbs down, replay, play or pause, skip, thumbs up.
        var down = items.FindIndex(item => IsButton(item) && DownLabel.IsMatch(item.Name));
        var up = items.FindIndex(Math.Max(0, down), item => IsButton(item) && UpLabel.IsMatch(item.Name));
        if (up < 0)
        {
            up = items.FindIndex(item => IsButton(item) && UpLabel.IsMatch(item.Name));
        }

        var bar = down >= 0 && up > down ? items.GetRange(down, up - down + 1) : items;
        var playPause = bar.FirstOrDefault(item => IsButton(item) && PlayLabel.IsMatch(item.Name));
        if (playPause is null && down < 0 && up < 0)
        {
            return null;
        }

        var skip = bar.FirstOrDefault(item => IsButton(item) && SkipLabel.IsMatch(item.Name));
        var replay = bar.FirstOrDefault(item => IsButton(item) && ReplayLabel.IsMatch(item.Name));

        // Where it's up to and how long it is, beside the buttons: 1:18 then 4:15 (or what's left, -2:57).
        var end = Math.Max(up, down);
        var times = Times(items.Skip(Math.Max(0, end)));
        if (times.Count == 0)
        {
            times = Times(items);
        }

        var position = times.Count > 0 ? Seconds(times[0]) : TimeSpan.Zero;
        var length = times.Count > 1 ? times[1].StartsWith('-') ? position + Seconds(times[1]) : Seconds(times[1]) : TimeSpan.Zero;

        var (title, artist, album, link) = Song(items, down >= 0 ? down : items.Count);
        var upLit = up >= 0 ? IsLit(items[up]) : null;
        var downLit = down >= 0 ? IsLit(items[down]) : null;
        SongRating? lit = upLit == true ? SongRating.Up : downLit == true ? SongRating.Down : upLit == false && downLit == false ? SongRating.None : null;
        return new Screen(window.Handle, window.AudioProcess, title, artist, album, link, position, length, DateTime.UtcNow,
            playPause is not null && playPause.Name.TrimStart().StartsWith("pause", StringComparison.OrdinalIgnoreCase),
            playPause?.Element, skip?.Element, replay?.Element, up >= 0 ? items[up].Element : null, down >= 0 ? items[down].Element : null, lit);
    }

    /// <summary>
    /// The song's title, artist, album and page, from the song details before the player's buttons (which come at
    /// <paramref name="before"/>): by Pandora's names for them, or by the links to the song's, artist's and album's pages.
    /// </summary>
    private static (string? Title, string? Artist, string? Album, string? Link) Song(List<Item> items, int before)
    {
        var details = items.GetRange(0, Math.Min(before, items.Count));
        string? Named(params string[] names)
        {
            // The last one before the buttons: the song playing, not one listed further up the page.
            var at = details.FindLastIndex(item => names.Any(name => item.Class.Contains(name, StringComparison.OrdinalIgnoreCase)));
            if (at < 0)
            {
                return null;
            }

            // A box with the class has no name of its own; its text is the next thing in it that has one.
            return details.Skip(at).Take(4).Select(item => item.Name.Trim()).FirstOrDefault(name => name.Length > 0);
        }

        var links = details.Select((item, index) => (item, index, Kind: PageKind.Match(Text(item.Element, ValuePattern.ValueProperty))))
            .Where(each => each.Kind.Success && each.item.Name.Trim().Length > 0).ToList();
        var song = links.LastOrDefault(each => each.Kind.Groups[1].Value.Equals("TR", StringComparison.OrdinalIgnoreCase));
        string? Linked(string kind) => song.Kind is null
            ? null
            : links.FirstOrDefault(each => each.index > song.index && each.Kind.Groups[1].Value.Equals(kind, StringComparison.OrdinalIgnoreCase)).item?.Name.Trim();

        var title = Named("trackName", "TrackDetail__title", "TrackInfo__title") ?? song.item?.Name.Trim();
        var artist = Named("artistName", "TrackDetail__artist", "TrackInfo__artist") ?? Linked("AR");
        var album = Named("albumName", "TrackDetail__album", "TrackInfo__album") ?? Linked("AL");
        var link = song.Kind is not null && Uri.TryCreate(Text(song.item!.Element, ValuePattern.ValueProperty), UriKind.Absolute, out var page) ? page.AbsolutePath : null;
        return (title, artist, album, link);
    }

    // A toggle button says whether it's on; a page's button with aria-pressed shows up as one.
    private static bool? IsLit(Item button)
    {
        if (button.Element.GetCachedPropertyValue(TogglePattern.ToggleStateProperty, true) is ToggleState state)
        {
            return state == ToggleState.On;
        }

        if (button.Element.GetCachedPropertyValue(SelectionItemPattern.IsSelectedProperty, true) is bool selected)
        {
            return selected;
        }

        return button.Name.Contains("selected", StringComparison.OrdinalIgnoreCase) || button.Name.Contains("undo", StringComparison.OrdinalIgnoreCase) ? true : null;
    }

    private static bool Press(AutomationElement button)
    {
        try
        {
            if (button.TryGetCurrentPattern(InvokePattern.Pattern, out var invoke))
            {
                ((InvokePattern)invoke).Invoke();
                return true;
            }

            if (button.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
            {
                ((TogglePattern)toggle).Toggle();
                return true;
            }
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            // Gone, or not pressable just now.
        }

        return false;
    }

    private static string Text(AutomationElement element, AutomationProperty property)
    {
        try
        {
            return element.GetCachedPropertyValue(property, true) as string ?? string.Empty;
        }
        catch (InvalidOperationException)
        {
            return string.Empty;
        }
    }

    private static TimeSpan Seconds(string clock)
    {
        var seconds = 0;
        foreach (var part in clock.TrimStart('-').Split(':'))
        {
            seconds = seconds * 60 + int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture);
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static List<string> Times(IEnumerable<Item> items) =>
        items.Select(item => item.Name.Trim()).Where(name => Clock.IsMatch(name)).Take(2).ToList();

    private static Regex Label(string start) => new($@"^\s*{start}", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Writes what Lucent could see to errors.log, once, when Pandora's window can't be read: the apps telling Windows'
    /// media controls, and the windows that look like Pandora's. It's what's needed to make Lucent understand a new
    /// Pandora app.
    /// </summary>
    private void Report(RemoteState? media)
    {
        if (_reported)
        {
            return;
        }

        _reported = true;
        var windows = PandoraWindows();
        ErrorLog.Write("Pandora", $"Couldn't read Pandora. Media controls: {SourceAppId ?? "none for Pandora"}{(media is null ? string.Empty : ", no song")}. "
            + $"Pandora windows: {(windows.Count == 0 ? "none" : string.Join("; ", windows.Select(window => $"{window.Process} ({window.Class}, sound from {window.AudioProcess})")))}.");
    }

    // The Pandora app from the Microsoft Store is "PandoraMediaInc.29680B314EFC2_…!App"; installed from Pandora's site, it's Pandora too.
    private static bool IsPandoraApp(string? appId) =>
        appId is not null && appId.Contains("Pandora", StringComparison.OrdinalIgnoreCase) && !YouTubeRemote.IsBrowser(appId);

    /// <summary>
    /// Windows showing Pandora: the Pandora app's, and browser windows whose title (the tab in front's) says Pandora,
    /// as "Eclectic Chillout - Now Playing on Pandora". Read from Windows itself rather than UI Automation, which is
    /// slow and would ask Lucent's own windows too.
    /// </summary>
    private static List<PandoraWindow> PandoraWindows()
    {
        var found = new List<PandoraWindow>();
        var names = new Dictionary<uint, string?>();
        var lucent = (uint)Environment.ProcessId;
        string? NameOf(uint id)
        {
            if (!names.TryGetValue(id, out var name))
            {
                name = ProcessName(id);
                names[id] = name;
            }

            return name;
        }

        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var id);
            if (id == lucent || NameOf(id) is not { } process)
            {
                return true;
            }

            var inBrowser = YouTubeRemote.AllBrowsers.Contains(process, StringComparer.OrdinalIgnoreCase);
            var isApp = process.Contains("Pandora", StringComparison.OrdinalIgnoreCase);
            var className = ClassName(handle);
            // The app can also be a web app in a window of its own: Edge's (an installed site), a Store app's frame,
            // or a Chromium window of another name. Its title says Pandora either way.
            var webWindow = className.StartsWith("Chrome_WidgetWin", StringComparison.Ordinal) || className == "ApplicationFrameWindow";
            if (!isApp && !((inBrowser || webWindow) && Title(handle).Contains("Pandora", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // A Store app's frame belongs to Windows; its sound comes from the app inside.
            var audio = className == "ApplicationFrameWindow" && FrameContent(handle) is { } content && NameOf(content) is { } inside ? inside : process;
            found.Add(new PandoraWindow(handle, process, audio, className, InBrowser: inBrowser && !isApp));
            return true;
        }, IntPtr.Zero);
        return found;
    }

    // The process of the app inside a Store app's frame: the frame's child window that isn't the frame's own.
    private static uint? FrameContent(IntPtr frame)
    {
        GetWindowThreadProcessId(frame, out var host);
        uint? content = null;
        EnumChildWindows(frame, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var id);
            if (id != host)
            {
                content = id;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return content;
    }

    private static string? ProcessName(uint id)
    {
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName;
        }
        catch (Exception)
        {
            // Gone already.
            return null;
        }
    }

    private static string Title(IntPtr handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var text = new StringBuilder(length + 1);
        GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    private static string ClassName(IntPtr handle)
    {
        var text = new StringBuilder(256);
        return GetClassName(handle, text, text.Capacity) > 0 ? text.ToString() : string.Empty;
    }

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(IntPtr handle, StringBuilder text, int length);

    /// <summary>One thing in Pandora's window, as UI Automation read it.</summary>
    private sealed record Item(AutomationElement Element, string Name, string Class, ControlType? Type);

    /// <summary>What Pandora's window showed when read at <paramref name="ReadAt"/>, and its buttons.</summary>
    private sealed record Screen(IntPtr Window, string Process, string? Title, string? Artist, string? Album, string? Link,
        TimeSpan Position, TimeSpan Length, DateTime ReadAt, bool Playing, AutomationElement? PlayPause, AutomationElement? Skip,
        AutomationElement? Replay, AutomationElement? Up, AutomationElement? Down, SongRating? Lit);

    private sealed record PandoraWindow(IntPtr Handle, string Process, string AudioProcess, string Class, bool InBrowser);
}
