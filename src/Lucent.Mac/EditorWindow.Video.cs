using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Lucent.Mac.Editor;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Video;

namespace Lucent.Mac;

/// <summary>
/// Videos in the editor: the sound is edited as any song's is, with the picture shown above it, and Delete and
/// Trim cut the pictures with the sound. Saving copies what's kept of the original pictures and sound as they are,
/// without re-encoding (<see cref="VideoEdit"/>). Everything else changes only the sound, so it's turned off.
/// </summary>
public partial class EditorWindow
{
    private VideoEdit? _videoEdit;
    private VideoPreview? _videoPreview;
    private WriteableBitmap? _previewBitmap;
    private DispatcherTimer? _previewTimer;
    private long _previewFrameShown = -1;
    // The moment of the original video the preview was last asked to show, so it's asked again only when it moves.
    private double _previewAsked = -1;
    private FrameRange _previewSelection;
    private DateTime _previewPlayAsked;

    private bool IsVideo => _videoEdit is not null;

    /// <summary>Starts editing a video that's just been opened: shows its picture above the waveform.</summary>
    private void OpenVideo(VideoEdit video)
    {
        _videoEdit = video;
        try
        {
            _videoPreview = new VideoPreview(video.Movie.Path);
        }
        catch (Exception)
        {
            // Without libVLC, the cuts still work by the sound; there's just no picture.
            _videoPreview = null;
        }

        VideoPanel.IsVisible = true;
        // The effects, plugins and the rest change only the sound, which a video's save doesn't use: only Trim and
        // Delete stay, which cut the pictures too.
        ShowVideoTools(true);
        PluginBar.IsVisible = false;
        _previewAsked = -1;
        _previewTimer ??= new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _previewTimer.Tick -= OnPreviewTick;
        _previewTimer.Tick += OnPreviewTick;
        _previewTimer.Start();
    }

    /// <summary>Lets go of the video, when another sound opens or the editor closes.</summary>
    private void CloseVideo()
    {
        _previewTimer?.Stop();
        _videoPreview?.Dispose();
        _videoPreview = null;
        _videoEdit = null;
        VideoPicture.Source = null;
        _previewBitmap?.Dispose();
        _previewBitmap = null;
        _previewFrameShown = -1;
        VideoPanel.IsVisible = false;
        ShowVideoTools(false);
        PluginBar.IsVisible = _settings.ShowInserts;
    }

    // The tools under the waveform: for a video, only the ones that cut its pictures. What was hidden for a video is
    // put back as it was.
    private readonly Dictionary<Control, bool> _hiddenForVideo = new();

    private void ShowVideoTools(bool video)
    {
        if (video)
        {
            foreach (var tool in ToolsPanel.Children)
            {
                if (tool != TrimButton && tool != DeleteButton && !_hiddenForVideo.ContainsKey(tool))
                {
                    _hiddenForVideo[tool] = tool.IsVisible;
                    tool.IsVisible = false;
                }
            }

            ScopeHint.IsVisible = false;
            return;
        }

        foreach (var (tool, visible) in _hiddenForVideo)
        {
            tool.IsVisible = visible;
        }

        _hiddenForVideo.Clear();
        ScopeHint.IsVisible = true;
    }

    /// <summary>
    /// True (after saying why) when <paramref name="what"/> would change a video's sound alone. Only Delete and Trim
    /// work on a video, since saving copies the original pictures and sound.
    /// </summary>
    private bool VideoRefuses(string what)
    {
        if (!IsVideo)
        {
            return false;
        }

        ShowStatus($"{what} doesn't work on a video. Delete and Trim do: they cut the picture and sound together, without re-encoding.");
        return true;
    }

    // ---- Cutting ----

    private async Task CutVideoAsync(FrameRange selection, bool trim)
    {
        if (_videoEdit is not { } video || _session is null)
        {
            return;
        }

        var cut = trim ? video.SnapTrim(selection) : video.SnapDelete(selection);
        if (cut.IsEmpty)
        {
            ShowStatus(trim
                ? "A video can only be trimmed to start on a keyframe (a complete picture), and there isn't one in the selection. Select a little more."
                : "A video can only be cut where a keyframe (a complete picture) starts what's left, and there isn't one near the selection's end. Select a little more.");
            return;
        }

        var description = trim ? "Trim" : "Delete";
        await EditAsync(description, session =>
        {
            if (trim)
            {
                session.TrimTo(cut);
                video.Trim(cut, session.StateId);
            }
            else
            {
                session.Delete(cut);
                video.Delete(cut, session.StateId);
            }
        }, () => new FrameRange(trim ? 0 : cut.Start, 0), video: true);

        var moved = Math.Abs(cut.Start - selection.Start) + Math.Abs(cut.End - selection.End);
        if (moved > _session.Audio.SampleRate / 100)
        {
            ShowStatus($"{description}: done, from {Format(cut.Start)} to {Format(cut.End)}. The edges moved to the nearest places a video can be cut without re-encoding (a keyframe starts what's left). ⌘Z undoes it.");
        }
    }

    // ---- Saving ----

    /// <summary>Writes the cut video to <paramref name="target"/>, then opens what was saved, as it now is.</summary>
    private async Task<bool> SaveVideoAsync(string target, bool replaceExisting)
    {
        if (_videoEdit is not { } video || _session is not { } session)
        {
            return false;
        }

        string? temporary = null;
        string? problem = null;
        var ok = await RunBusyAsync($"Saving {Path.GetFileName(target)}…", async (progress, token) =>
        {
            try
            {
                temporary = await Task.Run(() => video.Write(target, progress, token), token);
                var ready = temporary;
                await _mainPlayer.RunWithFileReleasedAsync(target, () =>
                {
                    // The preview reads the original, so it lets go of it too.
                    if (replaceExisting)
                    {
                        _videoPreview?.Dispose();
                        _videoPreview = null;
                    }

                    SafeFile.Commit(ready, target, replaceExisting);
                    return Task.CompletedTask;
                });
                temporary = null;
            }
            catch (VideoCutException ex)
            {
                problem = ex.Message;
            }
        });

        if (temporary is not null)
        {
            SafeFile.Discard(temporary);
        }

        if (problem is not null)
        {
            await MessageWindow.ShowAsync(this, problem, "Save");
            return false;
        }

        if (!ok)
        {
            return false;
        }

        session.MarkSaved();
        AudioTools.NotifyMainPlayerChanged();
        if (replaceExisting)
        {
            AudioTools.NotifyFilesReplaced(new[] { target });
        }

        // The saved file is a new video, with its own keyframes: it opens afresh, so cuts start from it.
        _lastSavedPath = target;
        await LoadAsync(target);
        ShowStatus($"Saved \"{Path.GetFileName(target)}\", without re-encoding: the pictures and sound kept are exactly as they were.");
        ShowFileButton.IsVisible = true;
        return true;
    }

    /// <summary>Asks where to save a copy of the cut video, keeping the original.</summary>
    private async Task<bool> SaveVideoAsNewAsync()
    {
        var extension = Path.GetExtension(_path);
        IStorageFile? file;
        try
        {
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save as new version",
                SuggestedFileName = Path.GetFileNameWithoutExtension(_path) + " (edited)" + extension,
                DefaultExtension = extension,
                ShowOverwritePrompt = true,
                SuggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(_path) ?? "."),
                FileTypeChoices = new[] { new FilePickerFileType($"{extension.TrimStart('.').ToUpperInvariant()} video") { Patterns = new[] { "*" + extension } } },
            });
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't ask where to save the video ({ex.Message}).");
            return false;
        }

        if (file?.TryGetLocalPath() is not { } target)
        {
            return false;
        }

        if (!Mp4Cutter.CanCut(target))
        {
            target = Path.ChangeExtension(target, extension);
        }

        return await SaveVideoAsync(target, File.Exists(target));
    }

    // ---- The picture ----

    // Every 30 ms: puts up the newest picture, and keeps the preview at the moment the editor is at.
    private void OnPreviewTick(object? sender, EventArgs e)
    {
        if (_videoEdit is not { } video || _session is not { } session)
        {
            return;
        }

        var rate = (double)session.Audio.SampleRate;
        PreviewTimeText.Text = Format(_player.IsPlaying ? _player.PositionFrame : Waveform.CursorFrame);
        if (_videoPreview is not { } preview)
        {
            return;
        }

        if (_player.IsPlaying)
        {
            // Plays along, moved to the right place when it's drifted off (or the playhead has crossed a cut).
            var wanted = video.OriginalTime(_player.PositionFrame / rate);
            // libVLC takes a moment to start, so it isn't asked again straight away.
            if ((!preview.IsPlaying || Math.Abs(preview.Time - wanted) > 0.35) && DateTime.UtcNow - _previewPlayAsked > TimeSpan.FromMilliseconds(600))
            {
                _previewPlayAsked = DateTime.UtcNow;
                preview.Play(wanted);
            }

            _previewAsked = -1;
        }
        else
        {
            // Shows the moment at the cursor, or the selection's edge that last moved.
            var selection = Selection;
            var frame = Waveform.CursorFrame;
            if (!selection.IsEmpty)
            {
                frame = selection.End != _previewSelection.End && selection.Start == _previewSelection.Start ? selection.End - 1 : selection.Start;
            }

            _previewSelection = selection;
            var wanted = video.OriginalTime(Math.Max(0, frame) / rate);
            if (preview.IsPlaying || Math.Abs(wanted - _previewAsked) > 0.0005)
            {
                _previewAsked = wanted;
                preview.Show(wanted);
            }
        }

        ShowPreviewPicture(preview.Frames);
    }

    private void ShowPreviewPicture(VideoFrames frames)
    {
        if (frames.Count == _previewFrameShown)
        {
            return;
        }

        var shown = frames.CopyLatest((pixels, width, height, stride) =>
        {
            if (_previewBitmap is null || _previewBitmap.PixelSize.Width != width || _previewBitmap.PixelSize.Height != height)
            {
                _previewBitmap?.Dispose();
                _previewBitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
                VideoPicture.Source = _previewBitmap;
            }

            using (var target = _previewBitmap.Lock())
            {
                CopyRows(pixels, stride, target.Address, target.RowBytes, Math.Min(stride, target.RowBytes), height);
            }

            _previewFrameShown = frames.Count;
        });
        if (shown)
        {
            VideoPicture.InvalidateVisual();
        }
    }

    private static unsafe void CopyRows(nint source, int sourceStride, nint target, int targetStride, int rowBytes, int rows)
    {
        if (sourceStride == targetStride)
        {
            Buffer.MemoryCopy((void*)source, (void*)target, (long)targetStride * rows, (long)sourceStride * rows);
            return;
        }

        for (var row = 0; row < rows; row++)
        {
            Buffer.MemoryCopy((byte*)source + (long)row * sourceStride, (byte*)target + (long)row * targetStride, rowBytes, rowBytes);
        }
    }
}
