namespace WinampInspiredVisualizer.Editor;

/// <summary>
/// What the waveform needs to know about a clip to offer its edge and fade handles.
/// </summary>
/// <param name="Silent">True for a clip of silence (left by a cut, a trim or a slide), which has no handles.</param>
/// <param name="HiddenBefore">How much audio is hidden before its start, so how far its start can be dragged out.</param>
/// <param name="HiddenAfter">How much audio is hidden after its end.</param>
/// <param name="FadeIn">How long its fade in is, in frames (0 for none).</param>
/// <param name="FadeOut">How long its fade out is.</param>
public sealed record ClipEdges(FrameRange Range, bool Silent, int HiddenBefore, int HiddenAfter, int FadeIn, int FadeOut);

/// <summary>
/// Editing clips by their edges, the way a DAW does: dragging an edge in trims the clip, leaving silence, and
/// dragging it back out brings the trimmed audio back. Each clip also has a fade in and a fade out that can be
/// changed as often as wanted, since the audio from before the fade is kept.
/// </summary>
public sealed partial class EditSession
{
    /// <summary>Every clip, with what its edges can do.</summary>
    public IReadOnlyList<ClipEdges> ClipEdgeDetails
    {
        get
        {
            var clips = Clips;
            var details = new List<ClipEdges>(clips.Count);
            foreach (var clip in clips)
            {
                details.Add(new ClipEdges(clip, IsSilent(clip),
                    HiddenAt(clip.Start, before: true)?.Frames ?? 0,
                    HiddenAt(clip.End, before: false)?.Frames ?? 0,
                    FadeAt(clip.Start, fadeIn: true)?.Length ?? 0,
                    FadeAt(clip.End, fadeIn: false)?.Length ?? 0));
            }

            return details;
        }
    }

    /// <summary>
    /// Moves one edge of a clip to a frame: in, to trim it (silence takes the place of what's trimmed, which is
    /// kept), or out, over the silence next to it, to bring back audio trimmed or cut from that side. Its fades
    /// go with the edge. Returns where the clip now is.
    /// </summary>
    public FrameRange MoveClipEdge(int clipIndex, bool start, int frame)
    {
        var clip = Clips[clipIndex];
        var fadeIn = FadeAt(clip.Start, fadeIn: true)?.Length ?? 0;
        var fadeOut = FadeAt(clip.End, fadeIn: false)?.Length ?? 0;
        return start
            ? RenderClip(frame < clip.Start ? "Extend clip" : "Trim clip", clipIndex, frame, clip.End, fadeIn, fadeOut, keepEnd: true)
            : RenderClip(frame > clip.End ? "Extend clip" : "Trim clip", clipIndex, clip.Start, frame, fadeIn, fadeOut, keepEnd: false);
    }

    /// <summary>Sets how long a clip's fade in (or out) is, in frames; 0 takes it away. Returns how long it came out.</summary>
    public int SetClipFade(int clipIndex, bool fadeIn, int frames)
    {
        var clip = Clips[clipIndex];
        if (IsSilent(clip))
        {
            return 0;
        }

        var currentIn = FadeAt(clip.Start, fadeIn: true)?.Length ?? 0;
        var currentOut = FadeAt(clip.End, fadeIn: false)?.Length ?? 0;
        frames = Math.Clamp(frames, 0, clip.Length - (fadeIn ? currentOut : currentIn));
        if (frames == (fadeIn ? currentIn : currentOut))
        {
            return frames;
        }

        var description = frames == 0 ? fadeIn ? "Remove fade in" : "Remove fade out" : fadeIn ? "Fade in" : "Fade out";
        RenderClip(description, clipIndex, clip.Start, clip.End, fadeIn ? frames : currentIn, fadeIn ? currentOut : frames, keepEnd: fadeIn);
        return frames;
    }

    /// <summary>
    /// Moves the split between two clips that touch, so one gets longer as the other gets shorter and no audio is
    /// lost. Fades at the split stay in the audio where they are.
    /// </summary>
    public void MoveSplit(int split, int frame)
    {
        var index = _splits.IndexOf(split);
        if (index < 0)
        {
            return;
        }

        var low = index > 0 ? _splits[index - 1] : 0;
        var high = index < _splits.Count - 1 ? _splits[index + 1] : Audio.Frames;
        frame = Math.Clamp(frame, low + 1, high - 1);
        if (frame != split)
        {
            var splits = _splits.ToList();
            splits[index] = frame;
            Commit("Move clip edge", Array.Empty<Change>(), splits,
                hiddenAfter: _hidden.Where(h => h.Frame != split).ToList(), fadesAfter: _fades.Where(f => f.Frame != split).ToList());
        }
    }

    /// <summary>
    /// Puts a clip back together from what it has hidden either side of it and the audio under its fades, and
    /// lays it out again between new edges with new fades. Whatever it leaves becomes silence, and whatever falls
    /// outside the new edges is hidden behind them. The edges can't go past the silence either side.
    /// </summary>
    /// <param name="keepEnd">When the fades don't both fit, the one at this end keeps its length (true for the end).</param>
    private FrameRange RenderClip(string description, int clipIndex, int newStart, int newEnd, int fadeIn, int fadeOut, bool keepEnd)
    {
        var clips = Clips;
        var clip = clips[clipIndex];
        var head = HiddenAt(clip.Start, before: true);
        var tail = HiddenAt(clip.End, before: false);
        var inFade = FadeAt(clip.Start, fadeIn: true);
        var outFade = FadeAt(clip.End, fadeIn: false);
        var headLength = head?.Frames ?? 0;
        var tailLength = tail?.Frames ?? 0;
        newStart = Math.Clamp(newStart, clip.Start - Math.Min(headLength, SilenceBefore(clipIndex)), clip.End - 1);
        newEnd = Math.Clamp(newEnd, newStart + 1, clip.End + Math.Min(tailLength, SilenceAfter(clipIndex)));
        var length = newEnd - newStart;
        if (keepEnd)
        {
            fadeOut = Math.Clamp(fadeOut, 0, length);
            fadeIn = Math.Clamp(fadeIn, 0, length - fadeOut);
        }
        else
        {
            fadeIn = Math.Clamp(fadeIn, 0, length);
            fadeOut = Math.Clamp(fadeOut, 0, length - fadeIn);
        }

        if (newStart == clip.Start && newEnd == clip.End && fadeIn == (inFade?.Length ?? 0) && fadeOut == (outFade?.Length ?? 0))
        {
            return clip;
        }

        // The clip's whole source, as it would be with nothing trimmed or faded: frame f of the song is source[f - origin].
        var origin = clip.Start - headLength;
        var from = Math.Min(clip.Start, newStart);
        var to = Math.Max(clip.End, newEnd);
        var channels = Audio.ChannelCount;
        var output = new float[channels][];
        var hiddenHead = new float[channels][];
        var hiddenTail = new float[channels][];
        var originalIn = new float[channels][];
        var originalOut = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            var source = new float[headLength + clip.Length + tailLength];
            if (head is not null)
            {
                Array.Copy(head.Channels[c], 0, source, 0, headLength);
            }

            Array.Copy(Audio.Channels[c], clip.Start, source, headLength, clip.Length);
            if (inFade is not null)
            {
                Array.Copy(inFade.Original.Channels[c], 0, source, headLength, inFade.Length);
            }

            if (outFade is not null)
            {
                Array.Copy(outFade.Original.Channels[c], 0, source, headLength + clip.Length - outFade.Length, outFade.Length);
            }

            if (tail is not null)
            {
                Array.Copy(tail.Channels[c], 0, source, headLength + clip.Length, tailLength);
            }

            var result = new float[to - from];
            Array.Copy(source, newStart - origin, result, newStart - from, length);
            originalIn[c] = result.AsSpan(newStart - from, fadeIn).ToArray();
            originalOut[c] = result.AsSpan(newEnd - from - fadeOut, fadeOut).ToArray();
            for (var i = 0; i < fadeIn; i++)
            {
                result[newStart - from + i] *= FadeGain(i, fadeIn, rising: true);
            }

            for (var i = 0; i < fadeOut; i++)
            {
                result[newEnd - from - fadeOut + i] *= FadeGain(i, fadeOut, rising: false);
            }

            output[c] = result;
            hiddenHead[c] = source.AsSpan(0, newStart - origin).ToArray();
            hiddenTail[c] = source.AsSpan(newEnd - origin).ToArray();
        }

        var rate = Audio.SampleRate;
        var hidden = _hidden.Where(h => !(h.Frame == clip.Start && h.Before) && !(h.Frame == clip.End && !h.Before)).ToList();
        if (hiddenHead[0].Length > 0)
        {
            hidden.Add(new HiddenAudio(newStart, true, new AudioBuffer(hiddenHead, rate)));
        }

        if (hiddenTail[0].Length > 0)
        {
            hidden.Add(new HiddenAudio(newEnd, false, new AudioBuffer(hiddenTail, rate)));
        }

        var fades = _fades.Where(f => !(f.Frame == clip.Start && f.In) && !(f.Frame == clip.End && !f.In)).ToList();
        if (fadeIn > 0)
        {
            fades.Add(new ClipFade(newStart, true, new AudioBuffer(originalIn, rate)));
        }

        if (fadeOut > 0)
        {
            fades.Add(new ClipFade(newEnd, false, new AudioBuffer(originalOut, rate)));
        }

        // The splits inside the clip's new extent go (the silence it grew over), and new ones go at its edges.
        // Silence left by a trim joins the silent clip next to it, or becomes a clip of its own.
        var splits = _splits.Where(s => s != clip.Start && s != clip.End && (s < newStart || s > newEnd))
            .Append(newStart).Append(newEnd).ToList();
        if (newStart > clip.Start && clipIndex > 0 && !IsSilent(clips[clipIndex - 1]))
        {
            splits.Add(clip.Start);
        }

        if (newEnd < clip.End && clipIndex < clips.Count - 1 && !IsSilent(clips[clipIndex + 1]))
        {
            splits.Add(clip.End);
        }

        Commit(description, new[] { new Change(from, to - from, new AudioBuffer(output, rate)) }, Normalized(splits),
            hiddenAfter: hidden, fadesAfter: fades);
        return new FrameRange(newStart, length);
    }

    /// <summary>
    /// What's hidden after a cut that leaves silence: when it's inside one clip, the cut audio stays behind the
    /// edges either side of the silence, so the clips there (the clip that was cut, or the ones it was split from)
    /// can be dragged back over the silence to bring it back.
    /// </summary>
    private List<HiddenAudio> HiddenAfterCut(int start, int end)
    {
        var hidden = _hidden.Where(h => !h.TouchedBy(start, end - start)).ToList();
        var clips = Clips;
        var index = ClipAt(start);
        var clip = clips[index];
        if (end > clip.End || IsSilent(new FrameRange(start, end - start)))
        {
            return hidden;
        }

        var cut = Audio.Slice(start, end - start);
        if (start > clip.Start || (index > 0 && !IsSilent(clips[index - 1])))
        {
            // What was hidden after the clip carries on from the end of the cut.
            hidden.RemoveAll(h => h.Frame == start && !h.Before);
            hidden.Add(new HiddenAudio(start, false,
                end == clip.End && HiddenAt(clip.End, before: false) is { } tail ? Joined(cut, tail) : cut));
        }

        if (end < clip.End || (index < clips.Count - 1 && !IsSilent(clips[index + 1])))
        {
            hidden.RemoveAll(h => h.Frame == end && h.Before);
            hidden.Add(new HiddenAudio(end, true,
                start == clip.Start && HiddenAt(clip.Start, before: true) is { } head ? Joined(head, cut) : cut));
        }

        return hidden;
    }

    private AudioBuffer? HiddenAt(int frame, bool before) =>
        _hidden.FirstOrDefault(h => h.Frame == frame && h.Before == before)?.Audio;

    private ClipFade? FadeAt(int frame, bool fadeIn) =>
        _fades.FirstOrDefault(f => f.Frame == frame && f.In == fadeIn);

    /// <summary>
    /// Keeps only what's hidden and the fades that still fit the clips: each at the edge of a clip with sound in
    /// it, and each fade inside its clip without crossing the other.
    /// </summary>
    private (List<HiddenAudio>, List<ClipFade>) OnClipEdges(List<HiddenAudio> hidden, List<ClipFade> fades)
    {
        if (hidden.Count == 0 && fades.Count == 0)
        {
            return (hidden, fades);
        }

        var clips = Clips;
        var silent = new Dictionary<int, bool>();
        FrameRange? Starting(int frame) => ClipAt(frame) is var i && i < clips.Count && clips[i].Start == frame ? clips[i] : null;
        FrameRange? Ending(int frame) => frame > 0 && ClipAt(frame - 1) is var i && clips[i].End == frame ? clips[i] : null;
        bool Audible(FrameRange clip)
        {
            if (!silent.TryGetValue(clip.Start, out var quiet))
            {
                silent[clip.Start] = quiet = IsSilent(clip);
            }

            return !quiet;
        }

        var keptHidden = hidden.Where(h => h.Audio.ChannelCount == Audio.ChannelCount && h.Audio.Frames > 0
            && (h.Before ? Starting(h.Frame) : Ending(h.Frame)) is { } clip && Audible(clip)).ToList();
        var keptFades = fades.Where(f => f.Original.ChannelCount == Audio.ChannelCount
            && (f.In ? Starting(f.Frame) : Ending(f.Frame)) is { } clip && Audible(clip) && f.Length <= clip.Length).ToList();
        // A fade out that would cross the fade in goes.
        keptFades.RemoveAll(f => !f.In && Ending(f.Frame) is { } clip
            && keptFades.Any(g => g.In && g.Frame == clip.Start && g.Length + f.Length > clip.Length));
        return (keptHidden, keptFades);
    }

    private static AudioBuffer Joined(AudioBuffer first, AudioBuffer second)
    {
        var channels = new float[first.ChannelCount][];
        for (var c = 0; c < channels.Length; c++)
        {
            channels[c] = first.Channels[c].Concat(second.Channels[c]).ToArray();
        }

        return new AudioBuffer(channels, first.SampleRate);
    }

    /// <summary>A fade's gain at frame <paramref name="i"/> of <paramref name="length"/>: the same S-shaped curve as the Fade buttons.</summary>
    public static float FadeGain(int i, int length, bool rising)
    {
        var t = length < 2 ? 1 : (double)i / (length - 1);
        var gain = 0.5 - 0.5 * Math.Cos(Math.PI * t);
        return (float)(rising ? gain : 1 - gain);
    }

    /// <summary>Audio hidden behind a clip edge: trimmed off the clip, or covered by a cut.</summary>
    /// <param name="Frame">The edge.</param>
    /// <param name="Before">True for audio before a clip's start (its trimmed-off beginning), false for audio after its end.</param>
    private sealed record HiddenAudio(int Frame, bool Before, AudioBuffer Audio)
    {
        /// <summary>True when a change replaces the audio this carries on from, so it no longer fits.</summary>
        public bool TouchedBy(int start, int length) => Before
            ? start <= Frame && Frame < start + Math.Max(1, length)
            : start < Frame && Frame <= start + length;
    }

    /// <summary>A fade made with a clip's fade handle, with the audio it faded so it can be changed.</summary>
    /// <param name="Frame">The clip edge it's at: the start for a fade in, the end for a fade out.</param>
    private sealed record ClipFade(int Frame, bool In, AudioBuffer Original)
    {
        public int Length => Original.Frames;

        public bool Overlaps(int start, int length)
        {
            var low = In ? Frame : Frame - Length;
            var high = low + Length;
            return length == 0 ? low < start && start < high : start < high && low < start + length;
        }
    }
}
