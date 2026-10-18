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

            await Task.Delay(TimeSpan.FromSeconds(3));
            lines.Add(Check(visuals, "as opened", ref failed));
            foreach (var (width, height) in Sizes)
            {
                visuals.Width = width;
                visuals.Height = height;
                await Task.Delay(TimeSpan.FromSeconds(1.5));
                lines.Add(Check(visuals, $"window {width} x {height}", ref failed));
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

    private static string Check(Window visuals, string label, ref bool failed)
    {
        var view = visuals.GetVisualDescendants().OfType<MetalView>().FirstOrDefault();
        if (view is null)
        {
            failed = true;
            return $"MISMATCH {label}: no Metal view in the visuals window";
        }

        var (matches, report) = view.CheckGeometry();
        failed |= !matches;
        return $"{(matches ? "ok" : "MISMATCH")} {label}: {report}";
    }
}
