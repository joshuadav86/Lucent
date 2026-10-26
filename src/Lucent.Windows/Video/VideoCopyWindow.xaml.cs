using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Lucent.Editor;

namespace Lucent;

/// <summary>
/// Asks how to save a copy of a stretch of a video in the editor, to make it smaller or turn it into another format:
/// H.264 or HEVC video (its size, frame rate and quality) or just its sound as MP3, FLAC or WAV, with about how big
/// it'll be. The editor does the saving (<see cref="VideoCopy"/>).
/// </summary>
public partial class VideoCopyWindow : Window
{
    private readonly VideoEdit _video;
    private readonly AudioBuffer _audio;
    private readonly double _from;
    private readonly double _to;
    private readonly int _videoWidth;
    private readonly int _videoHeight;
    private VideoCopyChoice _choice;

    private VideoCopyWindow(VideoEdit video, AudioBuffer audio, double from, double to, (int Width, int Height) size)
    {
        InitializeComponent();
        _video = video;
        _audio = audio;
        _from = from;
        _to = to;
        (_videoWidth, _videoHeight) = size;
        var path = video.Movie.Path;
        var last = VideoCopy.LastChoice;
        var folder = Directory.Exists(Path.GetDirectoryName(last.Path)) ? Path.GetDirectoryName(last.Path)! : Path.GetDirectoryName(path) ?? string.Empty;
        _choice = last with { Path = UniqueFile(Path.Combine(folder, Path.GetFileNameWithoutExtension(path) + " (copy)" + VideoCopy.Extension(last.Format))) };
        FullSizeChoice.Content = $"Full ({Math.Min(_videoWidth, _videoHeight)}p)";
        FullRateChoice.Content = $"Full ({VideoCopy.OwnFrameRate(video)})";
        RangeText.Text = $"{Path.GetFileName(path)}, from {WaveformView.FormatTime(from)} to {WaveformView.FormatTime(to)}";
        ShowChoices();
    }

    /// <summary>
    /// Asks how to save a copy of the stretch from <paramref name="from"/> to <paramref name="to"/> (seconds of the
    /// edit) of a video whose pictures are <paramref name="size"/>. Returns the choice, or null if cancelled.
    /// </summary>
    public static VideoCopyChoice? Ask(Window owner, VideoEdit video, AudioBuffer audio, double from, double to, (int Width, int Height) size)
    {
        var window = new VideoCopyWindow(video, audio, from, to, size) { Owner = owner };
        if (window.ShowDialog() != true)
        {
            return null;
        }

        VideoCopy.LastChoice = window._choice;
        return window._choice;
    }

    private void ShowChoices()
    {
        var shorter = Math.Min(_videoWidth, _videoHeight);
        var ownRate = VideoCopy.OwnFrameRate(_video);
        foreach (var radio in FindRadios(OptionsArea))
        {
            var tag = radio.Tag as string;
            var number = int.TryParse(tag, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;
            // Only what's smaller than the video itself.
            radio.IsEnabled = radio.GroupName switch
            {
                "Size" => number == 0 || number < shorter,
                "FrameRate" => number == 0 || number < ownRate,
                _ => true,
            };
            radio.IsChecked = radio.GroupName switch
            {
                "Format" => tag == _choice.Format.ToString(),
                "Size" => number == (_choice.Size >= shorter ? 0 : _choice.Size),
                "FrameRate" => number == (_choice.FrameRate >= ownRate ? 0 : _choice.FrameRate),
                "Quality" => tag == _choice.Quality.ToString(),
                _ => radio.IsChecked,
            };
        }

        PicturePanel.Visibility = _choice.IsVideo ? Visibility.Visible : Visibility.Collapsed;
        FormatHint.Text = _choice.Format switch
        {
            VideoCopyFormat.H264 => "An MP4 video that plays everywhere.",
            VideoCopyFormat.Hevc => "An MP4 video about half the size of H.264 at the same quality. Phones and computers from the last few years play it.",
            VideoCopyFormat.Mp3 => "Just the sound, as an MP3.",
            VideoCopyFormat.Flac => "Just the sound, without losing any of it, about half the size of WAV.",
            _ => "Just the sound, uncompressed.",
        };

        var seconds = Math.Max(0, _to - _from);
        var bytes = VideoCopy.EstimateBytes(_video, _audio, _choice, _videoWidth, _videoHeight, seconds);
        var original = _video.Movie.Duration > 0 ? (long)(new FileInfo(_video.Movie.Path).Length * Math.Min(1, seconds / _video.Movie.Duration)) : 0;
        var (width, height) = VideoCopy.SizeFor(_videoWidth, _videoHeight, _choice.Size);
        var detail = _choice.IsVideo
            ? $"{width} × {height} at {VideoCopy.FrameRateFor(_video, _choice.FrameRate)} fps"
            : _choice.Format switch
            {
                VideoCopyFormat.Mp3 => _choice.Quality switch { VideoCopyQuality.High => "320 kbps", VideoCopyQuality.Medium => "192 kbps", _ => "128 kbps" },
                _ => _choice.Quality == VideoCopyQuality.High ? "24-bit" : "16-bit",
            };
        SizeHint.Text = $"About {VideoExport.DescribeBytes(bytes)}, {detail}"
            + (original > 0 ? $". The original is about {VideoExport.DescribeBytes(original)} for this stretch." : ".");

        FileText.Text = _choice.Path;
        FileText.ToolTip = _choice.Path;
        SaveButton.IsEnabled = seconds > 0;
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
            "Format" => _choice with
            {
                Format = Enum.Parse<VideoCopyFormat>(tag),
                Path = Path.ChangeExtension(_choice.Path, VideoCopy.Extension(Enum.Parse<VideoCopyFormat>(tag))),
            },
            "Size" => _choice with { Size = int.Parse(tag, CultureInfo.InvariantCulture) },
            "FrameRate" => _choice with { FrameRate = int.Parse(tag, CultureInfo.InvariantCulture) },
            "Quality" => _choice with { Quality = Enum.Parse<VideoCopyQuality>(tag) },
            _ => _choice,
        };
        ShowChoices();
    }

    private void OnChooseFile(object sender, RoutedEventArgs e)
    {
        var extension = VideoCopy.Extension(_choice.Format);
        var dialog = new SaveFileDialog
        {
            Title = "Save the copy as",
            InitialDirectory = Path.GetDirectoryName(_choice.Path),
            FileName = Path.GetFileName(_choice.Path),
            DefaultExt = extension,
            Filter = $"{extension.TrimStart('.').ToUpperInvariant()}|*{extension}",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _choice = _choice with { Path = Path.ChangeExtension(dialog.FileName, extension) };
            ShowChoices();
        }
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (string.Equals(Path.GetFullPath(_choice.Path), Path.GetFullPath(_video.Movie.Path), StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(this, "The copy can't replace the video it's made from. Pick another name.", "Save a copy",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    // "Clip (copy).mp4", or "Clip (copy) (2).mp4" if that's taken.
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
