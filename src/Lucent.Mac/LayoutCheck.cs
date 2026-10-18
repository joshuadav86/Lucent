using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Visuals;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

/// <summary>
/// For the launch check in CI: with LUCENT_LAYOUT_CHECK set to a file, resizes the visuals window a
/// few times and writes down after each whether the Metal view and layer filled the space the layout
/// gave them. A mismatch shows as the visuals squashed into part of the window. Then it minimizes
/// the player and checks the windows around it went to the Dock too, and came back with it, and quits
/// the way people do. Run again on the same settings with LUCENT_LAYOUT_CHECK_REOPENED=1, it first
/// checks the windows came back as they were left: the visuals open, the equalizer under the player.
/// </summary>
internal static class LayoutCheck
{
    private static readonly (double Width, double Height)[] Sizes =
    {
        // The last one fits on the runners' small screens: a window taller than the screen is shrunk
        // to fit when Lucent reopens, which the reopened run would count as moved.
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
