using System.Runtime.InteropServices;
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
/// switches single window on and off (CheckSingleWindowAsync).
/// </summary>
internal static class LayoutCheck
{
    private static readonly (double Width, double Height)[] Sizes =
    {
        // The last one fits on the runners' small screens, where the reopened run checks it came back
        // the same size.
        (820, 560), (700, 900), (1000, 420), (500, 1000), (860, 480),
    };

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
    /// window's resized. Then off again: the visuals are a window of their own again, back where they were.
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

        SingleWindow.Toggle?.Invoke();
        await Task.Delay(TimeSpan.FromSeconds(2));
        var back = !SingleWindow.IsOn() && visuals.GetVisualDescendants().OfType<MetalView>().Any() && IsOnScreen(visuals);
        lines.Add($"{(back ? "ok" : "MISMATCH")} single window off: the visuals {(back ? "are a window again" : "didn't come back as a window")}");
        lines.Add((await SettleAsync(visuals, "single window off")).Line);
        lines.Add($"{(Near(Describe(visuals), before) ? "ok" : "MISMATCH")} single window off: the visuals are at {Describe(visuals)}, were at {before}");
        return lines;
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
