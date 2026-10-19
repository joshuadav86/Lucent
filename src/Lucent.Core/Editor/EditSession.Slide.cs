namespace WinampInspiredVisualizer.Editor;

/// <summary>Where a clip being slid would land, and how far it would overlap the clips either side of it.</summary>
/// <param name="Offset">How far it moves (what was asked, kept to how far it can go).</param>
/// <param name="Placed">Where all of the clip would be, including any of its start hidden under the clip before it.</param>
/// <param name="OverlapBefore">How far it would overlap the clip before it, crossfading from it.</param>
/// <param name="OverlapAfter">How far it would overlap the clip after it, crossfading into it.</param>
public sealed record SlidePlan(int Offset, FrameRange Placed, int OverlapBefore, int OverlapAfter);

/// <summary>
/// Sliding clips along, as in REAPER: into the silence either side of a clip, or over the clip next to it, where
/// the two overlap and crossfade (an equal-power crossfade at first; right-click it to choose another shape).
/// The audio under an overlap is kept, so sliding the clip away again brings the clips back as they were. The
/// last clip can slide on past the end, making the song longer.
/// </summary>
public sealed partial class EditSession
{
    /// <summary>The shape a new crossfade has.</summary>
    public const FadeShape CrossfadeShape = FadeShape.EqualPower;

    /// <summary>Where a clip would land if it slid by an offset, kept to where it can go.</summary>
    public SlidePlan PlanSlide(int clipIndex, int offset) => Plan(SetUpSlide(clipIndex), offset);

    /// <summary>
    /// Slides a clip along by <paramref name="offset"/> frames, leaving silence where it was. The audio around it
    /// stays where it is: sliding over the clip next to it overlaps the two, with a crossfade, and sliding away
    /// from one it overlapped brings that clip back whole. Only the last clip can slide past the end of the song
    /// (which gets longer). Its fades and what's hidden behind its edges go with it. Returns where the clip now is
    /// (the part of it that's heard on its own, after any crossfade into it).
    /// </summary>
    public FrameRange SlideClip(int clipIndex, int offset)
    {
        var slide = SetUpSlide(clipIndex);
        var plan = Plan(slide, offset);
        var clip = slide.Clip;
        offset = plan.Offset;
        if (offset == 0)
        {
            return clip;
        }

        var clips = Clips;
        FrameRange? previous = slide.Previous is { } p ? clips[p] : null;
        FrameRange? next = slide.Next is { } n ? clips[n] : null;
        var (a0, a1) = (plan.Placed.Start, plan.Placed.End);
        var (overlapBefore, overlapAfter) = (plan.OverlapBefore, plan.OverlapAfter);
        var shapeBefore = slide.CrossfadeIn?.Shape ?? CrossfadeShape;
        var shapeAfter = slide.CrossfadeOut?.Shape ?? CrossfadeShape;

        // The clip's own fades go, where it now overlaps or stops overlapping, and shorten to fit.
        var inFade = FadeAt(clip.Start, fadeIn: true);
        var outFade = FadeAt(clip.End, fadeIn: false);
        var heard = a0 + overlapBefore;
        var fadeOut = overlapAfter > 0 || slide.CrossfadeOut is not null ? 0 : Math.Min(outFade?.Length ?? 0, a1 - heard);
        var fadeIn = overlapBefore > 0 || slide.CrossfadeIn is not null ? 0 : Math.Max(0, Math.Min(inFade?.Length ?? 0, a1 - heard - Math.Max(fadeOut, overlapAfter)));
        var source = UnfadedSource(clip, out var headLength);
        var origin = clip.Start - headLength + offset;

        // What's rewritten: from the end of the clip before (as far back as its fade or the new crossfade goes) to
        // the start of the clip after (as far on as its fade in or the new crossfade goes), or to the end.
        var previousOut = previous is { } before ? FadeAt(before.End, fadeIn: false) : null;
        var nextIn = next is { } after && slide.CrossfadeOut is null ? FadeAt(after.Start, fadeIn: true) : null;
        var nextHidden = next is { } following ? HiddenAt(following.Start, before: true) : null;
        var covered = slide.CrossfadeOut?.Length ?? 0;
        var lo = previous is { } prior ? prior.End - Math.Max(previousOut?.Length ?? 0, overlapBefore) : 0;
        var hi = next is { } later ? Math.Max(Math.Max(later.Start + (nextIn?.Length ?? 0), slide.NextPlacedStart + overlapAfter), later.Start) : Audio.Frames;
        var grown = next is null ? Math.Max(0, a1 - hi) : 0;

        var channels = Audio.ChannelCount;
        var rate = Audio.SampleRate;
        var output = new float[channels][];
        var previousTail = new float[channels][];
        var nextStart = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            var result = output[c] = new float[hi + grown - lo];
            var audio = Audio.Channels[c];
            if (previous is { } pv)
            {
                // The clip before, as it was before its fade out: faded again, or crossfaded under this one.
                previousTail[c] = new float[overlapBefore];
                for (var f = lo; f < pv.End; f++)
                {
                    var value = previousOut is not null && f >= pv.End - previousOut.Length
                        ? previousOut.Original.Channels[c][f - (pv.End - previousOut.Length)]
                        : audio[f];
                    var gain = 1f;
                    if (overlapBefore > 0)
                    {
                        if (f >= pv.End - overlapBefore)
                        {
                            previousTail[c][f - (pv.End - overlapBefore)] = value;
                            gain = FadeGain(f - (pv.End - overlapBefore), overlapBefore, rising: false, shapeBefore);
                        }
                    }
                    else if (previousOut is { Crossfade: false } && f >= pv.End - previousOut.Length)
                    {
                        gain = FadeGain(f - (pv.End - previousOut.Length), previousOut.Length, rising: false, previousOut.Shape);
                    }

                    result[f - lo] = value * gain;
                }
            }

            // The clip itself, where it lands.
            for (var f = a0; f < a1; f++)
            {
                float gain;
                if (f < heard)
                {
                    gain = FadeGain(f - a0, overlapBefore, rising: true, shapeBefore);
                }
                else
                {
                    gain = f < heard + fadeIn ? FadeGain(f - heard, fadeIn, rising: true, inFade!.Shape) : 1f;
                    if (overlapAfter > 0 && f >= a1 - overlapAfter)
                    {
                        gain *= FadeGain(f - (a1 - overlapAfter), overlapAfter, rising: false, shapeAfter);
                    }
                    else if (fadeOut > 0 && f >= a1 - fadeOut)
                    {
                        gain *= FadeGain(f - (a1 - fadeOut), fadeOut, rising: false, outFade!.Shape);
                    }
                }

                result[f - lo] += source[c][f - origin] * gain;
            }

            if (next is { } nx)
            {
                // The clip after, from where it would start with nothing over it: its start, hidden under this
                // clip until now, comes back, and its fade in is made again or crossfaded under this one.
                nextStart[c] = new float[overlapAfter];
                for (var f = slide.NextPlacedStart; f < hi; f++)
                {
                    var value = f < nx.Start
                        ? nextHidden!.Channels[c][nextHidden.Frames - (nx.Start - f)]
                        : nextIn is not null && f < nx.Start + nextIn.Length
                            ? nextIn.Original.Channels[c][f - nx.Start]
                            : audio[f];
                    var gain = 1f;
                    if (overlapAfter > 0)
                    {
                        if (f < slide.NextPlacedStart + overlapAfter)
                        {
                            nextStart[c][f - slide.NextPlacedStart] = value;
                            gain = FadeGain(f - slide.NextPlacedStart, overlapAfter, rising: true, shapeAfter);
                        }
                    }
                    else if (nextIn is not null && f >= nx.Start && f < nx.Start + nextIn.Length)
                    {
                        gain = FadeGain(f - nx.Start, nextIn.Length, rising: true, nextIn.Shape);
                    }

                    result[f - lo] += value * gain;
                }
            }
        }

        AudioBuffer Slice(float[][] from, int start, int length) =>
            new(from.Select(channel => channel.AsSpan(start, length).ToArray()).ToArray(), rate);

        // What's hidden: before the clip, its start (and any of it under the clip before); after it, its end as
        // before; and before the clip after it, that clip's start, as far as this one now covers it.
        var hidden = _hidden.Where(h => !(h.Before && h.Frame == clip.Start) && !(!h.Before && h.Frame == clip.End)
            && !(next is { } nh && h.Before && h.Frame == nh.Start)).ToList();
        if (heard - origin > 0)
        {
            hidden.Add(new HiddenAudio(heard, true, Slice(source, 0, heard - origin)));
        }

        if (HiddenAt(clip.End, before: false) is { } tail)
        {
            hidden.Add(new HiddenAudio(a1, false, tail));
        }

        var nextHeard = slide.NextPlacedStart + overlapAfter;
        if (next is not null)
        {
            var reallyHidden = (nextHidden?.Frames ?? 0) - covered;
            var head = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                head[c] = (nextHidden is null ? Array.Empty<float>() : nextHidden.Channels[c].AsSpan(0, reallyHidden).ToArray())
                    .Concat(nextStart[c]).ToArray();
            }

            if (head[0].Length > 0)
            {
                hidden.Add(new HiddenAudio(nextHeard, true, new AudioBuffer(head, rate)));
            }
        }

        var fades = _fades.Where(f => !(f.In && f.Frame == clip.Start) && !(!f.In && f.Frame == clip.End)
            && !(previous is { } pf && !f.In && f.Frame == pf.End) && !(next is { } nf && f.In && f.Frame == nf.Start)).ToList();
        if (previous is { } end)
        {
            if (overlapBefore > 0)
            {
                fades.Add(new ClipFade(end.End, false, new AudioBuffer(previousTail, rate), shapeBefore, Crossfade: true));
            }
            else if (previousOut is { Crossfade: false })
            {
                fades.Add(previousOut);
            }
        }

        if (fadeIn > 0)
        {
            fades.Add(new ClipFade(heard, true, Slice(source, heard - origin, fadeIn), inFade!.Shape));
        }

        if (overlapAfter > 0)
        {
            fades.Add(new ClipFade(a1, false, Slice(source, a1 - overlapAfter - origin, overlapAfter), shapeAfter, Crossfade: true));
        }
        else if (fadeOut > 0)
        {
            fades.Add(new ClipFade(a1, false, Slice(source, a1 - fadeOut - origin, fadeOut), outFade!.Shape));
        }

        if (overlapAfter == 0 && nextIn is not null)
        {
            fades.Add(nextIn);
        }

        // The splits between the clip before and the clip after go, and new ones go at each clip's edges.
        var regionStart = previous?.End ?? 0;
        var regionEnd = next?.Start ?? Audio.Frames;
        var splits = _splits.Where(s => s < regionStart || s > regionEnd).Append(heard).Append(a1).ToList();
        if (previous is { } left)
        {
            splits.Add(left.End);
        }

        if (next is not null)
        {
            splits.Add(nextHeard);
        }

        // Markers in the clip go with it; ones past the end of the song stay at its end.
        var markers = _markers.Select(m => m.Frame >= clip.Start && m.Frame < clip.End
            ? m with { Frame = m.Frame + offset }
            : next is null && m.Frame >= hi ? m with { Frame = m.Frame + grown } : m).OrderBy(m => m.Frame).ToList();

        Commit(overlapBefore + overlapAfter > 0 ? "Overlap clips" : "Move clip", new[] { new Change(lo, hi - lo, new AudioBuffer(output, rate)) },
            Normalized(splits, Audio.Frames + grown), markersAfter: markers, hiddenAfter: hidden, fadesAfter: fades);
        return new FrameRange(heard, a1 - heard);
    }

    private static SlidePlan Plan(SlideSetup slide, int offset)
    {
        offset = Math.Clamp(offset, slide.MinOffset, slide.MaxOffset);
        var a0 = slide.PlacedStart + offset;
        var a1 = slide.Clip.End + offset;
        var before = slide.PreviousEnd is { } end ? Math.Max(0, end - a0) : 0;
        var after = slide.Next is not null ? Math.Max(0, a1 - slide.NextPlacedStart) : 0;
        return new SlidePlan(offset, new FrameRange(a0, a1 - a0), before, after);
    }

    /// <summary>
    /// A clip about to slide: the clips with sound either side of it (past any silence), any crossfades it has
    /// with them, and how far it can go. It can overlap the one before or after it by all but a frame of the
    /// shorter of the two (less their fades at the far ends), and a clip of silence doesn't overlap.
    /// </summary>
    private SlideSetup SetUpSlide(int clipIndex)
    {
        var clips = Clips;
        var clip = clips[clipIndex];
        var audible = !IsSilent(clip);
        int? previous = null;
        int? next = null;
        for (var i = clipIndex - 1; i >= 0 && previous is null; i--)
        {
            previous = IsSilent(clips[i]) ? null : i;
        }

        for (var i = clipIndex + 1; i < clips.Count && next is null; i++)
        {
            next = IsSilent(clips[i]) ? null : i;
        }

        var crossfadeIn = CrossfadeInto(clip.Start);
        var crossfadeOut = FadeAt(clip.End, fadeIn: false) is { Crossfade: true } fade ? fade : null;
        var placedStart = clip.Start - (crossfadeIn?.Length ?? 0);
        var placedLength = clip.End - placedStart;

        var min = -placedStart;
        int? previousEnd = null;
        if (previous is { } p)
        {
            var before = clips[p];
            previousEnd = before.End;
            var room = audible ? Math.Min(before.Length - (FadeAt(before.Start, fadeIn: true)?.Length ?? 0), placedLength) - 1 : 0;
            min = Math.Min(0, before.End - Math.Max(0, room) - placedStart);
        }

        var max = int.MaxValue / 2;
        var nextPlacedStart = Audio.Frames;
        if (next is { } n)
        {
            var after = clips[n];
            nextPlacedStart = after.Start - (crossfadeOut?.Length ?? 0);
            var room = audible ? Math.Min(after.End - nextPlacedStart - (FadeAt(after.End, fadeIn: false)?.Length ?? 0), placedLength) - 1 : 0;
            max = Math.Max(0, nextPlacedStart + Math.Max(0, room) - clip.End);
        }

        return new SlideSetup(clip, previous, previousEnd, next, crossfadeIn, crossfadeOut, placedStart, nextPlacedStart, min, max);
    }

    /// <summary>
    /// A clip's audio as it was before its fades, with what's hidden either side of it: its start is
    /// <paramref name="headLength"/> frames in.
    /// </summary>
    private float[][] UnfadedSource(FrameRange clip, out int headLength)
    {
        var head = HiddenAt(clip.Start, before: true);
        var tail = HiddenAt(clip.End, before: false);
        var inFade = FadeAt(clip.Start, fadeIn: true);
        var outFade = FadeAt(clip.End, fadeIn: false);
        headLength = head?.Frames ?? 0;
        var tailLength = tail?.Frames ?? 0;
        var source = new float[Audio.ChannelCount][];
        for (var c = 0; c < source.Length; c++)
        {
            var channel = source[c] = new float[headLength + clip.Length + tailLength];
            if (head is not null)
            {
                Array.Copy(head.Channels[c], 0, channel, 0, headLength);
            }

            Array.Copy(Audio.Channels[c], clip.Start, channel, headLength, clip.Length);
            if (inFade is not null)
            {
                Array.Copy(inFade.Original.Channels[c], 0, channel, headLength, inFade.Length);
            }

            if (outFade is not null)
            {
                Array.Copy(outFade.Original.Channels[c], 0, channel, headLength + clip.Length - outFade.Length, outFade.Length);
            }

            if (tail is not null)
            {
                Array.Copy(tail.Channels[c], 0, channel, headLength + clip.Length, tailLength);
            }
        }

        return source;
    }

    /// <param name="PlacedStart">Where the clip really starts, counting any of it hidden under the clip before.</param>
    /// <param name="NextPlacedStart">Where the clip after it really starts, counting any of it hidden under this one.</param>
    private sealed record SlideSetup(FrameRange Clip, int? Previous, int? PreviousEnd, int? Next, ClipFade? CrossfadeIn, ClipFade? CrossfadeOut,
        int PlacedStart, int NextPlacedStart, int MinOffset, int MaxOffset);
}
