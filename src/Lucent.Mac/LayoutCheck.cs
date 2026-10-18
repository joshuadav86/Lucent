using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Visuals;

namespace Lucent.Mac;

/// <summary>
/// For the launch check in CI: with LUCENT_LAYOUT_CHECK set to a file, resizes the visuals window a
/// few times and writes down after each whether the Metal view and layer filled the space the layout
/// gave them. A mismatch shows as the visuals squashed into part of the window. Then it minimizes
/// the player and checks the windows around it went to the Dock too, and came back with it, and quits.
/// </summary>
internal static class LayoutCheck
{
    private static readonly (double Width, double Height)[] Sizes =
    {
        (820, 560), (700, 900), (1000, 420), (500, 1000), (900, 700),
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
