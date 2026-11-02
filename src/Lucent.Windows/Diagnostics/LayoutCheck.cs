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
/// and PASSED or FAILED, and quits. On the way it picks every skin with the windows apart and in a single window
/// (each window and panel staying where it was), and takes the single window full screen and back before and
/// after. The Mac app checks the same in its own LayoutCheck.
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
        LeaveSingleWindow();
        var windows = places.Keys.ToList();
        foreach (var wait in new[] { 0, 300, 1500, 4000 })
        {
            await Task.Delay(wait);
            lines.Add($"{label}: after {wait} ms: " + string.Join(", ", windows.Select(window => $"{NameFor(window)} {Describe(OnScreen(window))}")));
        }

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

    /// <summary>Each of Lucent's own skins, ending on Lucent's, which a fresh start has.</summary>
    private static readonly string[] SkinsToCheck = { "Linework", "Verdes", "Glass", "Neon", "Studio", "Lucent" };

    /// <summary>
    /// Picks each skin in turn and checks nothing moved or shrank: every window (or in a single window, the window
    /// and every panel in it) where it was, give or take what a skin's type does to a height, all of them still
    /// shown, none over another, the player at its own width, and the equalizer's sliders all fitting in it. Back
    /// on Lucent's skin, everything's just where it started.
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

            if (EqualizerWindow.Current is { IsVisible: true } equalizer)
            {
                var needs = equalizer.HeightNeeded;
                lines.Add($"{(needs <= equalizer.Height + 2 ? "ok" : "MISMATCH")} {name}: the equalizer is {equalizer.Height:0} tall, its sliders need {needs:0}");
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
