using WinampInspiredVisualizer.Editor.Effects;

namespace WinampInspiredVisualizer.Editor;

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
/// as an unsaved change (they aren't saved in the file).
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
    private long _nextStepId = 1;
    private long _savedStepId;
    // The state below the oldest step still in the undo history (0 is the file as opened).
    private long _baseStepId;

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

    /// <summary>Where the audio is split into clips, in order. Empty when it's one piece.</summary>
    public IReadOnlyList<int> Splits => _splits;

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

    /// <summary>True when the audio differs from the file on disk (or the last save).</summary>
    public bool IsModified => CurrentStepId != _savedStepId;

    private long CurrentStepId => _undo.Count > 0 ? _undo[^1].Id : _baseStepId;

    /// <summary>Remembers the current state as saved, so <see cref="IsModified"/> turns false.</summary>
    public void MarkSaved() => _savedStepId = CurrentStepId;

    /// <summary>
    /// Runs an effect over a range (the whole audio when the range is empty) and puts the result
    /// in its place. Returns where the processed audio now is, and any note from the effect.
    /// </summary>
    public (FrameRange Range, string? Note) Apply(IAudioEffect effect, FrameRange range, IProgress<double>? progress,
        CancellationToken cancellation)
    {
        if (range.IsEmpty)
        {
            range = new FrameRange(0, Audio.Frames);
        }

        var context = new EffectContext(progress, cancellation);
        var result = effect.Process(Audio.Slice(range.Start, range.Length), context);
        if (result.ChannelCount != Audio.ChannelCount || result.SampleRate != Audio.SampleRate)
        {
            throw new InvalidOperationException($"{effect.Description} changed the channels or sample rate.");
        }

        cancellation.ThrowIfCancellationRequested();
        Commit(effect.Description, new[] { new Change(range.Start, range.Length, result) });
        return (new FrameRange(range.Start, result.Frames), context.Note);
    }

    /// <summary>Removes a range, closing the gap.</summary>
    public void Delete(FrameRange range)
    {
        if (!range.IsEmpty)
        {
            Commit("Delete", new[] { Removal(range) });
        }
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

        var audio = Audio.Slice(clip.Start, clip.Length);
        var splits = new List<int>();
        var position = 0;
        foreach (var piece in order)
        {
            position += piece.Length;
            splits.Add(position);
        }

        Commit("Move clip", new[] { Removal(clip), new Change(destination, 0, audio) }, Normalized(splits));
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

    /// <summary>Undoes the last change. Returns the range it affected, to select it.</summary>
    public FrameRange? Undo() => Swap(_undo, _redo);

    /// <summary>Redoes the last undone change. Returns the range it affected, to select it.</summary>
    public FrameRange? Redo() => Swap(_redo, _undo);

    private Change Removal(FrameRange range) =>
        new(range.Start, range.Length, AudioBuffer.Silent(Audio.ChannelCount, 0, Audio.SampleRate));

    /// <summary>
    /// Makes the changes as one step. The splits move with the audio around them, unless the step says
    /// where they go (<paramref name="splitsAfter"/>).
    /// </summary>
    private void Commit(string description, IReadOnlyList<Change> changes, List<int>? splitsAfter = null)
    {
        var splitsBefore = _splits;
        var splits = _splits;
        var inverse = new List<Change>();
        foreach (var change in changes)
        {
            splits = Shifted(splits, change);
            inverse.Add(ApplyChange(change));
        }

        _splits = splitsAfter ?? Normalized(splits);
        // Undoing walks the inverse changes backwards.
        inverse.Reverse();
        // A step that only moves splits leaves the audio as it was, so it keeps the same id and the
        // audio doesn't count as changed since the last save.
        var id = changes.Count > 0 ? _nextStepId++ : CurrentStepId;
        _undo.Add(new HistoryStep(id, description, inverse, splitsBefore));
        _redo.Clear();
        TrimHistory();
        if (changes.Count > 0)
        {
            Peaks = PeakCache.Build(Audio);
        }
    }

    /// <summary>Where splits end up after a change: later ones move with the audio, ones inside a stretched range scale with it.</summary>
    private static List<int> Shifted(List<int> splits, Change change)
    {
        var end = change.Start + change.Length;
        var delta = change.Replacement.Frames - change.Length;
        var result = new List<int>(splits.Count);
        foreach (var split in splits)
        {
            if (split <= change.Start)
            {
                result.Add(split);
            }
            else if (split >= end)
            {
                result.Add(split + delta);
            }
            else if (delta == 0)
            {
                result.Add(split);
            }
            else if (change.Replacement.Frames > 0)
            {
                result.Add(change.Start + (int)((long)(split - change.Start) * change.Replacement.Frames / change.Length));
            }

            // A split inside audio that was removed goes with it.
        }

        return result;
    }

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
        to.Add(new HistoryStep(step.Id, step.Description, inverse, _splits));
        _splits = step.Splits;
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

    /// <param name="Splits">Where the splits go when this step is undone (or redone).</param>
    private sealed record HistoryStep(long Id, string Description, IReadOnlyList<Change> Changes, List<int> Splits)
    {
        public long SizeInBytes => Changes.Sum(change => change.Replacement.SizeInBytes);
    }
}
