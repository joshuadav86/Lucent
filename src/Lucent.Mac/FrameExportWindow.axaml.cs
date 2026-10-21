using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Lucent.Mac.Editor;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Editor;

namespace Lucent.Mac;

/// <summary>
/// Asks how to save a stretch of a video in the editor as pictures: as images (every frame or so many a second, PNG
/// or JPEG, into a new folder) or as an animated GIF (its width, frame rate, whether it loops, dithering, and the
/// file), with how many there'll be and about how big. The editor does the saving. The Mac version of the Windows
/// app's FrameExportWindow.
/// </summary>
public partial class FrameExportWindow : Window
{
    private VideoEdit? _video;
    private double _from;
    private double _to;
    private int _videoWidth;
    private int _videoHeight;
    private FrameExportChoice _choice = new();

    /// <summary>For the XAML loader, and <see cref="AskAsync"/>.</summary>
    public FrameExportWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
    }

    /// <summary>
    /// Asks how to save the stretch from <paramref name="from"/> to <paramref name="to"/> (seconds of the edit) of a
    /// video whose pictures are <paramref name="size"/>. Returns the choice, or null if cancelled.
    /// </summary>
    /// <remarks>Windows' version waits in ShowDialog; Avalonia's dialogs are awaited instead.</remarks>
    public static async Task<FrameExportChoice?> AskAsync(Window owner, VideoEdit video, double from, double to, (int Width, int Height) size, bool gif)
    {
        var window = new FrameExportWindow();
        window.Prepare(video, from, to, size, gif);
        if (!await window.ShowDialog<bool>(owner))
        {
            return null;
        }

        VideoExport.LastChoice = window._choice;
        return window._choice;
    }

    private void Prepare(VideoEdit video, double from, double to, (int Width, int Height) size, bool gif)
    {
        _video = video;
        _from = from;
        _to = to;
        (_videoWidth, _videoHeight) = size;
        var path = video.Movie.Path;
        var folder = Path.GetDirectoryName(path) ?? string.Empty;
        var last = VideoExport.LastChoice;
        _choice = last with
        {
            Gif = gif,
            Folder = Directory.Exists(last.Folder) ? last.Folder : folder,
            Format = VideoExport.EncodeJpeg is null ? FrameImageFormat.Png : last.Format,
            GifPath = UniqueFile(Path.Combine(Directory.Exists(Path.GetDirectoryName(last.GifPath)) ? Path.GetDirectoryName(last.GifPath)! : folder,
                Path.GetFileNameWithoutExtension(path) + ".gif")),
        };

        Title = gif ? "Save a GIF - Lucent" : "Save frames - Lucent";
        Heading.Text = gif ? "SAVE A GIF" : "SAVE FRAMES";
        FramesPanel.IsVisible = !gif;
        GifPanel.IsVisible = gif;
        JpegChoice.IsEnabled = VideoExport.EncodeJpeg is not null;
        FullWidthChoice.Content = $"Full ({_videoWidth})";
        RangeText.Text = $"{Path.GetFileName(path)}, from {WaveformView.FormatTime(from)} to {WaveformView.FormatTime(to)}";
        ShowChoices();
    }

    private void ShowChoices()
    {
        if (_video is not { } video)
        {
            return;
        }

        foreach (var radio in OptionsArea.GetLogicalDescendants().OfType<RadioButton>())
        {
            var tag = radio.Tag as string;
            radio.IsChecked = radio.GroupName switch
            {
                "Rate" => tag == ((int)(_choice.PerSecond ?? 0)).ToString(CultureInfo.InvariantCulture),
                "Format" => tag == _choice.Format.ToString(),
                "Width" => tag == _choice.GifWidth.ToString(CultureInfo.InvariantCulture),
                "GifRate" => tag == _choice.GifRate.ToString(CultureInfo.InvariantCulture),
                "Loop" => tag == _choice.Loop.ToString(),
                "Dither" => tag == _choice.Dither.ToString(),
                _ => radio.IsChecked,
            };
        }

        var canSave = true;
        if (_choice.Gif)
        {
            var frames = VideoExport.Pictures(video, _from, _to, _choice.GifRate).Count;
            var (width, height) = VideoExport.GifSize(_videoWidth, _videoHeight, _choice.GifWidth);
            if ((long)width * height * 3 * frames > VideoExport.GifMemoryLimit)
            {
                canSave = false;
                GifHint.Text = "That's too long for a GIF this size. Select a shorter stretch, or pick a smaller width or frame rate.";
                GifHint.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            }
            else
            {
                var bytes = VideoExport.EstimateGifBytes(width, height, frames, _choice.Dither);
                GifHint.Text = $"About {VideoExport.DescribeBytes(bytes)}: {frames} frames at {width} × {height}. "
                    + "A GIF has 256 colours: dithering smooths gradients, and makes the file a little bigger.";
                GifHint.SetResourceReference(TextBlock.ForegroundProperty, "MutedTextBrush");
            }

            FileText.Text = _choice.GifPath;
            ToolTip.SetTip(FileText, _choice.GifPath);
        }
        else
        {
            var count = VideoExport.Pictures(video, _from, _to, _choice.PerSecond).Count;
            canSave = count > 0;
            FramesHint.Text = $"{count} {(count == 1 ? "image" : "images")} at {_videoWidth} × {_videoHeight}. "
                + (_choice.Format == FrameImageFormat.Png ? "PNG keeps every pixel exactly." : "JPEG files are much smaller.");
            FolderText.Text = _choice.Folder;
            ToolTip.SetTip(FolderText, _choice.Folder);
            FolderHint.Text = $"They go in a new folder, \"{Path.GetFileNameWithoutExtension(video.Movie.Path)} frames\", each named by its time in the video, so they sort in order.";
        }

        SaveButton.IsEnabled = canSave;
    }

    private void OnOptionChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } radio)
        {
            return;
        }

        _choice = radio.GroupName switch
        {
            "Rate" => _choice with { PerSecond = tag == "0" ? null : double.Parse(tag, CultureInfo.InvariantCulture) },
            "Format" => _choice with { Format = Enum.Parse<FrameImageFormat>(tag) },
            "Width" => _choice with { GifWidth = int.Parse(tag, CultureInfo.InvariantCulture) },
            "GifRate" => _choice with { GifRate = int.Parse(tag, CultureInfo.InvariantCulture) },
            "Loop" => _choice with { Loop = bool.Parse(tag) },
            "Dither" => _choice with { Dither = bool.Parse(tag) },
            _ => _choice,
        };
        ShowChoices();
    }

    private async void OnChooseFolder(object? sender, RoutedEventArgs e)
    {
        IReadOnlyList<IStorageFolder> folders;
        try
        {
            folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Save the frames in",
                SuggestedStartLocation = Directory.Exists(_choice.Folder) ? await StorageProvider.TryGetFolderFromPathAsync(_choice.Folder) : null,
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Choosing where to save frames", ex);
            return;
        }

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            _choice = _choice with { Folder = path };
            ShowChoices();
        }
    }

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        IStorageFile? file;
        try
        {
            var folder = Path.GetDirectoryName(_choice.GifPath);
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the GIF as",
                SuggestedFileName = Path.GetFileName(_choice.GifPath),
                SuggestedStartLocation = folder is not null && Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null,
                DefaultExtension = "gif",
                // macOS asks before replacing a file that's there.
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType("GIF") { Patterns = new[] { "*.gif" } } },
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Choosing where to save a GIF", ex);
            return;
        }

        if (file?.TryGetLocalPath() is { } path)
        {
            _choice = _choice with { GifPath = Path.ChangeExtension(path, ".gif") };
            ShowChoices();
        }
    }

    private void OnSave(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    // "Clip.gif", or "Clip (2).gif" if that's taken.
    private static string UniqueFile(string path)
    {
        var candidate = path;
        for (var n = 2; File.Exists(candidate); n++)
        {
            candidate = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, $"{Path.GetFileNameWithoutExtension(path)} ({n}){Path.GetExtension(path)}");
        }

        return candidate;
    }
}
