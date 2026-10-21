using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace WinampInspiredVisualizer.Video;

/// <summary>A problem cutting a video, with a message that can be shown to the user as it is.</summary>
public sealed class VideoCutException : Exception
{
    public VideoCutException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>A stretch of a video, in seconds from the first picture shown.</summary>
public readonly record struct VideoRange(double Start, double End)
{
    public double Length => End - Start;
}

/// <summary>
/// Cuts MP4, M4V and MOV videos without re-encoding: the pictures and sound kept are copied as they are into a
/// new file, and only the file's index (its "moov") is written afresh. Saving takes as long as copying the file.
/// </summary>
/// <remarks>
/// <para>
/// A cut can only start on a keyframe, a picture that's complete on its own; the ones between keyframes only
/// describe changes from the pictures before them. <see cref="Mp4Movie.SnapStart"/> moves a start back to the
/// keyframe at or before it. Where a video has B-frames (pictures stored out of the order they're shown), a cut
/// can also only end where every picture stored before the cut is shown before it, every few pictures;
/// <see cref="Mp4Movie.SnapEnd"/> moves an end to the nearest such place. Without B-frames, any picture will do.
/// </para>
/// <para>
/// The sound is cut to match, to the nearest packet of compressed sound (about 21 ms for AAC). Where a kept
/// stretch's sound comes out a little long or short, the next stretch's sound starts a little earlier or later,
/// so it never drifts from the picture. Other tracks (chapter titles, timecode) are cut the same way.
/// </para>
/// <para>
/// Fragmented MP4 files (streamed DASH pieces, some screen recorders while recording) aren't cut.
/// </para>
/// </remarks>
public static class Mp4Cutter
{
    /// <summary>The extensions that can be cut without re-encoding.</summary>
    public static readonly IReadOnlySet<string> Extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".m4v", ".mov" };

    /// <summary>True for a file whose extension this can cut.</summary>
    public static bool CanCut(string path) => Extensions.Contains(Path.GetExtension(path));

    /// <summary>Reads a video's index, to snap cuts and then write them. Throws <see cref="VideoCutException"/>.</summary>
    public static Mp4Movie Read(string path)
    {
        try
        {
            using var file = File.OpenRead(path);
            return Mp4Movie.Read(file, path);
        }
        catch (VideoCutException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VideoCutException("The video couldn't be read: " + ex.Message, ex);
        }
        catch (Exception ex) when (ex is EndOfStreamException or ArgumentException or IndexOutOfRangeException or OverflowException or InvalidDataException)
        {
            throw new VideoCutException("The video's index looks damaged, so it can't be cut without re-encoding.", ex);
        }
    }

    /// <summary>
    /// Writes just the sound of <paramref name="movie"/> (its first sound track, copied as it is) into
    /// <paramref name="target"/>, a new .m4a file, for decoders that read music files but not videos. Returns false
    /// when the video has no sound. Throws <see cref="VideoCutException"/>.
    /// </summary>
    public static bool WriteSound(Mp4Movie movie, string target, CancellationToken cancellation = default)
    {
        var sound = movie.Tracks.FirstOrDefault(track => track.IsSound && track.Samples.Length > 0);
        if (sound is null)
        {
            return false;
        }

        try
        {
            using var source = File.OpenRead(movie.Path);
            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            movie.WriteCut(source, output, new[] { new VideoRange(0, movie.Duration) }, sound, null, cancellation);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new VideoCutException("The video's sound couldn't be read: " + ex.Message, ex);
        }
    }

    /// <summary>
    /// Writes just the pictures of <paramref name="movie"/> from the keyframe at or before <paramref name="from"/> to
    /// the first place after <paramref name="to"/> a cut can end, copied as they are, into <paramref name="target"/>:
    /// a new video that starts at that keyframe. Returns where that is in <paramref name="movie"/>.
    /// </summary>
    internal static double WritePictures(Mp4Movie movie, double from, double to, string target, CancellationToken cancellation)
    {
        var video = movie.Tracks.First(track => track.IsVideo && track.Samples.Length > 0);
        var start = movie.SnapStart(from);
        var end = video.CleanPoints.FirstOrDefault(point => point.Time >= to - 0.0005, video.CleanPoints[^1]).Time;
        using var source = File.OpenRead(movie.Path);
        using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        movie.WriteCut(source, output, new[] { new VideoRange(start, end) }, video, null, cancellation);
        return start;
    }

    /// <summary>
    /// Writes the stretches of <paramref name="movie"/> in <paramref name="keep"/> (snapped as
    /// <see cref="Mp4Movie.Snap"/> does) one after another into <paramref name="target"/>, a new file.
    /// Throws <see cref="VideoCutException"/>.
    /// </summary>
    public static void Write(Mp4Movie movie, IReadOnlyList<VideoRange> keep, string target, IProgress<double>? progress = null,
        CancellationToken cancellation = default)
    {
        var ranges = movie.Snap(keep);
        if (ranges.Count == 0)
        {
            throw new VideoCutException("Nothing of the video would be left.");
        }

        var created = false;
        try
        {
            using var source = File.OpenRead(movie.Path);
            using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20);
            created = true;
            movie.WriteCut(source, output, ranges, null, progress, cancellation);
        }
        catch (Exception ex)
        {
            // Never leaves half a video behind.
            if (created)
            {
                try
                {
                    File.Delete(target);
                }
                catch (IOException)
                {
                }
            }

            if (ex is IOException or UnauthorizedAccessException)
            {
                throw new VideoCutException("The cut video couldn't be written: " + ex.Message, ex);
            }

            throw;
        }
    }
}

/// <summary>An MP4 or MOV file's index: its tracks and every sample (picture or packet of sound) in them.</summary>
public sealed class Mp4Movie
{
    // Boxes that hold other boxes, among the ones this reads into.
    private static readonly HashSet<string> Containers = new() { "moov", "trak", "mdia", "minf", "stbl", "edts" };

    // Sample tables written afresh, and per-sample ones that'd be wrong after a cut, so are left out.
    private static readonly HashSet<string> Rewritten = new()
    {
        "stts", "ctts", "stss", "stsz", "stz2", "stsc", "stco", "co64", "sdtp", "sbgp", "subs", "saiz", "saio", "stps", "padb", "stdp", "cslg",
    };

    private readonly Box? _ftyp;
    private readonly Box _moov;
    private readonly List<Box> _otherTopLevel;
    private readonly Mp4Track? _video;

    private Mp4Movie(string path, Box? ftyp, Box moov, List<Box> other, List<Mp4Track> tracks, uint timescale)
    {
        Path = path;
        _ftyp = ftyp;
        _moov = moov;
        _otherTopLevel = other;
        Tracks = tracks;
        Timescale = timescale;
        _video = tracks.FirstOrDefault(track => track.IsVideo && track.Samples.Length > 0);
        Duration = tracks.Count == 0 ? 0 : tracks.Max(track => track.End);
        if (_video is { } video)
        {
            // A keyframe shown after pictures stored behind it (an HEVC "CRA" with leading pictures) can't start
            // a cut: those pictures need the ones before it.
            Keyframes = video.CleanPoints
                .Where(point => point.Index < video.Samples.Length && video.Samples[point.Index].Sync
                    && video.Shown(point.Index) <= point.Time + 0.5 / video.Timescale)
                .Select(point => point.Time).ToArray();
        }
        else
        {
            Keyframes = Array.Empty<double>();
        }

        PictureTimes = _video is { } pictures
            ? Enumerable.Range(0, pictures.Samples.Length).Select(pictures.Shown).Order().ToArray()
            : Array.Empty<double>();
    }

    public string Path { get; }

    /// <summary>The movie's own clock, in ticks per second.</summary>
    public uint Timescale { get; }

    public IReadOnlyList<Mp4Track> Tracks { get; }

    /// <summary>How long the video is, in seconds.</summary>
    public double Duration { get; }

    public bool HasVideo => _video is not null;

    /// <summary>Where a cut can start, in seconds, in order: the keyframes. Empty for a file without pictures.</summary>
    public IReadOnlyList<double> Keyframes { get; }

    /// <summary>When each picture is shown, in seconds, in order. Empty for a file without pictures.</summary>
    public IReadOnlyList<double> PictureTimes { get; }

    /// <summary>The place a cut starting at <paramref name="time"/> really starts: the keyframe at or before it.</summary>
    public double SnapStart(double time)
    {
        if (_video is null)
        {
            return Math.Clamp(time, 0, Duration);
        }

        var best = Keyframes.Count > 0 ? Keyframes[0] : 0;
        foreach (var keyframe in Keyframes)
        {
            if (keyframe > time + 0.0005)
            {
                break;
            }

            best = keyframe;
        }

        return best;
    }

    /// <summary>The place a cut ending at <paramref name="time"/> really ends: the nearest picture it can end on.</summary>
    public double SnapEnd(double time)
    {
        if (_video is null)
        {
            return Math.Clamp(time, 0, Duration);
        }

        return _video.CleanPoints.MinBy(point => Math.Abs(point.Time - time)).Time;
    }

    /// <summary>The stretches as they'd really be cut: snapped, in order, joined where they touch, and empty ones left out.</summary>
    public IReadOnlyList<VideoRange> Snap(IEnumerable<VideoRange> ranges)
    {
        var snapped = ranges
            .Select(range => new VideoRange(SnapStart(Math.Max(0, range.Start)), SnapEnd(Math.Min(Duration, range.End))))
            .Where(range => range.End - range.Start > 0.0005)
            .OrderBy(range => range.Start)
            .ToList();
        var joined = new List<VideoRange>();
        foreach (var range in snapped)
        {
            if (joined.Count > 0 && range.Start <= joined[^1].End + 0.0005)
            {
                joined[^1] = joined[^1] with { End = Math.Max(joined[^1].End, range.End) };
            }
            else
            {
                joined.Add(range);
            }
        }

        return joined;
    }

    internal static Mp4Movie Read(Stream file, string path)
    {
        Box? ftyp = null;
        Box? moov = null;
        var other = new List<Box>();
        foreach (var (type, start, size, headerSize) in ReadHeaders(file, 0, file.Length))
        {
            switch (type)
            {
                case "moov":
                    moov = Box.Parse(type, ReadBytes(file, start + headerSize, size - headerSize), Containers);
                    break;
                case "ftyp":
                    ftyp = new Box(type, ReadBytes(file, start + headerSize, size - headerSize));
                    break;
                case "moof":
                case "mfra":
                case "sidx":
                case "styp":
                    throw new VideoCutException("This video is stored in fragments (like a stream), which Lucent can't cut without re-encoding yet.");
                case "mdat":
                case "free":
                case "skip":
                case "wide":
                    break;
                default:
                    if (size - headerSize <= 16 << 20)
                    {
                        other.Add(new Box(type, ReadBytes(file, start + headerSize, size - headerSize)));
                    }

                    break;
            }
        }

        if (moov is null)
        {
            throw new VideoCutException("The video has no index (its \"moov\" is missing), so it may not have finished recording.");
        }

        if (moov.Find("mvex") is not null)
        {
            throw new VideoCutException("This video is stored in fragments (like a stream), which Lucent can't cut without re-encoding yet.");
        }

        var mvhd = moov.Find("mvhd") ?? throw new VideoCutException("The video's index has no header.");
        var timescale = BinaryPrimitives.ReadUInt32BigEndian(mvhd.Data.AsSpan(mvhd.Data[0] == 1 ? 20 : 12));
        if (timescale == 0)
        {
            throw new VideoCutException("The video's index looks damaged, so it can't be cut without re-encoding.");
        }

        var tracks = moov.Children!.Where(child => child.Type == "trak").Select(trak => Mp4Track.Read(trak, timescale)).ToList();
        return new Mp4Movie(path, ftyp, moov, other, tracks, timescale);
    }

    // ---- Writing ----

    // With `only`, writes just that track (a sound track as an .m4a file).
    internal void WriteCut(Stream source, Stream output, IReadOnlyList<VideoRange> ranges, Mp4Track? only, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        // Where each stretch goes in the new file. With pictures, the stretches' lengths are counted in the
        // picture track's own ticks, so they add up exactly.
        var starts = new double[ranges.Count];
        var total = 0.0;
        for (var i = 0; i < ranges.Count; i++)
        {
            starts[i] = total;
            total += ranges[i].Length;
        }

        var plans = Tracks.Select(track => only is null || track == only ? track.Plan(ranges, starts) : null).ToList();

        // Interleaves the tracks in half-second chunks, as players read them.
        var chunks = new List<(int Track, int First, int Count)>();
        var next = new int[plans.Count];
        for (var window = 0.5; next.Select((n, t) => n < (plans[t]?.Samples.Count ?? 0)).Any(left => left); window += 0.5)
        {
            for (var t = 0; t < plans.Count; t++)
            {
                if (plans[t] is not { } plan)
                {
                    continue;
                }

                var first = next[t];
                while (next[t] < plan.Samples.Count && (plan.Samples[next[t]].Dts - plan.Shift) / (double)Tracks[t].Timescale < window)
                {
                    // A chunk only holds samples of one description.
                    if (next[t] > first && plan.Samples[next[t]].Description != plan.Samples[first].Description)
                    {
                        chunks.Add((t, first, next[t] - first));
                        first = next[t];
                    }

                    next[t]++;
                }

                if (next[t] > first)
                {
                    chunks.Add((t, first, next[t] - first));
                }
            }
        }

        long dataBytes = 0;
        foreach (var plan in plans)
        {
            foreach (var sample in plan?.Samples ?? new List<Mp4Sample>())
            {
                dataBytes += sample.Size;
            }
        }

        var bigMdat = dataBytes + 8 > uint.MaxValue;
        var mdatHeader = bigMdat ? 16 : 8;

        // The index is the same size whatever the offsets in it, so it's built once to measure it, then again.
        // Sound alone is marked as an .m4a file, whatever the video was (a QuickTime movie, say).
        var ftypBytes = only is { IsSound: true } ? SoundType().ToBytes() : _ftyp is null ? Array.Empty<byte>() : _ftyp.ToBytes();
        var otherBytes = only is not null ? Array.Empty<byte>() : _otherTopLevel.SelectMany(box => box.ToBytes()).ToArray();
        var probe = BuildMoov(plans, chunks, new long[chunks.Count], total, wide: false);
        var useCo64 = ftypBytes.Length + probe.Length + otherBytes.Length + mdatHeader + dataBytes > uint.MaxValue;
        var moovSize = BuildMoov(plans, chunks, new long[chunks.Count], total, useCo64).Length;
        var offsets = new long[chunks.Count];
        var at = (long)ftypBytes.Length + moovSize + otherBytes.Length + mdatHeader;
        for (var c = 0; c < chunks.Count; c++)
        {
            offsets[c] = at;
            var (t, first, count) = chunks[c];
            for (var s = first; s < first + count; s++)
            {
                at += plans[t]!.Samples[s].Size;
            }
        }

        var moov = BuildMoov(plans, chunks, offsets, total, useCo64);
        output.Write(ftypBytes);
        output.Write(moov);
        output.Write(otherBytes);
        Span<byte> header = stackalloc byte[16];
        if (bigMdat)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, 1);
            Encoding.ASCII.GetBytes("mdat", header[4..]);
            BinaryPrimitives.WriteUInt64BigEndian(header[8..], (ulong)(dataBytes + 16));
            output.Write(header);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(dataBytes + 8));
            Encoding.ASCII.GetBytes("mdat", header[4..]);
            output.Write(header[..8]);
        }

        var buffer = new byte[1 << 20];
        long copied = 0;
        foreach (var (t, first, count) in chunks)
        {
            cancellation.ThrowIfCancellationRequested();
            for (var s = first; s < first + count; s++)
            {
                var sample = plans[t]!.Samples[s];
                source.Position = sample.Offset;
                var left = sample.Size;
                while (left > 0)
                {
                    var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
                    if (read == 0)
                    {
                        throw new VideoCutException("The video ends sooner than its index says, so it may be damaged.");
                    }

                    output.Write(buffer, 0, read);
                    left -= read;
                }

                copied += sample.Size;
            }

            progress?.Report(dataBytes == 0 ? 1 : copied / (double)dataBytes);
        }
    }

    private static Box SoundType() =>
        new("ftyp", new BoxData().U32(0x4D344120).U32(0).U32(0x4D344120).U32(0x6D703432).U32(0x69736F6D).ToArray());

    private byte[] BuildMoov(List<TrackPlan?> plans, List<(int Track, int First, int Count)> chunks, long[] offsets, double total, bool wide)
    {
        var children = new List<Box>();
        var t = 0;
        foreach (var child in _moov.Children!)
        {
            if (child.Type == "mvhd")
            {
                children.Add(WithDuration(child, (ulong)Math.Round(total * Timescale)));
            }
            else if (child.Type == "trak")
            {
                var index = t++;
                if (plans[index] is null)
                {
                    continue;
                }

                var trackChunks = chunks.Select((chunk, c) => (chunk, offset: offsets[c])).Where(pair => pair.chunk.Track == index)
                    .Select(pair => (pair.chunk.First, pair.chunk.Count, pair.offset)).ToList();
                children.Add(BuildTrak(child, Tracks[index], plans[index]!, trackChunks, wide));
            }
            else
            {
                children.Add(child);
            }
        }

        return new Box("moov", children).ToBytes();
    }

    private Box BuildTrak(Box trak, Mp4Track track, TrackPlan plan, List<(int First, int Count, long Offset)> chunks, bool wide)
    {
        var presented = plan.Delay + plan.Length;
        var children = new List<Box>();
        foreach (var child in trak.Children!)
        {
            switch (child.Type)
            {
                case "tkhd":
                    children.Add(WithDuration(child, (ulong)Math.Round(presented * Timescale)));
                    if (trak.Find("edts") is null)
                    {
                        children.Add(BuildEdits(plan, track));
                    }

                    break;
                case "edts":
                    children.Add(BuildEdits(plan, track));
                    break;
                case "mdia":
                    children.Add(BuildMdia(child, plan, chunks, wide));
                    break;
                default:
                    children.Add(child);
                    break;
            }
        }

        return new Box("trak", children);
    }

    private Box BuildEdits(TrackPlan plan, Mp4Track track)
    {
        var entries = new List<(ulong Duration, long MediaTime)>();
        if (plan.Delay > 0.0005)
        {
            entries.Add(((ulong)Math.Round(plan.Delay * Timescale), -1));
        }

        entries.Add(((ulong)Math.Round(plan.Length * Timescale), plan.Shift));
        var wide = entries.Any(entry => entry.Duration > uint.MaxValue || entry.MediaTime > int.MaxValue);
        var data = new BoxData();
        data.U8((byte)(wide ? 1 : 0)).U24(0).U32((uint)entries.Count);
        foreach (var (duration, mediaTime) in entries)
        {
            if (wide)
            {
                data.U64(duration).I64(mediaTime);
            }
            else
            {
                data.U32((uint)duration).I32((int)mediaTime);
            }

            data.U32(0x00010000);
        }

        return new Box("edts", new List<Box> { new("elst", data.ToArray()) });
    }

    private static Box BuildMdia(Box mdia, TrackPlan plan, List<(int First, int Count, long Offset)> chunks, bool wide)
    {
        var length = plan.Samples.Count == 0 ? 0UL : (ulong)(plan.Samples[^1].Dts + plan.Samples[^1].Duration);
        var children = new List<Box>();
        foreach (var child in mdia.Children!)
        {
            if (child.Type == "mdhd")
            {
                children.Add(WithDuration(child, length));
            }
            else if (child.Type == "minf")
            {
                var minf = new List<Box>();
                foreach (var part in child.Children!)
                {
                    minf.Add(part.Type == "stbl" ? BuildStbl(part, plan, chunks, wide) : part);
                }

                children.Add(new Box("minf", minf));
            }
            else
            {
                children.Add(child);
            }
        }

        return new Box("mdia", children);
    }

    private static Box BuildStbl(Box stbl, TrackPlan plan, List<(int First, int Count, long Offset)> chunks, bool wide)
    {
        var samples = plan.Samples;
        var children = stbl.Children!.Where(child => !Rewritten.Contains(child.Type)).ToList();

        // How long each sample lasts.
        var stts = new BoxData().U32(0);
        var runs = RunLengths(samples.Select(sample => (long)sample.Duration));
        stts.U32((uint)runs.Count);
        foreach (var (count, value) in runs)
        {
            stts.U32((uint)count).U32((uint)value);
        }

        children.Add(new Box("stts", stts.ToArray()));

        // When each picture is shown, from when it's decoded.
        if (stbl.Find("ctts") is not null)
        {
            var offsets = RunLengths(samples.Select(sample => (long)sample.CompositionOffset));
            var ctts = new BoxData().U8((byte)(offsets.Any(run => run.Value < 0) ? 1 : 0)).U24(0).U32((uint)offsets.Count);
            foreach (var (count, value) in offsets)
            {
                ctts.U32((uint)count).I32((int)value);
            }

            children.Add(new Box("ctts", ctts.ToArray()));
        }

        // The keyframes.
        if (stbl.Find("stss") is not null)
        {
            var sync = Enumerable.Range(0, samples.Count).Where(i => samples[i].Sync).ToList();
            var stss = new BoxData().U32(0).U32((uint)sync.Count);
            foreach (var number in sync)
            {
                stss.U32((uint)number + 1);
            }

            children.Add(new Box("stss", stss.ToArray()));
        }

        // Sizes.
        var stsz = new BoxData().U32(0);
        if (samples.Count > 0 && samples.All(sample => sample.Size == samples[0].Size))
        {
            stsz.U32((uint)samples[0].Size).U32((uint)samples.Count);
        }
        else
        {
            stsz.U32(0).U32((uint)samples.Count);
            foreach (var sample in samples)
            {
                stsz.U32((uint)sample.Size);
            }
        }

        children.Add(new Box("stsz", stsz.ToArray()));

        // Chunks: how many samples each holds, of which description, and where it is.
        var stsc = new BoxData().U32(0);
        var entries = new List<(int FirstChunk, int Count, uint Description)>();
        for (var c = 0; c < chunks.Count; c++)
        {
            var description = samples[chunks[c].First].Description;
            if (entries.Count == 0 || entries[^1].Count != chunks[c].Count || entries[^1].Description != description)
            {
                entries.Add((c + 1, chunks[c].Count, description));
            }
        }

        stsc.U32((uint)entries.Count);
        foreach (var (firstChunk, count, description) in entries)
        {
            stsc.U32((uint)firstChunk).U32((uint)count).U32(description);
        }

        children.Add(new Box("stsc", stsc.ToArray()));

        var stco = new BoxData().U32(0).U32((uint)chunks.Count);
        foreach (var chunk in chunks)
        {
            if (wide)
            {
                stco.U64((ulong)chunk.Offset);
            }
            else
            {
                stco.U32((uint)chunk.Offset);
            }
        }

        children.Add(new Box(wide ? "co64" : "stco", stco.ToArray()));
        return new Box("stbl", children);
    }

    private static List<(int Count, long Value)> RunLengths(IEnumerable<long> values)
    {
        var runs = new List<(int Count, long Value)>();
        foreach (var value in values)
        {
            if (runs.Count > 0 && runs[^1].Value == value)
            {
                runs[^1] = (runs[^1].Count + 1, value);
            }
            else
            {
                runs.Add((1, value));
            }
        }

        return runs;
    }

    // A header box (mvhd, tkhd, mdhd) with its duration changed. After the version and flags come the created and
    // modified times (64-bit in version 1), then the timescale (mvhd, mdhd) or the track number and a spare field (tkhd).
    private static Box WithDuration(Box box, ulong duration)
    {
        var data = (byte[])box.Data.Clone();
        var version1 = data[0] == 1;
        var offset = (box.Type, version1) switch
        {
            ("tkhd", false) => 20,
            ("tkhd", true) => 28,
            (_, false) => 16,
            (_, true) => 24,
        };

        if (version1)
        {
            BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(offset), duration);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), (uint)Math.Min(duration, uint.MaxValue));
        }

        return new Box(box.Type, data);
    }

    // ---- Reading boxes ----

    internal static IEnumerable<(string Type, long Start, long Size, int HeaderSize)> ReadHeaders(Stream file, long from, long to)
    {
        var header = new byte[16];
        var at = from;
        while (at + 8 <= to)
        {
            file.Position = at;
            file.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header, 4, 4);
            var headerSize = 8;
            if (size == 1)
            {
                file.ReadExactly(header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = to - at;
            }

            if (size < headerSize || at + size > to)
            {
                // A file cut short mid-box: what came before is still read.
                if (type == "mdat")
                {
                    yield break;
                }

                throw new InvalidDataException($"The {type} box runs past the end of the file.");
            }

            yield return (type, at, size, headerSize);
            at += size;
        }
    }

    private static byte[] ReadBytes(Stream file, long at, long count)
    {
        if (count > 512 << 20)
        {
            throw new VideoCutException("The video's index is too big to read.");
        }

        var bytes = new byte[count];
        file.Position = at;
        file.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>One sample: a picture, or a packet of compressed sound.</summary>
/// <param name="Dts">When it's decoded, in the track's ticks.</param>
/// <param name="CompositionOffset">How long after it's decoded it's shown, in the track's ticks.</param>
/// <param name="Description">Which of the track's sample descriptions (codec setups) it uses, from 1.</param>
public readonly record struct Mp4Sample(long Offset, long Size, long Dts, uint Duration, int CompositionOffset, bool Sync, uint Description);

/// <summary>A place a video track can be cut: before sample number <see cref="Index"/> in the order they're stored.</summary>
/// <param name="Time">The time there, in seconds: when the first picture after it is shown.</param>
public readonly record struct Mp4CutPoint(int Index, double Time);

/// <summary>What one track keeps of a cut: its samples with their new times.</summary>
/// <param name="Shift">Where in the samples' times the track starts to be shown, in the track's ticks.</param>
/// <param name="Delay">How long before the track starts, in seconds (an empty edit before it).</param>
/// <param name="Length">How long the track is shown, in seconds.</param>
internal sealed record TrackPlan(List<Mp4Sample> Samples, long Shift, double Delay, double Length);

/// <summary>A track of an MP4 or MOV file.</summary>
public sealed class Mp4Track
{
    private Mp4Track(string handler, uint timescale, Mp4Sample[] samples, long shift, double delay, double end)
    {
        Handler = handler;
        Timescale = timescale;
        Samples = samples;
        Shift = shift;
        Delay = delay;
        End = end;
        CleanPoints = IsVideo ? FindCleanPoints() : Array.Empty<Mp4CutPoint>();
    }

    /// <summary>What's in the track: "vide" for pictures, "soun" for sound, "text", "sbtl", "tmcd" and so on.</summary>
    public string Handler { get; }

    public bool IsVideo => Handler == "vide";

    public bool IsSound => Handler == "soun";

    /// <summary>The track's own clock, in ticks per second.</summary>
    public uint Timescale { get; }

    public Mp4Sample[] Samples { get; }

    /// <summary>Where in the samples' times the track starts to be shown (its edit list's start), in its ticks.</summary>
    public long Shift { get; }

    /// <summary>How long before the track starts, in seconds.</summary>
    public double Delay { get; }

    /// <summary>When the track ends, in seconds.</summary>
    public double End { get; }

    /// <summary>For a video track, every place it can be cut, in order.</summary>
    public IReadOnlyList<Mp4CutPoint> CleanPoints { get; }

    /// <summary>
    /// The track's first sample description (its format, as the "stsd" box gives it: size, type such as "avc1" or
    /// "mp4a", then the type's own fields and boxes), or null when there isn't one.
    /// </summary>
    public byte[]? SampleEntry { get; private init; }

    /// <summary>The track's format, such as "avc1", "hvc1", "mp4a" or "lpcm", or empty when it doesn't say.</summary>
    public string Format => SampleEntry is { Length: >= 8 } entry ? Encoding.ASCII.GetString(entry, 4, 4) : string.Empty;

    /// <summary>When sample <paramref name="i"/> is shown, in seconds.</summary>
    public double Shown(int i) => (Samples[i].Dts + Samples[i].CompositionOffset - Shift) / (double)Timescale + Delay;

    internal static Mp4Track Read(Box trak, uint movieTimescale)
    {
        var mdia = trak.Find("mdia") ?? throw new InvalidDataException("A track has no media.");
        var mdhd = mdia.Find("mdhd") ?? throw new InvalidDataException("A track has no media header.");
        var timescale = BinaryPrimitives.ReadUInt32BigEndian(mdhd.Data.AsSpan(mdhd.Data[0] == 1 ? 20 : 12));
        var hdlr = mdia.Find("hdlr");
        var handler = hdlr is { Data.Length: >= 12 } ? Encoding.ASCII.GetString(hdlr.Data, 8, 4) : "";
        var stbl = mdia.Find("minf")?.Find("stbl") ?? throw new InvalidDataException("A track has no sample table.");
        if (timescale == 0)
        {
            throw new InvalidDataException("A track has no clock.");
        }

        var samples = ReadSamples(stbl);

        // The edit list: an empty edit first delays the track; the first real one says where its shown part starts.
        long shift = 0;
        var delay = 0.0;
        if (trak.Find("edts")?.Find("elst") is { } elst)
        {
            var data = elst.Data;
            var version1 = data[0] == 1;
            var count = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4));
            var at = 8;
            for (var i = 0; i < count && at + (version1 ? 20 : 12) <= data.Length; i++)
            {
                long duration = version1 ? (long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(at)) : BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(at));
                long mediaTime = version1 ? BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(at + 8)) : BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(at + 4));
                at += version1 ? 20 : 12;
                if (mediaTime == -1)
                {
                    delay += duration / (double)movieTimescale;
                    continue;
                }

                shift = mediaTime;
                break;
            }
        }

        var end = 0.0;
        foreach (var sample in samples)
        {
            end = Math.Max(end, (sample.Dts + sample.CompositionOffset + sample.Duration - shift) / (double)timescale + delay);
        }

        return new Mp4Track(handler, timescale, samples, shift, delay, end) { SampleEntry = FirstSampleEntry(stbl.Find("stsd")) };
    }

    private static byte[]? FirstSampleEntry(Box? stsd)
    {
        if (stsd is not { Data.Length: >= 16 } box || BinaryPrimitives.ReadUInt32BigEndian(box.Data.AsSpan(4)) == 0)
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt32BigEndian(box.Data.AsSpan(8));
        return size >= 8 && 8 + size <= box.Data.Length ? box.Data.AsSpan(8, (int)size).ToArray() : null;
    }

    private static Mp4Sample[] ReadSamples(Box stbl)
    {
        // Sizes.
        long[] sizes;
        if (stbl.Find("stsz") is { } stsz)
        {
            var constant = BinaryPrimitives.ReadUInt32BigEndian(stsz.Data.AsSpan(4));
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(stsz.Data.AsSpan(8));
            sizes = new long[count];
            for (var i = 0; i < count; i++)
            {
                sizes[i] = constant != 0 ? constant : BinaryPrimitives.ReadUInt32BigEndian(stsz.Data.AsSpan(12 + i * 4));
            }
        }
        else if (stbl.Find("stz2") is { } stz2)
        {
            var bits = stz2.Data[7];
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(stz2.Data.AsSpan(8));
            sizes = new long[count];
            for (var i = 0; i < count; i++)
            {
                sizes[i] = bits switch
                {
                    4 => (stz2.Data[12 + i / 2] >> (i % 2 == 0 ? 4 : 0)) & 0xF,
                    8 => stz2.Data[12 + i],
                    16 => BinaryPrimitives.ReadUInt16BigEndian(stz2.Data.AsSpan(12 + i * 2)),
                    _ => throw new InvalidDataException("Unknown sample size width."),
                };
            }
        }
        else
        {
            throw new InvalidDataException("A track has no sample sizes.");
        }

        var samples = sizes.Length;

        // Chunk offsets.
        long[] chunkOffsets;
        if (stbl.Find("stco") is { } stco)
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(stco.Data.AsSpan(4));
            chunkOffsets = new long[count];
            for (var i = 0; i < count; i++)
            {
                chunkOffsets[i] = BinaryPrimitives.ReadUInt32BigEndian(stco.Data.AsSpan(8 + i * 4));
            }
        }
        else if (stbl.Find("co64") is { } co64)
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(co64.Data.AsSpan(4));
            chunkOffsets = new long[count];
            for (var i = 0; i < count; i++)
            {
                chunkOffsets[i] = (long)BinaryPrimitives.ReadUInt64BigEndian(co64.Data.AsSpan(8 + i * 8));
            }
        }
        else
        {
            throw new InvalidDataException("A track has no chunk offsets.");
        }

        // Samples per chunk, which gives each sample's place in the file and its description.
        var offsets = new long[samples];
        var descriptions = new uint[samples];
        var stsc = stbl.Find("stsc") ?? throw new InvalidDataException("A track has no chunk table.");
        var entries = (int)BinaryPrimitives.ReadUInt32BigEndian(stsc.Data.AsSpan(4));
        var sample = 0;
        for (var e = 0; e < entries && sample < samples; e++)
        {
            var firstChunk = (int)BinaryPrimitives.ReadUInt32BigEndian(stsc.Data.AsSpan(8 + e * 12)) - 1;
            var perChunk = (int)BinaryPrimitives.ReadUInt32BigEndian(stsc.Data.AsSpan(12 + e * 12));
            var description = BinaryPrimitives.ReadUInt32BigEndian(stsc.Data.AsSpan(16 + e * 12));
            var lastChunk = e + 1 < entries ? (int)BinaryPrimitives.ReadUInt32BigEndian(stsc.Data.AsSpan(8 + (e + 1) * 12)) - 1 : chunkOffsets.Length;
            for (var chunk = firstChunk; chunk < lastChunk && chunk < chunkOffsets.Length && sample < samples; chunk++)
            {
                var at = chunkOffsets[chunk];
                for (var k = 0; k < perChunk && sample < samples; k++)
                {
                    offsets[sample] = at;
                    descriptions[sample] = description;
                    at += sizes[sample];
                    sample++;
                }
            }
        }

        if (sample < samples)
        {
            throw new InvalidDataException("The chunk table doesn't cover every sample.");
        }

        // Durations, and so decode times.
        var durations = new uint[samples];
        var stts = stbl.Find("stts") ?? throw new InvalidDataException("A track has no sample times.");
        var runs = (int)BinaryPrimitives.ReadUInt32BigEndian(stts.Data.AsSpan(4));
        sample = 0;
        for (var r = 0; r < runs && sample < samples; r++)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(stts.Data.AsSpan(8 + r * 8));
            var delta = BinaryPrimitives.ReadUInt32BigEndian(stts.Data.AsSpan(12 + r * 8));
            for (var k = 0; k < count && sample < samples; k++)
            {
                durations[sample++] = delta;
            }
        }

        // Composition offsets (B-frames).
        var composition = new int[samples];
        if (stbl.Find("ctts") is { } ctts)
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(ctts.Data.AsSpan(4));
            sample = 0;
            for (var r = 0; r < count && sample < samples; r++)
            {
                var run = BinaryPrimitives.ReadUInt32BigEndian(ctts.Data.AsSpan(8 + r * 8));
                var offset = BinaryPrimitives.ReadInt32BigEndian(ctts.Data.AsSpan(12 + r * 8));
                for (var k = 0; k < run && sample < samples; k++)
                {
                    composition[sample++] = offset;
                }
            }
        }

        // Keyframes: every sample is one when the table is missing.
        var sync = new bool[samples];
        if (stbl.Find("stss") is { } stss)
        {
            var count = (int)BinaryPrimitives.ReadUInt32BigEndian(stss.Data.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                var number = (int)BinaryPrimitives.ReadUInt32BigEndian(stss.Data.AsSpan(8 + i * 4)) - 1;
                if (number >= 0 && number < samples)
                {
                    sync[number] = true;
                }
            }
        }
        else
        {
            Array.Fill(sync, true);
        }

        var result = new Mp4Sample[samples];
        long dts = 0;
        for (var i = 0; i < samples; i++)
        {
            result[i] = new Mp4Sample(offsets[i], sizes[i], dts, durations[i], composition[i], sync[i], descriptions[i]);
            dts += durations[i];
        }

        return result;
    }

    // The places a video can be cut: between stored samples where everything stored before is shown before
    // everything stored after.
    private Mp4CutPoint[] FindCleanPoints()
    {
        var count = Samples.Length;
        var points = new List<Mp4CutPoint>();
        var suffixMin = new double[count + 1];
        suffixMin[count] = double.PositiveInfinity;
        for (var i = count - 1; i >= 0; i--)
        {
            suffixMin[i] = Math.Min(suffixMin[i + 1], Shown(i));
        }

        var prefixMax = double.NegativeInfinity;
        var last = 0.0;
        for (var m = 0; m <= count; m++)
        {
            if (m < count && prefixMax < suffixMin[m])
            {
                points.Add(new Mp4CutPoint(m, suffixMin[m]));
            }

            if (m < count)
            {
                prefixMax = Math.Max(prefixMax, Shown(m));
                last = Math.Max(last, Shown(m) + Samples[m].Duration / (double)Timescale);
            }
        }

        // The very end.
        points.Add(new Mp4CutPoint(count, last));
        return points.ToArray();
    }

    // ---- Planning a cut ----

    /// <summary>The samples this track keeps of each stretch, timed one stretch after another.</summary>
    internal TrackPlan Plan(IReadOnlyList<VideoRange> ranges, double[] starts)
    {
        var kept = new List<Mp4Sample>();
        var total = ranges.Count == 0 ? 0 : starts[^1] + ranges[^1].Length;
        if (Samples.Length == 0)
        {
            return new TrackPlan(kept, 0, 0, 0);
        }

        if (IsVideo)
        {
            return PlanPictures(ranges, starts, total, kept);
        }

        // Sound and the rest: whole samples, each stretch starting where the sound so far has got to, so the
        // sound never drifts from the picture. A track that starts late keeps its delay.
        var delay = 0.0;
        var ahead = 0.0;
        long dts = 0;
        for (var r = 0; r < ranges.Count; r++)
        {
            var (start, end) = (ranges[r].Start, ranges[r].End);
            var from = start + ahead;
            var first = kept.Count;
            for (var i = 0; i < Samples.Length; i++)
            {
                var shown = Shown(i);
                var length = Samples[i].Duration / (double)Timescale;
                var middle = shown + length / 2;
                if (IsSound)
                {
                    if (middle < from || middle >= end)
                    {
                        continue;
                    }

                    if (r == 0 && kept.Count == 0 && shown > start + 0.0005)
                    {
                        delay = shown - start;
                    }

                    kept.Add(Samples[i] with { Dts = dts, CompositionOffset = 0 });
                    dts += Samples[i].Duration;
                }
                else
                {
                    // A long sample (a chapter title, a timecode) is shortened to the part kept.
                    var visible = Math.Min(end, shown + length) - Math.Max(start, shown);
                    if (visible <= 0)
                    {
                        continue;
                    }

                    if (r == 0 && kept.Count == 0 && shown > start + 0.0005)
                    {
                        delay = shown - start;
                    }

                    var ticks = (uint)Math.Max(1, Math.Round(visible * Timescale));
                    kept.Add(Samples[i] with { Dts = dts, Duration = ticks, CompositionOffset = 0 });
                    dts += ticks;
                }
            }

            // Where the next stretch's sound should begin: how far this one's ran past (or short of) its picture.
            var reached = dts / (double)Timescale + delay;
            ahead = reached - (starts[r] + ranges[r].Length);
            if (kept.Count == first)
            {
                ahead = 0;
            }
        }

        var shownLength = Math.Max(0, Math.Min(total, dts / (double)Timescale + delay) - delay);
        return new TrackPlan(kept, 0, delay, shownLength);
    }

    private TrackPlan PlanPictures(IReadOnlyList<VideoRange> ranges, double[] starts, double total, List<Mp4Sample> kept)
    {
        long shift = 0;
        for (var r = 0; r < ranges.Count; r++)
        {
            var from = CleanPoints.MinBy(point => Math.Abs(point.Time - ranges[r].Start)).Index;
            var to = CleanPoints.MinBy(point => Math.Abs(point.Time - ranges[r].End)).Index;
            if (to <= from)
            {
                continue;
            }

            // The stretch's first picture is shown at its place in the new file.
            var place = (long)Math.Round(starts[r] * Timescale);
            var firstShown = Samples[from].Dts + Samples[from].CompositionOffset;
            for (var i = from; i < to; i++)
            {
                firstShown = Math.Min(firstShown, Samples[i].Dts + Samples[i].CompositionOffset);
            }

            if (kept.Count == 0)
            {
                // The new file's decode times start at 0, and its edit list starts showing at the first picture.
                shift = firstShown - Samples[from].Dts - place;
            }

            var dts = place + shift - (firstShown - Samples[from].Dts);
            if (kept.Count > 0)
            {
                // The previous stretch's last sample lasts until this one's first is decoded.
                var previous = kept[^1];
                var gap = dts - previous.Dts;
                if (gap <= 0)
                {
                    dts = previous.Dts + 1;
                    gap = 1;
                }

                kept[^1] = previous with { Duration = (uint)gap };
            }

            var offset = dts - Samples[from].Dts;
            for (var i = from; i < to; i++)
            {
                kept.Add(Samples[i] with { Dts = Samples[i].Dts + offset });
            }
        }

        return new TrackPlan(kept, shift, 0, total);
    }
}

/// <summary>An MP4 box: a leaf with its bytes (after the size and type), or a container of other boxes.</summary>
internal sealed class Box
{
    public Box(string type, byte[] data)
    {
        Type = type;
        Data = data;
    }

    public Box(string type, List<Box> children)
    {
        Type = type;
        Data = Array.Empty<byte>();
        Children = children;
    }

    public string Type { get; }

    public byte[] Data { get; }

    public List<Box>? Children { get; }

    public Box? Find(string type) => Children?.FirstOrDefault(child => child.Type == type);

    public static Box Parse(string type, byte[] data, HashSet<string> containers)
    {
        if (!containers.Contains(type))
        {
            return new Box(type, data);
        }

        var children = new List<Box>();
        using var stream = new MemoryStream(data, writable: false);
        foreach (var (childType, start, size, headerSize) in Mp4Movie.ReadHeaders(stream, 0, data.Length))
        {
            children.Add(Parse(childType, data.AsSpan((int)(start + headerSize), (int)(size - headerSize)).ToArray(), containers));
        }

        return new Box(type, children);
    }

    public byte[] ToBytes()
    {
        using var output = new MemoryStream();
        Write(output);
        return output.ToArray();
    }

    private void Write(Stream output)
    {
        byte[] body;
        if (Children is null)
        {
            body = Data;
        }
        else
        {
            using var inner = new MemoryStream();
            foreach (var child in Children)
            {
                child.Write(inner);
            }

            body = inner.ToArray();
        }

        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)(body.Length + 8));
        Encoding.ASCII.GetBytes(Type, header[4..]);
        output.Write(header);
        output.Write(body);
    }
}

/// <summary>Builds a box's bytes, big-endian.</summary>
internal sealed class BoxData
{
    private readonly MemoryStream _bytes = new();

    public BoxData U8(byte value)
    {
        _bytes.WriteByte(value);
        return this;
    }

    public BoxData U24(uint value)
    {
        _bytes.WriteByte((byte)(value >> 16));
        _bytes.WriteByte((byte)(value >> 8));
        _bytes.WriteByte((byte)value);
        return this;
    }

    public BoxData U32(uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        _bytes.Write(bytes);
        return this;
    }

    public BoxData I32(int value) => U32(unchecked((uint)value));

    public BoxData U64(ulong value)
    {
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);
        _bytes.Write(bytes);
        return this;
    }

    public BoxData I64(long value) => U64(unchecked((ulong)value));

    public byte[] ToArray() => _bytes.ToArray();
}
