using WinampInspiredVisualizer.Editor.Effects;

namespace WinampInspiredVisualizer.Editor;

/// <summary>One entry in the editor's history list.</summary>
/// <param name="Details">More about the step than its description says (a plugin chain's plugins), or null.</param>
/// <param name="IsUndone">True for a step that was undone and can be redone.</param>
public sealed record HistoryEntry(string Description, string? Details, DateTime Time, bool IsUndone);

/// <summary>A range of frames: where it starts and how long it is.</summary>
public readonly record struct FrameRange(int Start, int Length)
{
    public int End => Start + Length;

    public bool IsEmpty => Length <= 0;
}

/// <summary>
/// The audio being edited, with undo and redo. Every change replaces one or more ranges of
/// frames; the history keeps only the frames each change replaced, never whole copies of the
/// song, and forgets the oldest steps once it holds more than <see cref="HistoryBudgetBytes"/>.
/// </summary>
/// <remarks>
/// Splits cut the audio into clips that can be moved around, with silence put between them. They are
/// part of the history too, but only mark places in the audio, so splitting on its own doesn't count
/// as an unsaved change (they aren't saved in the file). Markers work the same way.
/// </remarks>
/// <remarks>
/// Cutting, pasting and moving clips join audio that wasn't next to each other before. With
/// <see cref="JoinFrames"/> set, each join is a short crossfade: both sides play on past the join and
/// fade across, so there's no click where the waveform jumps.
/// </remarks>
/// <remarks>
/// Methods that change the audio can take seconds, so the editor calls them off the UI thread,
/// one at a time. <see cref="Peaks"/> is replaced as a whole after each change, so the UI can keep
/// drawing the previous version until the new one is ready.
/// </remarks>
public sealed class EditSession
{
    private readonly List<HistoryStep> _undo = new();
    private readonly List<HistoryStep> _redo = new();
    // Where the audio is split into clips: sorted, each strictly between the start and the end.
    private List<int> _splits = new();
    // Sorted by frame.
    private List<Marker> _markers = new();
    private long _nextStepId = 1;
    private long _savedStepId;
    // The state below the oldest step still in the undo history (0 is the file as opened).
    private long _baseStepId;
    // What the history list shows for that state: the file as opened, or the steps forgotten to save memory.
    private string _baseDescription = "Opened";
    private DateTime _baseTime = DateTime.Now;

    public EditSession(AudioBuffer audio)
    {
        Audio = audio;
        Peaks = PeakCache.Build(audio);
        // Keep the history to a sensible share of the computer's memory.
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        HistoryBudgetBytes = Math.Clamp(available / 8, 256L << 20, 1536L << 20);
    }

    public AudioBuffer Audio { get; private set; }

    public PeakCache Peaks { get; private set; }

    public long HistoryBudgetBytes { get; }

    /// <summary>
    /// How far each side of a join made by a cut, paste or clip move plays on under the other, in frames;
    /// the crossfade is twice this long. 0 joins the audio as it is.
    /// </summary>
    public int JoinFrames { get; set; }

    /// <summary>Where the audio is split into clips, in order. Empty when it's one piece.</summary>
    public IReadOnlyList<int> Splits => _splits;

    /// <summary>The markers, in order along the audio.</summary>
    public IReadOnlyList<Marker> Markers => _markers;

    /// <summary>The clips between the splits, in order; the whole audio is one clip until it's split.</summary>
    public IReadOnlyList<FrameRange> Clips
    {
        get
        {
            var clips = new List<FrameRange>(_splits.Count + 1);
            var start = 0;
            foreach (var split in _splits.Append(Audio.Frames))
            {
                clips.Add(new FrameRange(start, split - start));
                start = split;
            }

            return clips;
        }
    }

    /// <summary>The number of the clip a frame is in (a frame on a split belongs to the clip after it).</summary>
    public int ClipAt(int frame)
    {
        var index = _splits.BinarySearch(frame);
        return index >= 0 ? index + 1 : ~index;
    }

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoDescription => _undo.Count > 0 ? _undo[^1].Description : null;

    public string? RedoDescription => _redo.Count > 0 ? _redo[^1].Description : null;

    /// <summary>
    /// Everything done to the audio, oldest first: the state it started from, then each step, then the steps
    /// that were undone (and could be redone), nearest first. <see cref="HistoryPosition"/> is the one the
    /// audio is at now; <see cref="JumpTo"/> goes to any of them.
    /// </summary>
    public IReadOnlyList<HistoryEntry> History
    {
        get
        {
            var entries = new List<HistoryEntry>(_undo.Count + _redo.Count + 1)
            {
                new(_baseDescription, null, _baseTime, IsUndone: false),
            };
            entries.AddRange(_undo.Select(step => new HistoryEntry(step.Description, step.Details, step.Time, IsUndone: false)));
            entries.AddRange(Enumerable.Reverse(_redo).Select(step => new HistoryEntry(step.Description, step.Details, step.Time, IsUndone: true)));
            return entries;
        }
    }

    /// <summary>Which entry of <see cref="History"/> the audio is at now.</summary>
    public int HistoryPosition => _undo.Count;

    /// <summary>
    /// Undoes or redoes as many steps as it takes to get to an entry of <see cref="History"/>. Returns the
    /// range the last of them affected, to select it.
    /// </summary>
    public FrameRange? JumpTo(int position)
    {
        // Counted as moves, not as a target size: on a long jump the oldest steps may be let go part way.
        var moves = Math.Clamp(position, 0, _undo.Count + _redo.Count) - _undo.Count;
        FrameRange? range = null;
        for (; moves < 0 && CanUndo; moves++)
        {
            range = Undo() ?? range;
        }

        for (; moves > 0 && CanRedo; moves--)
        {
            range = Redo() ?? range;
        }

        return range;
    }

    /// <summary>True when the audio differs from the file on disk (or the last save).</summary>
    public bool IsModified => CurrentStepId != _savedStepId;

    private long CurrentStepId => _undo.Count > 0 ? _undo[^1].Id : _baseStepId;

    /// <summary>Remembers the current state as saved, so <see cref="IsModified"/> turns false.</summary>
    public void MarkSaved() => _savedStepId = CurrentStepId;

    /// <summary>
    /// Runs an effect over a range (the whole audio when the range is empty) and puts the result
    /// in its place. Returns where the processed audio now is, and any note from the effect.
    /// </summary>
    /// <param name="channel">
    /// Only change this channel (0 is left), leaving the other as it was; null changes them all. An effect
    /// that changes the length can't work on one channel alone, and throws <see cref="EditException"/>.
    /// </param>
    public (FrameRange Range, string? Note) Apply(IAudioEffect effect, FrameRange range, IProgress<double>? progress,
        CancellationToken cancellation, int? channel = null)
    {
        if (range.IsEmpty)
        {
            range = new FrameRange(0, Audio.Frames);
        }

        if (channel is { } only && (only < 0 || only >= Audio.ChannelCount))
        {
            channel = null;
        }

        var context = new EffectContext(progress, cancellation);
        var result = effect.Process(Audio.Slice(range.Start, range.Length), context);
        if (result.ChannelCount != Audio.ChannelCount || result.SampleRate != Audio.SampleRate)
        {
            throw new InvalidOperationException($"{effect.Description} changed the channels or sample rate.");
        }

        if (channel is { } kept)
        {
            if (result.Frames != range.Length)
            {
                throw new EditException(
                    $"{effect.Description} changes the length, so it can't be used on one channel alone. Select both channels to use it.");
            }

            for (var c = 0; c < Audio.ChannelCount; c++)
            {
                if (c != kept)
                {
                    Array.Copy(Audio.Channels[c], range.Start, result.Channels[c], 0, range.Length);
                }
            }
        }

        cancellation.ThrowIfCancellationRequested();
        var description = channel is null || Audio.ChannelCount < 2
            ? effect.Description
            : $"{effect.Description} ({(channel == 0 ? "left" : "right")})";
        Commit(description, new[] { new Change(range.Start, range.Length, result) }, details: effect.Details);
        return (new FrameRange(range.Start, result.Frames), context.Note);
    }

    /// <summary>Removes a range, closing the gap (crossfading the join).</summary>
    public void Delete(FrameRange range, string description = "Delete")
    {
        if (range.IsEmpty)
        {
            return;
        }

        var edit = new Edit(range.Start, range.Length, 0);
        Commit(description, new[] { JoinedSplice(range.Start, range.End, null) }, Remapped(_splits, edit, keepInside: false),
            new[] { edit });
    }

    /// <summary>A copy of a range, with what's either side of it so pasting can crossfade.</summary>
    public ClipAudio Copy(FrameRange range) => ClipAudio.From(Audio, range);

    /// <summary>
    /// Puts copied audio in place of a range (or at a frame, when the range is empty), crossfading both joins.
    /// The clip must already have the session's channels and sample rate (<see cref="ClipAudio.ConvertTo"/>).
    /// Returns where the pasted audio is.
    /// </summary>
    public FrameRange Paste(FrameRange range, ClipAudio clip)
    {
        if (clip.Audio.ChannelCount != Audio.ChannelCount || clip.Audio.SampleRate != Audio.SampleRate)
        {
            throw new ArgumentException("Convert the clip to this audio's format first.", nameof(clip));
        }

        var start = Math.Clamp(range.Start, 0, Audio.Frames);
        var end = Math.Clamp(range.End, start, Audio.Frames);
        if (clip.Frames == 0)
        {
            return new FrameRange(start, 0);
        }

        var edit = new Edit(start, end - start, clip.Frames);
        Commit(range.IsEmpty ? "Paste" : "Paste over selection", new[] { JoinedSplice(start, end, clip) },
            Remapped(_splits, edit, keepInside: false), new[] { edit });
        return new FrameRange(start, clip.Frames);
    }

    /// <summary>Keeps only a range, removing everything before and after it.</summary>
    public void TrimTo(FrameRange range)
    {
        if (range.IsEmpty)
        {
            return;
        }

        // The end goes first so the start position stays valid; only the removed audio is remembered.
        var changes = new List<Change>();
        if (range.End < Audio.Frames)
        {
            changes.Add(Removal(new FrameRange(range.End, Audio.Frames - range.End)));
        }

        if (range.Start > 0)
        {
            changes.Add(Removal(new FrameRange(0, range.Start)));
        }

        if (changes.Count > 0)
        {
            Commit("Trim", changes);
        }
    }

    /// <summary>Splits the audio at each of the given frames. Returns false if none of them made a new split.</summary>
    public bool Split(IEnumerable<int> frames)
    {
        var splits = Normalized(_splits.Concat(frames));
        if (splits.Count == _splits.Count)
        {
            return false;
        }

        Commit("Split", Array.Empty<Change>(), splits);
        return true;
    }

    /// <summary>Joins a clip to the next one by removing the split between them.</summary>
    public void Join(int split)
    {
        if (_splits.Contains(split))
        {
            Commit("Join clips", Array.Empty<Change>(), _splits.Where(s => s != split).ToList());
        }
    }

    /// <summary>
    /// Moves a clip so it becomes clip number <paramref name="toIndex"/>, the others closing up around it.
    /// Returns where the clip now is.
    /// </summary>
    public FrameRange MoveClip(int clipIndex, int toIndex)
    {
        var clips = Clips;
        var clip = clips[clipIndex];
        toIndex = Math.Clamp(toIndex, 0, clips.Count - 1);
        if (toIndex == clipIndex)
        {
            return clip;
        }

        // Where it goes once it's been taken out: after the clips that end up before it.
        var order = clips.ToList();
        order.RemoveAt(clipIndex);
        var destination = order.Take(toIndex).Sum(piece => piece.Length);
        order.Insert(toIndex, clip);

        var copy = Copy(clip);
        var splits = new List<int>();
        var position = 0;
        foreach (var piece in order)
        {
            position += piece.Length;
            splits.Add(position);
        }

        // Markers travel with the clip they're in; the rest close up and open out around it.
        var removal = new Edit(clip.Start, clip.Length, 0);
        var insertion = new Edit(destination, 0, clip.Length);
        var markers = _markers.Select(m => m.Frame >= clip.Start && m.Frame < clip.End
            ? m with { Frame = destination + m.Frame - clip.Start }
            : m with { Frame = Remap(Remap(m.Frame, removal, true), insertion, true) }).OrderBy(m => m.Frame).ToList();

        // The insertion is worked out once the clip has been taken out, from the audio as it is by then.
        IEnumerable<Change> Changes()
        {
            yield return JoinedSplice(clip.Start, clip.End, null);
            yield return JoinedSplice(destination, destination, copy);
        }

        Commit("Move clip", Changes(), Normalized(splits), markersAfter: markers);
        return new FrameRange(destination, clip.Length);
    }

    /// <summary>Puts silence in at a frame, as a clip of its own. Returns where the silence is.</summary>
    public FrameRange InsertSilence(int at, int frames)
    {
        at = Math.Clamp(at, 0, Audio.Frames);
        if (frames <= 0)
        {
            return new FrameRange(at, 0);
        }

        var splits = _splits.Select(s => s >= at ? s + frames : s).Append(at).Append(at + frames);
        Commit("Add silence", new[] { new Change(at, 0, AudioBuffer.Silent(Audio.ChannelCount, frames, Audio.SampleRate)) },
            Normalized(splits, Audio.Frames + frames));
        return new FrameRange(at, frames);
    }

    /// <summary>
    /// Changes the whole audio to one channel (mixing them, at their average level) or to two (copying mono
    /// to both sides). Saving afterwards writes a file with that many channels.
    /// </summary>
    public bool ConvertChannels(int channels)
    {
        if (channels is < 1 or > 2 || channels == Audio.ChannelCount)
        {
            return false;
        }

        var frames = Audio.Frames;
        var mixed = new float[frames];
        foreach (var channel in Audio.Channels)
        {
            for (var i = 0; i < frames; i++)
            {
                mixed[i] += channel[i] / Audio.ChannelCount;
            }
        }

        var result = channels == 1 ? new[] { mixed } : new[] { mixed, (float[])mixed.Clone() };
        Commit(channels == 1 ? "Convert to mono" : "Convert to stereo",
            new[] { new Change(0, frames, new AudioBuffer(result, Audio.SampleRate)) });
        return true;
    }

    // ---- Markers ----

    /// <summary>Puts a marker at a frame. Returns its number in <see cref="Markers"/>.</summary>
    public int AddMarker(int frame, string name)
    {
        var marker = new Marker(Math.Clamp(frame, 0, Audio.Frames), name);
        var markers = _markers.Append(marker).OrderBy(m => m.Frame).ToList();
        Commit("Add marker", Array.Empty<Change>(), markersAfter: markers);
        return _markers.IndexOf(marker);
    }

    public void RenameMarker(int index, string name)
    {
        if (index >= 0 && index < _markers.Count && _markers[index].Name != name)
        {
            var markers = _markers.ToList();
            markers[index] = markers[index] with { Name = name };
            Commit("Rename marker", Array.Empty<Change>(), markersAfter: markers);
        }
    }

    public void MoveMarker(int index, int frame)
    {
        if (index >= 0 && index < _markers.Count && _markers[index].Frame != frame)
        {
            var markers = _markers.ToList();
            markers[index] = markers[index] with { Frame = Math.Clamp(frame, 0, Audio.Frames) };
            Commit("Move marker", Array.Empty<Change>(), markersAfter: markers.OrderBy(m => m.Frame).ToList());
        }
    }

    public void RemoveMarker(int index)
    {
        if (index >= 0 && index < _markers.Count)
        {
            Commit("Delete marker", Array.Empty<Change>(), markersAfter: _markers.Where((_, i) => i != index).ToList());
        }
    }

    /// <summary>Puts markers at several frames in one step (the clipping finder uses this). Returns how many were added.</summary>
    public int AddMarkers(IEnumerable<(int Frame, string Name)> markers, string description)
    {
        var added = markers.Select(m => new Marker(Math.Clamp(m.Frame, 0, Audio.Frames), m.Name)).ToList();
        if (added.Count > 0)
        {
            Commit(description, Array.Empty<Change>(), markersAfter: _markers.Concat(added).OrderBy(m => m.Frame).ToList());
        }

        return added.Count;
    }

    /// <summary>Removes every marker <paramref name="which"/> picks, in one step. Returns how many went.</summary>
    public int RemoveMarkers(Func<Marker, bool> which, string description)
    {
        var kept = _markers.Where(m => !which(m)).ToList();
        var removed = _markers.Count - kept.Count;
        if (removed > 0)
        {
            Commit(description, Array.Empty<Change>(), markersAfter: kept);
        }

        return removed;
    }

    /// <summary>Undoes the last change. Returns the range it affected, to select it.</summary>
    public FrameRange? Undo() => Swap(_undo, _redo);

    /// <summary>Redoes the last undone change. Returns the range it affected, to select it.</summary>
    public FrameRange? Redo() => Swap(_redo, _undo);

    private Change Removal(FrameRange range) =>
        new(range.Start, range.Length, AudioBuffer.Silent(Audio.ChannelCount, 0, Audio.SampleRate));

    /// <summary>
    /// Replaces frames <paramref name="start"/> to <paramref name="end"/> with <paramref name="insert"/> (nothing,
    /// for a cut), crossfading each join over up to twice <see cref="JoinFrames"/>. The change covers a little
    /// either side of the range, where the crossfades are.
    /// </summary>
    private Change JoinedSplice(int start, int end, ClipAudio? insert)
    {
        var audio = Audio;
        var frames = audio.Frames;
        var channels = audio.ChannelCount;
        var join = Math.Max(0, JoinFrames);
        var length = insert?.Frames ?? 0;
        if (insert is null || length == 0)
        {
            // A cut: the audio before plays on into the removed part, fading out under the audio after,
            // which fades in from a little before where it was.
            var half = Math.Min(join, Math.Min(start, frames - end));
            if (half <= 0)
            {
                return Removal(new FrameRange(start, end - start));
            }

            var blended = new float[channels][];
            for (var c = 0; c < channels; c++)
            {
                var source = audio.Channels[c];
                var result = new float[2 * half];
                for (var i = 0; i < result.Length; i++)
                {
                    var w = Rising(i, result.Length);
                    result[i] = source[start - half + i] * (1 - w) + source[end - half + i] * w;
                }

                blended[c] = result;
            }

            return new Change(start - half, end - start + 2 * half, new AudioBuffer(blended, audio.SampleRate));
        }

        var context = Math.Min(insert.Before.Frames, insert.After.Frames);
        var left = Math.Min(Math.Min(join, context), Math.Min(Math.Min(start, frames - start), length / 2));
        var right = Math.Min(Math.Min(join, context), Math.Min(Math.Min(end, frames - end), length / 2));
        var middle = length - left - right;
        var output = new float[channels][];
        for (var c = 0; c < channels; c++)
        {
            var source = audio.Channels[c];
            var clip = insert.Audio.Channels[c];
            var before = insert.Before.Channels[c];
            var after = insert.After.Channels[c];
            float Clip(int i) => i < 0 ? before[before.Length + i] : i >= length ? after[i - length] : clip[i];

            var result = new float[length + left + right];
            for (var i = 0; i < 2 * left; i++)
            {
                var w = Rising(i, 2 * left);
                result[i] = source[start - left + i] * (1 - w) + Clip(i - left) * w;
            }

            Array.Copy(clip, left, result, 2 * left, middle);
            var tail = 2 * left + middle;
            for (var i = 0; i < 2 * right; i++)
            {
                var w = Rising(i, 2 * right);
                result[tail + i] = Clip(length - right + i) * (1 - w) + source[end - right + i] * w;
            }

            output[c] = result;
        }

        return new Change(start - left, end - start + left + right, new AudioBuffer(output, audio.SampleRate));
    }

    /// <summary>A crossfade's rising half: from just above 0 to just below 1 over <paramref name="length"/> frames, S-shaped.</summary>
    private static float Rising(int i, int length) => (float)(0.5 - 0.5 * Math.Cos(Math.PI * (i + 0.5) / length));

    /// <summary>
    /// Makes the changes as one step. The splits and markers move with the audio around them, unless the
    /// step says where they go. <paramref name="edits"/> says how frames moved, when a change covers more than
    /// the edit (a crossfade either side); otherwise the changes themselves say it.
    /// </summary>
    /// <remarks>Changes are made one at a time, in order, so a later one can be worked out from the audio the earlier ones left.</remarks>
    private void Commit(string description, IEnumerable<Change> changes, List<int>? splitsAfter = null,
        IReadOnlyList<Edit>? edits = null, List<Marker>? markersAfter = null, string? details = null)
    {
        var splitsBefore = _splits;
        var markersBefore = _markers;
        var splits = _splits;
        var markers = _markers;
        var inverse = new List<Change>();
        foreach (var change in changes)
        {
            if (edits is null)
            {
                var edit = new Edit(change.Start, change.Length, change.Replacement.Frames);
                splits = Remapped(splits, edit, keepInside: false);
                markers = markers.Select(m => m with { Frame = Remap(m.Frame, edit, keepInside: true) }).ToList();
            }

            inverse.Add(ApplyChange(change));
        }

        if (edits is not null)
        {
            foreach (var edit in edits)
            {
                markers = markers.Select(m => m with { Frame = Remap(m.Frame, edit, keepInside: true) }).ToList();
            }
        }

        _splits = splitsAfter ?? Normalized(splits);
        _markers = markersAfter ?? markers;
        // Undoing walks the inverse changes backwards.
        inverse.Reverse();
        // A step that only moves splits or markers leaves the audio as it was, so it keeps the same id and the
        // audio doesn't count as changed since the last save.
        var id = inverse.Count > 0 ? _nextStepId++ : CurrentStepId;
        _undo.Add(new HistoryStep(id, description, details, DateTime.Now, inverse, splitsBefore, markersBefore));
        _redo.Clear();
        TrimHistory();
        if (inverse.Count > 0)
        {
            Peaks = PeakCache.Build(Audio);
        }
    }

    /// <summary>
    /// Where a frame ends up after an edit: later ones move with the audio, ones inside a stretched range scale
    /// with it, and ones inside a removed range go to where it was (or, with <paramref name="keepInside"/> false, -1).
    /// </summary>
    private static int Remap(int frame, Edit edit, bool keepInside)
    {
        var end = edit.Start + edit.Length;
        if (frame <= edit.Start)
        {
            return frame;
        }

        if (frame >= end)
        {
            return frame + edit.NewLength - edit.Length;
        }

        if (edit.NewLength == edit.Length)
        {
            return frame;
        }

        if (edit.NewLength > 0)
        {
            return edit.Start + (int)((long)(frame - edit.Start) * edit.NewLength / edit.Length);
        }

        return keepInside ? edit.Start : -1;
    }

    /// <summary>Splits after an edit. A split inside audio that was removed goes with it.</summary>
    private List<int> Remapped(List<int> splits, Edit edit, bool keepInside) =>
        Normalized(splits.Select(s => Remap(s, edit, keepInside)).Where(s => s >= 0), Audio.Frames + edit.NewLength - edit.Length);

    /// <summary>Sorted, without repeats, and only the splits strictly inside the audio.</summary>
    private List<int> Normalized(IEnumerable<int> splits, int? frames = null)
    {
        var length = frames ?? Audio.Frames;
        return splits.Where(s => s > 0 && s < length).Distinct().Order().ToList();
    }

    private FrameRange? Swap(List<HistoryStep> from, List<HistoryStep> to)
    {
        if (from.Count == 0)
        {
            return null;
        }

        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        var inverse = new List<Change>();
        var start = int.MaxValue;
        var end = 0;
        foreach (var change in step.Changes)
        {
            inverse.Add(ApplyChange(change));
            start = Math.Min(start, change.Start);
            end = Math.Max(end, change.Start + change.Replacement.Frames);
        }

        inverse.Reverse();
        // The step keeps its id, so undoing back to the saved state counts as unmodified again.
        to.Add(step with { Changes = inverse, Splits = _splits, Markers = _markers });
        _splits = step.Splits;
        _markers = step.Markers;
        TrimHistory();
        if (step.Changes.Count > 0)
        {
            Peaks = PeakCache.Build(Audio);
        }

        return start == int.MaxValue ? null : new FrameRange(start, Math.Max(0, end - start));
    }

    /// <summary>Puts a change's audio in place, returning the change that would put back what was there.</summary>
    private Change ApplyChange(Change change)
    {
        if (change.Replacement.ChannelCount != Audio.ChannelCount)
        {
            // Converting between mono and stereo swaps the whole audio at once.
            var whole = Audio;
            Audio = change.Replacement;
            return new Change(0, change.Replacement.Frames, whole);
        }

        var removed = Audio.Slice(change.Start, change.Length);
        Audio = Audio.Splice(change.Start, change.Length, change.Replacement);
        return new Change(change.Start, change.Replacement.Frames, removed);
    }

    /// <summary>Forgets the oldest undo steps while the history is over budget (always keeping the latest one).</summary>
    private void TrimHistory()
    {
        long Size(IEnumerable<HistoryStep> steps) => steps.Sum(s => s.SizeInBytes);
        while (_undo.Count > 1 && Size(_undo) + Size(_redo) > HistoryBudgetBytes)
        {
            _baseStepId = _undo[0].Id;
            _baseDescription = "Earlier steps (let go to save memory)";
            _baseTime = _undo[0].Time;
            _undo.RemoveAt(0);
        }

        // An undone step can be dropped too, if one step on its own is too big.
        while (_redo.Count > 1 && Size(_undo) + Size(_redo) > HistoryBudgetBytes)
        {
            _redo.RemoveAt(0);
        }
    }

    /// <summary>Replace <see cref="Length"/> frames at <see cref="Start"/> with <see cref="Replacement"/>.</summary>
    private sealed record Change(int Start, int Length, AudioBuffer Replacement);

    /// <summary>How an edit moved the audio: <see cref="Length"/> frames at <see cref="Start"/> became <see cref="NewLength"/>.</summary>
    private readonly record struct Edit(int Start, int Length, int NewLength);

    /// <param name="Splits">Where the splits go when this step is undone (or redone).</param>
    /// <param name="Markers">Where the markers go when this step is undone (or redone).</param>
    /// <param name="Time">When the step was first made (redoing it keeps the time).</param>
    private sealed record HistoryStep(long Id, string Description, string? Details, DateTime Time, IReadOnlyList<Change> Changes,
        List<int> Splits, List<Marker> Markers)
    {
        public long SizeInBytes => Changes.Sum(change => change.Replacement.SizeInBytes);
    }
}
