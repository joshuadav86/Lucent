using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Dsp;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// Format and quality choices for a new file. Two uses: converting one or more files (it then runs
/// the conversion and shows progress), and the editor's "Save as new version" (it asks for a name too).
/// </summary>
public partial class ExportWindow : Window
{
    private static readonly Dictionary<Mp3Quality, string> Mp3Hints = new()
    {
        [Mp3Quality.Cbr128] = "128 kbps: small files, fine for speech and casual listening.",
        [Mp3Quality.Cbr192] = "192 kbps: good quality at a modest size.",
        [Mp3Quality.Cbr256] = "256 kbps: very good quality.",
        [Mp3Quality.Cbr320] = "320 kbps: the best quality MP3 can do at a constant bitrate.",
        [Mp3Quality.VbrV2] = "Variable bitrate, about 190 kbps on average: sounds as good as 256 to most ears, in smaller files.",
        [Mp3Quality.VbrV0] = "Variable bitrate, about 245 kbps on average: LAME's best quality, usually smaller than 320.",
    };

    private readonly bool _saveMode;
    private readonly ExportOptions _options;
    private readonly IReadOnlyList<string> _files;
    private readonly AppSettings? _settings;
    private string _saveFolder = string.Empty;
    private CancellationTokenSource? _running;
    private bool _closeWhenDone;
    private bool _finished;
    private string? _resultFolder;

    /// <summary>What's done to every song on the way through, in order (converting only).</summary>
    private readonly List<ProcessingStep> _steps;

    private ExportWindow(bool saveMode, ExportOptions options, IReadOnlyList<string> files, AppSettings? settings)
    {
        InitializeComponent();
        _saveMode = saveMode;
        _options = options.Clone();
        _files = files;
        _settings = settings;
        SaveTarget.Visibility = saveMode ? Visibility.Visible : Visibility.Collapsed;
        ConvertTarget.Visibility = saveMode ? Visibility.Collapsed : Visibility.Visible;
        StepsArea.Visibility = saveMode ? Visibility.Collapsed : Visibility.Visible;
        _steps = settings?.ConvertSteps.Select(step => step.Clone()).ToList() ?? new List<ProcessingStep>();
        ShowSteps();
    }

    /// <summary>True while files are being converted.</summary>
    public bool IsRunning => _running is not null;

    /// <summary>The file and options chosen in "Save as new version", or null if cancelled.</summary>
    public (string Path, ExportOptions Options)? SaveChoice { get; private set; }

    /// <summary>
    /// Asks where and how to save an edited song. Suggests "&lt;name&gt; (edited)" next to the original,
    /// in <paramref name="initial"/>'s format. Shows <paramref name="note"/> above the choices if given.
    /// </summary>
    public static (string Path, ExportOptions Options)? ShowSaveDialog(Window owner, string sourcePath, ExportOptions initial, string? note)
    {
        var window = new ExportWindow(saveMode: true, initial, new[] { sourcePath }, settings: null) { Owner = owner };
        window.Title = "Save as new version - Lucent";
        window.Heading.Text = "SAVE AS NEW VERSION";
        window.PrimaryText.Text = "Save";
        window._saveFolder = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Environment.CurrentDirectory;
        window.SaveFolderText.Text = window._saveFolder;
        window.SaveFolderText.ToolTip = window._saveFolder;
        window.NameInput.Text = $"{Path.GetFileNameWithoutExtension(sourcePath)} (edited)";
        window.AiffChoice.Visibility = initial.Format == ExportFormat.Aiff ? Visibility.Visible : Visibility.Collapsed;
        if (note is not null)
        {
            window.NoteText.Text = note;
            window.NoteText.Visibility = Visibility.Visible;
        }

        window.ShowChoices();
        window.Loaded += (_, _) =>
        {
            window.NameInput.Focus();
            window.NameInput.SelectAll();
        };
        return window.ShowDialog() == true ? window.SaveChoice : null;
    }

    /// <summary>Opens the converter for some files. It stays open (not blocking the player) while it works.</summary>
    public static ExportWindow ShowConverter(Window owner, IReadOnlyList<string> files, AppSettings settings)
    {
        var options = settings.ConvertOptions.Format == ExportFormat.Aiff ? new ExportOptions() : settings.ConvertOptions;
        var window = new ExportWindow(saveMode: false, options, files, settings) { Owner = owner };
        window.Title = "Convert - Lucent";
        window.Heading.Text = files.Count == 1 ? "CONVERT A SOUND" : $"CONVERT {files.Count} SOUNDS";
        window.PrimaryText.Text = "Convert";
        var names = files.Take(4).Select(Path.GetFileName).ToList();
        window.FilesSummary.Text = files.Count <= 4
            ? string.Join("\n", names)
            : string.Join("\n", names) + $"\nand {files.Count - 4} more";
        window.ShowChoices();
        window.Show();
        return window;
    }

    // ---- Choices ----

    /// <summary>Ticks the buttons matching the options, and shows only the settings for the chosen format.</summary>
    private void ShowChoices()
    {
        foreach (var radio in Descendants<RadioButton>(OptionsArea))
        {
            var tag = radio.Tag as string;
            radio.IsChecked = radio.GroupName switch
            {
                "Format" => tag == _options.Format.ToString(),
                "Mp3Quality" => tag == _options.Mp3Quality.ToString(),
                "FlacBits" => tag == _options.FlacBitDepth.ToString(),
                "FlacCompression" => tag == _options.FlacCompression.ToString(),
                "WavDepth" => tag == _options.WavDepth.ToString(),
                "SampleRate" => tag == _options.SampleRate.ToString(),
                "Channels" => tag == (_options.Mono ? "Mono" : "Keep"),
                _ => radio.IsChecked,
            };
        }

        NextToOriginals.IsChecked = _options.OutputFolder is null;
        InChosenFolder.IsChecked = _options.OutputFolder is not null;
        OutputFolderRow.Visibility = _options.OutputFolder is null ? Visibility.Collapsed : Visibility.Visible;
        OutputFolderText.Text = _options.OutputFolder ?? string.Empty;
        OutputFolderText.ToolTip = _options.OutputFolder;

        Mp3Panel.Visibility = _options.Format == ExportFormat.Mp3 ? Visibility.Visible : Visibility.Collapsed;
        FlacPanel.Visibility = _options.Format == ExportFormat.Flac ? Visibility.Visible : Visibility.Collapsed;
        WavPanel.Visibility = _options.Format is ExportFormat.Wav or ExportFormat.Aiff ? Visibility.Visible : Visibility.Collapsed;
        FloatChoice.Visibility = _options.Format == ExportFormat.Aiff ? Visibility.Collapsed : Visibility.Visible;
        ExtensionText.Text = _options.Extension;

        var hint = Mp3Hints[_options.Mp3Quality];
        if (!Mp3Writer.IsLameAvailable)
        {
            hint += Mp3Writer.ConstantBitrate(_options.Mp3Quality) is null
                ? " The LAME encoder is missing, so Windows' own encoder will be used, at a constant bitrate."
                : " The LAME encoder is missing, so Windows' own encoder will be used.";
        }

        Mp3Hint.Text = hint;
    }

    private void OnOptionChosen(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag } radio)
        {
            return;
        }

        switch (radio.GroupName)
        {
            case "Format":
                _options.Format = Enum.Parse<ExportFormat>(tag);
                if (_options.Format == ExportFormat.Aiff && _options.WavDepth == PcmDepth.Float32)
                {
                    _options.WavDepth = PcmDepth.Int24;
                }

                break;
            case "Mp3Quality":
                _options.Mp3Quality = Enum.Parse<Mp3Quality>(tag);
                break;
            case "FlacBits":
                _options.FlacBitDepth = int.Parse(tag);
                break;
            case "FlacCompression":
                _options.FlacCompression = Enum.Parse<FlacCompression>(tag);
                break;
            case "WavDepth":
                _options.WavDepth = Enum.Parse<PcmDepth>(tag);
                break;
            case "SampleRate":
                _options.SampleRate = Enum.Parse<SampleRateChoice>(tag);
                break;
            case "Channels":
                _options.Mono = tag == "Mono";
                break;
        }

        ShowChoices();
    }

    private void OnOutputChoice(object sender, RoutedEventArgs e)
    {
        if (sender == NextToOriginals)
        {
            _options.OutputFolder = null;
        }
        else if (_options.OutputFolder is null && !ChooseOutputFolder())
        {
            // No folder chosen: stay with "next to the originals".
            _options.OutputFolder = null;
        }

        ShowChoices();
    }

    private void OnChooseOutputFolder(object sender, RoutedEventArgs e)
    {
        ChooseOutputFolder();
        ShowChoices();
    }

    private bool ChooseOutputFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Choose where the converted files go" };
        if (_options.OutputFolder is { } current && Directory.Exists(current))
        {
            dialog.InitialDirectory = current;
        }

        if (dialog.ShowDialog(this) != true)
        {
            return false;
        }

        _options.OutputFolder = dialog.FolderName;
        return true;
    }

    private void OnChooseSaveFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Choose where to save", InitialDirectory = _saveFolder };
        if (dialog.ShowDialog(this) == true)
        {
            _saveFolder = dialog.FolderName;
            SaveFolderText.Text = _saveFolder;
            SaveFolderText.ToolTip = _saveFolder;
        }
    }

    // ---- Processing steps ----

    /// <summary>Lists the steps, each with a button to take it out.</summary>
    private void ShowSteps()
    {
        StepsList.Children.Clear();
        for (var i = 0; i < _steps.Count; i++)
        {
            var step = _steps[i];
            var row = new DockPanel { Margin = new Thickness(2, 2, 0, 2) };
            var remove = new Button
            {
                Style = (Style)FindResource("PillButton"),
                Content = "REMOVE",
                ToolTip = "Take this step out",
            };
            DockPanel.SetDock(remove, Dock.Right);
            remove.Click += (_, _) =>
            {
                _steps.Remove(step);
                ShowSteps();
            };
            row.Children.Add(remove);
            row.Children.Add(new TextBlock
            {
                Text = $"{i + 1}.  {step.Description}",
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = (System.Windows.Media.Brush)FindResource("TextBrush"),
            });
            StepsList.Children.Add(row);
        }

        StepsHint.Text = _steps.Count == 0
            ? "Nothing: the sounds are only converted. Add steps to clean up or level a whole album at once."
            : "Done in this order, to every sound, before it's saved in the new format.";
    }

    private void OnAddStep(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = AddStepButton, Placement = PlacementMode.Bottom };
        void Add(ItemsControl parent, string header, Func<ProcessingStep> make, string? tip = null)
        {
            var item = new MenuItem { Header = header, ToolTip = tip };
            item.Click += (_, _) =>
            {
                _steps.Add(make());
                ShowSteps();
            };
            parent.Items.Add(item);
        }

        ProcessingStep Step(ProcessingKind kind, double? value = null)
        {
            var step = ProcessingStep.Create(kind);
            if (value is { } v)
            {
                step.Value = v;
            }

            return step;
        }

        MenuItem Group(string header, string? tip = null)
        {
            var group = new MenuItem { Header = header, ToolTip = tip };
            menu.Items.Add(group);
            return group;
        }

        var peak = Group("Normalize peaks to");
        foreach (var db in new[] { -0.1, -1, -3 })
        {
            Add(peak, $"{db:0.0#} dB".Replace("-", "−"), () => Step(ProcessingKind.NormalizePeak, db));
        }

        var loudness = Group("Normalize loudness to", "Measured the way streaming services do (ITU BS.1770); peaks are kept below −1 dB");
        Add(loudness, "−14 LUFS  (Spotify, YouTube)", () => Step(ProcessingKind.NormalizeLoudness, -14));
        Add(loudness, "−16 LUFS  (Apple Music, podcasts)", () => Step(ProcessingKind.NormalizeLoudness, -16));
        Add(loudness, "−23 LUFS  (TV and radio)", () => Step(ProcessingKind.NormalizeLoudness, -23));

        var limit = Group("Limit peaks to", "A true-peak limiter: nothing goes over, even after converting to MP3");
        Add(limit, "−1 dBTP  (recommended)", () => Step(ProcessingKind.Limit, -1));
        Add(limit, "−0.3 dBTP", () => Step(ProcessingKind.Limit, -0.3));

        var volume = Group("Volume");
        foreach (var db in new[] { 6, 3, -3, -6 })
        {
            Add(volume, $"{db:+0;−0} dB", () => Step(ProcessingKind.Volume, db));
        }

        menu.Items.Add(new Separator());
        Add(menu, "Equalizer, as set up in the editor", () =>
        {
            var step = Step(ProcessingKind.Equalizer);
            step.Bands = EqBand.CloneAll(EditorSettings.Load().EqBands);
            return step;
        }, "Uses the bands from the editor's EQ card as they were last left");
        Add(menu, "Compressor, as set up in the editor", () =>
        {
            var step = Step(ProcessingKind.Compress);
            step.Compressor = EditorSettings.Load().Compressor.Clone();
            return step;
        }, "Uses the settings from the editor's Dynamics card as they were last left");

        menu.Items.Add(new Separator());
        var hum = Group("Remove hum");
        Add(hum, "Find 50 or 60 Hz", () => Step(ProcessingKind.RemoveHum, 0));
        Add(hum, "50 Hz  (Europe, Asia, Africa)", () => Step(ProcessingKind.RemoveHum, 50));
        Add(hum, "60 Hz  (the Americas)", () => Step(ProcessingKind.RemoveHum, 60));
        Add(menu, "Remove clicks", () => Step(ProcessingKind.RemoveClicks), "Vinyl crackle and digital clicks, at a medium sensitivity");
        Add(menu, "Remove DC offset", () => Step(ProcessingKind.RemoveDcOffset));
        Add(menu, "Trim silence from the start and end", () => Step(ProcessingKind.TrimSilence), "Anything quieter than −60 dB");

        menu.Items.Add(new Separator());
        var fadeIn = Group("Fade in over");
        foreach (var seconds in new[] { 1, 2, 5 })
        {
            Add(fadeIn, $"{seconds} s", () => Step(ProcessingKind.FadeIn, seconds));
        }

        var fadeOut = Group("Fade out over");
        foreach (var seconds in new[] { 2, 3, 5, 10 })
        {
            Add(fadeOut, $"{seconds} s", () => Step(ProcessingKind.FadeOut, seconds));
        }

        menu.IsOpen = true;
    }

    // ---- Buttons ----

    private async void OnPrimary(object sender, RoutedEventArgs e)
    {
        if (_finished)
        {
            Close();
            return;
        }

        if (_saveMode)
        {
            var name = NameInput.Text.Trim();
            if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show(this, "Please type a name for the new file, without any of these: \\ / : * ? \" < > |",
                    "Save as new version", MessageBoxButton.OK, MessageBoxImage.Information);
                NameInput.Focus();
                return;
            }

            SaveChoice = (Path.Combine(_saveFolder, name + _options.Extension), _options.Clone());
            DialogResult = true;
            return;
        }

        await ConvertAsync();
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
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

    private void OnCloseButton(object sender, RoutedEventArgs e) => Close();

    private void OnShowFolder(object sender, RoutedEventArgs e)
    {
        if (_resultFolder is null)
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", $"\"{_resultFolder}\"");
        }
        catch (Exception)
        {
            // File Explorer normally opens; nothing more to do if it doesn't.
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_running is not null)
        {
            // Stop the conversion first; the window closes once the files in progress have been cleaned up.
            e.Cancel = true;
            _closeWhenDone = true;
            _running.Cancel();
        }
    }

    // ---- Converting ----

    private async Task ConvertAsync()
    {
        if (_settings is not null)
        {
            _settings.ConvertOptions = _options.Clone();
            _settings.ConvertSteps = _steps.Select(step => step.Clone()).ToList();
            _settings.Save();
        }

        var jobs = PlanJobs();
        if (jobs is null)
        {
            return;
        }

        _running = new CancellationTokenSource();
        OptionsArea.IsEnabled = false;
        PrimaryButton.IsEnabled = false;
        SecondaryButton.Content = "Stop";
        ProgressArea.Visibility = Visibility.Visible;
        ProgressText.Text = jobs.Count == 1 ? $"Converting {Path.GetFileName(jobs[0].Source)}…" : $"Converting {jobs.Count} sounds…";
        var progress = new Progress<double>(value =>
        {
            Progress.Value = value;
            if (_running is { IsCancellationRequested: false })
            {
                ProgressText.Text = $"Converting… {value * 100:0}%";
            }
        });

        IReadOnlyList<ConvertResult> results;
        try
        {
            results = await FileConverter.ConvertAsync(jobs, _options.Clone(), progress, _running.Token,
                _steps.Select(step => step.Clone()).ToList());
        }
        finally
        {
            _running.Dispose();
            _running = null;
        }

        ShowResults(results);
        if (_closeWhenDone)
        {
            Close();
        }
    }

    /// <summary>Works out every new file's name, asking what to do about files that already exist. Null if the user cancelled.</summary>
    private List<ConvertJob>? PlanJobs()
    {
        var destinations = _files.Select(file => FileConverter.DestinationFor(file, _options)).ToList();
        var existing = destinations.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var replace = false;
        if (existing.Count > 0)
        {
            var example = Path.GetFileName(existing[0]);
            var what = existing.Count == 1 ? $"\"{example}\" already exists." : $"{existing.Count} of the new files already exist, like \"{example}\".";
            var answer = MessageBox.Show(this,
                $"{what}\n\nYes: replace {(existing.Count == 1 ? "it" : "them")}.\nNo: keep both (the new {(existing.Count == 1 ? "file gets" : "files get")} a number, like \"Sound (2)\").\nCancel: don't convert.",
                "Convert", MessageBoxButton.YesNoCancel, MessageBoxImage.Question, MessageBoxResult.No);
            if (answer == MessageBoxResult.Cancel || answer == MessageBoxResult.None)
            {
                return null;
            }

            replace = answer == MessageBoxResult.Yes;
        }

        // Two sources can map to the same name (Song.flac and Song.wav both to Song.mp3): number the later ones.
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var jobs = new List<ConvertJob>();
        for (var i = 0; i < _files.Count; i++)
        {
            var destination = destinations[i];
            var exists = File.Exists(destination);
            if (taken.Contains(destination) || (exists && !replace))
            {
                destination = FileConverter.NumberedName(destination, taken);
                exists = false;
            }

            taken.Add(destination);
            jobs.Add(new ConvertJob(_files[i], destination, ReplaceExisting: exists));
        }

        return jobs;
    }

    private void ShowResults(IReadOnlyList<ConvertResult> results)
    {
        _finished = true;
        Progress.Value = 1;
        var succeeded = results.Where(r => r.Succeeded).ToList();
        AudioTools.NotifyFilesReplaced(succeeded.Where(r => r.Job.ReplaceExisting).Select(r => r.Job.Destination).ToList());
        var failed = results.Where(r => !r.Succeeded && r.Error != "Cancelled").ToList();
        var cancelled = results.Count(r => r.Error == "Cancelled");
        var lines = new List<string>
        {
            succeeded.Count == results.Count
                ? results.Count == 1 ? $"Done: {Path.GetFileName(succeeded[0].Job.Destination)}" : $"Done: converted {results.Count} sounds."
                : $"Converted {succeeded.Count} of {results.Count} sounds.",
        };
        if (cancelled > 0)
        {
            lines.Add($"{cancelled} {(cancelled == 1 ? "was" : "were")} skipped because you stopped.");
        }

        lines.AddRange(failed.Take(5).Select(r => $"• {Path.GetFileName(r.Job.Source)}: {r.Error}"));
        if (failed.Count > 5)
        {
            lines.Add($"…and {failed.Count - 5} more couldn't be converted.");
        }

        lines.AddRange(succeeded.Select(r => r.Note).OfType<string>().Distinct().Take(2));
        ProgressText.Text = string.Join("\n", lines);

        _resultFolder = succeeded.Select(r => Path.GetDirectoryName(r.Job.Destination)).FirstOrDefault();
        ShowFolderButton.Visibility = _resultFolder is null ? Visibility.Collapsed : Visibility.Visible;
        PrimaryButton.IsEnabled = true;
        PrimaryText.Text = "Done";
        SecondaryButton.Visibility = Visibility.Collapsed;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }
}
