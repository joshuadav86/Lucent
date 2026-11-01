using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Lucent.ClassicSkins;

/// <summary>
/// A classic Winamp skin (.wsz): a zip of .bmp sprite sheets and a few text files, laid out the way Winamp 2
/// laid them out. Lucent reads the main window's, the equalizer's, the playlist's and the other windows' (gen) sheets, the visualizer's
/// colours and the playlist's colours and font. Like Winamp, a skin
/// that leaves a sheet out borrows it from the default skin (Lucent's own), and one without a balance
/// sheet uses its volume sheet for both.
/// </summary>
public sealed partial class ClassicSkin
{
    /// <summary>The sprite sheets the player, equalizer and playlist windows draw from, by file name without ".bmp".</summary>
    public static readonly string[] SheetNames =
    {
        "main", "titlebar", "cbuttons", "numbers", "nums_ex", "playpaus", "monoster", "posbar", "volume", "balance",
        "shufrep", "text", "eqmain", "pledit", "gen",
    };

    // A skin is a few hundred kilobytes; anything far bigger isn't one, and isn't unpacked.
    private const long MaxFileSize = 8 * 1024 * 1024;

    private readonly Dictionary<string, SkinImage> _images;

    private ClassicSkin(string name, Dictionary<string, SkinImage> images, uint[] visColors, ClassicPlaylistStyle playlist)
    {
        Name = name;
        _images = images;
        VisColors = visColors;
        Playlist = playlist;
        UsesExtendedNumbers = images.ContainsKey("nums_ex");
    }

    public string Name { get; }

    /// <summary>The visualizer's 24 colours from viscolor.txt (0xAARRGGBB): background, dots, 16 for the bars from top to bottom, 5 for the oscilloscope, and the peaks.</summary>
    public uint[] VisColors { get; }

    /// <summary>The playlist's text colours and font, from pledit.txt.</summary>
    public ClassicPlaylistStyle Playlist { get; }

    /// <summary>True when the time is drawn from nums_ex.bmp, which has its own minus sign, rather than numbers.bmp.</summary>
    public bool UsesExtendedNumbers { get; }

    /// <summary>The sheet with this name (such as "cbuttons"), or null if neither this skin nor the default has it.</summary>
    public SkinImage? Sheet(string name) => _images.TryGetValue(name, out var image) ? image : null;

    /// <summary>Reads a .wsz or .zip file, or a folder of a skin's files.</summary>
    /// <param name="fallback">Where sheets this skin leaves out come from: the default skin.</param>
    public static ClassicSkin Load(string path, ClassicSkin? fallback)
    {
        var name = Path.GetFileNameWithoutExtension(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (Directory.Exists(path))
        {
            var files = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)
                .Select(file => (Path.GetRelativePath(path, file), (Func<byte[]?>)(() => ReadSmall(file))));
            return Build(name, files, fallback);
        }

        using var stream = File.OpenRead(path);
        return Load(stream, name, fallback);
    }

    /// <summary>Reads a .wsz (a zip) from a stream.</summary>
    /// <exception cref="InvalidDataException">It isn't a zip, or has no main window picture.</exception>
    public static ClassicSkin Load(Stream wsz, string name, ClassicSkin? fallback)
    {
        using var zip = new ZipArchive(wsz, ZipArchiveMode.Read, leaveOpen: true);
        var files = zip.Entries
            .Where(entry => entry.Length is > 0 and < MaxFileSize)
            .Select(entry => (entry.FullName, (Func<byte[]?>)(() => ReadEntry(entry))))
            .ToList();
        return Build(name, files, fallback);
    }

    private static ClassicSkin Build(string name, IEnumerable<(string Path, Func<byte[]?> Read)> files, ClassicSkin? fallback)
    {
        // Skins often keep their files in a folder inside the zip, and name them in any case. Where the same
        // name turns up twice, the one nearest the top wins.
        var byName = new Dictionary<string, Func<byte[]?>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, read) in files.OrderBy(file => file.Path.Count(c => c is '/' or '\\')))
        {
            var fileName = Path.GetFileName(path.Replace('\\', '/'));
            byName.TryAdd(fileName, read);
        }

        var images = new Dictionary<string, SkinImage>();
        foreach (var sheet in SheetNames)
        {
            if (byName.TryGetValue(sheet + ".bmp", out var read) && read() is { } bytes && BmpReader.Read(bytes) is { } image)
            {
                images[sheet] = image;
            }
        }

        if (!images.ContainsKey("main"))
        {
            throw new InvalidDataException("This isn't a classic .wsz skin: it has no main.bmp.");
        }

        if (!images.ContainsKey("balance") && images.TryGetValue("volume", out var volume))
        {
            images["balance"] = volume;
        }

        if (fallback is not null)
        {
            // nums_ex.bmp is never borrowed: a skin with its own numbers.bmp should show those.
            foreach (var sheet in SheetNames.Where(sheet => sheet != "nums_ex"))
            {
                if (!images.ContainsKey(sheet) && fallback.Sheet(sheet) is { } borrowed)
                {
                    images[sheet] = borrowed;
                }
            }
        }

        var colours = byName.TryGetValue("viscolor.txt", out var readColours) && readColours() is { } text
            ? ParseVisColors(Encoding.Latin1.GetString(text), fallback?.VisColors)
            : fallback?.VisColors ?? DefaultVisColors;
        var playlist = byName.TryGetValue("pledit.txt", out var readPlaylist) && readPlaylist() is { } ini
            ? ClassicPlaylistStyle.Parse(Encoding.Latin1.GetString(ini), fallback?.Playlist)
            : fallback?.Playlist ?? ClassicPlaylistStyle.Default;
        return new ClassicSkin(name, images, colours, playlist);
    }

    /// <summary>
    /// viscolor.txt: one "r,g,b" a line, usually followed by a comment. Lines that don't parse, and any of the
    /// 24 a skin leaves out, keep the default's colour.
    /// </summary>
    public static uint[] ParseVisColors(string text, uint[]? defaults)
    {
        var colours = (defaults ?? DefaultVisColors).ToArray();
        var index = 0;
        foreach (var line in text.Split('\n'))
        {
            if (index >= colours.Length)
            {
                break;
            }

            // A number too big for an int is still just "more than 255".
            var numbers = Number().Matches(line).Take(3)
                .Select(match => int.TryParse(match.Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
                    ? number
                    : match.Value.StartsWith('-') ? 0 : 255)
                .ToArray();
            if (numbers.Length < 3)
            {
                continue;
            }

            colours[index++] = 0xFF000000 | (uint)Math.Clamp(numbers[0], 0, 255) << 16
                | (uint)Math.Clamp(numbers[1], 0, 255) << 8 | (uint)Math.Clamp(numbers[2], 0, 255);
        }

        return colours;
    }

    /// <summary>Lucent Classic's visualizer colours, used when nothing else gives any.</summary>
    public static uint[] DefaultVisColors { get; } =
    {
        0xFF07070C, 0xFF1C1C28,
        0xFFF0ABFC, 0xFFE38CF7, 0xFFD371F4, 0xFFC05CF0, 0xFFAB4EEA, 0xFF9447E3, 0xFF7C45DD, 0xFF6650D9,
        0xFF5560D8, 0xFF4573D8, 0xFF3787D9, 0xFF2B9BDB, 0xFF22AFDD, 0xFF1EC2E0, 0xFF1FD3E6, 0xFF22E0EC,
        0xFFF5F5FF, 0xFFD6CCFF, 0xFFB4A4FF, 0xFF8F7CF0, 0xFF6C5AD0,
        0xFFFFFFFF,
    };

    private static byte[]? ReadSmall(string file)
    {
        var info = new FileInfo(file);
        return info.Length is > 0 and < MaxFileSize ? File.ReadAllBytes(file) : null;
    }

    private static byte[]? ReadEntry(ZipArchiveEntry entry)
    {
        try
        {
            using var stream = entry.Open();
            using var memory = new MemoryStream((int)entry.Length);
            stream.CopyTo(memory);
            return memory.ToArray();
        }
        catch (InvalidDataException)
        {
            // A damaged or oddly compressed file: treat it as missing.
            return null;
        }
    }

    [GeneratedRegex(@"-?\d+")]
    private static partial Regex Number();
}
