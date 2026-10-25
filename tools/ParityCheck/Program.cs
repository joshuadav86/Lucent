using System.Reflection;
using System.Text.RegularExpressions;
using WinampInspiredVisualizer.Settings;

// Usage: dotnet run --project tools/ParityCheck
// Checks the Windows app (src/Lucent.Windows) and the Mac app (src/Lucent.Mac) keep the same
// features, three ways:
//   1. every source file in one app has its counterpart at the same place in the other (a WPF window's
//      .xaml and .xaml.cs pair with an Avalonia window's .axaml and .axaml.cs);
//   2. every setting in Lucent.Core's Settings is used by both apps, or by neither;
//   3. every public type in Lucent.Core that one app uses, the other uses too.
// Whatever's on one platform on purpose is listed in docs/PARITY.md's tables, with why and what the other
// platform has instead. Exits with 1 if anything is missing from one app and not listed there, or if an
// entry there is out of date (it's on both now, say), so the list stays true.
var repository = FindRepository();
var apps = new[]
{
    new App("Windows", Path.Combine(repository, "src", "Lucent.Windows")),
    new App("Mac", Path.Combine(repository, "src", "Lucent.Mac")),
};
var parityPath = Path.Combine(repository, "docs", "PARITY.md");
var listed = ReadListed(parityPath);
var problems = new List<string>();

// ---- 1. Files ----

var filesByApp = apps.ToDictionary(app => app, app => SourceFiles(app.Folder).ToDictionary(file => PairKey(file)));
foreach (var app in apps)
{
    var other = apps.Single(candidate => candidate != app);
    foreach (var (key, file) in filesByApp[app])
    {
        var isListed = listed.Any(entry => entry.Platform == app.Name && entry.IsFile && PairKey(entry.Item) == key);
        if (!filesByApp[other].ContainsKey(key) && !isListed)
        {
            problems.Add($"{app.Relative(file)} is only in the {app.Name} app: add its counterpart to {other.Relative(other.Folder)}, "
                + "or list it in docs/PARITY.md with why and what the other app has instead.");
        }
    }

    foreach (var entry in listed.Where(entry => entry.Platform == app.Name && entry.IsFile))
    {
        var key = PairKey(entry.Item);
        if (!filesByApp[app].ContainsKey(key))
        {
            problems.Add($"docs/PARITY.md lists {entry.Item} as {app.Name}-only, but {app.Relative(app.Folder)} has no such file.");
        }
        else if (filesByApp[other].ContainsKey(key))
        {
            problems.Add($"docs/PARITY.md lists {entry.Item} as {app.Name}-only, but the {other.Name} app has it too: take it off the list.");
        }
    }
}

// ---- 2 and 3. Settings and Lucent.Core's types ----

var core = typeof(AppSettings).Assembly;
var settings = core.GetExportedTypes()
    .Where(type => type.Namespace == typeof(AppSettings).Namespace)
    .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
    .Select(property => property.Name)
    .ToHashSet();
var types = core.GetExportedTypes()
    .Where(type => !type.IsNested)
    .Select(type => type.Name.Split('`')[0])
    .ToHashSet();
var namesByApp = apps.ToDictionary(app => app, app => NamesUsed(app.Folder, filesByApp[app].Values));

void CheckNames(IReadOnlySet<string> names, string kind, bool unusedIsFine)
{
    foreach (var name in names.Order())
    {
        var users = apps.Where(app => namesByApp[app].Contains(name)).ToList();
        var entry = listed.FirstOrDefault(candidate => !candidate.IsFile && candidate.Item == name);
        if (users.Count == 1)
        {
            var app = users[0];
            var other = apps.Single(candidate => candidate != app);
            if (entry is null)
            {
                problems.Add($"The {kind} {name} is only used by the {app.Name} app: use it in the {other.Name} app too, "
                    + "or list it in docs/PARITY.md with why and what the other app has instead.");
            }
            else if (entry.Platform != app.Name)
            {
                problems.Add($"docs/PARITY.md lists {name} as {entry.Platform}-only, but it's the {app.Name} app that uses it.");
            }
        }
        else if (entry is not null && (users.Count == 2 || !unusedIsFine))
        {
            problems.Add($"docs/PARITY.md lists {name} as {entry.Platform}-only, but "
                + (users.Count == 2 ? "both apps use it now: take it off the list." : "neither app uses it."));
        }
    }
}

CheckNames(settings, "setting", unusedIsFine: true);
CheckNames(types, "Lucent.Core type", unusedIsFine: true);
foreach (var entry in listed.Where(entry => !entry.IsFile && !settings.Contains(entry.Item) && !types.Contains(entry.Item)))
{
    problems.Add($"docs/PARITY.md lists {entry.Item}, which isn't a file, a setting, or a public type in Lucent.Core.");
}

// ---- Report ----

var github = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
foreach (var problem in problems)
{
    Console.WriteLine(github ? $"::error::{problem}" : problem);
}

Console.WriteLine($"Checked {filesByApp.Values.Sum(files => files.Count)} files, {settings.Count} settings, and {types.Count} Lucent.Core types; "
    + $"{listed.Count} things are listed as one-platform in docs/PARITY.md. "
    + (problems.Count == 0 ? "The apps match." : $"{problems.Count} problem(s)."));
return problems.Count == 0 ? 0 : 1;

static string FindRepository()
{
    for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
    {
        if (Directory.Exists(Path.Combine(folder.FullName, "src", "Lucent.Core")))
        {
            return folder.FullName;
        }
    }

    throw new DirectoryNotFoundException("Run this from inside the Lucent repository.");
}

static IEnumerable<string> SourceFiles(string folder) => Directory
    .EnumerateFiles(folder, "*", SearchOption.AllDirectories)
    .Where(file => file.EndsWith(".cs") || file.EndsWith(".xaml") || file.EndsWith(".axaml"))
    .Where(file => !Path.GetRelativePath(folder, file).Split(Path.DirectorySeparatorChar)
        .Any(part => part is "bin" or "obj"))
    .Select(file => Path.GetRelativePath(folder, file).Replace('\\', '/'));

// The same name for a file in either app: WPF's .xaml and Avalonia's .axaml both become .view.
static string PairKey(string file) => Regex.Replace(file.Replace('\\', '/'), @"\.a?xaml(\.cs)?$", ".view$1");

// Every name the app's code and windows mention, leaving out comments (a comment saying what the other
// app does isn't using it) and the test and screenshot tools, which aren't features.
static HashSet<string> NamesUsed(string folder, IEnumerable<string> files)
{
    var names = new HashSet<string>();
    foreach (var file in files)
    {
        var name = Path.GetFileName(file);
        if (name is "SelfTest.cs" or "LayoutCheck.cs" || name.StartsWith("Screenshots"))
        {
            continue;
        }

        var text = File.ReadAllText(Path.Combine(folder, file));
        text = Regex.Replace(text, @"<!--.*?-->", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        text = Regex.Replace(text, @"(^|[^:""])//.*$", "$1", RegexOptions.Multiline);
        foreach (Match match in Regex.Matches(text, @"[A-Za-z_][A-Za-z0-9_]*"))
        {
            names.Add(match.Value);
        }
    }

    return names;
}

// The rows of docs/PARITY.md's tables: the first column names the files (as each app has them, under its
// own folder), settings, or Lucent.Core types in `code`, and the second says which app has them.
static List<Listed> ReadListed(string path)
{
    var entries = new List<Listed>();
    foreach (var line in File.ReadLines(path))
    {
        var cells = line.Split('|', StringSplitOptions.TrimEntries);
        if (cells.Length < 4 || !line.TrimStart().StartsWith('|'))
        {
            continue;
        }

        var platform = cells[2] switch
        {
            "Windows" => "Windows",
            "Mac" => "Mac",
            _ => null,
        };
        if (platform is null)
        {
            continue;
        }

        foreach (Match match in Regex.Matches(cells[1], "`([^`]+)`"))
        {
            entries.Add(new Listed(match.Groups[1].Value, platform));
        }
    }

    return entries;
}

internal sealed record App(string Name, string Folder)
{
    public string Relative(string path) =>
        Path.GetRelativePath(Path.GetDirectoryName(Path.GetDirectoryName(Folder))!, Path.IsPathRooted(path) ? path : Path.Combine(Folder, path))
            .Replace('\\', '/');
}

internal sealed record Listed(string Item, string Platform)
{
    public bool IsFile => Item.EndsWith(".cs") || Item.EndsWith(".xaml") || Item.EndsWith(".axaml");
}
