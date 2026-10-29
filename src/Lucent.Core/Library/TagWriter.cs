using System.IO;
using TagLib;

namespace Lucent.Library;

/// <summary>What to change in a file's pictures.</summary>
public sealed class ArtworkEdit
{
    private ArtworkEdit(IReadOnlyList<TagPicture>? replaceAll, TagPicture? frontCover)
    {
        ReplaceAll = replaceAll;
        FrontCover = frontCover;
    }

    /// <summary>When set, the file ends up with exactly these pictures (an empty list removes them all).</summary>
    public IReadOnlyList<TagPicture>? ReplaceAll { get; }

    /// <summary>When set, this becomes the file's front cover and its other pictures are kept.</summary>
    public TagPicture? FrontCover { get; }

    public static ArtworkEdit Replace(IEnumerable<TagPicture> pictures) => new(pictures.ToList(), null);

    public static ArtworkEdit SetFrontCover(TagPicture cover) => new(null, cover with { Type = PictureType.FrontCover });

    /// <summary>The pictures a file should have after this edit, given the ones it has now.</summary>
    public IReadOnlyList<TagPicture> ApplyTo(IReadOnlyList<TagPicture> current)
    {
        if (ReplaceAll is not null)
        {
            return ReplaceAll;
        }

        var result = current.ToList();
        var index = result.FindIndex(picture => picture.Type == PictureType.FrontCover);
        if (index >= 0)
        {
            result[index] = FrontCover!;
        }
        else
        {
            result.Insert(0, FrontCover!);
        }

        return result;
    }
}

/// <summary>The changes to write to one or more files. Fields that aren't listed are left as they are.</summary>
public sealed class TagChanges
{
    /// <summary>New text for each changed field. Blank text clears the field.</summary>
    public Dictionary<TagField, string> Fields { get; } = new();

    public ArtworkEdit? Artwork { get; set; }

    /// <summary>Put back the file's modified date after saving, so the library's newest-first order doesn't change.</summary>
    public bool KeepFileDates { get; set; } = true;

    public bool IsEmpty => Fields.Count == 0 && Artwork is null;
}

/// <summary>A file whose new tags are ready in a copy next to it, waiting to replace the original.</summary>
public sealed class PreparedSave
{
    internal PreparedSave(string path, string tempPath, DateTime created, DateTime modified, bool keepDates, IReadOnlyList<TagField> notStored)
    {
        Path = path;
        TempPath = tempPath;
        Created = created;
        Modified = modified;
        KeepDates = keepDates;
        NotStored = notStored;
    }

    public string Path { get; }

    internal string TempPath { get; }

    internal DateTime Created { get; }

    internal DateTime Modified { get; }

    internal bool KeepDates { get; }

    /// <summary>Fields this kind of file can't hold, so they were left out.</summary>
    public IReadOnlyList<TagField> NotStored { get; }
}

/// <summary>Raised after files' tags have been saved, so lists and the now playing info can show the new info.</summary>
public sealed class TagsSavedEventArgs : EventArgs
{
    public TagsSavedEventArgs(IReadOnlyList<string> paths) => Paths = paths;

    public IReadOnlyList<string> Paths { get; }
}

/// <summary>A save that failed, with the reason in plain words.</summary>
public sealed class TagSaveException : Exception
{
    public TagSaveException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>
/// Writes tags with TagLib without ever risking the music. TagLib rewrites files in place, so a crash or
/// a full disk halfway through could ruin a song. Instead the new tags are written to a copy next to the
/// file, the copy is checked, and only then does it replace the original in one step.
/// </summary>
public static class TagWriter
{
    internal const string TempSuffix = ".lucent-saving";
    internal const string BackupSuffix = ".lucent-backup";

    static TagWriter()
    {
        // New ID3 tags are written as v2.3, which every player and Windows itself reads.
        // Files that already have v2.4 tags keep v2.4.
        TagLib.Id3v2.Tag.DefaultVersion = 3;
        TagLib.Id3v2.Tag.ForceDefaultVersion = false;
    }

    /// <summary>Extensions TagLib can write tags to.</summary>
    public static bool CanWrite(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() is
        ".mp3" or ".flac" or ".ogg" or ".oga" or ".opus" or ".m4a" or ".mp4" or ".aac" or ".wma" or ".wav" or ".aif" or ".aiff" or ".aifc"
        or ".wv" or ".ape";

    /// <summary>
    /// The type name TagLib uses for a file. It's passed explicitly so the temporary copy, whose name ends
    /// in ".lucent-saving", is still read as the right format.
    /// </summary>
    public static string TagLibMimeType(string path) => System.IO.Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".aifc" => "taglib/aif",
        var extension => "taglib/" + extension.TrimStart('.'),
    };

    /// <summary>
    /// Step one, which can take a while: copies the file, writes the new tags into the copy and checks it.
    /// The original isn't touched, so this works while the file is playing. Throws <see cref="TagSaveException"/>
    /// (or an IO error) when it can't be done.
    /// </summary>
    public static PreparedSave Prepare(string path, TagChanges changes)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new FileNotFoundException("The file is missing.", path);
        }

        if (!CanWrite(path))
        {
            throw new TagSaveException("Lucent can't write info into this kind of file.");
        }

        if (info.IsReadOnly)
        {
            throw new TagSaveException("The file is marked read-only. Untick \"Read-only\" in its Properties in File Explorer, then try again.");
        }

        var created = info.CreationTime;
        var modified = info.LastWriteTime;
        var temp = path + TempSuffix;
        var mime = TagLibMimeType(path);
        DeleteQuietly(temp);

        try
        {
            System.IO.File.Copy(path, temp, overwrite: true);

            TimeSpan lengthBefore;
            using (var file = TagLib.File.Create(temp, mime, ReadStyle.Average))
            {
                lengthBefore = file.Properties?.Duration ?? TimeSpan.Zero;
                EnsureTags(file, path);
                Apply(file, changes);
                file.Save();
            }

            if (System.IO.Path.GetExtension(path).ToLowerInvariant() is ".aif" or ".aiff" or ".aifc")
            {
                RepairAiffSize(temp);
            }

            var notStored = Verify(temp, mime, lengthBefore, changes);
            return new PreparedSave(path, temp, created, modified, changes.KeepFileDates, notStored);
        }
        catch
        {
            DeleteQuietly(temp);
            throw;
        }
    }

    /// <summary>
    /// Step two, which is quick: swaps the checked copy in for the original. Windows won't allow this while
    /// another program (or Lucent's own player) has the file open; see AudioPlayer.RunWithFileReleasedAsync.
    /// </summary>
    public static void Commit(PreparedSave save)
    {
        if (!System.IO.File.Exists(save.TempPath))
        {
            throw new TagSaveException("The saved copy went missing before it could be used, so the original file was left as it was.");
        }

        var backup = save.Path + BackupSuffix;
        DeleteQuietly(backup);
        try
        {
            try
            {
                // One step on NTFS: the original keeps its attributes, permissions and creation date.
                System.IO.File.Replace(save.TempPath, save.Path, backup, ignoreMetadataErrors: true);
            }
            catch (IOException ex) when (!IsInUse(ex) && System.IO.File.Exists(save.Path) && System.IO.File.Exists(save.TempPath))
            {
                // Some network drives can't swap files in one step; move them one at a time instead.
                System.IO.File.Move(save.Path, backup);
                try
                {
                    System.IO.File.Move(save.TempPath, save.Path);
                }
                catch
                {
                    System.IO.File.Move(backup, save.Path);
                    throw;
                }
            }
        }
        catch
        {
            // If the swap stopped halfway, the original is still there under the backup name: put it back.
            if (!System.IO.File.Exists(save.Path) && System.IO.File.Exists(backup))
            {
                System.IO.File.Move(backup, save.Path);
            }

            DeleteQuietly(save.TempPath);
            throw;
        }

        DeleteQuietly(backup);
        try
        {
            // The library lists files by the later of these dates, so both are put back when keeping dates.
            // (Only Windows keeps a separate creation date; elsewhere setting it would change the modified date.)
            if (OperatingSystem.IsWindows())
            {
                System.IO.File.SetCreationTime(save.Path, save.Created);
            }

            if (save.KeepDates)
            {
                System.IO.File.SetLastWriteTime(save.Path, save.Modified);
            }
        }
        catch (Exception)
        {
            // The new tags are saved; a date that couldn't be put back isn't worth failing over.
        }
    }

    /// <summary>Throws away a prepared copy that won't be used.</summary>
    public static void Discard(PreparedSave save) => DeleteQuietly(save.TempPath);

    /// <summary>Why a save failed, in words for people rather than programmers.</summary>
    public static string Explain(Exception ex) => ex switch
    {
        TagSaveException => ex.Message,
        FileNotFoundException or DirectoryNotFoundException => "The file has been moved or deleted.",
        UnauthorizedAccessException => "Windows didn't let Lucent change it. The file may be read-only, or in a folder that needs administrator permission.",
        IOException io when IsInUse(io) => "Another program is using the file. Close it there and try again.",
        IOException io when IsDiskFull(io) => "The drive is full. Free up some space and try again.",
        PathTooLongException => "The file's location is too long for Windows to handle.",
        UnsupportedFormatException => "Lucent can't write info into this kind of file.",
        CorruptFileException => "The file looks damaged, so Lucent didn't change it.",
        _ => $"Something unexpected went wrong ({ex.Message}).",
    };

    /// <summary>Makes sure the file has the tag type that players expect for its format.</summary>
    private static void EnsureTags(TagLib.File file, string originalPath)
    {
        // The extension comes from the original: the copy being written ends in ".lucent-saving".
        switch (System.IO.Path.GetExtension(originalPath).ToLowerInvariant())
        {
            case ".mp3" or ".aac" or ".aif" or ".aiff" or ".aifc":
                EnsureId3v2(file);
                break;
            case ".wav":
                // ID3 holds everything (including pictures); RIFF INFO is what File Explorer reads.
                EnsureId3v2(file);
                file.GetTag(TagTypes.RiffInfo, true);
                break;
            case ".flac" or ".ogg" or ".oga" or ".opus":
                file.GetTag(TagTypes.Xiph, true);
                break;
            case ".m4a" or ".mp4":
                file.GetTag(TagTypes.Apple, true);
                break;
            case ".wma":
                file.GetTag(TagTypes.Asf, true);
                break;
            case ".wv" or ".ape":
                file.GetTag(TagTypes.Ape, true);
                break;
        }
    }

    private static void EnsureId3v2(TagLib.File file)
    {
        // ID3v2.2 is long obsolete and many players can't read it, so it's brought up to v2.3.
        if (file.GetTag(TagTypes.Id3v2, true) is TagLib.Id3v2.Tag { Version: < 3 } id3)
        {
            id3.Version = 3;
        }
    }

    private static void Apply(TagLib.File file, TagChanges changes)
    {
        if (file.GetTag(TagTypes.Asf) is TagLib.Asf.Tag asf)
        {
            // Some programs store the title and artist a second time under these names, and players
            // read those first, so the old ones would keep showing.
            if (changes.Fields.ContainsKey(TagField.Title))
            {
                asf.RemoveDescriptors("Title");
                asf.RemoveDescriptors("title");
            }

            if (changes.Fields.ContainsKey(TagField.Artist))
            {
                asf.RemoveDescriptors("Author");
                asf.RemoveDescriptors("author");
            }
        }

        var tag = file.Tag;
        foreach (var (field, raw) in changes.Fields)
        {
            var text = raw.Trim();
            var value = text.Length == 0 ? null : text;
            var number = TagFields.TryParseNumber(text, out var parsed) ? parsed : 0u;
            var names = TagFields.SplitList(text);
            switch (field)
            {
                case TagField.Title:
                    tag.Title = value;
                    break;
                case TagField.Artist:
                    tag.Performers = names;
                    break;
                case TagField.Album:
                    tag.Album = value;
                    break;
                case TagField.AlbumArtist:
                    tag.AlbumArtists = names;
                    break;
                case TagField.Year:
                    tag.Year = number;
                    break;
                case TagField.Track:
                    tag.Track = number;
                    break;
                case TagField.TrackCount:
                    tag.TrackCount = number;
                    break;
                case TagField.Disc:
                    tag.Disc = number;
                    break;
                case TagField.DiscCount:
                    tag.DiscCount = number;
                    break;
                case TagField.Genre:
                    tag.Genres = names;
                    break;
                case TagField.Composer:
                    tag.Composers = names;
                    break;
                case TagField.Bpm:
                    tag.BeatsPerMinute = number;
                    break;
                case TagField.Key:
                    tag.InitialKey = value;
                    break;
                case TagField.Comment:
                    tag.Comment = value;
                    break;
                case TagField.Lyrics:
                    tag.Lyrics = value;
                    break;
            }
        }

        if (changes.Artwork is { } artwork)
        {
            var current = (tag.Pictures ?? Array.Empty<IPicture>()).Where(picture => picture.Data is { Count: > 0 }).Select(TagPicture.From).ToList();
            var pictures = artwork.ApplyTo(current).Select(picture => picture.ToTagLib()).ToArray();
            SetPictures(file, pictures);
        }
    }

    private static void SetPictures(TagLib.File file, IPicture[] pictures)
    {
        if (file is TagLib.Flac.File flac)
        {
            // FLAC keeps pictures in their own blocks. Setting them through the combined tag would also copy
            // them into the Vorbis comment, so every picture would be stored twice.
            flac.GetTag(TagTypes.FlacMetadata, true).Pictures = pictures;
            if (flac.GetTag(TagTypes.Xiph) is { } xiph)
            {
                xiph.Pictures = Array.Empty<IPicture>();
            }

            return;
        }

        file.Tag.Pictures = pictures;
    }

    /// <summary>
    /// Opens the saved copy again to be sure it's still a playable file of the same length, and notes any
    /// fields that didn't stick because the format has nowhere to keep them.
    /// </summary>
    private static IReadOnlyList<TagField> Verify(string temp, string mime, TimeSpan lengthBefore, TagChanges changes)
    {
        try
        {
            using var check = TagLib.File.Create(temp, mime, ReadStyle.Average);
            var lengthAfter = check.Properties?.Duration ?? TimeSpan.Zero;
            if (lengthBefore > TimeSpan.Zero && Math.Abs((lengthAfter - lengthBefore).TotalSeconds) > 0.5)
            {
                throw new TagSaveException("The saved copy didn't check out, so the original file was left as it was.");
            }

            var notStored = new List<TagField>();
            var tag = check.Tag;
            foreach (var (field, text) in changes.Fields)
            {
                if (TagFields.Normalize(field, text) != TagFields.Normalize(field, ReadBack(tag, field)))
                {
                    notStored.Add(field);
                }
            }

            return notStored;
        }
        catch (TagSaveException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new TagSaveException("The saved copy didn't check out, so the original file was left as it was.", ex);
        }
    }

    private static string ReadBack(Tag tag, TagField field) => field switch
    {
        TagField.Title => tag.Title ?? string.Empty,
        TagField.Artist => TagFields.JoinList(tag.Performers),
        TagField.Album => tag.Album ?? string.Empty,
        TagField.AlbumArtist => TagFields.JoinList(tag.AlbumArtists),
        TagField.Year => TagFields.NumberText(tag.Year),
        TagField.Track => TagFields.NumberText(tag.Track),
        TagField.TrackCount => TagFields.NumberText(tag.TrackCount),
        TagField.Disc => TagFields.NumberText(tag.Disc),
        TagField.DiscCount => TagFields.NumberText(tag.DiscCount),
        TagField.Genre => TagFields.JoinList(tag.Genres),
        TagField.Composer => TagFields.JoinList(tag.Composers),
        TagField.Bpm => TagFields.NumberText(tag.BeatsPerMinute),
        TagField.Key => tag.InitialKey ?? string.Empty,
        TagField.Comment => tag.Comment ?? string.Empty,
        _ => tag.Lyrics ?? string.Empty,
    };

    /// <summary>
    /// TagLib adds a new ID3 chunk to the end of an AIFF file without updating the size in the file's
    /// header, so stricter players don't see the tags. This sets the size to cover every chunk, but only
    /// when the chunks line up exactly with the end of the file.
    /// </summary>
    private static void RepairAiffSize(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var header = new byte[12];
        if (stream.Read(header, 0, 12) != 12 || header[0] != 'F' || header[1] != 'O' || header[2] != 'R' || header[3] != 'M')
        {
            return;
        }

        var length = stream.Length;
        var chunk = new byte[8];
        var position = 12L;
        while (position + 8 <= length)
        {
            stream.Position = position;
            if (stream.Read(chunk, 0, 8) != 8)
            {
                return;
            }

            long size = (uint)(chunk[4] << 24 | chunk[5] << 16 | chunk[6] << 8 | chunk[7]);
            position += 8 + size + (size & 1);
        }

        // A missing pad byte after an odd-sized last chunk is common and harmless.
        if (position != length && position != length + 1)
        {
            return;
        }

        var declared = (long)(uint)(header[4] << 24 | header[5] << 16 | header[6] << 8 | header[7]);
        var actual = length - 8;
        if (declared != actual && actual <= uint.MaxValue)
        {
            stream.Position = 4;
            stream.Write(new[] { (byte)(actual >> 24), (byte)(actual >> 16), (byte)(actual >> 8), (byte)actual }, 0, 4);
        }
    }

    // Windows error codes: 32 = in use by another process, 33 = part of it is locked.
    internal static bool IsInUse(IOException ex) => (ex.HResult & 0xFFFF) is 32 or 33;

    // 112 = not enough space on the disk, 39 = the disk is full.
    private static bool IsDiskFull(IOException ex) => (ex.HResult & 0xFFFF) is 112 or 39;

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception)
        {
            // A leftover copy is harmless; the next save replaces it.
        }
    }
}
