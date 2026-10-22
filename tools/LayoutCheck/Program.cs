using WinampInspiredVisualizer.Layout;

// Usage: dotnet run --project tools/LayoutCheck
// Lays Lucent's windows out full screen (FullScreenLayout, what Shift+F11 does in both apps) on the
// screens people have, at every Windows scaling from 100% to 200% and on Mac screens, with a few sets of
// windows open, the player at its usual size and bigger, and some windows locked. Then checks, for each:
//   - every window it placed is on the screen, at its smallest size or more (the player and equalizer
//     at exactly their own size), and none is over another or over a locked window;
//   - a locked window isn't moved;
//   - the space under the player and equalizer isn't left empty when the queue's closed and another
//     window could fill it;
//   - nothing is left out on a screen big enough for everything (the windows' smallest sizes say what
//     that is), so each set of windows fits from the screens listed in Fits below.
// Prints each layout, and exits with 1 if anything's wrong.
var problems = new List<string>();
var checkedCount = 0;

// Screens in pixels, and the taskbar (Windows) or menu bar and Dock (Mac) taken off their height.
var windowsScreens = new (int Width, int Height)[] { (1366, 768), (1920, 1080), (1920, 1200), (2560, 1440), (2880, 1800), (3440, 1440), (3840, 2160) };
var windowsScales = new[] { 1.0, 1.25, 1.5, 1.75, 2.0 };
var macScreens = new (int Width, int Height)[] { (1280, 800), (1440, 900), (1512, 982), (1728, 1117), (1920, 1080), (2560, 1440) };

var areas = new List<(string Name, TileRect Area)>();
foreach (var (width, height) in windowsScreens)
{
    // Windows only offers scalings that leave the screen at least about 600 units tall.
    foreach (var scale in windowsScales.Where(scale => height / scale >= 600))
    {
        // Windows' taskbar is 48 units tall at any scaling.
        areas.Add(($"Windows {width}x{height} at {scale:P0}", new TileRect(0, 0, width / scale, height / scale - 48)));
    }
}

foreach (var (width, height) in macScreens)
{
    // In points. The menu bar (taller with a notch) and the Dock.
    areas.Add(($"Mac {width}x{height} points", new TileRect(0, 25, width, height - 25 - 70)));
}

foreach (var playerScale in new[] { 1.0, 1.5 })
{
    foreach (var (setName, windows) in Sets(playerScale))
    {
        foreach (var (areaName, area) in areas)
        {
            Check($"{areaName}, player at {playerScale:P0}, {setName}", area, windows);
        }

        // The player locked a third of the way across, and the queue locked in the bottom-right corner.
        foreach (var (areaName, area) in areas.Where(entry => entry.Area.Width >= 1500))
        {
            var playerAt = new TileRect(area.Left + area.Width / 3, area.Top + 40, windows[0].Width, windows[0].Height);
            Check($"{areaName}, player at {playerScale:P0}, {setName}, player locked", area,
                windows.Select(window => window.Kind == TileKind.Player ? window with { LockedAt = playerAt } : window).ToList());
            if (windows.Any(window => window.Kind == TileKind.Queue))
            {
                var queueAt = new TileRect(area.Right - 520, area.Bottom - 320, 520, 320);
                Check($"{areaName}, player at {playerScale:P0}, {setName}, queue locked", area,
                    windows.Select(window => window.Kind == TileKind.Queue ? window with { LockedAt = queueAt } : window).ToList());
            }
        }
    }
}

Console.WriteLine();
if (problems.Count > 0)
{
    foreach (var problem in problems)
    {
        Console.WriteLine($"::error::{problem}");
    }

    Console.WriteLine($"{problems.Count} problems in {checkedCount} layouts.");
    return 1;
}

Console.WriteLine($"All {checkedCount} layouts are good.");
return 0;

void Check(string name, TileRect area, IReadOnlyList<TileWindow> windows)
{
    checkedCount++;
    var result = FullScreenLayout.Arrange(area, windows);
    var line = $"{name}: " + string.Join(", ", result.Places.Select(place =>
        $"{place.Key} {place.Value.Left:0},{place.Value.Top:0} {place.Value.Width:0}x{place.Value.Height:0}"));
    if (result.LeftOut.Count > 0)
    {
        line += $"; left out: {string.Join(", ", result.LeftOut)}";
    }

    Console.WriteLine(line);
    var placed = result.Places.ToList();
    foreach (var (key, place) in placed)
    {
        var window = windows.Single(candidate => candidate.Key == key);
        if (window.LockedAt is not null)
        {
            problems.Add($"{name}: {key} is locked, but was moved.");
        }

        // A player made bigger than the screen can't be helped.
        if (!place.IsInside(area) && !(window.Kind == TileKind.Player && (window.Width > area.Width || window.Height > area.Height)))
        {
            problems.Add($"{name}: {key} at {place} is off the screen {area}.");
        }

        if (window.FixedSize && window.Kind == TileKind.Player && (Math.Abs(place.Width - window.Width) > 1 || Math.Abs(place.Height - window.Height) > 1))
        {
            problems.Add($"{name}: the player was resized to {place.Width:0}x{place.Height:0}.");
        }

        if (window.Kind == TileKind.Equalizer && Math.Abs(place.Height - window.Height) > 1)
        {
            problems.Add($"{name}: the equalizer was resized to {place.Height:0} high.");
        }

        if (place.Width < window.MinWidth - 0.5 || place.Height < window.MinHeight - 0.5)
        {
            problems.Add($"{name}: {key} is {place.Width:0}x{place.Height:0}, smaller than its smallest, {window.MinWidth}x{window.MinHeight}.");
        }

        foreach (var (otherKey, other) in placed.Where(entry => string.CompareOrdinal(entry.Key, key) > 0))
        {
            if (place.Overlaps(other))
            {
                problems.Add($"{name}: {key} {place} is over {otherKey} {other}.");
            }
        }

        foreach (var locked in windows.Where(candidate => candidate.LockedAt is not null && candidate.Key != key))
        {
            if (place.Overlaps(locked.LockedAt!.Value))
            {
                problems.Add($"{name}: {key} {place} is over the locked {locked.Key}.");
            }
        }
    }

    foreach (var window in windows.Where(window => window.LockedAt is null))
    {
        if (!result.Places.ContainsKey(window.Key) && !result.LeftOut.Contains(window.Key))
        {
            problems.Add($"{name}: {window.Key} was neither placed nor left out.");
        }
    }

    // With the queue closed, something else fills the space under the player and equalizer if it can.
    if (windows.All(window => window.LockedAt is null && window.Kind != TileKind.Queue)
        && result.Places.TryGetValue("Player", out var playerPlace)
        && windows.Any(window => window.Kind is TileKind.Visuals or TileKind.Video or TileKind.Analyzer or TileKind.Radio or TileKind.Editor))
    {
        var top = playerPlace.Bottom + (result.Places.TryGetValue("Equalizer", out var eq) && eq.Left == playerPlace.Left ? eq.Height : 0);
        var space = new TileRect(playerPlace.Left, top, playerPlace.Width, area.Bottom - top);
        var couldFill = windows.Any(window => window.Kind is TileKind.Artwork or TileKind.Library or TileKind.Analyzer or TileKind.Video
            && window.MinWidth <= space.Width + 0.5 && window.MinHeight <= space.Height + 0.5
            && !(window.Kind == TileKind.Video && !windows.Any(other => other.Kind == TileKind.Visuals)));
        if (couldFill && space.Height > 0.5 && !result.Places.Values.Any(place => place.Overlaps(space)))
        {
            problems.Add($"{name}: the space under the player and equalizer, {space}, was left empty.");
        }
    }

    if (result.LeftOut.Count > 0 && Fits(windows, area))
    {
        problems.Add($"{name}: {string.Join(", ", result.LeftOut)} left out, on a screen with room for everything.");
    }
}

// A screen with room for every window: as wide as the widest row of the layout and as tall as the
// tallest column, every window at its smallest. Below this, some window may be left out, but the
// layout must still be tidy. Left out windows are listed in the output, to see how small a screen
// each set needs.
static bool Fits(IReadOnlyList<TileWindow> windows, TileRect area)
{
    if (windows.Any(window => window.LockedAt is not null))
    {
        return false;
    }

    double Min(TileKind kind, Func<TileWindow, double> size) => windows.Where(window => window.Kind == kind).Sum(size);
    var player = windows.Single(window => window.Kind == TileKind.Player);
    var column = player.Height + Min(TileKind.Equalizer, window => window.Height) + Min(TileKind.Queue, window => window.MinHeight);
    var side = Math.Max(Min(TileKind.Library, window => window.MinWidth), Min(TileKind.Artwork, window => window.MinWidth));
    var mainKinds = new[] { TileKind.Visuals, TileKind.Video, TileKind.Analyzer, TileKind.Radio, TileKind.Editor };
    var main = windows.Where(window => mainKinds.Contains(window.Kind)).ToList();
    var mainWidth = main.Count == 0 ? 0 : main.Max(window => window.MinWidth);
    // The visuals on top, the rest under them, each on a row of its own when the width's tight.
    var mainHeight = main.Sum(window => window.MinHeight);
    var sideHeight = windows.Where(window => window.Kind is TileKind.Library or TileKind.Artwork).Sum(window => window.MinHeight);
    return area.Width >= player.Width + side + mainWidth && area.Height >= Math.Max(column, Math.Max(mainHeight, sideHeight));
}

// The sets of open windows checked, with their smallest sizes and their usual sizes, as in the apps' window files.
static List<(string Name, List<TileWindow> Windows)> Sets(double playerScale)
{
    var player = new TileWindow("Player", TileKind.Player, 520 * playerScale, 215 * playerScale, 520 * playerScale, 215 * playerScale, FixedSize: true);
    var equalizer = new TileWindow("Equalizer", TileKind.Equalizer, 520, 392, 520, 392, FixedSize: true);
    var queue = new TileWindow("Queue", TileKind.Queue, 300, 160, 520, 320);
    var library = new TileWindow("Library", TileKind.Library, 280, 200, 380, 560);
    var visuals = new TileWindow("Visuals", TileKind.Visuals, 360, 240, 820, 560);
    var artwork = new TileWindow("Artwork", TileKind.Artwork, 200, 236, 420, 456);
    var analyzer = new TileWindow("Analyzer", TileKind.Analyzer, 440, 220, 760, 460);
    var radio = new TileWindow("Radio", TileKind.Radio, 620, 380, 820, 600);
    var editor = new TileWindow("Editor", TileKind.Editor, 980, 480, 1240, 700);
    var video = new TileWindow("Video", TileKind.Video, 360, 240, 820, 500);
    return new()
    {
        ("player and visuals", new() { player, visuals }),
        ("player, queue and visuals", new() { player, queue, visuals }),
        ("the usual (player, equalizer, queue, library, visuals)", new() { player, equalizer, queue, library, visuals }),
        ("player, queue, library and artwork, no visuals", new() { player, queue, library, artwork }),
        ("everything", new() { player, equalizer, queue, library, artwork, visuals, analyzer, radio, editor }),
        ("player, queue, visuals and a video", new() { player, queue, visuals, video }),
        ("everything but the editor", new() { player, equalizer, queue, library, artwork, visuals, analyzer, radio }),
        ("player, equalizer, library, visuals and analyzer, no queue", new() { player, equalizer, library, visuals, analyzer }),
        ("player, equalizer, library and visuals, no queue", new() { player, equalizer, library, visuals }),
        ("everything but the queue", new() { player, equalizer, library, artwork, visuals, analyzer, radio, editor }),
    };
}
