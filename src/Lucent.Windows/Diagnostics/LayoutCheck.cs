using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Lucent.Windows;

namespace Lucent;

/// <summary>
/// For CI: with LUCENT_LAYOUT_CHECK set to a file, Lucent (opening as a single window, as it does the first time)
/// opens the queue, library, and artwork beside the equalizer and visuals, switches single window off, and checks
/// each window opened just where its panel was: nothing overlapping, nothing outside where the single window was,
/// the player at its own size, and the equalizer no wider than its panel. Then it does it again, and again after
/// the windows were spread out and stretched on their own (as an older layout left them). It writes down where
/// every window was just after switching and a few times as they settled, then ok or MISMATCH for each check,
/// and PASSED or FAILED, and quits. The Mac app checks the same in its own LayoutCheck.
/// </summary>
public partial class MainWindow
{
    /// <summary>True when Lucent was opened to run the layout check.</summary>
    internal static bool LayoutCheckIsAsked => Environment.GetEnvironmentVariable("LUCENT_LAYOUT_CHECK") is { Length: > 0 };

    /// <summary>What the player's sizing did while the check ran, written down with its results.</summary>
    private static List<string>? _layoutTrace;

    private void TraceLayout(string what) =>
        _layoutTrace?.Add($"  {DateTime.Now:ss.fff} {what}: player Width {Width:0} actual {ActualWidth:0} native {OnScreen(this).Width:0} " +
            $"min {MinWidth:0} max {MaxWidth:0} {SizeToContent} scale {PlayerScale:0.00} single {InSingleWindow}");

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
        }
        catch (Exception ex)
        {
            lines.Add($"MISMATCH the check failed: {ex}");
        }

        lines.Add(lines.Any(line => line.StartsWith("MISMATCH", StringComparison.Ordinal)) ? "FAILED" : "PASSED");
        await File.WriteAllLinesAsync(file, lines);
        Close();
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
        _layoutTrace = new List<string>();
        LeaveSingleWindow();
        TraceLayout("left single window");
        var windows = places.Keys.ToList();
        foreach (var wait in new[] { 0, 300, 1500, 4000 })
        {
            await Task.Delay(wait);
            lines.Add($"{label}: after {wait} ms: " + string.Join(", ", windows.Select(window => $"{NameFor(window)} {Describe(OnScreen(window))}")));
        }

        TraceLayout("settled");
        lines.AddRange(_layoutTrace);
        _layoutTrace = null;

        foreach (var (window, place) in places)
        {
            var now = OnScreen(window);
            var near = Math.Abs(now.Left - place.Left) <= 4 && Math.Abs(now.Top - place.Top) <= 4
                && Math.Abs(now.Width - place.Width) <= 4 && Math.Abs(now.Height - place.Height) <= 4;
            lines.Add($"{(near ? "ok" : "MISMATCH")} {label}: the {NameFor(window)} is at {Describe(now)}, its panel was at {Describe(place)}");
        }

        var player = OnScreen(this);
        var playerWidth = PlayerWidth * PlayerScale;
        lines.Add($"{(Math.Abs(player.Width - playerWidth) <= 2 ? "ok" : "MISMATCH")} {label}: the player is {player.Width:0} wide, its size makes it {playerWidth:0}");
        foreach (var window in windows)
        {
            var at = OnScreen(window);
            var inside = at.Left >= frame.Left - 4 && at.Top >= frame.Top - 4 && at.Right <= frame.Right + 4 && at.Bottom <= frame.Bottom + 4;
            if (!inside)
            {
                lines.Add($"MISMATCH {label}: the {NameFor(window)} at {Describe(at)} is outside where the single window was");
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

    private static string NameFor(Window window) => window switch
    {
        MainWindow => "player",
        EqualizerWindow => "equalizer",
        ArtworkWindow => "artwork",
        _ => window.Title.Replace("Lucent ", string.Empty, StringComparison.Ordinal),
    };

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
