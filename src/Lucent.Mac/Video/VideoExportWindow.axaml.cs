using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Platform.Storage;
using Lucent.Mac.Export;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Playlist;
using WinampInspiredVisualizer.Visualizer;

namespace Lucent.Mac;

/// <summary>
/// Records the song that's playing, drawn with a visual, as an MP4 video for sharing: the visual,
/// the whole song or a part of it, the size, frame rate and quality, and the file. It stays open
/// (not blocking the player) while it records, showing progress, and the visuals carry on meanwhile.
/// The Mac version of the Windows app's VideoExportWindow.
/// </summary>
public partial class VideoExportWindow : Window
{
    // A Mac file name can't have a slash in it; a colon shows as a slash in Finder. Windows' own list is
    // kept too, so names stay usable when the video is copied to a PC.
    private static readonly char[] InvalidNameCharacters = { '\\', '/', ':', '*', '?', '"', '<', '>', '|', '\0' };

    // The last choices, for the next recording this session.
    private static int _lastHeight = 1080;
    private static int _lastFrameRate = 60;
    private static VideoQuality _lastQuality = VideoQuality.High;
    private static string? _lastFolder;

    private readonly VideoRecordingOptions _template;
    private readonly TimeSpan _duration;
    private int _presetIndex;
    private bool _auto;
    private int _height = _lastHeight;
    private int _frameRate = _lastFrameRate;
    private VideoQuality _quality = _lastQuality;
    private string _outputPath;
    private CancellationTokenSource? _running;
    private bool _closeWhenDone;
    private bool _finished;

    /// <summary>For the XAML loader only.</summary>
    public VideoExportWindow()
        : this(new VideoRecordingOptions { TrackPath = "Song.mp3", OutputPath = string.Empty, Presets = Array.Empty<PresetSource>() },
            string.Empty, TimeSpan.Zero, TimeSpan.Zero)
    {
    }

    private VideoExportWindow(VideoRecordingOptions template, string trackTitle, TimeSpan duration, TimeSpan position)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        _template = template;
        _duration = duration;
        _presetIndex = template.PresetIndex;
        _auto = template.Auto;
        TrackText.Text = trackTitle;
        Closing += OnClosing;

        // ~/Movies, where the Mac keeps videos.
        var folder = _lastFolder is { } last && Directory.Exists(last)
            ? last
            : Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            folder = Path.GetDirectoryName(Path.GetFullPath(template.TrackPath)) ?? Environment.CurrentDirectory;
        }

        // A name of its own, so an earlier recording is never replaced without asking.
        var suggested = Path.Combine(folder, SafeName(Path.GetFileNameWithoutExtension(template.TrackPath)) + ".mp4");
        _outputPath = File.Exists(suggested) ? FileConverter.NumberedName(suggested, new HashSet<string>()) : suggested;

        // A part starts where the song is now (handy for picking a moment), and runs 30 seconds.
        var from = duration > TimeSpan.Zero && position < duration ? TimeSpan.FromSeconds(Math.Floor(position.TotalSeconds)) : TimeSpan.Zero;
        var to = duration > TimeSpan.Zero ? Min(from + TimeSpan.FromSeconds(30), duration) : from + TimeSpan.FromSeconds(30);
        FromInput.Text = TimeFormat.Format(from);
        ToInput.Text = TimeFormat.Format(to);
        WholeSong.IsChecked = true;
        ShowChoices();
    }

    /// <summary>
    /// Opens the window for the song in <paramref name="template"/>, which also carries the visual
    /// showing now and the graphics settings to draw with. <paramref name="position"/> is where the song is.
    /// </summary>
    public static VideoExportWindow Open(Window owner, VideoRecordingOptions template, string trackTitle, TimeSpan duration, TimeSpan position)
    {
        var window = Prepare(template, trackTitle, duration, position);
        window.Show(owner);
        return window;
    }

    /// <summary>The window, filled in and ready to show (the screenshot tool shows it without an owner).</summary>
    internal static VideoExportWindow Prepare(VideoRecordingOptions template, string trackTitle, TimeSpan duration, TimeSpan position) =>
        new(template, trackTitle, duration, position);

    /// <summary>True while a video is being recorded.</summary>
    public bool IsRunning => _running is not null;

    // ---- Choices ----

    private void ShowChoices()
    {
        foreach (var radio in OptionsArea.GetLogicalDescendants().OfType<RadioButton>())
        {
            var tag = radio.Tag as string;
            radio.IsChecked = radio.GroupName switch
            {
                "Size" => tag == _height.ToString(CultureInfo.InvariantCulture),
                "FrameRate" => tag == _frameRate.ToString(CultureInfo.InvariantCulture),
                "Quality" => tag == _quality.ToString(),
                _ => radio.IsChecked,
            };
        }

        var presets = _template.Presets;
        var name = _presetIndex >= 0 && _presetIndex < presets.Count ? presets[_presetIndex].Label : "No visual";
        VisualText.Text = _auto ? $"Auto, starting with {name}" : name;
        VisualHint.Text = _auto
            ? $"Changes visual about every {TimeFormat.Format(TimeSpan.FromSeconds(_template.AutoCycleSeconds))}, landing on a downbeat, as Auto does on screen."
            : "Stays on this visual all the way through.";

        RangePanel.IsVisible = PartOfSong.IsChecked == true;
        FileText.Text = _outputPath;
        ToolTip.SetTip(FileText, _outputPath);
        ShowRangeAndSize();
    }

    /// <summary>Checks the from and to times, and estimates the file's size from them.</summary>
    private void ShowRangeAndSize()
    {
        if (RangeHint is null || QualityHint is null)
        {
            // Still loading.
            return;
        }

        var range = ReadRange(out var problem);
        RangeHint.Text = problem ?? "Minutes and seconds, like 1:30.";
        var options = BuildOptions(range?.From ?? TimeSpan.Zero, range?.To);
        var megabits = (options.VideoBitrate + options.AudioBitrate) / 1_000_000.0;
        var hint = $"About {megabits:0} Mbit/s";
        var seconds = range is { } r ? ((r.To ?? _duration) - r.From).TotalSeconds : 0;
        if (seconds > 0)
        {
            var megabytes = megabits * seconds / 8;
            hint += megabytes >= 1000 ? $", so about {megabytes / 1000:0.0} GB for this video." : $", so about {Math.Max(1, megabytes):0} MB for this video.";
        }
        else
        {
            hint += ".";
        }

        QualityHint.Text = hint + _quality switch
        {
            VideoQuality.Good => " Fine for sharing on social media, which compresses videos again anyway.",
            VideoQuality.High => " Crisp detail, even in busy visuals.",
            _ => " The most detail, in big files: for editing further or uploading at the best quality.",
        };
    }

    /// <summary>The part of the song to record (To null for the end), or null with a reason if the times don't make sense.</summary>
    private (TimeSpan From, TimeSpan? To)? ReadRange(out string? problem)
    {
        problem = null;
        if (PartOfSong.IsChecked != true)
        {
            return (TimeSpan.Zero, null);
        }

        if (!TryParseTime(FromInput.Text ?? string.Empty, out var from) || !TryParseTime(ToInput.Text ?? string.Empty, out var to))
        {
            problem = "Type the times as minutes and seconds, like 1:30.";
            return null;
        }

        if (_duration > TimeSpan.Zero && from >= _duration)
        {
            problem = $"The song is only {TimeFormat.Format(_duration)} long.";
            return null;
        }

        if (to <= from + TimeSpan.FromSeconds(0.5))
        {
            problem = "The end needs to come after the start.";
            return null;
        }

        return (from, _duration > TimeSpan.Zero ? Min(to, _duration) : to);
    }

    /// <summary>Reads "1:30", "1:02:03", "90" or "1:30.5".</summary>
    private static bool TryParseTime(string text, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        var parts = text.Trim().Split(':');
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        var seconds = 0.0;
        foreach (var part in parts)
        {
            if (!double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
                && !double.TryParse(part, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value))
            {
                return false;
            }

            seconds = seconds * 60 + value;
        }

        time = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private void OnOptionChosen(object? sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } radio)
        {
            return;
        }

        switch (radio.GroupName)
        {
            case "Size":
                _height = int.Parse(tag, CultureInfo.InvariantCulture);
                break;
            case "FrameRate":
                _frameRate = int.Parse(tag, CultureInfo.InvariantCulture);
                break;
            case "Quality":
                _quality = Enum.Parse<VideoQuality>(tag);
                break;
        }

        ShowChoices();
    }

    private void OnPartChosen(object? sender, RoutedEventArgs e)
    {
        ShowChoices();
        if (PartOfSong.IsChecked == true)
        {
            FromInput.Focus();
            FromInput.SelectAll();
        }
    }

    private void OnRangeChanged(object? sender, TextChangedEventArgs e) => ShowRangeAndSize();

    /// <summary>Auto, or any one visual, from a menu with a folder for each family, as in the picker.</summary>
    private void OnChooseVisual(object? sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = VisualButton, Placement = PlacementMode.BottomEdgeAlignedLeft };
        var auto = new MenuItem
        {
            Header = "Auto: change visual on the beat",
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = _auto,
        };
        ToolTip.SetTip(auto, "Starts with the visual ticked below, then changes as Auto does on screen (from the visuals it's set to pick from)");
        auto.Click += (_, _) =>
        {
            _auto = !_auto;
            ShowChoices();
        };
        menu.Items.Add(auto);
        menu.Items.Add(new Separator());

        var presets = _template.Presets;
        var families = Enumerable.Range(0, presets.Count)
            .GroupBy(index => presets[index].Family)
            .OrderBy(family => FamilyOrder(family.Key))
            .ThenBy(family => family.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var family in families)
        {
            var folder = new MenuItem { Header = family.Key, ToggleType = MenuItemToggleType.CheckBox };
            foreach (var index in family.OrderBy(index => presets[index].Name, StringComparer.OrdinalIgnoreCase))
            {
                var item = new MenuItem { Header = presets[index].Label, ToggleType = MenuItemToggleType.CheckBox, IsChecked = index == _presetIndex };
                var chosen = index;
                item.Click += (_, _) =>
                {
                    _presetIndex = chosen;
                    ShowChoices();
                };
                folder.Items.Add(item);
                folder.IsChecked |= index == _presetIndex;
            }

            menu.Items.Add(folder);
        }

        menu.Open(VisualButton);
    }

    private static int FamilyOrder(string family)
    {
        for (var i = 0; i < VisualStyles.Families.Count; i++)
        {
            if (string.Equals(VisualStyles.Families[i], family, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return int.MaxValue;
    }

    private async void OnChooseFile(object? sender, RoutedEventArgs e)
    {
        IStorageFile? file;
        try
        {
            var folder = Path.GetDirectoryName(_outputPath);
            file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the video as",
                SuggestedFileName = Path.GetFileName(_outputPath),
                SuggestedStartLocation = folder is not null && Directory.Exists(folder) ? await StorageProvider.TryGetFolderFromPathAsync(folder) : null,
                DefaultExtension = "mp4",
                // macOS asks before replacing a file that's there.
                ShowOverwritePrompt = true,
                FileTypeChoices = new[] { new FilePickerFileType("MP4 video") { Patterns = new[] { "*.mp4" } } },
            });
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Choosing where to save a video", ex);
            return;
        }

        if (file?.TryGetLocalPath() is { } path)
        {
            _outputPath = string.IsNullOrEmpty(Path.GetExtension(path)) ? path + ".mp4" : path;
            ShowChoices();
        }
    }

    // ---- Buttons ----

    private async void OnPrimary(object? sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            Close();
            return;
        }

        var range = ReadRange(out var problem);
        if (range is null)
        {
            await MessageWindow.ShowAsync(this, problem ?? string.Empty, "Record video");
            FromInput.Focus();
            return;
        }

        await RecordAsync(BuildOptions(range.Value.From, range.Value.To));
    }

    private void OnSecondary(object? sender, RoutedEventArgs e)
    {
        if (_running is not null)
        {
            _running.Cancel();
            SecondaryButton.IsEnabled = false;
            ProgressText.Text = "Stopping…";
            return;
        }

        Close();
    }

    private void OnCloseButton(object? sender, RoutedEventArgs e) => Close();

    private void OnShowFile(object? sender, RoutedEventArgs e)
    {
        try
        {
            // Opens the folder in Finder with the video selected.
            var open = new ProcessStartInfo("open") { UseShellExecute = false };
            open.ArgumentList.Add("-R");
            open.ArgumentList.Add(_outputPath);
            Process.Start(open)?.Dispose();
        }
        catch (Exception)
        {
            // Finder normally opens; nothing more to do if it doesn't.
        }
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_running is not null)
        {
            // Stop recording first; the window closes once the unfinished file has been removed.
            e.Cancel = true;
            _closeWhenDone = true;
            _running.Cancel();
        }
    }

    // ---- Recording ----

    private VideoRecordingOptions BuildOptions(TimeSpan from, TimeSpan? to) => _template with
    {
        OutputPath = _outputPath,
        // Always 16:9, at the height chosen.
        Width = _height * 16 / 9,
        Height = _height,
        FrameRate = _frameRate,
        Quality = _quality,
        From = from,
        To = to,
        PresetIndex = _presetIndex,
        Auto = _auto,
    };

    private async Task RecordAsync(VideoRecordingOptions options)
    {
        _lastHeight = _height;
        _lastFrameRate = _frameRate;
        _lastQuality = _quality;
        _lastFolder = Path.GetDirectoryName(_outputPath);

        _running = new CancellationTokenSource();
        OptionsArea.IsEnabled = false;
        PrimaryButton.IsEnabled = false;
        SecondaryButton.Content = "Stop";
        ProgressArea.IsVisible = true;
        Progress.Value = 0;
        ProgressText.Text = "Starting…";
        var progress = new Progress<VideoRecordingProgress>(report =>
        {
            if (_running is not { IsCancellationRequested: false })
            {
                return;
            }

            Progress.Value = report.Fraction;
            ProgressText.Text = report.Speed > 0
                ? $"{report.Stage}  ·  {report.Speed:0.0#}× real time"
                : report.Stage;
        });

        try
        {
            var result = await VideoRecorder.RecordAsync(options, progress, _running.Token);
            _finished = true;
            Progress.Value = 1;
            ProgressText.Text = $"Done: {Path.GetFileName(result.Path)}, {TimeFormat.Format(result.Length)} long, "
                + $"made in {TimeFormat.Format(result.TimeTaken)}. {result.Note}";
            ShowFileButton.IsVisible = true;
            PrimaryButton.IsEnabled = true;
            PrimaryText.Text = "Done";
            SecondaryButton.IsVisible = false;
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = "Stopped. The unfinished video was removed.";
            ResetButtons();
        }
        catch (Exception ex)
        {
            ErrorLog.Write("Video recording", ex);
            ProgressText.Text = $"The video couldn't be recorded. {FriendlyError(ex)}";
            ResetButtons();
        }
        finally
        {
            _running.Dispose();
            _running = null;
        }

        if (_closeWhenDone)
        {
            Close();
        }
    }

    private void ResetButtons()
    {
        OptionsArea.IsEnabled = true;
        PrimaryButton.IsEnabled = true;
        SecondaryButton.IsEnabled = true;
        SecondaryButton.Content = "Cancel";
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => "Lucent isn't allowed to write to that folder.",
        IOException io when io.HResult == 28 => "The disk is full.", // ENOSPC
        _ => ex.Message,
    };

    private static string SafeName(string name)
    {
        var safe = new string(name.Select(c => Array.IndexOf(InvalidNameCharacters, c) >= 0 ? '_' : c).ToArray()).Trim();
        return safe.Length == 0 ? "Lucent video" : safe;
    }

    private static TimeSpan Min(TimeSpan a, TimeSpan b) => a < b ? a : b;
}
