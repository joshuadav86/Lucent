using System.IO;
using Lucent.Export;
using Lucent.Video;

namespace Lucent.Editor;

/// <summary>
/// A video open in the editor: its sound is what the waveform shows and the editor plays, and its pictures are cut
/// along with it, without re-encoding (<see cref="Mp4Cutter"/>). Only Delete and Trim change a video, since what's
/// saved is the original pictures and sound, copied as they are.
/// </summary>
/// <remarks>
/// <para>
/// The cut is kept as the stretches of the original video that are left (<see cref="Kept"/>), one after another,
/// for each state of the editor's history (<see cref="EditSession.StateId"/>), so undo, redo and jumping in the
/// history bring the pictures back with the sound.
/// </para>
/// <para>
/// A video can only be cut where its pictures allow (see <see cref="Mp4Cutter"/>): what's left after a cut has to
/// start on a keyframe. <see cref="SnapDelete"/> and <see cref="SnapTrim"/> move a selection's edges to the nearest
/// places that work, before the sound is cut at the same places.
/// </para>
/// </remarks>
public sealed class VideoEdit
{
    private readonly Dictionary<long, List<VideoRange>> _states = new();
    private List<VideoRange> _kept;

    private VideoEdit(Mp4Movie movie, int sampleRate)
    {
        Movie = movie;
        SampleRate = sampleRate;
        _kept = movie.Snap(new[] { new VideoRange(0, movie.Duration) }).ToList();
        _states[0] = _kept;
    }

    public Mp4Movie Movie { get; }

    /// <summary>The sound's sample rate, which turns the editor's frames into seconds.</summary>
    public int SampleRate { get; }

    /// <summary>The stretches of the original video left, in seconds of the original, in the order they play.</summary>
    public IReadOnlyList<VideoRange> Kept => _kept;

    /// <summary>How long the cut video is, in seconds.</summary>
    public double Length => _kept.Sum(range => range.Length);

    /// <summary>True for a file that opens in the editor as a video: one with pictures that can be cut without re-encoding.</summary>
    public static bool IsEditableVideo(string path) => VideoFiles.IsVideo(path) && Mp4Cutter.CanCut(path);

    /// <summary>
    /// What a menu item that opens <paramref name="path"/> in the editor says, and whether it can: "Edit video…" for a
    /// video the editor cuts, a turned-off item saying which ones it cuts for any other video, and "Open in editor…"
    /// for a sound.
    /// </summary>
    public static (string Header, bool CanOpen) EditorMenuItem(string path) =>
        !VideoFiles.IsVideo(path) ? ("Open in editor…", true)
        : IsEditableVideo(path) ? ("Edit video…", true)
        : ("Edit video (MP4, M4V and MOV only, for now)", false);

    /// <summary>
    /// Opens a video for editing: reads its index, then decodes its sound for the waveform. Throws
    /// <see cref="AudioFileException"/> with a message for the user.
    /// </summary>
    public static (VideoEdit Video, AudioBuffer Audio, SourceFileInfo Info, bool MixedDown) Load(string path, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        Mp4Movie movie;
        try
        {
            movie = Mp4Cutter.Read(path);
        }
        catch (VideoCutException ex)
        {
            throw new AudioFileException(ex.Message, ex);
        }

        if (!movie.HasVideo)
        {
            throw new AudioFileException("This file has no pictures, so it isn't a video.");
        }

        // AAC (nearly every video's sound) and uncompressed sound are decoded straight from the file by Lucent's own
        // decoders, the same on Windows and the Mac. Anything else is copied out on its own (an .m4a file) for the
        // system's music decoders.
        try
        {
            if (Mp4Sound.Decode(movie, AudioFileLoader.MaxDuration, progress, cancellation) is { } decoded)
            {
                var info = new SourceFileInfo(path, decoded.SampleRate, decoded.SourceChannels, decoded.Bits, false, decoded.BitrateKbps);
                return (new VideoEdit(movie, decoded.SampleRate), new AudioBuffer(decoded.Channels, decoded.SampleRate), info, decoded.SourceChannels > 2);
            }
        }
        catch (InvalidDataException ex)
        {
            throw new AudioFileException(ex.Message, ex);
        }
        catch (IOException ex)
        {
            throw new AudioFileException($"The video couldn't be read: {ex.Message}", ex);
        }

        // The sound is copied out on its own (an .m4a file), which every system's music decoders read, then decoded.
        var sound = Path.Combine(Path.GetTempPath(), $"lucent-sound-{Guid.NewGuid():N}.m4a");
        try
        {
            if (!Mp4Cutter.WriteSound(movie, sound, cancellation))
            {
                throw new AudioFileException("This video has no sound. The editor cuts a video by its sound, so it can't open one without.");
            }

            (AudioBuffer Audio, SourceFileInfo Info, bool MixedDown) loaded;
            try
            {
                loaded = AudioFileLoader.Load(sound, progress, cancellation);
            }
            catch (AudioFileException)
            {
                // A sound the system can't read in an .m4a (a camera's uncompressed sound in a .mov, say) may still
                // read from the video itself.
                try
                {
                    loaded = AudioFileLoader.Load(path, progress, cancellation);
                }
                catch (AudioFileException ex)
                {
                    var format = movie.Tracks.FirstOrDefault(track => track.IsSound)?.Format ?? string.Empty;
                    throw new AudioFileException(
                        $"The editor couldn't decode this video's sound ({SoundFormatName(format)}), so it can't open the video. It plays in the player as usual.", ex);
                }
            }

            var video = new VideoEdit(movie, loaded.Audio.SampleRate);
            return (video, loaded.Audio, loaded.Info with { Path = path }, loaded.MixedDown);
        }
        catch (VideoCutException ex)
        {
            throw new AudioFileException(ex.Message, ex);
        }
        finally
        {
            try
            {
                File.Delete(sound);
            }
            catch (IOException)
            {
            }
        }
    }

    // A sound format's name, from its code in the file.
    private static string SoundFormatName(string format) => format.Trim().ToLowerInvariant() switch
    {
        "mp4a" => "AAC",
        "ac-3" => "Dolby Digital",
        "ec-3" => "Dolby Digital Plus",
        "opus" => "Opus",
        "flac" => "FLAC",
        "alac" => "Apple Lossless",
        ".mp3" or "mp3" => "MP3",
        "lpcm" or "sowt" or "twos" or "in24" or "in32" or "fl32" or "fl64" or "raw" or "ipcm" or "fpcm" => "uncompressed",
        "samr" => "AMR",
        "" => "an unknown format",
        _ => $"\"{format.Trim()}\"",
    };

    // ---- Times ----

    /// <summary>Where in the original video a moment of the cut one is, in seconds.</summary>
    public double OriginalTime(double time)
    {
        var at = 0.0;
        foreach (var range in _kept)
        {
            if (time < at + range.Length)
            {
                return range.Start + Math.Max(0, time - at);
            }

            at += range.Length;
        }

        return _kept.Count > 0 ? _kept[^1].End : 0;
    }

    /// <summary>Where the cut video's stretches join, in seconds of the cut video (not counting its start).</summary>
    public IEnumerable<double> Joins()
    {
        var at = 0.0;
        for (var i = 0; i < _kept.Count - 1; i++)
        {
            at += _kept[i].Length;
            yield return at;
        }
    }

    private double Seconds(int frame) => frame / (double)SampleRate;

    private int Frame(double seconds) => (int)Math.Round(seconds * SampleRate);

    // ---- Snapping ----

    /// <summary>
    /// The part of <paramref name="selection"/> (in the editor's frames) that Delete really takes out: it starts
    /// where a picture can end, and ends on a keyframe, both the nearest to the selection's edges. Empty when there's
    /// no keyframe close enough to take anything out.
    /// </summary>
    public FrameRange SnapDelete(FrameRange selection)
    {
        var start = Snap(Seconds(selection.Start), keyframe: false);
        var end = Snap(Seconds(selection.End), keyframe: true);
        return end > start ? new FrameRange(Frame(start), Frame(end) - Frame(start)) : default;
    }

    /// <summary>The part of <paramref name="selection"/> Trim really keeps: from a keyframe, to where a picture can end.</summary>
    public FrameRange SnapTrim(FrameRange selection)
    {
        var start = Snap(Seconds(selection.Start), keyframe: true);
        var end = Snap(Seconds(selection.End), keyframe: false);
        return end > start ? new FrameRange(Frame(start), Frame(end) - Frame(start)) : default;
    }

    // The nearest place to `time` (in the cut video) where what follows can start (a keyframe), or where what came
    // before can end. The ends of each stretch work for both.
    private double Snap(double time, bool keyframe)
    {
        var best = 0.0;
        var bestDistance = double.MaxValue;
        var at = 0.0;
        foreach (var range in _kept)
        {
            void Consider(double original)
            {
                var place = at + (original - range.Start);
                if (Math.Abs(place - time) < bestDistance)
                {
                    bestDistance = Math.Abs(place - time);
                    best = place;
                }
            }

            Consider(range.Start);
            Consider(range.End);
            var places = keyframe ? Movie.Keyframes : Movie.Tracks.First(track => track.IsVideo && track.Samples.Length > 0).CleanPoints.Select(point => point.Time);
            foreach (var place in places)
            {
                if (place > range.Start && place < range.End)
                {
                    Consider(place);
                }
            }

            at += range.Length;
        }

        return best;
    }

    // ---- Cutting ----

    /// <summary>Takes <paramref name="range"/> (snapped with <see cref="SnapDelete"/>) out, for the history state <paramref name="state"/>.</summary>
    public void Delete(FrameRange range, long state)
    {
        var (from, to) = (Seconds(range.Start), Seconds(range.End));
        _kept = Keep(0, from).Concat(Keep(to, double.MaxValue)).ToList();
        _states[state] = _kept;
    }

    /// <summary>Keeps only <paramref name="range"/> (snapped with <see cref="SnapTrim"/>), for the history state <paramref name="state"/>.</summary>
    public void Trim(FrameRange range, long state)
    {
        _kept = Keep(Seconds(range.Start), Seconds(range.End)).ToList();
        _states[state] = _kept;
    }

    /// <summary>Goes to the cut for the history state the editor is now in (after an undo, say).</summary>
    public void Follow(long state)
    {
        if (_states.TryGetValue(state, out var kept))
        {
            _kept = kept;
        }
    }

    /// <summary>True when anything of the video has been taken out.</summary>
    public bool IsCut => !_kept.SequenceEqual(_states[0]);

    // The original stretches between two moments of the cut video.
    private IEnumerable<VideoRange> Keep(double from, double to)
    {
        var at = 0.0;
        foreach (var range in _kept)
        {
            var start = Math.Max(from, at);
            var end = Math.Min(to, at + range.Length);
            if (end - start > 0.0005)
            {
                yield return new VideoRange(range.Start + (start - at), range.Start + (end - at));
            }

            at += range.Length;
        }
    }

    // ---- Saving ----

    /// <summary>
    /// Writes the cut video into a temporary file next to <paramref name="target"/> and returns its path, for
    /// <see cref="SafeFile.Commit"/>. Throws <see cref="VideoCutException"/>.
    /// </summary>
    public string Write(string target, IProgress<double>? progress, CancellationToken cancellation)
    {
        var temporary = SafeFile.TemporaryPathFor(target);
        Mp4Cutter.Write(Movie, _kept, temporary, progress, cancellation);
        SafeFile.Hide(temporary);
        return temporary;
    }
}
