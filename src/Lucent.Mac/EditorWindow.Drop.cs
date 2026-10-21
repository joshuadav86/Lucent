using Avalonia.Input;
using Avalonia.Platform.Storage;
using Lucent.Mac.Editor;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;

namespace Lucent.Mac;

/// <summary>
/// Sounds dropped on the editor. Into an empty editor, the first one opens and any others go on the end of it.
/// Onto the waveform, they go in where they're let go, each as its own clip (and undo step). Anywhere else in the
/// window, the first one opens in place of the sound that's open (asking first if it has unsaved edits).
/// While something is dragged over the window, the status line says which of these letting go will do.
/// </summary>
public partial class EditorWindow
{
    // The status line from before the drag, put back if the drag leaves without dropping.
    private string? _statusBeforeDrag;

    private void InitDrop()
    {
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragOverEditor, handledEventsToo: true);
        AddHandler(DragDrop.DragOverEvent, OnDragOverEditor, handledEventsToo: true);
        AddHandler(DragDrop.DragLeaveEvent, (_, _) => EndDragHint(), handledEventsToo: true);
        AddHandler(DragDrop.DropEvent, OnDropOnEditor, handledEventsToo: true);
    }

    private void OnDragOverEditor(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.Contains(DataFormat.File);
        e.DragEffects = files && !IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (!files)
        {
            return;
        }

        // macOS only lends the files' names for the drop itself, so the hint doesn't name them.
        _statusBeforeDrag ??= StatusText.Text;
        ShowStatus(IsBusy ? "Wait for the editor to finish, then drop it in."
            : _session is null ? "Let go to open it."
            : DropFrame(e) is { } frame ? $"Let go to add it at {WaveformView.FormatTime(frame / (double)_session.Audio.SampleRate)}."
            : "Let go to open it in place of this sound. Drop onto the waveform to add it instead.");
    }

    private void EndDragHint()
    {
        if (_statusBeforeDrag is { } before)
        {
            ShowStatus(before);
            _statusBeforeDrag = null;
        }
    }

    private async void OnDropOnEditor(object? sender, DragEventArgs e)
    {
        // Read while the drop is still happening: macOS takes the drag data back afterwards.
        var dropped = e.DataTransfer.TryGetFiles() is { } files
            ? files.Select(file => file.TryGetLocalPath()).OfType<string>().ToList()
            : new List<string>();
        var sounds = dropped.Where(path => File.Exists(path) && AudioReaderFactory.IsSupported(path)).ToList();
        var frame = DropFrame(e);
        EndDragHint();
        if (dropped.Count > 0)
        {
            e.DragEffects = DragDropEffects.Copy;
            e.Handled = true;
        }

        if (IsBusy)
        {
            return;
        }

        if (sounds.Count == 0)
        {
            if (dropped.Count > 0)
            {
                ShowStatus("That isn't a sound the editor can open.");
            }

            return;
        }

        Activate();
        if (_session is null)
        {
            if (await OpenAsync(sounds[0]) && sounds.Count > 1 && _session is { } opened)
            {
                await InsertSoundsAsync(sounds.Skip(1).ToList(), opened.Audio.Frames);
            }
        }
        else if (frame is { } at)
        {
            await InsertSoundsAsync(sounds, at);
        }
        else
        {
            await OpenAsync(sounds[0]);
        }
    }

    /// <summary>Where on the sound a drop onto the waveform goes in, or null when it isn't over the waveform.</summary>
    private int? DropFrame(DragEventArgs e)
    {
        if (_session is not { } session)
        {
            return null;
        }

        var at = e.GetPosition(Waveform);
        if (at.X < 0 || at.Y < 0 || at.X > Waveform.Bounds.Width || at.Y > Waveform.Bounds.Height)
        {
            return null;
        }

        return Math.Clamp((int)Math.Round(Waveform.XToFrame(at.X)), 0, session.Audio.Frames);
    }

    /// <summary>
    /// Puts sounds into the one that's open at <paramref name="at"/>, one after another, each as its own clip and undo
    /// step, converted to its sample rate and channels. Selects what went in.
    /// </summary>
    private async Task InsertSoundsAsync(IReadOnlyList<string> paths, int at)
    {
        if (_session is not { } session || paths.Count == 0)
        {
            return;
        }

        var end = at;
        var added = 0;
        var converted = false;
        var message = paths.Count == 1 ? $"Adding \"{Path.GetFileName(paths[0])}\"…" : $"Adding {paths.Count} sounds…";
        await RunBusyAsync(message, async (progress, token) =>
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var path = paths[i];
                var part = i;
                var each = new Progress<double>(value => progress.Report((part + value) / paths.Count));
                var (audio, _, _) = await Task.Run(() => AudioFileLoader.Load(path, each, token), token);
                var target = session.Audio;
                converted |= audio.ChannelCount != target.ChannelCount || audio.SampleRate != target.SampleRate;
                var from = end;
                var pasted = await Task.Run(() => session.Paste(new FrameRange(from, 0),
                    ClipAudio.Bare(audio).ConvertTo(target.ChannelCount, target.SampleRate, token)), token);
                end = pasted.End;
                added++;
            }
        });

        if (added == 0)
        {
            return;
        }

        // Shown even when a later sound failed: the ones before it went in.
        Waveform.SelectedChannel = null;
        ShowAudio();
        Waveform.Selection = new FrameRange(at, end - at);
        if (added == paths.Count)
        {
            var audio = session.Audio;
            ShowStatus((added == 1 ? $"Added \"{Path.GetFileName(paths[0])}\"" : $"Added {added} sounds") +
                       (converted ? $", converted to {(audio.ChannelCount == 1 ? "mono" : "stereo")} at {audio.SampleRate / 1000.0:0.#} kHz" : string.Empty) +
                       ". ⌘Z takes it out again.");
        }
    }
}
