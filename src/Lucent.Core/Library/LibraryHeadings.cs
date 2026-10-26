using System.IO;

namespace Lucent.Library;

/// <summary>The headings a library file is listed under. <paramref name="Section"/> is null for a heading of its own.</summary>
public sealed record LibraryHeading(string Folder, string? Section, string? SectionDirectory);

/// <summary>
/// Which heading each library file goes under. Most files go under their folder ("Music / Albums / Some Album");
/// a file in a section folder (a store's, like Bandcamp) goes under that one heading, in a subheading for its
/// own folder ("Bandcamp / Artist / Album"), wherever the section folder sits in the library.
/// </summary>
public static class LibraryHeadings
{
    public static LibraryHeading For(string root, string directory, IReadOnlyList<string> sections)
    {
        foreach (var section in sections)
        {
            if (LibraryFolderList.IsAtOrInside(directory, section))
            {
                var name = LibraryFolderList.Name(section);
                return new LibraryHeading(Label(name, section, directory), name, section);
            }
        }

        return new LibraryHeading(Label(LibraryFolderList.Name(root), root, directory), null, null);
    }

    /// <summary>"Music", or "Music / Albums / Some Album" for files in subfolders.</summary>
    private static string Label(string rootName, string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory);
        return relative == "."
            ? rootName
            : rootName + " / " + relative.Replace(Path.DirectorySeparatorChar.ToString(), " / ");
    }
}
