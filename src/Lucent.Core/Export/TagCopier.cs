using TagLib;

namespace Lucent.Export;

/// <summary>Copies tags (title, artist, album, track number…) and artwork from one music file to another.</summary>
public static class TagCopier
{
    /// <summary>
    /// Copies everything TagLib understands from <paramref name="sourcePath"/> to <paramref name="destinationPath"/>.
    /// </summary>
    /// <param name="audioChanged">
    /// True when the audio was edited, so loudness information measured on the old audio (ReplayGain) is dropped.
    /// </param>
    /// <returns>False if the tags couldn't be copied (the audio itself is still fine).</returns>
    public static bool Copy(string sourcePath, string destinationPath, bool audioChanged)
    {
        try
        {
            using var source = TagLib.File.Create(sourcePath);
            using var destination = TagLib.File.Create(destinationPath);

            // Make sure the new file has the usual kind of tag for its format before copying into it.
            var preferred = destination switch
            {
                TagLib.Flac.File => TagTypes.Xiph,
                TagLib.Aiff.File => TagTypes.Id3v2,
                TagLib.Riff.File => TagTypes.Id3v2 | TagTypes.RiffInfo,
                _ => TagTypes.Id3v2,
            };
            foreach (var type in new[] { TagTypes.Xiph, TagTypes.Id3v2, TagTypes.RiffInfo })
            {
                if ((preferred & type) != 0)
                {
                    destination.GetTag(type, create: true);
                }
            }

            source.Tag.CopyTo(destination.Tag, overwrite: true);
            destination.Tag.Pictures = source.Tag.Pictures;
            if (audioChanged)
            {
                destination.Tag.ReplayGainTrackGain = double.NaN;
                destination.Tag.ReplayGainTrackPeak = double.NaN;
                destination.Tag.ReplayGainAlbumGain = double.NaN;
                destination.Tag.ReplayGainAlbumPeak = double.NaN;
            }

            destination.Save();
            return true;
        }
        catch (Exception)
        {
            // Untagged or unusual source files are common; the new file just goes without tags.
            return false;
        }
    }
}
