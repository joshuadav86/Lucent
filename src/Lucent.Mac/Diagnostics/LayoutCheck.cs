using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Layout;
using Lucent.Mac.Interop;
using Lucent.Mac.Visuals;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

/// <summary>
/// For the launch check in CI: with LUCENT_LAYOUT_CHECK set to a file, resizes the visuals window a
/// few times and writes down after each whether the Metal view and layer filled the space the layout
/// gave them. A mismatch shows as the visuals squashed into part of the window. Then it minimizes
/// the player and checks the windows around it went to the Dock too, and came back with it, and quits
/// the way people do. On the way it checks the app menu has Lucent's items, Quit among them. Run again on the same settings with LUCENT_LAYOUT_CHECK_REOPENED=1, it first
/// checks the windows came back as they were left: the visuals open, the equalizer under the player. Before minimizing it
/// switches single window on and off (CheckSingleWindowAsync), picking every skin and going full screen and back while
/// it's on, and every skin again once it's off, the windows ending flush as one block each time they're apart, and
/// switches it off while it fills the screen with every window out, laid out as it was and then "Player on the left,
/// queue beside it".
/// </summary>
internal static class LayoutCheck
{
    private static readonly (double Width, double Height)[] Sizes =
    {
        // The last one fits on the runners' small screens, where the reopened run checks it came back
        // the same size.
        (820, 560), (700, 900), (1000, 420), (500, 1000), (860, 480),
    };

    /// <summary>True when Lucent was opened to run this check.</summary>
    public static bool IsAsked => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CHECK"));

    public static void RunIfAsked(Window main, Window visuals)
    {
        var file = Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CHECK");
        if (string.IsNullOrEmpty(file))
        {
            return;
        }

        _ = RunAsync(main, visuals, file);
    }

    private static async Task RunAsync(Window main, Window visuals, string file)
    {
        var lines = new List<string>();
        var failed = false;
        try
        {
            if (Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CHECK_REOPENED") == "1")
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                lines.AddRange(CheckReopened(main, visuals, File.Exists(file + ".visuals") ? File.ReadAllText(file + ".visuals") : null));
            }

            if (!visuals.IsVisible)
            {
                visuals.Show(main);
            }

            var (matched, line) = await SettleAsync(visuals, "as opened");
            failed |= !matched;
            lines.Add(line);
            foreach (var (width, height) in Sizes)
            {
                visuals.Width = width;
                visuals.Height = height;
                (matched, line) = await SettleAsync(visuals, $"window {width} x {height}");
                failed |= !matched;
                lines.Add(line);
            }

            lines.AddRange(await CheckSingleWindowAsync(main, visuals));

            // Off the player's top edge, so it isn't docked beside it: docked there, the visuals are
            // stretched to the bottom of the column under the player when Lucent reopens, which the
            // reopened run would count as moved.
            visuals.SetTop(visuals.Top() + 40);
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            lines.AddRange(CheckAppMenu());
            lines.AddRange(await CheckMinimizeAsync(main));

            // Where the visuals and equalizer were left, for the run after this one to find them there again.
            await File.WriteAllTextAsync(file + ".visuals",
                Describe(visuals) + "\n" + (EqualizerWindow.Current is { IsVisible: true } equalizer ? Describe(equalizer) : string.Empty));
            failed |= lines.Any(entry => entry.StartsWith("MISMATCH", StringComparison.Ordinal));
        }
        catch (Exception ex)
        {
            failed = true;
            lines.Add($"MISMATCH the check failed: {ex}");
        }

        lines.Add(failed ? "FAILED" : "PASSED");
        await File.WriteAllLinesAsync(file, lines);
        Dispatcher.UIThread.Post(main.Close);
    }

    private static List<string> CheckReopened(Window main, Window visuals, string? leftAt)
    {
        var lines = new List<string>
        {
            $"{(visuals.IsVisible ? "ok" : "MISMATCH")} reopened: the visuals {(visuals.IsVisible ? "are open" : "were left closed")}",
        };
        var equalizer = EqualizerWindow.Current is { IsVisible: true } open ? open : null;
        if (equalizer is null)
        {
            lines.Add("MISMATCH reopened: the equalizer was left closed");
        }

        // Each window where it was left, give or take a few points (a window's frame can round differently).
        var left = (leftAt ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var (name, window, index) in new[] { ("visuals", (Window?)visuals, 0), ("equalizer", equalizer, 1) })
        {
            if (window is null)
            {
                continue;
            }

            var then = index < left.Length ? left[index] : null;
            var now = Describe(window);
            lines.Add($"{(Near(now, then) ? "ok" : "MISMATCH")} reopened: the {name} is at {now}, left at {then ?? "(not written down)"}, " +
                $"the player at {Describe(main)}");
        }

        return lines;
    }

    /// <summary>Two descriptions within 4 points of each other in every number.</summary>
    private static bool Near(string now, string? then)
    {
        if (then is null)
        {
            return false;
        }

        static double[] Numbers(string text) => text.Split(new[] { ',', ' ', 'x' }, StringSplitOptions.RemoveEmptyEntries).Select(double.Parse).ToArray();
        var (a, b) = (Numbers(now), Numbers(then));
        return a.Length == b.Length && a.Zip(b).All(pair => Math.Abs(pair.First - pair.Second) <= 4);
    }

    private static string Describe(Window window) =>
        $"{window.Left():0},{window.Top():0} {window.ActualWidth():0} x {window.ActualHeight():0}";

    /// <summary>The menu under Lucent's name in the menu bar, as macOS shows it: Lucent's own items, with Quit.</summary>
    private static List<string> CheckAppMenu()
    {
        var titles = new List<string>();
        var bar = ObjC.Send(ObjC.Send(ObjC.Class("NSApplication"), "sharedApplication"), "mainMenu");
        var appMenu = bar == IntPtr.Zero ? IntPtr.Zero : ObjC.Send(ObjC.Send(bar, "itemAtIndex:", IntPtr.Zero), "submenu");
        if (appMenu != IntPtr.Zero)
        {
            var count = (long)ObjC.Send(appMenu, "numberOfItems");
            for (var i = 0L; i < count; i++)
            {
                var item = ObjC.Send(appMenu, "itemAtIndex:", (IntPtr)i);
                if (!ObjC.SendBool(item, "isSeparatorItem"))
                {
                    titles.Add(Marshal.PtrToStringUTF8(ObjC.Send(ObjC.Send(item, "title"), "UTF8String")) ?? string.Empty);
                }
            }
        }

        var shown = string.Join(", ", titles);
        var right = titles.Contains("Quit Lucent") && titles.Contains("About Lucent")
            && !titles.Any(title => title.Contains("Avalonia", StringComparison.OrdinalIgnoreCase));
        return new List<string> { right ? $"app menu: {shown}" : $"MISMATCH app menu should have About Lucent and Quit Lucent, has: {shown}" };
    }

    /// <summary>
    /// Single window on: the visuals become a panel in the player's window, their own window out of sight but
    /// still open, and the Metal view (moved from one window to the other) still fills its space as the
    /// window's resized. Then off again: the visuals are a window of their own again, just where their panel was.
    /// </summary>
    private static async Task<List<string>> CheckSingleWindowAsync(Window main, Window visuals)
    {
        var lines = new List<string>();
        // Starting wholly on the screen: macOS pulls a window that hangs off the edge back on as it's shown again,
        // which on a small screen (the Apple silicon runners' is 1024 x 768) would read as coming back in the wrong place.
        var work = Placement.WorkArea(visuals);
        var size = Placement.SizeOf(visuals);
        var left = Math.Max(work.Left, Math.Min(visuals.Left(), work.Right - size.Width));
        var top = Math.Max(work.Top, Math.Min(visuals.Top(), work.Bottom - size.Height));
        if (Math.Abs(left - visuals.Left()) > 0.5 || Math.Abs(top - visuals.Top()) > 0.5)
        {
            visuals.MoveTo(left, top);
            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        var before = Describe(visuals);
        var playerWidth = main.ActualWidth();
        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var inMain = main.GetVisualDescendants().OfType<MetalView>().Any();
        lines.Add($"{(SingleWindow.IsOn() && SingleWindow.Holds(visuals) && inMain ? "ok" : "MISMATCH")} single window on: " +
            $"the visuals {(inMain ? "are a panel in the player's window" : "aren't in the player's window")}");
        lines.Add($"{(visuals.IsVisible && !IsOnScreen(visuals) ? "ok" : "MISMATCH")} single window on: the visuals' own window is " +
            $"{(IsOnScreen(visuals) ? "still on the screen" : "out of sight")}, and {(visuals.IsVisible ? "still open" : "closed")}");
        foreach (var (width, height) in new[] { (1400.0, 820.0), (1100.0, 700.0) })
        {
            main.Width = width;
            main.Height = height;
            // The Metal view's now in the player's window, so that's where it's looked for.
            lines.Add((await SettleAsync(main, $"single window {width} x {height}")).Line);
        }

        // Every skin picked in the single window (where picking Linework once squeezed it, hiding panels and keeping it
        // from filling the screen), and full screen and back before and after.
        if (main is MainWindow lucent)
        {
            lines.AddRange(await CheckFillAsync(lucent, "before changing skins"));
            lines.AddRange(await CheckSkinsAsync(lucent, visuals, "in a single window"));
            lines.AddRange(await CheckFillAsync(lucent, "after changing skins"));
        }

        // Where the visuals' panel is on the screen as it ends: their window goes just there. A panel along the top
        // reaches up over the title bar, where the window's own goes, so its top is anywhere from there to the window's top.
        var panel = PanelOnScreen(main, visuals);
        var equalizer = EqualizerWindow.Current is { IsVisible: true } open ? open : null;
        var equalizerPanel = equalizer is null ? null : PanelOnScreen(main, equalizer);
        // How far short of the single window's bottom the player's column (the player and equalizer, which keep their
        // own sizes) stops: the windows beside it end that much higher as windows, flush with it.
        var room = equalizerPanel is { } under ? Math.Max(0, Placement.BoundsOf(main).Bottom - under.Bottom) : 0;
        // A single window hanging off the screen (on the Apple silicon runners' 1024-wide one, it does) brings the windows
        // onto it together: sideways by just what that takes, and up or down by no more than the whole of it would need.
        var screen = Placement.WorkArea(main);
        var frame = Placement.BoundsOf(main);
        var (blockLeft, blockRight) = (frame.X, new[] { panel, equalizerPanel }.Max(r => r?.Right ?? frame.X));
        var blockBottom = new[] { panel, equalizerPanel }.Max(r => r?.Bottom ?? frame.Y);
        var dx = OntoScreen(blockLeft, blockRight, screen.X, screen.Right);
        var dyMost = OntoScreen(frame.Y, blockBottom, screen.Y, screen.Bottom);
        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var back = !SingleWindow.IsOn() && visuals.GetVisualDescendants().OfType<MetalView>().Any() && IsOnScreen(visuals);
        lines.Add($"{(back ? "ok" : "MISMATCH")} single window off: the visuals {(back ? "are a window again" : "didn't come back as a window")}");
        lines.Add((await SettleAsync(visuals, "single window off")).Line);
        var dy = main.Top() - frame.Y;
        var moved = Math.Abs(main.Left() - frame.X - dx) <= 4 && dy >= Math.Min(0, dyMost) - 4 && dy <= Math.Max(0, dyMost) + 4;
        lines.Add($"{(moved ? "ok" : "MISMATCH")} single window off: the player is at {Describe(main)}, the single window was at " +
            $"{Describe(frame)} on a screen of {Describe(screen)}, so it moves {dx:0} across and up to {dyMost:0} down");
        var (x, y, w, h) = (visuals.Left(), visuals.Top(), visuals.ActualWidth(), visuals.ActualHeight());
        // As wide as its panel, unless that's past the screen's edge once moved, where it gives up the room.
        var wide = panel is { } o ? Math.Min(o.Width, Math.Max(visuals.MinWidth, screen.Right - (o.X + dx))) : 0;
        var where = panel is { } p
            && Math.Abs(x - (p.X + dx)) <= 4 && Math.Abs(w - wide) <= 4
            && y + h <= p.Bottom + dy + 4 && y + h >= Math.Min(p.Bottom + dy, screen.Bottom) - room - 4
            && y >= main.Top() - 4 && y <= p.Y + dy + 4;
        var panelText = panel is { } r ? $"{r.X:0},{r.Y:0} {r.Width:0} x {r.Height:0}" : "(not found)";
        lines.Add($"{(where ? "ok" : "MISMATCH")} single window off: the visuals are at {Describe(visuals)}, their panel was at {panelText}");

        // The player back at its own width (not the single window's), the equalizer just where its panel was and
        // as wide (not stretched to a wide player), and nothing over anything else.
        var playerNow = main.ActualWidth();
        lines.Add($"{(Math.Abs(playerNow - playerWidth) <= 4 ? "ok" : "MISMATCH")} single window off: the player is {playerNow:0} wide, was {playerWidth:0}");
        if (equalizer is not null)
        {
            var eq = new Rect(equalizer.Left(), equalizer.Top(), equalizer.ActualWidth(), equalizer.ActualHeight());
            var fits = equalizerPanel is { } e && Math.Abs(eq.X - (e.X + dx)) <= 4 && Math.Abs(eq.Y - (e.Y + dy)) <= 4 && Math.Abs(eq.Width - e.Width) <= 4;
            var equalizerText = equalizerPanel is { } q ? $"{q.X:0},{q.Y:0} {q.Width:0} x {q.Height:0}" : "(not found)";
            lines.Add($"{(fits ? "ok" : "MISMATCH")} single window off: the equalizer is at {Describe(equalizer)}, its panel was at {equalizerText}");
        }

        var shown = new[] { ("player", (Window?)main), ("visuals", visuals), ("equalizer", equalizer) }
            .Where(entry => entry.Item2 is not null)
            .Select(entry => (Name: entry.Item1, At: new Rect(entry.Item2!.Left(), entry.Item2.Top(), entry.Item2.ActualWidth(), entry.Item2.ActualHeight())))
            .ToList();
        foreach (var (name, at) in shown.Where(entry => entry.At.X < screen.X - 4 || entry.At.Y < screen.Y - 4
            || entry.At.Right > screen.Right + 4 || entry.At.Bottom > screen.Bottom + 4))
        {
            lines.Add($"MISMATCH single window off: the {name} at {Describe(at)} isn't wholly on the screen, {Describe(screen)}");
        }

        for (var i = 0; i < shown.Count; i++)
        {
            for (var j = i + 1; j < shown.Count; j++)
            {
                var overlap = shown[i].At.Intersect(shown[j].At);
                if (overlap.Width > 4 && overlap.Height > 4)
                {
                    lines.Add($"MISMATCH single window off: the {shown[i].Name} and the {shown[j].Name} overlap by {overlap.Width:0} x {overlap.Height:0}");
                }
            }
        }

        lines.AddRange(CheckFlush(main, "single window off"));

        // Then every skin again with the windows apart.
        if (main is MainWindow apart)
        {
            lines.AddRange(await CheckSkinsAsync(apart, visuals, "as separate windows"));
        }

        // Then with every window out, as people have them, single window off while it fills the screen.
        if (main is MainWindow every)
        {
            lines.AddRange(await CheckLeavingFilledAsync(every));

            // And again laid out "Player on the left, queue beside it" (one of Tidy layout's): the art under the player
            // and the queue in a column of its own right of it.
            lines.AddRange(await CheckLeavingFilledAsync(every, TileStyle.QueueBeside));
        }

        // Then with nothing beside the player (just the queue, with or without the equalizer): the player and the
        // equalizer stretch across the single window, and as windows again they all end flush at the player's width.
        if (main is MainWindow only)
        {
            lines.AddRange(await CheckStretchedAsync(only, equalizer: true));
            lines.AddRange(await CheckStretchedAsync(only, equalizer: false));
        }

        return lines;
    }

    /// <summary>
    /// With every window out (the player, equalizer, queue, visuals, analyzer, library, and artwork), a single window
    /// full screen, then single window off while it fills the screen, where the queue, analyzer, and art once came back
    /// tucked under the windows above them. Each window goes where its panel was (the ones along the bottom can end
    /// higher, flush with the player's column), the windows end flush as one block, nothing's over anything else, and
    /// everything's on the screen. Then the windows it opened close again, for the rest of the check. With
    /// <paramref name="style"/>, the panels are laid out that way first (as Tidy layout would), and put back after.
    /// </summary>
    private static async Task<List<string>> CheckLeavingFilledAsync(MainWindow main, TileStyle? style = null)
    {
        var label = "single window off while full screen" + (style == TileStyle.QueueBeside ? ", player on the left, queue beside it" : string.Empty);
        var lines = new List<string>();
        var closeAgain = main.OpenEveryWindow();
        await Task.Delay(TimeSpan.FromSeconds(2));
        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var styleWas = style is { } use ? main.UseSingleWindowStyle(use) : (TileStyle?)null;
        main.ToggleLucentFullScreen();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var frame = Placement.BoundsOf(main);
        var screen = Placement.WorkArea(main);
        if (style == TileStyle.QueueBeside)
        {
            var body = main.LaidOutPanels.Values.ToList();
            lines.Add(QueueBesideLine(label, main.LaidOutPanels,
                body.Count == 0 ? 0 : body.Max(place => place.Right), body.Count == 0 ? 0 : body.Max(place => place.Bottom)));
        }

        var panels = main.OwnedWindows.Select(window => (Window: window, Panel: PanelOnScreen(main, window)))
            .Where(entry => entry.Panel is not null)
            .ToDictionary(entry => entry.Window, entry => entry.Panel!.Value);
        lines.Add($"{label}: the single window at {Describe(frame)} on a screen of {Describe(screen)}, panels: " +
            string.Join(", ", panels.Select(entry => $"{entry.Key.Title} {Describe(entry.Value)}")));
        SingleWindow.Toggle?.Invoke();
        foreach (var wait in new[] { 0, 300, 1500, 4000 })
        {
            await Task.Delay(wait);
            lines.Add($"{label}: after {wait} ms: player {Describe(main)}, " +
                string.Join(", ", panels.Keys.Select(window => $"{window.Title} {Describe(window)}")));
        }

        // How far short of the single window's bottom a column stops: the windows along the bottom can end that much
        // higher, flush with it. A panel along the top reaches up over the title bar, where the window's own goes.
        var room = panels.Values.Where(place => !panels.Values.Any(other => other != place && Touch(place.Bottom, other.Y) && Across(place, other)))
            .Select(place => frame.Bottom - place.Bottom).DefaultIfEmpty(0).Max();
        foreach (var (window, panel) in panels)
        {
            var at = Placement.BoundsOf(window);
            var highest = panel.Y - frame.Y < 60 ? frame.Y : panel.Y - room;
            var near = Math.Abs(at.X - panel.X) <= 4 && Math.Abs(at.Width - panel.Width) <= 4
                && at.Y >= highest - 4 && at.Y <= panel.Y + 4 && at.Bottom <= panel.Bottom + 4 && at.Bottom >= panel.Bottom - room - 4;
            lines.Add($"{(near ? "ok" : "MISMATCH")} {label}: the {window.Title} is at {Describe(at)}, its panel was at {Describe(panel)}");
        }

        var shown = new[] { (Name: "player", At: Placement.BoundsOf(main)) }
            .Concat(panels.Keys.Where(IsOnScreen).Select(window => (Name: window.Title ?? "window", At: Placement.BoundsOf(window))))
            .ToList();
        foreach (var (name, at) in shown.Where(entry => entry.At.X < screen.X - 4 || entry.At.Y < screen.Y - 4
            || entry.At.Right > screen.Right + 4 || entry.At.Bottom > screen.Bottom + 4))
        {
            lines.Add($"MISMATCH {label}: the {name} at {Describe(at)} isn't wholly on the screen, {Describe(screen)}");
        }

        for (var i = 0; i < shown.Count; i++)
        {
            for (var j = i + 1; j < shown.Count; j++)
            {
                var overlap = shown[i].At.Intersect(shown[j].At);
                if (overlap.Width > 4 && overlap.Height > 4)
                {
                    lines.Add($"MISMATCH {label}: the {shown[i].Name} and the {shown[j].Name} overlap by {overlap.Width:0} x {overlap.Height:0}");
                }
            }
        }

        // Only the windows that had a panel: one the single window left out (no room for it on a small screen) stays
        // where it was, behind the others, as the checks above leave it out too.
        lines.AddRange(CheckFlush(main, label, panels.Keys));
        closeAgain();
        if (styleWas is { } was)
        {
            main.UseSingleWindowStyle(was);
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
        return lines;
    }

    /// <summary>
    /// With the panels laid out "Player on the left, queue beside it": the player in the top left corner, the queue in a
    /// column of its own along the player's right edge, and the art under the player. A body too small for that (the art
    /// doesn't fit under the player, or there's no room for the queue's column beside the visuals) is laid out as usual
    /// instead, so it's only a mismatch on a big one: a full HD screen.
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

    /// <summary>
    /// Just the queue (and the equalizer) open with the player, in a single window wider than the player: no empty block
    /// beside the player or the equalizer (with the queue under them, they run the whole width; on a short screen the
    /// queue goes beside them instead); then single window off, where
    /// the player's back at its own width and the queue and equalizer end flush with it. Then the windows it closed open
    /// again, for the rest of the check.
    /// </summary>
    private static async Task<List<string>> CheckStretchedAsync(MainWindow main, bool equalizer)
    {
        var label = equalizer ? "with just the equalizer and queue" : "with just the queue";
        var lines = new List<string>();
        if (SingleWindow.IsOn())
        {
            SingleWindow.Toggle?.Invoke();
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        var playerWidth = main.ActualWidth();
        var putBack = main.OpenOnlyQueue(equalizer);
        await Task.Delay(TimeSpan.FromSeconds(2));
        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var screen = Placement.WorkArea(main);
        main.Width = Math.Min(screen.Width - 20, playerWidth + 300);
        main.MoveTo(screen.X + 10, Math.Max(screen.Y, main.Top()));
        await Task.Delay(TimeSpan.FromSeconds(2));
        var (player, eq, body) = main.PanelWidths();
        lines.Add($"{(Math.Abs(player - body) <= 1 ? "ok" : "MISMATCH")} {label}: the panels across the player's rows fill {player:0} of the single window's {body:0}");
        if (equalizer)
        {
            lines.Add($"{(eq is { } wide && Math.Abs(wide - body) <= 1 ? "ok" : "MISMATCH")} {label}: the panels across the equalizer's rows fill {eq:0} of the single window's {body:0}");
        }

        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(3));
        var playerNow = Placement.BoundsOf(main);
        lines.Add($"{(Math.Abs(playerNow.Width - playerWidth) <= 4 ? "ok" : "MISMATCH")} {label}, single window off: the player is {playerNow.Width:0} wide, was {playerWidth:0}");
        // The windows under the player (the equalizer, and the queue unless a short screen put it beside them) as wide as it.
        foreach (var window in main.OwnedWindows.Where(IsOnScreen).Where(window => Placement.BoundsOf(window).Y >= playerNow.Bottom - 4))
        {
            var at = Placement.BoundsOf(window);
            var flush = Math.Abs(at.X - playerNow.X) <= 4 && Math.Abs(at.Width - playerNow.Width) <= 4;
            lines.Add($"{(flush ? "ok" : "MISMATCH")} {label}, single window off: the {window.Title} is at {Describe(at)}, the player at {Describe(playerNow)}");
        }

        lines.AddRange(CheckFlush(main, $"{label}, single window off"));
        putBack();
        await Task.Delay(TimeSpan.FromSeconds(1));
        return lines;
    }

    /// <summary>Each of Lucent's own skins, ending on Lucent's, which a fresh start has.</summary>
    private static readonly string[] SkinsToCheck = { "Linework", "Verdes", "Glass", "Neon", "Studio", "Lucent" };

    /// <summary>
    /// Picks each skin in turn and checks nothing moved or shrank: the player's window where it was, and the visuals and
    /// equalizer (their panels in a single window) where they were, give or take what a skin's type does to a height,
    /// still shown, and not over each other, and the equalizer just tall enough for its sliders in the skin's type. Back
    /// on Lucent's skin, everything's just where it started.
    /// </summary>
    private static async Task<List<string>> CheckSkinsAsync(MainWindow main, Window visuals, string label)
    {
        var lines = new List<string>();
        var single = SingleWindow.IsOn();
        var before = PlacesNow(main, visuals);
        lines.Add($"skins {label}: before, " + DescribeAll(before));
        foreach (var skin in SkinsToCheck)
        {
            main.ChooseSkin(skin);
            await Task.Delay(TimeSpan.FromSeconds(1.5));
            var now = PlacesNow(main, visuals);
            var name = $"skins {label}: {skin}";
            lines.Add($"{name}: " + DescribeAll(now));
            // A skin's type can make the player a little taller or shorter, and what's under it moves with it. The
            // single window itself stays put.
            var heights = skin == "Lucent" ? 4 : 30;
            foreach (var (what, was) in before)
            {
                if (!now.TryGetValue(what, out var place))
                {
                    lines.Add($"MISMATCH {name}: the {what} isn't shown any more");
                }
                else if (!Near(place, was, 4, what == "player" && single ? 4 : heights))
                {
                    lines.Add($"MISMATCH {name}: the {what} is at {Describe(place)}, was at {Describe(was)}");
                }
            }

            if (!single)
            {
                lines.AddRange(CheckFlush(main, name));
            }

            if (!single && now.TryGetValue("player", out var player))
            {
                lines.Add($"{(Math.Abs(player.Width - before["player"].Width) <= 4 ? "ok" : "MISMATCH")} {name}: the player is {player.Width:0} wide, was {before["player"].Width:0}");
            }

            var keys = now.Keys.Where(key => !(single && key == "player")).ToList();
            for (var i = 0; i < keys.Count; i++)
            {
                if (single && now.TryGetValue("player", out var frame) && !Near(frame.Intersect(now[keys[i]]), now[keys[i]], 4, 4))
                {
                    lines.Add($"MISMATCH {name}: the {keys[i]} at {Describe(now[keys[i]])} is outside the single window");
                }

                for (var j = i + 1; j < keys.Count; j++)
                {
                    var overlap = now[keys[i]].Intersect(now[keys[j]]);
                    if (overlap.Width > 4 && overlap.Height > 4)
                    {
                        lines.Add($"MISMATCH {name}: the {keys[i]} and the {keys[j]} overlap by {overlap.Width:0} x {overlap.Height:0}");
                    }
                }
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
    /// The windows end flush, as one block with nothing hanging past the rest: each window's outer edges (the ones no
    /// other window is against) are on the block's edges, and together they fill it. With nothing under the player,
    /// the windows beside it are taller than it can be, so that isn't checked. Given <paramref name="only"/>, just those
    /// windows (and the player) are checked.
    /// </summary>
    private static List<string> CheckFlush(Window main, string label, IEnumerable<Window>? only = null)
    {
        var lines = new List<string>();
        var shown = new[] { main }.Concat((only ?? main.OwnedWindows).Where(IsOnScreen))
            .Select(window => (Window: window, Name: window == main ? "player" : window.Title ?? "window", At: Placement.BoundsOf(window)))
            .Where(entry => entry.At.Width > 0 && entry.At.Height > 0)
            .ToList();
        var player = shown[0].At;
        if (!shown.Skip(1).Any(entry => Touch(player.Bottom, entry.At.Y) && Across(player, entry.At)))
        {
            lines.Add($"{label}: nothing's under the player, so the windows beside it can't end flush with it");
            return lines;
        }

        var block = shown.Select(entry => entry.At).Aggregate((a, b) => a.Union(b));

        // The player's column (the player and what's stacked under it) keeps its own size. When a stack beside it can't
        // get as short as it, every window in it at its smallest (a small screen with every window out), the stacks end
        // level with that one below it, and it's let off stopping short of them.
        var column = new List<Rect> { player };
        while (shown.Select(entry => entry.At).FirstOrDefault(at => !column.Contains(at) && Touch(at.Y, column[^1].Bottom)
                   && at.X > player.X - 4 && at.Right < player.Right + 4) is { Width: > 0 } under)
        {
            column.Add(under);
        }

        bool AtSmallest((Window Window, string Name, Rect At) entry)
        {
            for (var current = entry; current.Window is not null;)
            {
                if (current.At.Height > current.Window.MinHeight + 2)
                {
                    return false;
                }

                var below = current.At;
                current = shown.FirstOrDefault(other => other.At != below && !column.Contains(other.At) && Touch(other.At.Bottom, below.Y)
                    && Math.Abs(other.At.X - below.X) <= 4 && Math.Abs(other.At.Width - below.Width) <= 4);
            }

            return true;
        }

        var lowest = shown.Where(entry => !column.Contains(entry.At) && Touch(entry.At.Bottom, block.Bottom)).ToList();
        var columnShort = column[^1].Bottom < block.Bottom - 4 && lowest.Any(AtSmallest);
        if (columnShort)
        {
            lines.Add($"ok {label}: the windows beside the player's column are as short as they go, so they end level " +
                $"{block.Bottom - column[^1].Bottom:0} below it");
        }

        foreach (var (_, name, place) in shown)
        {
            var others = shown.Where(entry => entry.At != place).Select(entry => entry.At).ToList();
            var off = new List<string>();
            if (!others.Any(other => Touch(place.Bottom, other.Y) && Across(place, other)) && Math.Abs(place.Bottom - block.Bottom) > 4
                && !(columnShort && column.Contains(place)))
            {
                off.Add($"its bottom is at {place.Bottom:0}, the others' at {block.Bottom:0}");
            }

            if (!others.Any(other => Touch(place.Y, other.Bottom) && Across(place, other)) && Math.Abs(place.Y - block.Y) > 4)
            {
                off.Add($"its top is at {place.Y:0}, the others' at {block.Y:0}");
            }

            if (!others.Any(other => Touch(place.X, other.Right) && Down(place, other)) && Math.Abs(place.X - block.X) > 4)
            {
                off.Add($"its left is at {place.X:0}, the others' at {block.X:0}");
            }

            if (!others.Any(other => Touch(place.Right, other.X) && Down(place, other)) && Math.Abs(place.Right - block.Right) > 4)
            {
                off.Add($"its right is at {place.Right:0}, the others' at {block.Right:0}");
            }

            if (off.Count > 0)
            {
                lines.Add($"MISMATCH {label}: the {name} isn't flush with the other windows: {string.Join(", ", off)}");
            }
        }

        var filled = shown.Sum(entry => entry.At.Width * entry.At.Height) / (block.Width * block.Height);
        lines.Add($"{(filled >= 0.985 ? "ok" : "MISMATCH")} {label}: the windows end flush, filling {filled:P1} of the block they make at {Describe(block)}");
        return lines;
    }

    /// <summary>Two edges together, give or take a few points.</summary>
    private static bool Touch(double edge, double other) => Math.Abs(edge - other) <= 4;

    /// <summary>Two places side by side across more than a few points (one over the other).</summary>
    private static bool Across(Rect a, Rect b) => Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X) > 4;

    /// <summary>Two places alongside each other down more than a few points.</summary>
    private static bool Down(Rect a, Rect b) => Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y) > 4;

    /// <summary>
    /// The player's window, and the visuals and equalizer: their panels in a single window (the player's window being
    /// the single window then), their windows otherwise. One that isn't shown is left out.
    /// </summary>
    private static Dictionary<string, Rect> PlacesNow(MainWindow main, Window visuals)
    {
        var places = new Dictionary<string, Rect> { ["player"] = Placement.BoundsOf(main) };
        var equalizer = EqualizerWindow.Current is { IsVisible: true } open ? open : null;
        foreach (var (name, window) in new[] { ("visuals", (Window?)visuals), ("equalizer", equalizer) })
        {
            if (window is null)
            {
                continue;
            }

            if (SingleWindow.IsOn())
            {
                if (PanelOnScreen(main, window) is { } panel && SingleWindow.PaneOf(window) is { IsVisible: true })
                {
                    places[name] = panel;
                }
            }
            else if (IsOnScreen(window))
            {
                places[name] = Placement.BoundsOf(window);
            }
        }

        return places;
    }

    /// <summary>
    /// Full screen in a single window: the window fills the usable part of its screen (give or take its title bar),
    /// then goes back to just where it was.
    /// </summary>
    private static async Task<List<string>> CheckFillAsync(MainWindow main, string label)
    {
        var before = Placement.BoundsOf(main);
        var screen = Placement.WorkArea(main);
        main.ToggleLucentFullScreen();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var filled = Placement.BoundsOf(main);
        var lines = new List<string>
        {
            $"{(Near(filled, screen, 4, 32) ? "ok" : "MISMATCH")} full screen {label}: the single window is at {Describe(filled)}, the screen at {Describe(screen)}",
        };
        main.ToggleLucentFullScreen();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var back = Placement.BoundsOf(main);
        lines.Add($"{(Near(back, before, 4, 4) ? "ok" : "MISMATCH")} full screen {label} off: the single window is at {Describe(back)}, was at {Describe(before)}");
        return lines;
    }

    private static string DescribeAll(Dictionary<string, Rect> places) =>
        string.Join(", ", places.Select(entry => $"{entry.Key} {Describe(entry.Value)}"));

    /// <summary>How far a span moves to be on the screen: to its near edge if it's too long for it, else just enough.</summary>
    private static double OntoScreen(double start, double end, double low, double high) =>
        end - start > high - low ? low - start : Math.Clamp(0, low - start, high - end);

    private static string Describe(Rect rect) => $"{rect.X:0},{rect.Y:0} {rect.Width:0} x {rect.Height:0}";

    /// <summary>Two places within <paramref name="across"/> in left and width, and <paramref name="down"/> in top and height.</summary>
    private static bool Near(Rect a, Rect b, double across, double down) =>
        a.Width > 0 && b.Width > 0 && Math.Abs(a.X - b.X) <= across && Math.Abs(a.Width - b.Width) <= across
        && Math.Abs(a.Y - b.Y) <= down && Math.Abs(a.Height - b.Height) <= down;

    /// <summary>A window's panel in the single window, where it is on the screen, or null if it isn't one.</summary>
    private static Rect? PanelOnScreen(Window main, Window window)
    {
        if (SingleWindow.PaneOf(window) is not { Bounds.Width: > 0 } pane || pane.TranslatePoint(new Point(0, 0), main) is not { } at)
        {
            return null;
        }

        return new Rect(main.Left() + at.X, main.Top() + at.Y, pane.Bounds.Width, pane.Bounds.Height);
    }

    private static async Task<List<string>> CheckMinimizeAsync(Window main)
    {
        var lines = new List<string>();
        var others = main.OwnedWindows.Where(window => window.IsVisible).ToList();
        main.WindowState = WindowState.Minimized;
        await Task.Delay(TimeSpan.FromSeconds(3));
        foreach (var window in others)
        {
            var down = !IsOnScreen(window);
            lines.Add($"{(down ? "ok" : "MISMATCH")} player minimized: {window.Title} {(down ? "went too" : "stayed up")}");
        }

        main.WindowState = WindowState.Normal;
        await Task.Delay(TimeSpan.FromSeconds(3));
        lines.Add($"{(IsOnScreen(main) ? "ok" : "MISMATCH")} player restored: {main.Title}");
        foreach (var window in others)
        {
            var back = IsOnScreen(window);
            lines.Add($"{(back ? "ok" : "MISMATCH")} player restored: {window.Title} {(back ? "came back" : "didn't come back")}");
        }

        return lines;
    }

    /// <summary>Whether macOS shows the window: up on a screen, not hidden or down in the Dock.</summary>
    private static bool IsOnScreen(Window window) =>
        window.TryGetPlatformHandle() is IMacOSTopLevelPlatformHandle { NSWindow: var native } && native != IntPtr.Zero
            ? Interop.ObjC.SendBool(native, "isVisible") && !Interop.ObjC.SendBool(native, "isMiniaturized")
            : window.IsVisible && window.WindowState != WindowState.Minimized;

    /// <summary>Gives the layout and AppKit a few seconds to catch up, then reports how it ended up.</summary>
    private static async Task<(bool Matched, string Line)> SettleAsync(Window visuals, string label)
    {
        var result = (Matched: false, Line: string.Empty);
        for (var attempt = 0; attempt < 24; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(0.25));
            result = Check(visuals, label);
            if (result.Matched && attempt >= 3)
            {
                break;
            }
        }

        return result;
    }

    private static (bool Matched, string Line) Check(Window visuals, string label)
    {
        var view = visuals.GetVisualDescendants().OfType<MetalView>().FirstOrDefault();
        if (view is null)
        {
            return (false, $"MISMATCH {label}: no Metal view in the visuals window");
        }

        var (matches, report) = view.CheckGeometry();
        return (matches, $"{(matches ? "ok" : "MISMATCH")} {label}: {report}");
    }
}
