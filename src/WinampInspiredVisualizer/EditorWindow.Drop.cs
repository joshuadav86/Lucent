using System.IO;
using System.Windows;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;

namespace WinampInspiredVisualizer;

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
        AllowDrop = true;
        PreviewDragOver += OnDragOverEditor;
        PreviewDragEnter += OnDragOverEditor;
        PreviewDragLeave += (_, _) => EndDragHint();
        PreviewDrop += OnDropOnEditor;
    }

    private void OnDragOverEditor(object sender, DragEventArgs e)
    {
        var sounds = DroppedSounds(e);
        e.Effects = sounds.Count > 0 && !IsBusy ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (sounds.Count == 0)
        {
            return;
        }

        _statusBeforeDrag ??= StatusText.Text;
        var what = sounds.Count == 1 ? $"\"{Path.GetFileName(sounds[0])}\"" : $"these {sounds.Count} sounds";
        ShowStatus(IsBusy ? "Wait for the editor to finish, then drop it in."
            : _session is null ? $"Let go to open {what}."
            : DropFrame(e) is { } frame ? $"Let go to add {what} at {WaveformView.FormatTime(frame / (double)_session.Audio.SampleRate)}."
            : $"Let go to open \"{Path.GetFileName(sounds[0])}\" in place of this one. Drop onto the waveform to add it instead.");
    }

    private void EndDragHint()
    {
        if (_statusBeforeDrag is { } before)
        {
            ShowStatus(before);
            _statusBeforeDrag = null;
        }
    }

    private async void OnDropOnEditor(object sender, DragEventArgs e)
    {
        var sounds = DroppedSounds(e);
        var frame = DropFrame(e);
        EndDragHint();
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Handled = true;
        }

        if (IsBusy)
        {
            return;
        }

        if (sounds.Count == 0)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
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
        else if (frame is { } at && !IsVideo && !VideoEdit.IsEditableVideo(sounds[0]))
        {
            await InsertSoundsAsync(sounds, at);
        }
        else if (frame is not null && IsVideo && !VideoEdit.IsEditableVideo(sounds[0]))
        {
            VideoRefuses("Adding a sound");
        }
        else
        {
            await OpenAsync(sounds[0]);
        }
    }

    /// <summary>The dropped files the editor can open, or none if it isn't files being dragged.</summary>
    private static IReadOnlyList<string> DroppedSounds(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) is string[] paths
            ? paths.Where(path => File.Exists(path) && (AudioReaderFactory.IsSupported(path) || VideoEdit.IsEditableVideo(path))).ToList()
            : Array.Empty<string>();

    /// <summary>Where on the sound a drop onto the waveform goes in, or null when it isn't over the waveform.</summary>
    private int? DropFrame(DragEventArgs e)
    {
        if (_session is not { } session)
        {
            return null;
        }

        var at = e.GetPosition(Waveform);
        if (at.X < 0 || at.Y < 0 || at.X > Waveform.ActualWidth || at.Y > Waveform.ActualHeight)
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
                       ". Ctrl+Z takes it out again.");
        }
    }
}
