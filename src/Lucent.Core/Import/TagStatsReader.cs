using System.Globalization;
using TagLib;
using Lucent.Library;

namespace Lucent.Import;

/// <summary>
/// Reads the ratings and play counts other players save in the songs themselves: the "popularimeter"
/// MusicBee, MediaMonkey and Windows Media Player write to MP3s, and the RATING, PLAY_COUNT and
/// LAST_PLAYED fields foobar2000 writes when its playback statistics are saved to the files' tags.
/// </summary>
public static class TagStatsReader
{
    /// <summary>Reads every file, reporting how many are done. Files with nothing to bring over are left out.</summary>
    public static ImportedLibrary ReadAll(IReadOnlyList<string> files, IProgress<int>? progress = null, CancellationToken cancel = default)
    {
        var library = new ImportedLibrary(ImportSource.FileTags);
        for (var i = 0; i < files.Count; i++)
        {
            cancel.ThrowIfCancellationRequested();
            if (Read(files[i]) is { HasStats: true } track)
            {
                library.Tracks.Add(track);
            }

            if (i % 50 == 0)
            {
                progress?.Report(i);
            }
        }

        progress?.Report(files.Count);
        return library;
    }

    public static ImportedTrack? Read(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path, TagWriter.TagLibMimeType(path), ReadStyle.None);
            var rating = 0;
            var plays = 0;
            DateTime? lastPlayed = null;

            void Field(string name, string? value)
            {
                if (string.IsNullOrWhiteSpace(value))
                {
                    return;
                }

                value = value.Trim();
                switch (name.ToUpperInvariant())
                {
                    case "RATING":
                        rating = Math.Max(rating, StarsFromText(value));
                        break;
                    case "FMPS_RATING":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var fraction))
                        {
                            rating = Math.Max(rating, ImportedTrack.StarsFromPercent(fraction * 100));
                        }

                        break;
                    case "PLAY_COUNT" or "PLAYCOUNT" or "FMPS_PLAYCOUNT" or "PLAY COUNT":
                        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var count))
                        {
                            plays = Math.Max(plays, (int)Math.Min(int.MaxValue, Math.Max(0, count)));
                        }

                        break;
                    case "LAST_PLAYED" or "LASTPLAYED" or "LAST_PLAYED_TIMESTAMP":
                        if (ParseTime(value) is { } time && (lastPlayed is null || time > lastPlayed))
                        {
                            lastPlayed = time;
                        }

                        break;
                }
            }

            if (file.GetTag(TagTypes.Id3v2) is TagLib.Id3v2.Tag id3)
            {
                foreach (var frame in id3.GetFrames())
                {
                    switch (frame)
                    {
                        case TagLib.Id3v2.PopularimeterFrame popularity:
                            rating = Math.Max(rating, StarsFromPopularimeter(popularity.Rating));
                            plays = Math.Max(plays, (int)Math.Min(int.MaxValue, popularity.PlayCount));
                            break;
                        case TagLib.Id3v2.PlayCountFrame count:
                            plays = Math.Max(plays, (int)Math.Min(int.MaxValue, count.PlayCount));
                            break;
                        case TagLib.Id3v2.UserTextInformationFrame text:
                            Field(text.Description ?? string.Empty, text.Text.FirstOrDefault());
                            break;
                    }
                }
            }

            if (file.GetTag(TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
            {
                foreach (var name in xiph)
                {
                    Field(name, xiph.GetFirstField(name));
                }
            }

            if (file.GetTag(TagTypes.Ape) is TagLib.Ape.Tag ape)
            {
                foreach (var name in ape)
                {
                    Field(name, ape.GetItem(name)?.ToString());
                }
            }

            if (file.GetTag(TagTypes.Apple) is TagLib.Mpeg4.AppleTag apple)
            {
                foreach (var name in new[] { "RATING", "FMPS_Rating", "PLAY_COUNT", "FMPS_Playcount", "LAST_PLAYED" })
                {
                    Field(name, apple.GetDashBox("com.apple.iTunes", name));
                }
            }

            return new ImportedTrack
            {
                Path = path,
                Title = file.Tag.Title,
                Artist = file.Tag.FirstPerformer,
                Album = file.Tag.Album,
                Plays = plays,
                Rating = rating,
                LastPlayed = lastPlayed,
            };
        }
        catch (Exception)
        {
            // Damaged or unusual files have nothing to bring over.
            return null;
        }
    }

    /// <summary>
    /// The popularimeter's 0–255, as Windows Media Player and MusicBee map their stars onto it
    /// (1, 64, 128, 196 and 255 for one to five).
    /// </summary>
    public static int StarsFromPopularimeter(byte value) => value switch
    {
        0 => 0,
        < 32 => 1,
        < 96 => 2,
        < 160 => 3,
        < 224 => 4,
        _ => 5,
    };

    /// <summary>foobar2000 writes 1–5; some players write 0–100 or 0–10.</summary>
    public static int StarsFromText(string value)
    {
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || number <= 0)
        {
            return 0;
        }

        return number switch
        {
            <= 5 => (int)Math.Round(number, MidpointRounding.AwayFromZero),
            <= 10 => (int)Math.Round(number / 2, MidpointRounding.AwayFromZero),
            _ => ImportedTrack.StarsFromPercent(Math.Min(number, 100)),
        };
    }

    private static DateTime? ParseTime(string value)
    {
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal | DateTimeStyles.AdjustToUniversal, out var time))
        {
            return time;
        }

        // Unix seconds.
        return long.TryParse(value, out var seconds) && seconds > 0 && seconds < 32503680000
            ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
            : null;
    }
}
