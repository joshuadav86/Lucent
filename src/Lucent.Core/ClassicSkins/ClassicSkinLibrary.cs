using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer.ClassicSkins;

/// <summary>
/// The classic skins Lucent has: its own (Lucent Classic, built in) and the .wsz files the user has imported,
/// which are copied to the Skins folder in Lucent's data folder.
/// </summary>
/// <remarks>
/// Lucent doesn't ship anyone else's Winamp skins: each belongs to whoever drew it, and few were released for
/// passing on. People load their own, from their old Winamp folder or the Winamp Skin Museum.
/// </remarks>
public static class ClassicSkinLibrary
{
    public const string DefaultName = "Lucent Classic";

    /// <summary>The Internet Archive's searchable collection of classic Winamp skins.</summary>
    public const string SkinMuseumUrl = "https://skins.webamp.org/";

    private static readonly Lazy<ClassicSkin> LazyDefault = new(() =>
    {
        using var stream = typeof(ClassicSkinLibrary).Assembly.GetManifestResourceStream("LucentClassic.wsz")
            ?? throw new InvalidOperationException("Lucent Classic is missing from the build.");
        return ClassicSkin.Load(stream, DefaultName, fallback: null);
    });

    /// <summary>Lucent's own classic skin, which also fills in for sheets other skins leave out.</summary>
    public static ClassicSkin Default => LazyDefault.Value;

    public static string Folder => Path.Combine(AppDataFolder.Path, "Skins");

    /// <summary>Every classic skin, Lucent's own first, then the imported ones by name.</summary>
    public static IReadOnlyList<string> Names()
    {
        var names = new List<string> { DefaultName };
        if (Directory.Exists(Folder))
        {
            names.AddRange(Directory.EnumerateFiles(Folder)
                .Where(IsSkinFile)
                .Select(Path.GetFileNameWithoutExtension)
                .OfType<string>()
                .Where(name => !string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.CurrentCultureIgnoreCase));
        }

        return names;
    }

    /// <summary>Whether a file is a classic skin, going by its name (.wsz). Import takes a skin in a .zip too.</summary>
    public static bool IsSkinFile(string path) => Path.GetExtension(path).Equals(".wsz", StringComparison.OrdinalIgnoreCase);

    /// <summary>The skin with this name, or Lucent Classic when it's gone or can't be read.</summary>
    public static ClassicSkin Load(string? name)
    {
        if (string.IsNullOrEmpty(name) || string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            return Default;
        }

        try
        {
            var path = PathOf(name);
            return path is null ? Default : ClassicSkin.Load(path, Default);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return Default;
        }
    }

    /// <summary>Copies a .wsz into the Skins folder, replacing one of the same name, and returns its name.</summary>
    /// <exception cref="InvalidDataException">It isn't a classic Winamp skin.</exception>
    public static string Import(string path)
    {
        // Read it first, so a file that isn't a skin is turned away before anything is copied.
        ClassicSkin.Load(path, Default);
        var name = Path.GetFileNameWithoutExtension(path);
        if (string.Equals(name, DefaultName, StringComparison.OrdinalIgnoreCase))
        {
            name += " (imported)";
        }

        Directory.CreateDirectory(Folder);
        var target = Path.Combine(Folder, name + ".wsz");
        if (PathOf(name) is { } existing)
        {
            if (string.Equals(Path.GetFullPath(existing), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }

            File.Delete(existing);
        }

        File.Copy(path, target, overwrite: true);
        return name;
    }

    /// <summary>Deletes an imported skin. Lucent Classic can't be removed.</summary>
    public static void Remove(string name)
    {
        if (PathOf(name) is { } path)
        {
            File.Delete(path);
        }
    }

    private static string? PathOf(string name) => Directory.Exists(Folder)
        ? Directory.EnumerateFiles(Folder).FirstOrDefault(file =>
            IsSkinFile(file) && string.Equals(Path.GetFileNameWithoutExtension(file), name, StringComparison.OrdinalIgnoreCase))
        : null;
}
