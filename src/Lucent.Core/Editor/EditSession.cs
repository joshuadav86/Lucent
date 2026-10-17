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
/// Methods that change the audio can take seconds, so the editor calls them off the UI thread,
/// one at a time. <see cref="Peaks"/> is replaced as a whole after each change, so the UI can keep
/// drawing the previous version until the new one is ready.
/// </remarks>
public sealed class EditSession
{
    private readonly List<HistoryStep> _undo = new();
    private readonly List<HistoryStep> _redo = new();
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

    /// <summary>Undoes the last change. Returns the range it affected, to select it.</summary>
    public FrameRange? Undo() => Swap(_undo, _redo);

    /// <summary>Redoes the last undone change. Returns the range it affected, to select it.</summary>
    public FrameRange? Redo() => Swap(_redo, _undo);

    private Change Removal(FrameRange range) =>
        new(range.Start, range.Length, AudioBuffer.Silent(Audio.ChannelCount, 0, Audio.SampleRate));

    private void Commit(string description, IReadOnlyList<Change> changes)
    {
        var inverse = new List<Change>();
        foreach (var change in changes)
        {
            inverse.Add(ApplyChange(change));
        }

        // Undoing walks the inverse changes backwards.
        inverse.Reverse();
        _undo.Add(new HistoryStep(_nextStepId++, description, inverse));
        _redo.Clear();
        TrimHistory();
        Peaks = PeakCache.Build(Audio);
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
        to.Add(new HistoryStep(step.Id, step.Description, inverse));
        TrimHistory();
        Peaks = PeakCache.Build(Audio);
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

    private sealed record HistoryStep(long Id, string Description, IReadOnlyList<Change> Changes)
    {
        public long SizeInBytes => Changes.Sum(change => change.Replacement.SizeInBytes);
    }
}
