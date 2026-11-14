using System.Windows;
using Lucent.ClassicSkins;
using Lucent.Windows;

namespace Lucent;

/// <summary>
/// Part of the layout check: a classic skin picked with the windows apart (and some of them out of place) tidies them
/// into a block round the player (2026-11-14, Joshua found them all over the place after importing a skin). After
/// picking Lucent Classic, double size on and off, and Lucent's own player back, every window is still shown, none is
/// over another, they end flush, and the player stays where it was. The Mac app checks the same.
/// </summary>
public partial class MainWindow
{
    private async Task<List<string>> CheckClassicTidyAsync()
    {
        const string label = "classic skin tidies the windows";
        var lines = new List<string>();
        if (InSingleWindow)
        {
            LeaveSingleWindow();
            await Task.Delay(TimeSpan.FromSeconds(2));
        }

        // Out of place, the way they'd been left before a skin was picked.
        var screen = ScreenArea.Of(this);
        Place(_libraryWindow, screen.Left + 10, screen.Top + screen.Height * 0.6, 380, screen.Height * 0.35);
        Place(_visuals, screen.Left + screen.Width * 0.55, screen.Top + 40, screen.Width * 0.4, screen.Height * 0.5);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var before = PlacesNow();
        var playerBefore = OnScreen(this);
        lines.Add($"{label}: before, " + DescribeAll(before));

        var steps = new (string Name, Action Change)[]
        {
            ("Lucent Classic picked", () => ChooseClassicSkin(ClassicSkinLibrary.DefaultName)),
            ("double size", ToggleClassicDoubleSize),
            ("double size off", ToggleClassicDoubleSize),
            ("Lucent's own player back", () => ChooseClassicSkin(null)),
        };

        foreach (var (step, change) in steps)
        {
            change();
            await Task.Delay(TimeSpan.FromSeconds(2));
            var name = $"{label}: {step}";
            var now = PlacesNow();
            lines.Add($"{name}: " + DescribeAll(now));
            foreach (var window in before.Keys.Where(window => !now.ContainsKey(window) || now[window].IsEmpty))
            {
                lines.Add($"MISMATCH {name}: the {NameFor(window)} window isn't shown any more");
            }

            var player = OnScreen(this);
            var stayed = Math.Abs(player.Left - playerBefore.Left) <= 4 && Math.Abs(player.Top - playerBefore.Top) <= 4;
            lines.Add($"{(stayed ? "ok" : "MISMATCH")} {name}: the player is at {Describe(player)}, was at {Describe(playerBefore)}");

            var windows = now.Keys.ToList();
            foreach (var window in windows)
            {
                foreach (var other in windows.Skip(windows.IndexOf(window) + 1))
                {
                    var overlap = Rect.Intersect(now[window], now[other]);
                    if (!overlap.IsEmpty && overlap.Width > 4 && overlap.Height > 4)
                    {
                        lines.Add($"MISMATCH {name}: the {NameFor(window)} and the {NameFor(other)} overlap by {overlap.Width:0} x {overlap.Height:0}");
                    }
                }
            }

            lines.AddRange(CheckFlush(name, now));
        }

        return lines;
    }
}
