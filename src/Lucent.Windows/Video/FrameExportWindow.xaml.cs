using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Lucent.Editor;

namespace Lucent;

/// <summary>
/// Asks how to save a stretch of a video in the editor as pictures: as images (every frame or so many a second, PNG
/// or JPEG, into a new folder) or as an animated GIF (its width, frame rate, whether it loops, dithering, and the
/// file), with how many there'll be and about how big. The editor does the saving.
/// </summary>
public partial class FrameExportWindow : Window
{
    private readonly VideoEdit _video;
    private readonly double _from;
    private readonly double _to;
    private readonly int _videoWidth;
    private readonly int _videoHeight;
    private FrameExportChoice _choice;

    private FrameExportWindow(VideoEdit video, double from, double to, (int Width, int Height) size, bool gif)
    {
        InitializeComponent();
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
        FramesPanel.Visibility = gif ? Visibility.Collapsed : Visibility.Visible;
        GifPanel.Visibility = gif ? Visibility.Visible : Visibility.Collapsed;
        JpegChoice.IsEnabled = VideoExport.EncodeJpeg is not null;
        FullWidthChoice.Content = $"Full ({_videoWidth})";
        RangeText.Text = $"{Path.GetFileName(path)}, from {WaveformView.FormatTime(from)} to {WaveformView.FormatTime(to)}";
        ShowChoices();
    }

    /// <summary>
    /// Asks how to save the stretch from <paramref name="from"/> to <paramref name="to"/> (seconds of the edit) of a
    /// video whose pictures are <paramref name="size"/>. Returns the choice, or null if cancelled.
    /// </summary>
    public static FrameExportChoice? Ask(Window owner, VideoEdit video, double from, double to, (int Width, int Height) size, bool gif)
    {
        var window = new FrameExportWindow(video, from, to, size, gif) { Owner = owner };
        if (window.ShowDialog() != true)
        {
            return null;
        }

        VideoExport.LastChoice = window._choice;
        return window._choice;
    }

    private void ShowChoices()
    {
        foreach (var radio in FindRadios(OptionsArea))
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
            var frames = VideoExport.Pictures(_video, _from, _to, _choice.GifRate).Count;
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
            FileText.ToolTip = _choice.GifPath;
        }
        else
        {
            var count = VideoExport.Pictures(_video, _from, _to, _choice.PerSecond).Count;
            canSave = count > 0;
            FramesHint.Text = $"{count} {(count == 1 ? "image" : "images")} at {_videoWidth} × {_videoHeight}. "
                + (_choice.Format == FrameImageFormat.Png ? "PNG keeps every pixel exactly." : "JPEG files are much smaller.");
            FolderText.Text = _choice.Folder;
            FolderText.ToolTip = _choice.Folder;
            FolderHint.Text = $"They go in a new folder, \"{Path.GetFileNameWithoutExtension(_video.Movie.Path)} frames\", each named by its time in the video, so they sort in order.";
        }

        SaveButton.IsEnabled = canSave;
    }

    private static IEnumerable<RadioButton> FindRadios(DependencyObject parent)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is RadioButton radio)
            {
                yield return radio;
            }

            foreach (var inner in FindRadios(child))
            {
                yield return inner;
            }
        }
    }

    private void OnOptionChosen(object sender, RoutedEventArgs e)
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

    private void OnChooseFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Save the frames in", InitialDirectory = _choice.Folder };
        if (dialog.ShowDialog(this) == true)
        {
            _choice = _choice with { Folder = dialog.FolderName };
            ShowChoices();
        }
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Save the GIF as",
            InitialDirectory = Path.GetDirectoryName(_choice.GifPath),
            FileName = Path.GetFileName(_choice.GifPath),
            DefaultExt = ".gif",
            Filter = "GIF|*.gif",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _choice = _choice with { GifPath = Path.ChangeExtension(dialog.FileName, ".gif") };
            ShowChoices();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

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
