using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using Windows.Media.Control;

namespace WinampInspiredVisualizer.Streaming;

/// <summary>
/// Pandora: its Windows app, or pandora.com in a browser, through Windows' media controls, which Pandora tells
/// what's playing and takes play, pause and skip from. Windows' media controls have no thumbs, so Lucent finds
/// Pandora's thumbs up and down buttons on screen with UI Automation (what screen readers use) and presses them,
/// reading whether they're lit as it goes. No Pandora sign-in is needed; whoever is signed in to Pandora is who's
/// playing.
/// </summary>
/// <remarks>
/// Pandora has no public API. A browser only shows UI Automation the tab in front, so the thumbs show while the
/// Pandora tab is the one showing in its window (the app always has them). When Pandora doesn't say whether a thumb
/// is lit, the player shows the thumb given from Lucent. A station can't go back a song or be sought through, and
/// Pandora's free tier limits skips. The Mac has no Pandora app, so it controls the web player in its tab (see the
/// Mac's Streaming/PandoraRemote.cs).
/// </remarks>
public sealed class PandoraRemote : MediaControlsRemote, IRemoteRating
{
    // UI Automation is slow to ask (Pandora is a web page inside), so the thumbs are read this often, and straight
    // away when the song changes.
    private static readonly TimeSpan ThumbsReadEvery = TimeSpan.FromSeconds(3);

    // Pandora's labels for its thumbs ("Thumb Up", "Thumbs down", "Thumb up, selected").
    private static readonly Regex UpLabel = new(@"^\s*thumbs?[\s_-]*up", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex DownLabel = new(@"^\s*thumbs?[\s_-]*down", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private Thumbs? _thumbs;
    private DateTime _thumbsReadAt;
    private string? _thumbsFor;
    private SongRating? _lit;

    // The thumb last given from Lucent and the song it was given to, shown when Pandora doesn't say which is lit.
    private (string Song, SongRating Rating)? _given;

    public override string Name => "Pandora";

    protected override string NotOpen => "Open Pandora (its app, or pandora.com in your browser) and play a station";

    // The app, or the browser playing Pandora.
    public override IReadOnlyList<string> ProcessNames => SourceAppId is { } id && IsPandoraApp(id) ? ["Pandora"] : YouTubeRemote.BrowserProcessNames(SourceAppId);

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
        var state = await base.ReadAsync(cancel);
        if (state?.Track is not { } track)
        {
            return state;
        }

        if (track.Id != _thumbsFor || DateTime.UtcNow - _thumbsReadAt >= ThumbsReadEvery)
        {
            _thumbsFor = track.Id;
            _thumbsReadAt = DateTime.UtcNow;
            (_thumbs, _lit) = await Task.Run(ReadThumbs, cancel);
        }

        // No thumbs on screen (an ad, or another tab in front): the buttons hide until they're back.
        if (_thumbs is null)
        {
            return state;
        }

        var rating = _lit ?? (_given is { } given && given.Song == track.Id ? given.Rating : SongRating.None);
        return state with { Rating = rating };
    }

    public async Task RateAsync(SongRating rating, CancellationToken cancel)
    {
        var song = _thumbsFor;
        var given = _given is { } before && before.Song == song ? before.Rating : SongRating.None;
        await Task.Run(() => Rate(rating, given), cancel);
        if (song is not null)
        {
            _given = (song, rating);
        }

        // Read again with the next read, to see the thumb lit.
        _thumbsReadAt = default;
    }

    private void Rate(SongRating rating, SongRating given)
    {
        var (thumbs, lit) = ReadThumbs();
        if (thumbs is null)
        {
            throw new RemoteProblem("Lucent can't see Pandora's thumbs right now. Bring Pandora to the front (its tab, in a browser) and try again.");
        }

        var now = lit ?? given;
        // Pressing a lit thumb takes it back, as in Pandora.
        var press = rating switch
        {
            SongRating.Up when now != SongRating.Up => thumbs.Up,
            SongRating.Down when now != SongRating.Down => thumbs.Down,
            SongRating.None when now == SongRating.Up => thumbs.Up,
            SongRating.None when now == SongRating.Down => thumbs.Down,
            _ => null,
        };
        if (press is not null && !Press(press))
        {
            throw new RemoteProblem("Pandora didn't take that thumb just now.");
        }
    }

    /// <summary>Pandora's thumbs on screen, and which is lit (null when Pandora doesn't say), or null thumbs when there are none.</summary>
    private (Thumbs? Thumbs, SongRating? Lit) ReadThumbs()
    {
        try
        {
            // The buttons found last time, while they're still there; otherwise Pandora's windows are looked through again.
            var thumbs = _thumbs is { } known && IsAlive(known) ? known : FindThumbs();
            if (thumbs is null)
            {
                return (null, null);
            }

            var up = IsLit(thumbs.Up);
            var down = IsLit(thumbs.Down);
            SongRating? lit = up == true ? SongRating.Up : down == true ? SongRating.Down : up == false && down == false ? SongRating.None : null;
            return (thumbs, lit);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return (null, null);
        }
    }

    private static Thumbs? FindThumbs()
    {
        var buttons = new OrCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox));
        // The app's window first, then a browser's.
        foreach (var window in PandoraWindows().OrderBy(window => window.InBrowser))
        {
            AutomationElement? up = null, down = null;
            try
            {
                foreach (AutomationElement button in AutomationElement.FromHandle(window.Handle).FindAll(TreeScope.Descendants, buttons))
                {
                    var name = button.Current.Name ?? string.Empty;
                    if (up is null && UpLabel.IsMatch(name))
                    {
                        up = button;
                    }
                    else if (down is null && DownLabel.IsMatch(name))
                    {
                        down = button;
                    }
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException or ArgumentException)
            {
                // The window closed while it was looked through.
                continue;
            }

            if (up is not null && down is not null)
            {
                return new Thumbs(up, down);
            }
        }

        return null;
    }

    private static bool IsAlive(Thumbs thumbs)
    {
        try
        {
            return !thumbs.Up.Current.IsOffscreen && UpLabel.IsMatch(thumbs.Up.Current.Name ?? string.Empty) && DownLabel.IsMatch(thumbs.Down.Current.Name ?? string.Empty);
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    // A toggle button says whether it's on; a page's button with aria-pressed shows up as one.
    private static bool? IsLit(AutomationElement button)
    {
        if (button.TryGetCurrentPattern(TogglePattern.Pattern, out var toggle))
        {
            return ((TogglePattern)toggle).Current.ToggleState == ToggleState.On;
        }

        if (button.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var selection))
        {
            return ((SelectionItemPattern)selection).Current.IsSelected;
        }

        var name = button.Current.Name ?? string.Empty;
        return name.Contains("selected", StringComparison.OrdinalIgnoreCase) || name.Contains("undo", StringComparison.OrdinalIgnoreCase) ? true : null;
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

    // The Pandora app from the Microsoft Store is "PandoraMediaInc.29680B314EFC2_…!App"; installed from Pandora's site, it's Pandora too.
    private static bool IsPandoraApp(string? appId) =>
        appId is not null && appId.Contains("Pandora", StringComparison.OrdinalIgnoreCase) && !YouTubeRemote.IsBrowser(appId);

    /// <summary>
    /// Windows showing Pandora: the Pandora app's, and browser windows whose title (the tab in front's) says Pandora.
    /// Read from Windows itself rather than UI Automation, which is slow and would ask Lucent's own windows too.
    /// </summary>
    private static List<PandoraWindow> PandoraWindows()
    {
        var found = new List<PandoraWindow>();
        var names = new Dictionary<uint, string?>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle))
            {
                return true;
            }

            GetWindowThreadProcessId(handle, out var id);
            if (!names.TryGetValue(id, out var process))
            {
                process = ProcessName(id);
                names[id] = process;
            }

            if (process is null)
            {
                return true;
            }

            if (process.Equals("Pandora", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new PandoraWindow(handle, process, InBrowser: false));
            }
            else if (YouTubeRemote.AllBrowsers.Contains(process, StringComparer.OrdinalIgnoreCase) && Title(handle).Contains("Pandora", StringComparison.OrdinalIgnoreCase))
            {
                found.Add(new PandoraWindow(handle, process, InBrowser: true));
            }

            return true;
        }, IntPtr.Zero);
        return found;
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

    private delegate bool EnumWindowsProc(IntPtr handle, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr state);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr handle, StringBuilder text, int length);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr handle);

    /// <summary>Pandora's thumbs up and down buttons, as UI Automation found them.</summary>
    private sealed record Thumbs(AutomationElement Up, AutomationElement Down);

    private sealed record PandoraWindow(IntPtr Handle, string Process, bool InBrowser);
}
