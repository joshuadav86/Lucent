using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lucent.Layout;
using Lucent.Windows;

namespace Lucent;

/// <summary>
/// For CI: with LUCENT_LAYOUT_CHECK set to a file, Lucent (opening as a single window, as it does the first time)
/// opens the queue, library, and artwork beside the equalizer and visuals, switches single window off, and checks
/// each window opened just where its panel was: nothing overlapping, nothing outside where the single window was,
/// the player at its own size, and the equalizer no wider than its panel. Then it does it again, and again after
/// the windows were spread out and stretched on their own (as an older layout left them). It writes down where
/// every window was just after switching and a few times as they settled (the ones along the bottom ending flush
/// with the shortest column, so the windows make one clean block), then ok or MISMATCH for each check,
/// and PASSED or FAILED, and quits. On the way it picks every skin with the windows apart and in a single window
/// (each window and panel staying where it was), and takes the single window full screen and back before and
/// after. Last, it lays the panels out "Player on the left, queue beside it" filling the screen and switches single
/// window off from there. The Mac app checks the same in its own LayoutCheck.
/// </summary>
public partial class MainWindow
{
    /// <summary>True when Lucent was opened to run the layout check.</summary>
    internal static bool LayoutCheckIsAsked => Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CHECK") is { Length: > 0 };

    private void RunLayoutCheckIfAsked()
    {
        if (Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CHECK") is { Length: > 0 } file)
        {
            _ = RunLayoutCheckAsync(file);
        }
    }

    private async Task RunLayoutCheckAsync(string file)
    {
        var lines = new List<string>();
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            lines.Add($"screen {Describe(ScreenArea.Of(this))} at {VisualTreeHelper.GetDpi(this).DpiScaleX * 100:0}%");
            lines.Add($"{(InSingleWindow ? "ok" : "MISMATCH")} Lucent opened as a single window the first time");
            if (!InSingleWindow)
            {
                EnterSingleWindow();
                await Task.Delay(TimeSpan.FromSeconds(2));
            }

            foreach (var window in new ToolWindow[] { _queue, _libraryWindow, _visuals }.Where(window => !window.IsVisible))
            {
                ToggleWindow(window);
            }

            if (!ArtworkWindow.IsOpen)
            {
                ToggleArtwork();
            }

            if (EqualizerWindow.Current is not { IsVisible: true })
            {
                OpenEqualizer();
            }

            await Task.Delay(TimeSpan.FromSeconds(3));
            lines.AddRange(await CheckLeavingSingleWindowAsync("first time"));

            EnterSingleWindow();
            await Task.Delay(TimeSpan.FromSeconds(3));
            lines.AddRange(await CheckLeavingSingleWindowAsync("second time"));

            // Spread out and stretched as windows of their own: the visuals big, the equalizer as wide as it goes.
            var screen = ScreenArea.Of(this);
            Place(_visuals, screen.Left + screen.Width * 0.45, screen.Top + 20, screen.Width * 0.5, screen.Height * 0.8);
            Place(_libraryWindow, screen.Left + 10, screen.Top + screen.Height * 0.5, 420, screen.Height * 0.45);
            EqualizerWindow.Current?.FitTo(PlayerWidth * 2.2);
            await Task.Delay(TimeSpan.FromSeconds(2));
            EnterSingleWindow();
            await Task.Delay(TimeSpan.FromSeconds(3));
            lines.AddRange(await CheckLeavingSingleWindowAsync("after the windows were spread out"));

            // Every skin picked with the windows apart, then in a single window (where picking Linework once squeezed
            // the window, hiding panels and keeping it from filling the screen), then single window off after them.
            lines.AddRange(await CheckSkinsAsync("as separate windows"));
            EnterSingleWindow();
            await Task.Delay(TimeSpan.FromSeconds(3));
            lines.AddRange(await CheckFillAsync("before changing skins"));
            lines.AddRange(await CheckSkinsAsync("in a single window"));
            lines.AddRange(await CheckFillAsync("after changing skins"));
            lines.AddRange(await CheckLeavingSingleWindowAsync("after changing skins"));

            // With the analyzer out too, as people with every window have it, single window off while it fills the
            // screen (where on the Mac the queue, analyzer, and art once came back tucked under the windows above).
            if (!AnalyzerWindow.IsOpen)
            {
                ToggleAnalyzer();
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
            EnterSingleWindow();
            await Task.Delay(TimeSpan.FromSeconds(3));
            ToggleLucentFullScreen();
            await Task.Delay(TimeSpan.FromSeconds(2));
            lines.AddRange(await CheckLeavingSingleWindowAsync("while it fills the screen"));

            // Player on the left, queue beside it (one of Tidy layout's): the art under the player and the queue in a
            // column of its own right of it, filling the screen, then single window off, the windows ending flush there too.
            const string queueBeside = "player on the left, queue beside it";
            EnterSingleWindow();
            await Task.Delay(TimeSpan.FromSeconds(3));
            var style = UseSingleWindowStyle(TileStyle.QueueBeside);
            ToggleLucentFullScreen();
            await Task.Delay(TimeSpan.FromSeconds(2));
            lines.Add(QueueBesideLine(queueBeside, LaidOutPanels, _paneArea?.ActualWidth ?? 0, _paneArea?.ActualHeight ?? 0));
            lines.AddRange(await CheckLeavingSingleWindowAsync(queueBeside));
            _settings.SingleWindowStyle = style;
        }
        catch (Exception ex)
        {
            lines.Add($"MISMATCH the check failed: {ex}");
        }

        lines.Add(lines.Any(line => line.StartsWith("MISMATCH", StringComparison.Ordinal)) ? "FAILED" : "PASSED");
        await File.WriteAllLinesAsync(file, lines);
        Close();
    }

    /// <summary>
    /// With the panels laid out "Player on the left, queue beside it": the player in the top left corner, the queue in a
    /// column of its own along the player's right edge, and the art under the player. A body too small for that (the art
    /// doesn't fit under the player, or there's no room for the queue's column beside the visuals) is laid out as usual
    /// instead, so it's only a mismatch on a big one: a full HD screen at 100%.
    /// </summary>
    private static string QueueBesideLine(string label, IReadOnlyDictionary<string, TileRect> places, double width, double height)
    {
        var text = string.Join(", ", places.Select(entry => $"{entry.Key} {entry.Value.Left:0},{entry.Value.Top:0} {entry.Value.Width:0}x{entry.Value.Height:0}"));
        if (!places.TryGetValue("Player", out var player) || !places.TryGetValue("Queue", out var queue))
        {
            return $"MISMATCH {label}: the player or queue panel isn't laid out: {text}";
        }

        var beside = player.Left < 1 && player.Top < 1 && Math.Abs(queue.Left - player.Right) < 1 && queue.Top < 1
            && places.TryGetValue("Artwork", out var art) && art.Left < 1 && art.Top >= player.Bottom - 1;
        var room = width >= 1400 && height >= 900;
        return $"{(beside || !room ? "ok" : "MISMATCH")} {label}: in a body {width:0} x {height:0}, the queue is " +
            $"{(beside ? "beside the player, the art under it" : "laid out as usual")}: {text}";
    }

    /// <summary>Switches single window off and checks every window went where its panel was.</summary>
    private async Task<List<string>> CheckLeavingSingleWindowAsync(string label)
    {
        var lines = new List<string>();
        if (!InSingleWindow)
        {
            lines.Add($"MISMATCH {label}: not in a single window to switch off");
            return lines;
        }

        var frame = OnScreen(this);
        var places = PanelPlaces();
        lines.Add($"{label}: single window at {Describe(frame)}, panels: " +
            string.Join(", ", places.Select(entry => $"{NameFor(entry.Key)} {Describe(entry.Value)}")));
        // A single window hanging off the screen brings the windows onto it together: sideways by just what that takes,
        // and up or down by no more than the whole of it would need (the ones along the bottom can end higher).
        var screen = ScreenArea.Of(this);
        var block = places.Count == 0 ? frame : places.Values.Aggregate((all, place) => Rect.Union(all, place));
        var dx = OntoScreen(block.Left, block.Right, screen.Left, screen.Right);
        var dyMost = OntoScreen(block.Top, block.Bottom, screen.Top, screen.Bottom);
        LeaveSingleWindow();
        var windows = places.Keys.ToList();
        foreach (var wait in new[] { 0, 300, 1500, 4000 })
        {
            await Task.Delay(wait);
            lines.Add($"{label}: after {wait} ms: " + string.Join(", ", windows.Select(window => $"{NameFor(window)} {Describe(OnScreen(window))}")));
        }

        // Each window just where its panel was, except that the ones along the bottom can end higher (by as much as the
        // shortest column stopped short of the bottom), to end flush with it, and the ones above them make room.
        var room = places.Values.Where(place => !places.Values.Any(other => other != place && Touch(place.Bottom, other.Top) && Across(place, other)))
            .Select(place => frame.Bottom - place.Bottom).DefaultIfEmpty(0).Max();
        var dy = places.TryGetValue(this, out var playerPanel) ? OnScreen(this).Top - playerPanel.Top : 0;
        var moved = dy >= Math.Min(0, dyMost) - 4 && dy <= Math.Max(0, dyMost) + 4;
        lines.Add($"{(moved ? "ok" : "MISMATCH")} {label}: the windows moved {dx:0} across and {dy:0} down, the single window " +
            $"needed {dx:0} and up to {dyMost:0} to be on the screen, {Describe(screen)}");
        foreach (var (window, place) in places)
        {
            var now = OnScreen(window);
            var to = new Rect(place.Left + dx, place.Top + dy, place.Width, place.Height);
            // As wide as its panel, unless that's past the screen's edge once moved, where it gives up the room.
            var wide = window == this || window is EqualizerWindow ? to.Width : Math.Min(to.Width, Math.Max(window.MinWidth, screen.Right - to.Left));
            bool Between(double value, double was) => value <= was + 4 && value >= Math.Min(was, screen.Bottom) - room - 4;
            var near = Math.Abs(now.Left - to.Left) <= 4 && Math.Abs(now.Width - wide) <= 4
                && Between(now.Top, to.Top) && Between(now.Bottom, to.Bottom);
            lines.Add($"{(near ? "ok" : "MISMATCH")} {label}: the {NameFor(window)} is at {Describe(now)}, its panel was at {Describe(place)}");
        }

        lines.AddRange(CheckFlush(label, places.Keys.ToDictionary(window => window, OnScreen)));

        var player = OnScreen(this);
        var playerWidth = PlayerWidth * PlayerScale;
        lines.Add($"{(Math.Abs(player.Width - playerWidth) <= 2 ? "ok" : "MISMATCH")} {label}: the player is {player.Width:0} wide, its size makes it {playerWidth:0}");
        foreach (var window in windows)
        {
            var at = OnScreen(window);
            var inside = at.Left >= frame.Left + dx - 4 && at.Top >= frame.Top + dy - 4 && at.Right <= frame.Right + dx + 4 && at.Bottom <= frame.Bottom + dy + 4;
            if (!inside)
            {
                lines.Add($"MISMATCH {label}: the {NameFor(window)} at {Describe(at)} is outside where the single window was");
            }

            if (at.Left < screen.Left - 4 || at.Top < screen.Top - 4 || at.Right > screen.Right + 4 || at.Bottom > screen.Bottom + 4)
            {
                lines.Add($"MISMATCH {label}: the {NameFor(window)} at {Describe(at)} isn't wholly on the screen, {Describe(screen)}");
            }

            foreach (var other in windows.Where(other => windows.IndexOf(other) > windows.IndexOf(window)))
            {
                var overlap = Rect.Intersect(at, OnScreen(other));
                if (!overlap.IsEmpty && overlap.Width > 4 && overlap.Height > 4)
                {
                    lines.Add($"MISMATCH {label}: the {NameFor(window)} and the {NameFor(other)} overlap by {overlap.Width:0} x {overlap.Height:0}");
                }
            }
        }

        return lines;
    }

    /// <summary>Each of Lucent's own skins, ending on Lucent's, which a fresh start has.</summary>
    private static readonly string[] SkinsToCheck = { "Linework", "Verdes", "Glass", "Neon", "Studio", "Lucent" };

    /// <summary>
    /// Picks each skin in turn and checks nothing moved or shrank: every window (or in a single window, the window
    /// and every panel in it) where it was, give or take what a skin's type does to a height, all of them still
    /// shown, none over another, the player at its own width, and the equalizer just tall enough for its sliders in
    /// the skin's type. Back on Lucent's skin, everything's just where it started.
    /// </summary>
    private async Task<List<string>> CheckSkinsAsync(string label)
    {
        var lines = new List<string>();
        var single = InSingleWindow;
        var frame = OnScreen(this);
        var before = PlacesNow();
        lines.Add($"skins {label}: before, " + DescribeAll(before) + (single ? $", the single window at {Describe(frame)}" : string.Empty));
        foreach (var skin in SkinsToCheck)
        {
            ChooseSkin(skin);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            var now = PlacesNow();
            var name = $"skins {label}: {skin}";
            lines.Add($"{name}: " + DescribeAll(now) + (single ? $", the single window at {Describe(OnScreen(this))}" : string.Empty));
            if (single)
            {
                var at = OnScreen(this);
                lines.Add($"{(Near(at, frame, 4, 4) ? "ok" : "MISMATCH")} {name}: the single window is at {Describe(at)}, was at {Describe(frame)}");
            }

            // A skin's type can make the player a little taller or shorter, and what's under it moves with it.
            var heights = skin == "Lucent" ? 4 : 30;
            foreach (var (window, was) in before)
            {
                if (!now.TryGetValue(window, out var place))
                {
                    lines.Add($"MISMATCH {name}: the {NameFor(window)} {(single ? "panel" : "window")} isn't shown any more");
                    continue;
                }

                if (!Near(place, was, 4, heights))
                {
                    lines.Add($"MISMATCH {name}: the {NameFor(window)} is at {Describe(place)}, was at {Describe(was)}");
                }
            }

            var playerWidth = PlayerWidth * PlayerScale;
            var player = now.TryGetValue(this, out var p) ? p.Width : 0;
            lines.Add($"{(Math.Abs(player - playerWidth) <= 2 ? "ok" : "MISMATCH")} {name}: the player is {player:0} wide, its size makes it {playerWidth:0}");
            var windows = now.Keys.ToList();
            foreach (var window in windows)
            {
                if (single && !Near(Rect.Intersect(now[window], frame), now[window], 4, 4))
                {
                    lines.Add($"MISMATCH {name}: the {NameFor(window)} at {Describe(now[window])} is outside the single window");
                }

                foreach (var other in windows.Skip(windows.IndexOf(window) + 1))
                {
                    var overlap = Rect.Intersect(now[window], now[other]);
                    if (!overlap.IsEmpty && overlap.Width > 4 && overlap.Height > 4)
                    {
                        lines.Add($"MISMATCH {name}: the {NameFor(window)} and the {NameFor(other)} overlap by {overlap.Width:0} x {overlap.Height:0}");
                    }
                }
            }

            if (!single)
            {
                lines.AddRange(CheckFlush(name, now));
            }

            if (EqualizerWindow.Current is { IsVisible: true } equalizer)
            {
                var needs = equalizer.HeightNeeded;
                lines.Add($"{(Math.Abs(needs - equalizer.Height) <= 2 ? "ok" : "MISMATCH")} {name}: the equalizer is {equalizer.Height:0} tall, its sliders need {needs:0}");
            }
        }

        return lines;
    }

    /// <summary>
    /// Full screen (Shift+F11) in a single window: the window fills its screen, every panel still in it, then goes
    /// back to just where it was.
    /// </summary>
    private async Task<List<string>> CheckFillAsync(string label)
    {
        var lines = new List<string>();
        if (!InSingleWindow)
        {
            lines.Add($"MISMATCH full screen {label}: not in a single window");
            return lines;
        }

        var before = OnScreen(this);
        var panels = PanelPlaces().Count;
        var screen = ScreenArea.Of(this);
        ToggleLucentFullScreen();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var filled = OnScreen(this);
        lines.Add($"{(Near(filled, screen, 4, 4) ? "ok" : "MISMATCH")} full screen {label}: the single window is at {Describe(filled)}, the screen at {Describe(screen)}");
        var shown = PanelPlaces().Count;
        lines.Add($"{(shown == panels ? "ok" : "MISMATCH")} full screen {label}: {shown} panels shown, {panels} before");
        ToggleLucentFullScreen();
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var back = OnScreen(this);
        lines.Add($"{(Near(back, before, 4, 4) ? "ok" : "MISMATCH")} full screen {label} off: the single window is at {Describe(back)}, was at {Describe(before)}");
        return lines;
    }

    /// <summary>
    /// The windows end flush, as one block with nothing hanging past the rest: each window's outer edges (the ones no
    /// other window is against) are on the block's edges, and together they fill it. With nothing under the player,
    /// the windows beside it are taller than it can be, so that isn't checked.
    /// </summary>
    private List<string> CheckFlush(string label, Dictionary<Window, Rect> at)
    {
        var lines = new List<string>();
        var shown = at.Where(entry => !entry.Value.IsEmpty).ToList();
        if (!at.TryGetValue(this, out var player)
            || !shown.Any(entry => entry.Key != this && Touch(player.Bottom, entry.Value.Top) && Across(player, entry.Value)))
        {
            lines.Add($"{label}: nothing's under the player, so the windows beside it can't end flush with it");
            return lines;
        }

        var block = shown.Select(entry => entry.Value).Aggregate((a, b) => Rect.Union(a, b));

        // The player's column (the player and what's stacked under it) keeps its own size. When a stack beside it can't
        // get as short as it, every window in it at its smallest (a small screen with every window out), the stacks end
        // level with that one below it, and it's let off stopping short of them.
        var column = new List<Window> { this };
        while (shown.FirstOrDefault(entry => !column.Contains(entry.Key) && Touch(entry.Value.Top, at[column[^1]].Bottom)
                   && entry.Value.Left > player.Left - 4 && entry.Value.Right < player.Right + 4).Key is { } under)
        {
            column.Add(under);
        }

        bool AtSmallest(Window window)
        {
            for (Window? current = window; current is not null;)
            {
                if (at[current].Height > current.MinHeight + 2)
                {
                    return false;
                }

                var below = current;
                current = shown.FirstOrDefault(entry => entry.Key != below && !column.Contains(entry.Key) && Touch(entry.Value.Bottom, at[below].Top)
                    && Math.Abs(entry.Value.Left - at[below].Left) <= 4 && Math.Abs(entry.Value.Width - at[below].Width) <= 4).Key;
            }

            return true;
        }

        var lowest = shown.Where(entry => !column.Contains(entry.Key) && Touch(entry.Value.Bottom, block.Bottom)).Select(entry => entry.Key).ToList();
        var columnShort = at[column[^1]].Bottom < block.Bottom - 4 && lowest.Any(AtSmallest);
        if (columnShort)
        {
            lines.Add($"ok {label}: the windows beside the player's column are as short as they go, so they end level " +
                $"{block.Bottom - at[column[^1]].Bottom:0} below it");
        }

        foreach (var (window, place) in shown)
        {
            var others = shown.Where(entry => entry.Key != window).Select(entry => entry.Value).ToList();
            var off = new List<string>();
            if (!others.Any(other => Touch(place.Bottom, other.Top) && Across(place, other)) && Math.Abs(place.Bottom - block.Bottom) > 4
                && !(columnShort && column.Contains(window)))
            {
                off.Add($"its bottom is at {place.Bottom:0}, the others' at {block.Bottom:0}");
            }

            if (!others.Any(other => Touch(place.Top, other.Bottom) && Across(place, other)) && Math.Abs(place.Top - block.Top) > 4)
            {
                off.Add($"its top is at {place.Top:0}, the others' at {block.Top:0}");
            }

            if (!others.Any(other => Touch(place.Left, other.Right) && Down(place, other)) && Math.Abs(place.Left - block.Left) > 4)
            {
                off.Add($"its left is at {place.Left:0}, the others' at {block.Left:0}");
            }

            if (!others.Any(other => Touch(place.Right, other.Left) && Down(place, other)) && Math.Abs(place.Right - block.Right) > 4)
            {
                off.Add($"its right is at {place.Right:0}, the others' at {block.Right:0}");
            }

            if (off.Count > 0)
            {
                lines.Add($"MISMATCH {label}: the {NameFor(window)} isn't flush with the other windows: {string.Join(", ", off)}");
            }
        }

        var filled = shown.Sum(entry => entry.Value.Width * entry.Value.Height) / (block.Width * block.Height);
        lines.Add($"{(filled >= 0.985 ? "ok" : "MISMATCH")} {label}: the windows end flush, filling {filled:P1} of the block they make at {Describe(block)}");
        return lines;
    }

    /// <summary>Two edges together, give or take a few pixels.</summary>
    private static bool Touch(double edge, double other) => Math.Abs(edge - other) <= 4;

    /// <summary>Two places side by side across more than a few pixels (one over the other).</summary>
    private static bool Across(Rect a, Rect b) => Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left) > 4;

    /// <summary>Two places alongside each other down more than a few pixels.</summary>
    private static bool Down(Rect a, Rect b) => Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top) > 4;

    /// <summary>Where every window is: in a single window, each panel (the player's among them); otherwise each window that's shown.</summary>
    private Dictionary<Window, Rect> PlacesNow() => InSingleWindow
        ? PanelPlaces()
        : TiledWindows().Select(entry => entry.Window).ToDictionary(window => window, OnScreen);

    private static string DescribeAll(Dictionary<Window, Rect> places) =>
        string.Join(", ", places.Select(entry => $"{NameFor(entry.Key)} {Describe(entry.Value)}"));

    /// <summary>Two places within <paramref name="across"/> in left and width, and <paramref name="down"/> in top and height.</summary>
    private static bool Near(Rect a, Rect b, double across, double down) =>
        !a.IsEmpty && !b.IsEmpty && Math.Abs(a.Left - b.Left) <= across && Math.Abs(a.Width - b.Width) <= across
        && Math.Abs(a.Top - b.Top) <= down && Math.Abs(a.Height - b.Height) <= down;

    private static string NameFor(Window window) => window switch
    {
        MainWindow => "player",
        EqualizerWindow => "equalizer",
        ArtworkWindow => "artwork",
        _ => window.Title.Replace("Lucent ", string.Empty, StringComparison.Ordinal),
    };

    /// <summary>How far a span moves to be on the screen: to its near edge if it's too long for it, else just enough.</summary>
    private static double OntoScreen(double start, double end, double low, double high) =>
        end - start > high - low ? low - start : Math.Clamp(0, low - start, high - end);

    private static string Describe(Rect rect) => $"{rect.Left:0},{rect.Top:0} {rect.Width:0} x {rect.Height:0}";

    /// <summary>Where Windows has the window on the screen, in pixels at 100% scaling; empty while it's hidden.</summary>
    private static Rect OnScreen(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero || !IsWindowVisible(handle) || !GetWindowRect(handle, out var rect))
        {
            return Rect.Empty;
        }

        var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
        return new Rect(rect.Left / scale, rect.Top / scale, (rect.Right - rect.Left) / scale, (rect.Bottom - rect.Top) / scale);
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "IsWindowVisible")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);
}
