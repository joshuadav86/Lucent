using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
/// it's on, and every skin again once it's off.
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
        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var back = !SingleWindow.IsOn() && visuals.GetVisualDescendants().OfType<MetalView>().Any() && IsOnScreen(visuals);
        lines.Add($"{(back ? "ok" : "MISMATCH")} single window off: the visuals {(back ? "are a window again" : "didn't come back as a window")}");
        lines.Add((await SettleAsync(visuals, "single window off")).Line);
        var (x, y, w, h) = (visuals.Left(), visuals.Top(), visuals.ActualWidth(), visuals.ActualHeight());
        var where = panel is { } p
            && Math.Abs(x - p.X) <= 4 && Math.Abs(w - p.Width) <= 4 && Math.Abs(y + h - p.Bottom) <= 4
            && y >= main.Top() - 4 && y <= p.Y + 4;
        var panelText = panel is { } r ? $"{r.X:0},{r.Y:0} {r.Width:0} x {r.Height:0}" : "(not found)";
        lines.Add($"{(where ? "ok" : "MISMATCH")} single window off: the visuals are at {Describe(visuals)}, their panel was at {panelText}");

        // The player back at its own width (not the single window's), the equalizer just where its panel was and
        // as wide (not stretched to a wide player), and nothing over anything else.
        var playerNow = main.ActualWidth();
        lines.Add($"{(Math.Abs(playerNow - playerWidth) <= 4 ? "ok" : "MISMATCH")} single window off: the player is {playerNow:0} wide, was {playerWidth:0}");
        if (equalizer is not null)
        {
            var eq = new Rect(equalizer.Left(), equalizer.Top(), equalizer.ActualWidth(), equalizer.ActualHeight());
            var fits = equalizerPanel is { } e && Math.Abs(eq.X - e.X) <= 4 && Math.Abs(eq.Y - e.Y) <= 4 && Math.Abs(eq.Width - e.Width) <= 4;
            var equalizerText = equalizerPanel is { } q ? $"{q.X:0},{q.Y:0} {q.Width:0} x {q.Height:0}" : "(not found)";
            lines.Add($"{(fits ? "ok" : "MISMATCH")} single window off: the equalizer is at {Describe(equalizer)}, its panel was at {equalizerText}");
        }

        var shown = new[] { ("player", (Window?)main), ("visuals", visuals), ("equalizer", equalizer) }
            .Where(entry => entry.Item2 is not null)
            .Select(entry => (Name: entry.Item1, At: new Rect(entry.Item2!.Left(), entry.Item2.Top(), entry.Item2.ActualWidth(), entry.Item2.ActualHeight())))
            .ToList();
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

        // Then every skin again with the windows apart.
        if (main is MainWindow apart)
        {
            lines.AddRange(await CheckSkinsAsync(apart, visuals, "as separate windows"));
        }

        return lines;
    }

    /// <summary>Each of Lucent's own skins, ending on Lucent's, which a fresh start has.</summary>
    private static readonly string[] SkinsToCheck = { "Linework", "Verdes", "Glass", "Neon", "Studio", "Lucent" };

    /// <summary>
    /// Picks each skin in turn and checks nothing moved or shrank: the player's window where it was, and the visuals and
    /// equalizer (their panels in a single window) where they were, give or take what a skin's type does to a height,
    /// still shown, and not over each other. Back on Lucent's skin, everything's just where it started.
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
                lines.Add($"{(needs <= equalizer.Height + 2 ? "ok" : "MISMATCH")} {name}: the equalizer is {equalizer.Height:0} tall, its sliders need {needs:0}");
            }
        }

        return lines;
    }

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
