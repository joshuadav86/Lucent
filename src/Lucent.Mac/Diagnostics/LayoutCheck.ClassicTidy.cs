using Avalonia;
using Avalonia.Controls;
using Lucent.ClassicSkins;
using Lucent.Mac.Windows;

namespace Lucent.Mac;

/// <summary>
/// Part of the layout check: a classic skin picked with the windows apart (and the visuals out of place) tidies them
/// into a block round the player (2026-11-14, Joshua found them all over the place after importing a skin). After
/// picking Lucent Classic, double size on and off, and Lucent's own player back, every window is still shown, none is
/// over another, they end flush, and the player stays where it was. The Windows app checks the same.
/// </summary>
internal static partial class LayoutCheck
{
    private static async Task<List<string>> CheckClassicTidyAsync(MainWindow main, Window visuals)
    {
        const string label = "classic skin tidies the windows";
        var lines = new List<string>();
        if (SingleWindow.IsOn())
        {
            lines.Add($"MISMATCH {label}: still in a single window");
            return lines;
        }

        // Out of place, the way it'd been left before a skin was picked: in the screen's top right corner.
        var work = Placement.WorkArea(main);
        var size = Placement.SizeOf(visuals);
        visuals.MoveTo(Math.Max(work.X, work.Right - size.Width), work.Y);
        await Task.Delay(TimeSpan.FromSeconds(1));
        var before = main.OwnedWindows.Where(IsOnScreen).ToList();
        var playerBefore = Placement.BoundsOf(main);

        var steps = new (string Name, Action Change)[]
        {
            ("Lucent Classic picked", () => main.ChooseClassicSkin(ClassicSkinLibrary.DefaultName)),
            ("double size", main.ToggleClassicDoubleSize),
            ("double size off", main.ToggleClassicDoubleSize),
            ("Lucent's own player back", () => main.ChooseClassicSkin(null)),
        };

        foreach (var (step, change) in steps)
        {
            change();
            await Task.Delay(TimeSpan.FromSeconds(2));
            var name = $"{label}: {step}";
            foreach (var window in before.Where(window => !IsOnScreen(window)))
            {
                lines.Add($"MISMATCH {name}: the {window.Title} window isn't shown any more");
            }

            var player = Placement.BoundsOf(main);
            var stayed = Math.Abs(player.X - playerBefore.X) <= 4 && Math.Abs(player.Y - playerBefore.Y) <= 4;
            lines.Add($"{(stayed ? "ok" : "MISMATCH")} {name}: the player is at {Describe(player)}, was at {Describe(playerBefore)}");

            var shown = new Window[] { main }.Concat(main.OwnedWindows.Where(IsOnScreen))
                .Select(window => (Name: window == main ? "player" : window.Title ?? "window", At: Placement.BoundsOf(window)))
                .ToList();
            lines.Add($"{name}: " + string.Join(", ", shown.Select(entry => $"{entry.Name} {Describe(entry.At)}")));
            for (var i = 0; i < shown.Count; i++)
            {
                for (var j = i + 1; j < shown.Count; j++)
                {
                    var overlap = shown[i].At.Intersect(shown[j].At);
                    if (overlap.Width > 4 && overlap.Height > 4)
                    {
                        lines.Add($"MISMATCH {name}: the {shown[i].Name} and the {shown[j].Name} overlap by {overlap.Width:0} x {overlap.Height:0}");
                    }
                }
            }

            lines.AddRange(CheckFlush(main, name));
        }

        return lines;
    }
}
