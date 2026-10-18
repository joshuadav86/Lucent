using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Lucent.Mac.Visuals;

namespace Lucent.Mac;

/// <summary>
/// For the launch check in CI: with LUCENT_LAYOUT_CHECK set to a file, resizes the visuals window a
/// few times and writes down after each whether the Metal view and layer filled the space the layout
/// gave them, then quits. A mismatch shows as the visuals squashed into part of the window.
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
