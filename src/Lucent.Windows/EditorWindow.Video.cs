using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Video;

namespace WinampInspiredVisualizer;

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

        VideoPanel.Visibility = Visibility.Visible;
        // The effects, plugins and the rest change only the sound, which a video's save doesn't use: only Trim and
        // Delete stay, which cut the pictures too.
        ShowVideoTools(true);
        PluginBar.Visibility = Visibility.Collapsed;
        _previewAsked = -1;
        _previewTimer ??= new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = TimeSpan.FromMilliseconds(30) };
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
        _previewBitmap = null;
        VideoPicture.Source = null;
        _previewFrameShown = -1;
        VideoPanel.Visibility = Visibility.Collapsed;
        ShowVideoTools(false);
        PluginBar.Visibility = _settings.ShowInserts ? Visibility.Visible : Visibility.Collapsed;
    }

    // The tools under the waveform: for a video, only the ones that cut its pictures. What was hidden for a video is
    // put back as it was.
    private readonly Dictionary<UIElement, Visibility> _hiddenForVideo = new();

    private void ShowVideoTools(bool video)
    {
        if (video)
        {
            foreach (UIElement tool in ToolsPanel.Children)
            {
                if (tool != TrimButton && tool != DeleteButton && tool != SavePicturesButton && !_hiddenForVideo.ContainsKey(tool))
                {
                    _hiddenForVideo[tool] = tool.Visibility;
                    tool.Visibility = Visibility.Collapsed;
                }
            }

            ScopeHint.Visibility = Visibility.Collapsed;
            SavePicturesButton.Visibility = Visibility.Visible;
            return;
        }

        SavePicturesButton.Visibility = Visibility.Collapsed;

        foreach (var (tool, visibility) in _hiddenForVideo)
        {
            tool.Visibility = visibility;
        }

        _hiddenForVideo.Clear();
        ScopeHint.Visibility = Visibility.Visible;
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
            ShowStatus($"{description}: done, from {Format(cut.Start)} to {Format(cut.End)}. The edges moved to the nearest places a video can be cut without re-encoding (a keyframe starts what's left). Ctrl+Z undoes it.");
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
            MessageBox.Show(this, problem, "Save", MessageBoxButton.OK, MessageBoxImage.Warning);
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
        ShowFileButton.Visibility = Visibility.Visible;
        return true;
    }

    /// <summary>Asks where to save a copy of the cut video, keeping the original.</summary>
    private async Task<bool> SaveVideoAsNewAsync()
    {
        var extension = Path.GetExtension(_path);
        var dialog = new SaveFileDialog
        {
            Title = "Save as new version",
            InitialDirectory = Path.GetDirectoryName(_path),
            FileName = Path.GetFileNameWithoutExtension(_path) + " (edited)" + extension,
            DefaultExt = extension,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()} video|*{extension}",
        };
        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        var target = dialog.FileName;
        if (!Mp4Cutter.CanCut(target))
        {
            target = Path.ChangeExtension(target, extension);
        }

        return await SaveVideoAsync(target, File.Exists(target));
    }

    // ---- Saving frames and GIFs ----

    private void OnSavePicturesMenu(object sender, RoutedEventArgs e)
    {
        if (!IsVideo)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = SavePicturesButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        void Add(string header, string tip, bool gif)
        {
            var item = new MenuItem { Header = header, ToolTip = tip };
            item.Click += async (_, _) => await SavePicturesAsync(gif);
            menu.Items.Add(item);
        }

        Add("Frames as images…", "Every frame, or so many a second, as PNG or JPEG files", gif: false);
        Add("GIF…", "An animated GIF, to share", gif: true);
        var copy = new MenuItem { Header = "Smaller copy or other format…", ToolTip = "Re-encoded smaller (H.264 or HEVC), or just the sound as MP3, FLAC or WAV" };
        copy.Click += async (_, _) => await SaveCopyAsync();
        menu.Items.Add(copy);
        menu.IsOpen = true;
    }

    /// <summary>
    /// Saves a copy of the selection (or the whole video), re-encoded smaller or in another format, after asking how.
    /// The pictures are the original video's and follow the edit, as the frames and GIFs do.
    /// </summary>
    private async Task SaveCopyAsync()
    {
        if (_videoEdit is not { } video || _session is not { } session || IsBusy)
        {
            return;
        }

        var audio = session.Audio;
        var rate = (double)audio.SampleRate;
        var selection = Selection;
        var (from, to) = selection.IsEmpty ? (0.0, video.Length) : (selection.Start / rate, selection.End / rate);
        (int Width, int Height) size;
        try
        {
            size = VideoExport.PictureSize(video);
        }
        catch (NotSupportedException)
        {
            size = (0, 0);
        }

        if (size.Width == 0)
        {
            ShowStatus("This video's pictures couldn't be read, so it can't be copied.");
            return;
        }

        if (VideoCopyWindow.Ask(this, video, audio, from, to, size) is not { } choice)
        {
            return;
        }

        string? note = null;
        string? problem = null;
        var ok = await RunBusyAsync($"Saving {Path.GetFileName(choice.Path)}…", async (progress, token) =>
        {
            try
            {
                note = await Task.Run(() => VideoCopy.Save(video, audio, from, to, choice, size, progress, token), token);
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or VideoCutException or IOException
                or UnauthorizedAccessException or SharpGen.Runtime.SharpGenException)
            {
                problem = ex.Message;
            }
        });

        if (problem is not null)
        {
            ShowStatus("That didn't work.");
            MessageBox.Show(this, problem, "Save a copy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ok || !File.Exists(choice.Path))
        {
            return;
        }

        _lastSavedPath = choice.Path;
        ShowFileButton.Visibility = Visibility.Visible;
        ShowStatus($"Saved \"{Path.GetFileName(choice.Path)}\": {VideoExport.DescribeBytes(new FileInfo(choice.Path).Length)}. {note}".TrimEnd());
    }

    /// <summary>
    /// Saves the selection (or the whole video) as images or a GIF, after asking how. The pictures are the original
    /// video's, exactly as it has them, and follow the edit: a stretch across a cut has the pictures either side.
    /// </summary>
    private async Task SavePicturesAsync(bool gif)
    {
        if (_videoEdit is not { } video || _session is not { } session || IsBusy)
        {
            return;
        }

        var rate = (double)session.Audio.SampleRate;
        var selection = Selection;
        var (from, to) = selection.IsEmpty ? (0.0, video.Length) : (selection.Start / rate, selection.End / rate);
        (int Width, int Height) size;
        try
        {
            size = VideoExport.PictureSize(video);
        }
        catch (NotSupportedException)
        {
            size = (0, 0);
        }

        if (size.Width == 0)
        {
            ShowStatus("This video's pictures couldn't be read, so they can't be saved.");
            return;
        }

        if (FrameExportWindow.Ask(this, video, from, to, size, gif) is not { } choice)
        {
            return;
        }

        string? saved = null;
        string? problem = null;
        var count = 0;
        var ok = await RunBusyAsync(gif ? $"Making {Path.GetFileName(choice.GifPath)}…" : "Saving frames…", async (progress, token) =>
        {
            try
            {
                saved = await Task.Run(() =>
                {
                    var pictures = VideoExport.Pictures(video, from, to, gif ? choice.GifRate : choice.PerSecond);
                    count = pictures.Count;
                    if (!gif)
                    {
                        return VideoExport.SaveFrames(video, pictures, choice.Folder, choice.Format, progress, token);
                    }

                    VideoExport.SaveGif(video, pictures, choice.GifRate, choice.GifWidth, choice.Loop, choice.Dither, choice.GifPath, progress, token);
                    return choice.GifPath;
                }, token);
            }
            catch (InvalidDataException ex)
            {
                problem = ex.Message;
            }
            catch (VideoCutException ex)
            {
                problem = ex.Message;
            }
        });

        if (problem is not null)
        {
            ShowStatus("That didn't work.");
            MessageBox.Show(this, problem, gif ? "Save a GIF" : "Save frames", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!ok || saved is null)
        {
            return;
        }

        _lastSavedPath = saved;
        ShowFileButton.Visibility = Visibility.Visible;
        ShowStatus(gif
            ? $"Saved \"{Path.GetFileName(saved)}\": {count} frames, {VideoExport.DescribeBytes(new FileInfo(saved).Length)}."
            : $"Saved {count} {(count == 1 ? "frame" : "frames")} in \"{Path.GetFileName(saved)}\".");
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
        if (_videoPreview is { } preview)
        {
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
    }

    private void ShowPreviewPicture(VideoFrames frames)
    {
        if (frames.Count == _previewFrameShown)
        {
            return;
        }

        frames.CopyLatest((pixels, width, height, stride) =>
        {
            if (_previewBitmap is null || _previewBitmap.PixelWidth != width || _previewBitmap.PixelHeight != height)
            {
                _previewBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                VideoPicture.Source = _previewBitmap;
            }

            _previewBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, stride * height, stride);
            _previewFrameShown = frames.Count;
        });
    }
}
