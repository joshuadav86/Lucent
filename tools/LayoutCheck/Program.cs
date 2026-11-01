using Lucent.Layout;

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
//   - windows swapped while full screen only trade places that fit them, and the player never moves.
//   - all of that again for each tidy layout (Tidy layout, Shift+D), and each press of Tidy layout
//     moves something while there's another tidy layout to go to.
//   - Tidy layout outside full screen (FullScreenLayout.TidyAround) with the player in a few spots and
//     the other windows scattered, some far away or huge, and some locked: every window on the screen,
//     at its smallest or more, none over another or a locked one; the windows in one block docked
//     together; the player left where it is unless the block had to move to stay on the screen; no window
//     blown up (wider than two players, or the widest window's smallest, with room to grow); and
//     pressing it round every layout twice gives the same layouts both times.
//   - layouts saved by name (SavedLayouts, right-click › Layouts): saving under a name that's taken replaces
//     that layout and keeps its key, renaming can't take another layout's name, names are tidied, the keys
//     1 to 9 go to the first nine, and a layout saved on a screen that's gone moves onto the player's screen
//     whole, keeping its shape.
//   - single window (SingleWindowLayout, U): for each set of panels (editors stay windows of their own) and
//     each layout, the smallest body really is the smallest any panel layout fits; the body it grows to
//     fits every panel, or is the most it may grow to; laid out there, every panel is placed inside the
//     body, at its smallest size or more (the player and equalizer at their own), none over another, and
//     locks make no difference; and the window put round the open windows lands on the screen. Every
//     divider between panels dragged as far as it goes either way (and all at once) keeps all of that true,
//     stops where its room runs out, and never touches the player or the equalizer.
// Prints each layout, and exits with 1 if anything's wrong.
var problems = new List<string>();
var checkedCount = 0;
var carriedLines = 0;

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
            foreach (var style in FullScreenLayout.TidyStyles)
            {
                Check($"{areaName}, player at {playerScale:P0}, {setName}, {style}", area, windows, style);
            }

            CheckTidy($"{areaName}, player at {playerScale:P0}, {setName}", area, windows);
            CheckTidyAround($"{areaName}, player at {playerScale:P0}, {setName}", area, windows);
        }

        // The player locked a third of the way across, and the queue locked in the bottom-right corner.
        foreach (var (areaName, area) in areas.Where(entry => entry.Area.Width >= 1500))
        {
            var playerAt = new TileRect(area.Left + area.Width / 3, area.Top + 40, windows[0].Width, windows[0].Height);
            foreach (var style in FullScreenLayout.TidyStyles.Prepend(TileStyle.AsTheyAre))
            {
                Check($"{areaName}, player at {playerScale:P0}, {setName}, player locked, {style}", area,
                    windows.Select(window => window.Kind == TileKind.Player ? window with { LockedAt = playerAt } : window).ToList(), style);
                if (windows.Any(window => window.Kind == TileKind.Queue))
                {
                    var queueAt = new TileRect(area.Right - 520, area.Bottom - 320, 520, 320);
                    Check($"{areaName}, player at {playerScale:P0}, {setName}, queue locked, {style}", area,
                        windows.Select(window => window.Kind == TileKind.Queue ? window with { LockedAt = queueAt } : window).ToList(), style);
                }
            }
        }
    }
}

// Windows swapped while full screen (dragged onto each other, WindowSwap) trade the places the layout gave
// them, but only when each fits the other's place: so nothing ends up below its smallest size, the player
// and equalizer keep theirs, and the places are the same ones, just traded, so nothing overlaps.
var swaps = new List<List<string>> { new() { "Artwork", "Library" }, new() { "Queue", "Visuals" }, new() { "Analyzer", "Artwork" }, new() { "Player", "Queue" } };
foreach (var (setName, windows) in Sets(1.0))
{
    foreach (var (areaName, area) in areas)
    {
        checkedCount++;
        var swapped = FullScreenLayout.Arrange(area, windows);
        var placesBefore = swapped.Places.Values.OrderBy(place => place.Left).ThenBy(place => place.Top).ToList();
        var playerPlace = swapped.Places.GetValueOrDefault("Player");
        WindowSwap.Apply(swapped, windows, swaps);
        var placesAfter = swapped.Places.Values.OrderBy(place => place.Left).ThenBy(place => place.Top).ToList();
        var swapName = $"{areaName}, {setName}, swapped";
        if (!placesBefore.SequenceEqual(placesAfter))
        {
            problems.Add($"{swapName}: swapping changed the places instead of trading them.");
        }

        if (swapped.Places.GetValueOrDefault("Player") != playerPlace)
        {
            problems.Add($"{swapName}: the player was swapped, but it sizes itself.");
        }

        foreach (var (key, place) in swapped.Places)
        {
            var window = windows.Single(candidate => candidate.Key == key);
            if (place.Width < window.MinWidth - 0.5 || place.Height < window.MinHeight - 0.5)
            {
                problems.Add($"{swapName}: {key} is {place.Width:0}x{place.Height:0}, smaller than its smallest, {window.MinWidth}x{window.MinHeight}.");
            }
        }
    }
}

CheckSavedLayouts();
CheckSingleWindow();
CheckDividers();
if (carriedLines == 0)
{
    problems.Add("Single window dividers: no layout let the player's column be moved, between the library and the visuals.");
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

// Tidy layout outside full screen, from a few starting places: the player at the top left, in the middle
// and against the bottom right; the other windows scattered round it, one far off in a corner, one made
// huge; and with the visuals locked beside the player or across the screen.
void CheckTidyAround(string name, TileRect area, IReadOnlyList<TileWindow> windows)
{
    var player = windows.Single(window => window.Kind == TileKind.Player);
    if (player.Width > area.Width || player.Height > area.Height)
    {
        return;
    }

    var spots = new (string Name, double Left, double Top)[]
    {
        ("top left", area.Left + 20, area.Top + 10),
        ("middle", area.Left + (area.Width - player.Width) / 2, area.Top + (area.Height - player.Height) / 2),
        ("bottom right", area.Right - player.Width, area.Bottom - player.Height),
    };
    foreach (var (spotName, left, top) in spots)
    {
        var playerAt = new TileRect(left, top, player.Width, player.Height);
        var now = new Dictionary<string, TileRect> { [player.Key] = playerAt };
        var step = 0;
        foreach (var window in windows.Where(window => window != player))
        {
            // Scattered: one far off in the opposite corner, one stretched across most of the screen, the rest beside the player.
            now[window.Key] = (step++ % 3) switch
            {
                0 => new TileRect(area.Right - window.Width, area.Bottom - window.Height, window.Width, window.Height),
                1 => new TileRect(area.Left, area.Top, Math.Max(window.MinWidth, area.Width * 0.9), Math.Max(window.MinHeight, area.Height * 0.9)),
                _ => new TileRect(playerAt.Right, playerAt.Top, window.Width, window.Height),
            };
        }

        RunTidyAround($"{name}, tidied from the player {spotName}", area, windows, now);
        if (windows.FirstOrDefault(window => window.Kind == TileKind.Visuals) is { } visuals)
        {
            // Locked beside the player (on the screen), and locked over on the far side of the screen.
            var beside = new TileRect(Math.Min(playerAt.Right, area.Right - visuals.Width), playerAt.Top, visuals.Width, Math.Min(visuals.Height, area.Bottom - playerAt.Top));
            var far = new TileRect(playerAt.Left > area.Left + area.Width / 2 ? area.Left : area.Right - visuals.Width, area.Top, visuals.Width, visuals.Height);
            foreach (var (lockName, at) in new[] { ("beside the player", beside), ("far off", far) })
            {
                if (at.Overlaps(playerAt) || !at.IsInside(area))
                {
                    continue;
                }

                var lockedSet = windows.Select(window => window == visuals ? window with { LockedAt = at } : window).ToList();
                RunTidyAround($"{name}, tidied from the player {spotName}, visuals locked {lockName}", area, lockedSet, new Dictionary<string, TileRect>(now) { [visuals.Key] = at });
            }
        }
    }
}

void RunTidyAround(string name, TileRect area, IReadOnlyList<TileWindow> windows, Dictionary<string, TileRect> start)
{
    var player = windows.Single(window => window.Kind == TileKind.Player);
    var style = TileStyle.AsTheyAre;
    var now = new Dictionary<string, TileRect>(start);
    var firstRound = new Dictionary<TileStyle, string>();
    for (var press = 0; press < FullScreenLayout.TidyStyles.Count * 2; press++)
    {
        checkedCount++;
        var (next, result) = FullScreenLayout.TidyAround(area, windows, now, style);
        var pressName = $"{name}, press {press + 1} ({next})";
        var placed = result.Places.ToList();
        Console.WriteLine($"{pressName}: " + string.Join(", ", placed.Select(place =>
            $"{place.Key} {place.Value.Left:0},{place.Value.Top:0} {place.Value.Width:0}x{place.Value.Height:0}"))
            + (result.LeftOut.Count > 0 ? $"; left out: {string.Join(", ", result.LeftOut)}" : ""));

        var full = FullScreenLayout.Arrange(area, windows, next);
        var fullScreen = full.Places.Count == result.Places.Count && full.Places.All(entry => result.Places.GetValueOrDefault(entry.Key) == entry.Value);
        var column = Math.Max(player.Width, windows.Where(window => window.Kind == TileKind.Equalizer).Select(window => window.Width).DefaultIfEmpty(0).Max());
        var widest = Math.Max(column * 2, windows.Where(window => window.Kind != TileKind.Player).Select(window => window.MinWidth).DefaultIfEmpty(0).Max());
        var before = now[player.Key];
        foreach (var (key, place) in placed)
        {
            var window = windows.Single(candidate => candidate.Key == key);
            if (window.LockedAt is not null)
            {
                problems.Add($"{pressName}: {key} is locked, but was moved.");
            }

            if (!place.IsInside(area))
            {
                problems.Add($"{pressName}: {key} at {place} is off the screen {area}.");
            }

            if (place.Width < window.MinWidth - 0.5 || place.Height < window.MinHeight - 0.5)
            {
                problems.Add($"{pressName}: {key} is {place.Width:0}x{place.Height:0}, smaller than its smallest, {window.MinWidth}x{window.MinHeight}.");
            }

            if (window.FixedSize && window.Kind == TileKind.Player && (Math.Abs(place.Width - window.Width) > 1 || Math.Abs(place.Height - window.Height) > 1))
            {
                problems.Add($"{pressName}: the player was resized to {place.Width:0}x{place.Height:0}.");
            }

            if (window.Kind == TileKind.Equalizer && Math.Abs(place.Height - window.Height) > 1)
            {
                problems.Add($"{pressName}: the equalizer was resized to {place.Height:0} high.");
            }

            // Blown up: wider than the block lets the big windows be (two players, or the widest one's smallest
            // if that's wider, and three quarters as much again when the block has to grow to fit everything), unless nothing
            // smaller fitted and it's the full screen layout.
            if (!window.FixedSize && !fullScreen && place.Width > widest * 1.75 + 1)
            {
                problems.Add($"{pressName}: {key} is {place.Width:0} wide, more than the block allows ({widest * 1.75:0}).");
            }

            foreach (var (otherKey, other) in placed.Where(entry => string.CompareOrdinal(entry.Key, key) > 0))
            {
                if (place.Overlaps(other))
                {
                    problems.Add($"{pressName}: {key} {place} is over {otherKey} {other}.");
                }
            }

            foreach (var locked in windows.Where(candidate => candidate.LockedAt is not null && candidate.Key != key))
            {
                if (place.Overlaps(locked.LockedAt!.Value))
                {
                    problems.Add($"{pressName}: {key} {place} is over the locked {locked.Key}.");
                }
            }
        }

        if (result.Places.Count == 0)
        {
            // Nothing moved: only right when even the full screen layout would go over a locked window.
            if (!windows.Any(window => window.LockedAt is { } at && full.Places.Values.Any(place => place.Overlaps(at)) || !full.Places.Values.All(place => place.IsInside(area))))
            {
                problems.Add($"{pressName}: nothing was moved, though the full screen layout would do.");
            }

            style = next;
            continue;
        }

        if (result.LeftOut.Count > 0 && full.LeftOut.Count == 0)
        {
            problems.Add($"{pressName}: {string.Join(", ", result.LeftOut)} left out, though full screen fits everything.");
        }

        // One block: every window docked to the player, directly or through others.
        var docked = new List<TileRect>();
        if (result.Places.TryGetValue(player.Key, out var playerPlace))
        {
            docked.Add(playerPlace);
        }
        else if (player.LockedAt is { } lockedPlayer)
        {
            docked.Add(lockedPlayer);
        }

        var rest = placed.Where(entry => entry.Key != player.Key).Select(entry => entry.Value).ToList();
        for (var i = 0; i < docked.Count; i++)
        {
            foreach (var place in rest.Where(place => Touching(docked[i], place)).ToList())
            {
                docked.Add(place);
                rest.Remove(place);
            }
        }

        if (rest.Count > 0 && !fullScreen)
        {
            problems.Add($"{pressName}: {rest.Count} window(s) not docked to the rest of the block.");
        }

        // The player stays put unless the block had to move to stay on the screen or clear of a locked window.
        if (playerPlace != default && !fullScreen && (Math.Abs(playerPlace.Left - before.Left) > 1 || Math.Abs(playerPlace.Top - before.Top) > 1))
        {
            var block = placed.Select(entry => entry.Value).ToList();
            var atEdge = block.Any(place => Math.Abs(place.Left - area.Left) < 1 || Math.Abs(place.Right - area.Right) < 1
                || Math.Abs(place.Top - area.Top) < 1 || Math.Abs(place.Bottom - area.Bottom) < 1)
                || windows.Any(window => window.LockedAt is { } at && block.Any(place => Touching(place, at)));
            if (!atEdge)
            {
                problems.Add($"{pressName}: the player moved from {before} to {playerPlace}, though the block wasn't kept on the screen.");
            }
        }

        // Compared by size and place in the block, since the block can have moved (to stay on the screen).
        var blockLeft = placed.Count == 0 ? 0 : placed.Min(entry => entry.Value.Left);
        var blockTop = placed.Count == 0 ? 0 : placed.Min(entry => entry.Value.Top);
        var layout = string.Join("; ", result.Places.OrderBy(entry => entry.Key)
            .Select(entry => $"{entry.Key} {entry.Value.Left - blockLeft:0},{entry.Value.Top - blockTop:0} {entry.Value.Width:0}x{entry.Value.Height:0}"));
        if (press < FullScreenLayout.TidyStyles.Count)
        {
            firstRound[next] = layout;
        }
        else if (firstRound.TryGetValue(next, out var firstTime) && firstTime != layout)
        {
            problems.Add($"{pressName}: came out different the second time round ({firstTime} then {layout}).");
        }

        style = next;
        foreach (var (key, place) in result.Places)
        {
            now[key] = place;
        }
    }
}

static bool Touching(TileRect a, TileRect b)
{
    const double tolerance = 1;
    var overlapX = a.Left < b.Right - tolerance && b.Left < a.Right - tolerance;
    var overlapY = a.Top < b.Bottom - tolerance && b.Top < a.Bottom - tolerance;
    return overlapY && (Math.Abs(a.Right - b.Left) <= tolerance || Math.Abs(b.Right - a.Left) <= tolerance)
        || overlapX && (Math.Abs(a.Bottom - b.Top) <= tolerance || Math.Abs(b.Bottom - a.Top) <= tolerance);
}

// Pressing Tidy layout again and again, from the layout as it was: each press moves something until it's
// been through every tidy layout that's different, and it comes back round to where it started.
void CheckTidy(string name, TileRect area, IReadOnlyList<TileWindow> windows)
{
    // Only the layouts that fit the most windows in count: Tidy layout passes over the others.
    var layouts = FullScreenLayout.TidyStyles.Select(style => FullScreenLayout.Arrange(area, windows, style)).ToList();
    var fewestOut = layouts.Min(result => result.LeftOut.Count);
    var different = layouts
        .Where(result => result.LeftOut.Count == fewestOut)
        .Select(result => string.Join(";", result.Places.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}")))
        .Distinct()
        .Count();
    var now = FullScreenLayout.Arrange(area, windows).Places;
    var style = TileStyle.AsTheyAre;
    var seen = new List<TileStyle>();
    for (var press = 0; press < FullScreenLayout.TidyStyles.Count; press++)
    {
        var (next, result) = FullScreenLayout.Tidy(area, windows, now, style);
        if (press < different && seen.Count > 0 && seen.Contains(next))
        {
            problems.Add($"{name}: Tidy layout went back to {next} after {string.Join(", ", seen)}, with {different} different layouts to go through.");
            return;
        }

        seen.Add(next);
        style = next;
        now = result.Places;
    }
}

void Check(string name, TileRect area, IReadOnlyList<TileWindow> windows, TileStyle style = TileStyle.AsTheyAre)
{
    checkedCount++;
    var result = FullScreenLayout.Arrange(area, windows, style);
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
        ("player, equalizer, library, visuals, video and analyzer, no queue", new() { player, equalizer, library, visuals, video, analyzer }),
        ("everything but the queue", new() { player, equalizer, library, artwork, visuals, analyzer, radio, editor }),
    };
}

// Single window: the windows as panels laid out in one window's body (its top left corner at 0, 0).
void CheckSingleWindow()
{
    var styles = FullScreenLayout.TidyStyles.Prepend(TileStyle.AsTheyAre).ToList();
    foreach (var playerScale in new[] { 1.0, 1.5 })
    {
        foreach (var (setName, windows) in Sets(playerScale))
        {
            var panels = windows.Where(window => window.Kind != TileKind.Editor).ToList();
            foreach (var style in styles)
            {
                var name = $"single window, player at {playerScale:P0}, {setName}, {style}";
                checkedCount++;
                var (smallestWidth, smallestHeight) = SingleWindowLayout.Smallest(panels, style);
                if (!SingleWindowLayout.Fits(smallestWidth, 8000, panels, style) || !SingleWindowLayout.Fits(8000, smallestHeight, panels, style))
                {
                    problems.Add($"{name}: nothing fits at its smallest size, {smallestWidth:0}x{smallestHeight:0}.");
                }

                // Any narrower (or shorter) than its smallest by more than a step and nothing fits.
                if (smallestWidth > Math.Max(SingleWindowLayout.SmallestSide, panels.Max(panel => panel.MinWidth)) + 0.5
                    && SingleWindowLayout.Fits(smallestWidth - 9, 8000, panels, style))
                {
                    problems.Add($"{name}: everything fits narrower than its smallest width, {smallestWidth:0}.");
                }

                if (smallestHeight > Math.Max(SingleWindowLayout.SmallestSide, panels.Max(panel => panel.MinHeight)) + 0.5
                    && SingleWindowLayout.Fits(8000, smallestHeight - 9, panels, style))
                {
                    problems.Add($"{name}: everything fits shorter than its smallest height, {smallestHeight:0}.");
                }

                foreach (var (areaName, area) in areas)
                {
                    // Grown from its smallest, and from a window about the size people have, up to the screen less the title bar.
                    foreach (var (startWidth, startHeight) in new[] { (smallestWidth, smallestHeight), (1000.0, 600.0) })
                    {
                        var most = (area.Width, area.Height - 34);
                        var (width, height) = SingleWindowLayout.Grown(startWidth, startHeight, panels, style, most);
                        var grownName = $"{name}, {areaName}, grown from {startWidth:0}x{startHeight:0} to {width:0}x{height:0}";
                        if (width < startWidth - 0.5 || height < startHeight - 0.5)
                        {
                            problems.Add($"{grownName}: it shrank.");
                        }

                        var fits = SingleWindowLayout.Fits(width, height, panels, style);
                        if (!fits && (width < Math.Max(startWidth, most.Width) - 0.5 || height < Math.Max(startHeight, most.Item2) - 0.5))
                        {
                            problems.Add($"{grownName}: not everything fits, and it stopped short of the most it could grow.");
                        }

                        if (fits)
                        {
                            CheckPanels(grownName, width, height, panels, style);
                        }
                    }
                }

                // Locked windows are free to move as panels.
                var locked = panels.Select(panel => panel with { LockedAt = new TileRect(0, 0, panel.Width, panel.Height) }).ToList();
                var size = SingleWindowLayout.Grown(1200, 800, locked, style, (8000, 8000));
                CheckPanels($"{name}, every window locked", size.Width, size.Height, locked, style);
            }
        }
    }

    // The window put round the open windows: on the screen, whatever they were doing.
    foreach (var (areaName, area) in areas)
    {
        checkedCount++;
        var scattered = new[]
        {
            new TileRect(area.Left - 300, area.Top + 100, 520, 215),
            new TileRect(area.Right - 200, area.Bottom - 100, 820, 560),
            new TileRect(area.Left + 100, area.Top + 50, area.Width * 2, 300),
        };
        foreach (var (spot, windows) in new[] { ("scattered", scattered), ("one window", scattered.Take(1).ToArray()), ("none", Array.Empty<TileRect>()) })
        {
            var around = SingleWindowLayout.Around(windows, area);
            if (!around.IsInside(area))
            {
                problems.Add($"single window around {spot} windows, {areaName}: {around} is off the screen {area}.");
            }
        }
    }
}

void CheckPanels(string name, double width, double height, IReadOnlyList<TileWindow> panels, TileStyle style)
{
    var body = new TileRect(0, 0, width, height);
    var result = SingleWindowLayout.Arrange(width, height, panels, style, new List<List<string>> { new() { "Queue", "Visuals" }, new() { "Artwork", "Library" } });
    foreach (var panel in panels.Where(panel => !result.Places.ContainsKey(panel.Key)))
    {
        problems.Add($"{name}: {panel.Key} was left out.");
    }

    CheckPlaced(name, body, result, panels);

    // Every divider dragged as far as it goes each way, and all of them at once: still every panel inside the
    // body, at its smallest or more, none over another, and the ones that size themselves left alone.
    var lines = PanelDividers.Lines(result.Places, panels);
    carriedLines += lines.Count(line => line.Carries.Contains("Player"));
    foreach (var line in lines)
    {
        var (least, most) = PanelDividers.Room(line, result.Places, panels);
        if (least > 0 || most < 0)
        {
            problems.Add($"{name}: the divider between {string.Join("+", line.Before)} and {string.Join("+", line.After)} has no room to stay where it is ({least:0} to {most:0}).");
        }

        foreach (var offset in new[] { -100000.0, 100000.0 })
        {
            var divider = new PanelDivider
            {
                Across = line.Across, Before = line.Before.ToList(), After = line.After.ToList(), Carried = line.Carries.ToList(), Offset = offset,
            };
            var moved = PanelDividers.Apply(result, panels, new[] { divider });
            var lineName = $"{name}, divider between {string.Join("+", line.Before)} and {string.Join("+", line.After)} moved {(offset < 0 ? "back" : "on")}";
            CheckPlaced(lineName, body, moved, panels);
            var expected = line.Position + line.CarriedWidth + (offset < 0 ? least : most);
            var after = moved.Places[line.After[0]];
            if (Math.Abs((line.Across ? after.Top : after.Left) - expected) > 0.5)
            {
                problems.Add($"{lineName}: it should be at {expected:0}, but is at {(line.Across ? after.Top : after.Left):0}.");
            }
        }
    }

    var all = lines.Select((line, index) => new PanelDivider
    {
        Across = line.Across, Before = line.Before.ToList(), After = line.After.ToList(), Carried = line.Carries.ToList(),
        Offset = index % 2 == 0 ? 37 : -53,
    }).ToList();
    CheckPlaced($"{name}, every divider moved", body, PanelDividers.Apply(result, panels, all), panels);
}

void CheckPlaced(string name, TileRect body, TileResult result, IReadOnlyList<TileWindow> panels)
{
    var placed = result.Places.ToList();
    foreach (var (key, place) in placed)
    {
        var panel = panels.Single(candidate => candidate.Key == key);
        if (!place.IsInside(body))
        {
            problems.Add($"{name}: {key} at {place} is outside the window's body {body}.");
        }

        // The player keeps its size; the equalizer its height (it can stretch across the player's column, as full screen).
        if (panel.FixedSize && (Math.Abs(place.Height - panel.Height) > 1 || panel.Kind == TileKind.Player && Math.Abs(place.Width - panel.Width) > 1))
        {
            problems.Add($"{name}: {key} sizes itself, but was made {place.Width:0}x{place.Height:0}.");
        }

        if (place.Width < panel.MinWidth - 0.5 || place.Height < panel.MinHeight - 0.5)
        {
            problems.Add($"{name}: {key} is {place.Width:0}x{place.Height:0}, smaller than its smallest, {panel.MinWidth}x{panel.MinHeight}.");
        }

        foreach (var (otherKey, other) in placed.Where(entry => string.CompareOrdinal(entry.Key, key) > 0))
        {
            if (place.Overlaps(other))
            {
                problems.Add($"{name}: {key} {place} is over {otherKey} {other}.");
            }
        }
    }
}

// Dividers kept for the single window: one moved again replaces what was kept for it, as the latest.
void CheckDividers()
{
    checkedCount++;
    var dividers = new List<PanelDivider>();
    var first = new DividerLine(false, 300, 0, 600, new[] { "Library" }, new[] { "Visuals" });
    var second = new DividerLine(true, 400, 300, 900, new[] { "Visuals" }, new[] { "Analyzer" });
    PanelDividers.Remember(dividers, first, 40);
    PanelDividers.Remember(dividers, second, -20);
    PanelDividers.Remember(dividers, first, 60);
    if (dividers.Count != 2 || !second.Is(dividers[0]) || !first.Is(dividers[1]) || dividers[1].Offset != 60)
    {
        problems.Add("Single window dividers: moving one again should replace what was kept for it, as the latest moved.");
    }
}

// Layouts saved by name: the list kept in the settings, and moving a layout onto a screen.
void CheckSavedLayouts()
{
    checkedCount++;
    static Lucent.Settings.WindowBounds At(double left, double top, double width, double height) =>
        new() { Left = left, Top = top, Width = width, Height = height };
    var layouts = new List<SavedLayout>();
    SavedLayouts.Save(layouts, new SavedLayout { Name = "  Studio   desk " });
    SavedLayouts.Save(layouts, new SavedLayout { Name = "Party" });
    SavedLayouts.Save(layouts, new SavedLayout { Name = "STUDIO DESK", Linked = true });
    if (layouts.Count != 2 || layouts[0].Name != "STUDIO DESK" || !layouts[0].Linked || layouts[1].Name != "Party")
    {
        problems.Add($"Saved layouts: saving under a taken name should replace that layout where it is, got {string.Join(", ", layouts.Select(layout => layout.Name))}.");
    }

    if (SavedLayouts.KeyOf(layouts, layouts[1]) != "2" || SavedLayouts.ForKey(layouts, 2) != layouts[1] || SavedLayouts.ForKey(layouts, 3) is not null)
    {
        problems.Add("Saved layouts: the keys should be 1 for the first layout, 2 for the second, and nothing past the last.");
    }

    for (var i = 3; i <= 10; i++)
    {
        SavedLayouts.Save(layouts, new SavedLayout { Name = $"Layout {i}" });
    }

    if (SavedLayouts.KeyOf(layouts, layouts[8]) != "9" || SavedLayouts.KeyOf(layouts, layouts[9]) is not null || SavedLayouts.ForKey(layouts, 10) is not null)
    {
        problems.Add("Saved layouts: only the first nine should have keys.");
    }

    if (SavedLayouts.Rename(layouts, "Party", "studio desk") || !SavedLayouts.Rename(layouts, "Party", "party") || layouts[1].Name != "party"
        || !SavedLayouts.Rename(layouts, "party", "Night") || SavedLayouts.Rename(layouts, "Night", "   "))
    {
        problems.Add("Saved layouts: renaming should refuse another layout's name or a blank one, and allow a change of capitals.");
    }

    if (!SavedLayouts.Delete(layouts, "night") || SavedLayouts.Find(layouts, "Night") is not null || layouts.Count != 9 || SavedLayouts.Delete(layouts, "Night"))
    {
        problems.Add("Saved layouts: deleting should take that layout out, whatever its capitals, once.");
    }

    if (SavedLayouts.CleanName(new string('x', 60)).Length != SavedLayouts.MaxNameLength)
    {
        problems.Add($"Saved layouts: names should be cut to {SavedLayouts.MaxNameLength} characters.");
    }

    // Saved on a screen to the right that's since been unplugged, onto a laptop's screen, and a group bigger than the screen.
    var places = new Dictionary<string, Lucent.Settings.WindowBounds>
    {
        ["Player"] = At(2200, 100, 400, 150), ["Equalizer"] = At(2200, 250, 400, 120), ["Visuals"] = At(2600, 100, 700, 600),
    };
    foreach (var area in new[] { new TileRect(0, 25, 1440, 805), new TileRect(0, 25, 1000, 500) })
    {
        var moved = SavedLayouts.MovedOnto(places, area);
        var shift = (X: moved["Player"].Left - places["Player"].Left, Y: moved["Player"].Top - places["Player"].Top);
        if (places.Any(entry => Math.Abs(moved[entry.Key].Left - entry.Value.Left - shift.X) > 0.01 || Math.Abs(moved[entry.Key].Top - entry.Value.Top - shift.Y) > 0.01
                || moved[entry.Key].Width != entry.Value.Width || moved[entry.Key].Height != entry.Value.Height))
        {
            problems.Add("Saved layouts: a layout moved onto another screen should keep its shape.");
        }

        var left = moved.Values.Min(place => place.Left);
        var top = moved.Values.Min(place => place.Top);
        var right = moved.Values.Max(place => place.Left + place.Width);
        var bottom = moved.Values.Max(place => place.Top + place.Height);
        var fits = right - left <= area.Width && bottom - top <= area.Height;
        if (left < area.Left || top < area.Top || fits && (right > area.Right || bottom > area.Bottom))
        {
            problems.Add($"Saved layouts: a layout moved onto {area} should be on it, at least its top left, but it's at {left:0},{top:0} to {right:0},{bottom:0}.");
        }
    }

    if (places["Player"].Left != 2200)
    {
        problems.Add("Saved layouts: moving a layout onto a screen changed the saved layout itself.");
    }
}
