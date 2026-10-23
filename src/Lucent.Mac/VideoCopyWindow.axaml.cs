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
/// Asks how to save a copy of a stretch of a video in the editor, to make it smaller or turn it into another format:
/// H.264 or HEVC video (its size, frame rate and quality) or just its sound as MP3, FLAC or WAV, with about how big
/// it'll be. The editor does the saving (<see cref="VideoCopy"/>). The Mac version of the Windows app's
/// VideoCopyWindow.
/// </summary>
public partial class VideoCopyWindow : Window
{
    private VideoEdit? _video;
    private AudioBuffer? _audio;
    private double _from;
    private double _to;
    private int _videoWidth;
    private int _videoHeight;
    private VideoCopyChoice _choice = new();

    /// <summary>For the XAML loader, and <see cref="AskAsync"/>.</summary>
    public VideoCopyWindow()
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
    }

    /// <summary>
    /// Asks how to save a copy of the stretch from <paramref name="from"/> to <paramref name="to"/> (seconds of the
    /// edit) of a video whose pictures are <paramref name="size"/>. Returns the choice, or null if cancelled.
    /// </summary>
    /// <remarks>Windows' version waits in ShowDialog; Avalonia's dialogs are awaited instead.</remarks>
    public static async Task<VideoCopyChoice?> AskAsync(Window owner, VideoEdit video, AudioBuffer audio, double from, double to, (int Width, int Height) size)
    {
        var window = new VideoCopyWindow();
        window.Prepare(video, audio, from, to, size);
        if (!await window.ShowDialog<bool>(owner))
        {
            return null;
        }

        VideoCopy.LastChoice = window._choice;
        return window._choice;
    }

    private void Prepare(VideoEdit video, AudioBuffer audio, double from, double to, (int Width, int Height) size)
    {
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

    private void ShowChoices()
    {
        if (_video is null || _audio is null)
        {
            return;
        }

        var shorter = Math.Min(_videoWidth, _videoHeight);
        var ownRate = VideoCopy.OwnFrameRate(_video);
        foreach (var radio in OptionsArea.GetLogicalDescendants().OfType<RadioButton>())
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

        PicturePanel.IsVisible = _choice.IsVideo;
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
        ToolTip.SetTip(FileText, _choice.Path);
        SaveButton.IsEnabled = seconds > 0;
    }

    private void OnOptionChosen(object? sender, RoutedEventArgs e)
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

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        var extension = VideoCopy.Extension(_choice.Format);
        IStorageFile? file;
        try
        {
            var folder = Path.GetDirectoryName(_choice.Path);
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the copy as",
                SuggestedFileName = Path.GetFileName(_choice.Path),
                SuggestedStartLocation = folder is not null && Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null,
                DefaultExtension = extension.TrimStart('.'),
                // macOS asks before replacing a file that's there.
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType(extension.TrimStart('.').ToUpperInvariant()) { Patterns = new[] { "*" + extension } } },
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Choosing where to save a copy of a video", ex);
            return;
        }

        if (file?.TryGetLocalPath() is { } path)
        {
            _choice = _choice with { Path = Path.ChangeExtension(path, extension) };
            ShowChoices();
        }
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        if (_video is not null && string.Equals(Path.GetFullPath(_choice.Path), Path.GetFullPath(_video.Movie.Path), StringComparison.OrdinalIgnoreCase))
        {
            await MessageWindow.ShowAsync(this, "The copy can't replace the video it's made from. Pick another name.", "Save a copy");
            return;
        }

        Close(true);
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

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
