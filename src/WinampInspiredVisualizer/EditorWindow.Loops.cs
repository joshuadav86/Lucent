using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Dsp;
using WinampInspiredVisualizer.Export;

namespace WinampInspiredVisualizer;

/// <summary>
/// The tempo, key and loop maker. Once a sound is open (and again a moment after each edit) the editor listens
/// for its beats and key in the background and shows them beside the length. With Beats on, the beats and bars
/// are drawn over the waveform and the cursor and selection snap to the beat (to half and quarter beats when
/// zoomed in; Ctrl places them freely). A selection always shows its length in bars and beats while it's dragged.
/// The loop maker selects a number of bars from the cursor, moves and resizes that loop a bar-count at a time,
/// finds the loops in the song that repeat most seamlessly, and saves loops as files named with their length,
/// tempo and key.
/// </summary>
public partial class EditorWindow
{
    private static readonly int[] LoopLengths = { 1, 2, 4, 8, 16 };
    private static readonly TimeSpan MusicDelay = TimeSpan.FromMilliseconds(700);
    private const int LoopsToFind = 8;

    // What was found, and the audio it was found in; and the audio being (or about to be) measured.
    private MusicAnalysis? _music;
    private AudioBuffer? _musicFor;
    private AudioBuffer? _musicOf;
    private CancellationTokenSource? _musicWork;
    private DispatcherTimer? _musicTimer;
    private IReadOnlyList<LoopCandidate> _foundLoops = Array.Empty<LoopCandidate>();

    private void InitLoops()
    {
        BeatsToggle.IsChecked = _settings.SnapToBeats;
        Waveform.ShowBeatGrid = _settings.SnapToBeats;
        foreach (var bars in LoopLengths)
        {
            var choice = new RadioButton
            {
                Style = (Style)FindResource("SegmentButton"),
                GroupName = "LoopBars",
                Content = bars == 1 ? "1 bar" : $"{bars}",
                Tag = bars,
                IsChecked = bars == _settings.LoopBars,
            };
            choice.Click += (_, _) =>
            {
                _settings.LoopBars = bars;
                _settings.Save();
                if (_foundLoops.Count > 0)
                {
                    FindLoops();
                }
            };
            LoopBarsChoices.Children.Add(choice);
        }

        Closed += (_, _) => _musicWork?.Cancel();
        ShowMusic();
    }

    /// <summary>
    /// Listens for the beats and key again when the sound has changed since they were last found: a moment after the
    /// change, so a run of edits is measured once.
    /// </summary>
    private void RefreshMusic()
    {
        if (_session is not { } session || ReferenceEquals(_musicOf, session.Audio))
        {
            return;
        }

        _musicOf = session.Audio;
        _musicWork?.Cancel();
        // The old beats would be in the wrong places after an edit that moved things along.
        Waveform.Grid = null;
        _foundLoops = Array.Empty<LoopCandidate>();
        if (_musicTimer is null)
        {
            _musicTimer = new DispatcherTimer { Interval = MusicDelay };
            _musicTimer.Tick += async (_, _) =>
            {
                _musicTimer.Stop();
                await AnalyzeMusicAsync();
            };
        }

        _musicTimer.Stop();
        _musicTimer.Start();
        ShowMusic();
    }

    private async Task AnalyzeMusicAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        var audio = session.Audio;
        var work = new CancellationTokenSource();
        _musicWork = work;
        MusicAnalysis? found = null;
        try
        {
            found = await Task.Run(() => MusicAnalysis.Analyze(audio, work.Token), work.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Measuring is a nicety: without it the editor works as before.
        }

        if (work.IsCancellationRequested || !ReferenceEquals(_session?.Audio, audio))
        {
            return;
        }

        _music = found ?? MusicAnalysis.Nothing;
        _musicFor = audio;
        Waveform.Grid = _music.Grid;
        UpdateSnap();
        UpdateSelectionInfo();
        ShowMusic();
    }

    /// <summary>The beats and key, when they're known and still match the sound.</summary>
    private MusicAnalysis? CurrentMusic => _music is not null && ReferenceEquals(_musicFor, _session?.Audio) ? _music : null;

    private BeatGrid? CurrentGrid => CurrentMusic?.Grid;

    private void ShowMusic()
    {
        var music = CurrentMusic;
        var grid = music?.Grid;
        MusicText.Text = music is null ? "Listening…" : music.Summary ?? "No beat or key found";
        MusicSummary.Text = music is null ? "Finding the tempo and key…" : music.Summary ?? string.Empty;
        LoopHint.Text = music is null
            ? "Lucent is listening for the beat and key."
            : grid is null
                ? "There's no steady beat to follow, so loops can't be measured in bars. Drag across the waveform to choose one by ear."
                : grid.IsSteady
                    ? "Loops start on a bar and stay in time. Turn on Beats to see the bars and snap to them while you drag the edges."
                    : "The tempo moves a little, so the bars follow the beats as played. Turn on Beats to see them and snap to them.";
        var hasGrid = grid is not null;
        MakeLoopButton.IsEnabled = hasGrid;
        LoopMoveButtons.IsEnabled = hasGrid;
        FindLoopsButton.IsEnabled = hasGrid;
        SaveLoopButton.IsEnabled = !Selection.IsEmpty;
        SaveFoundLoopsButton.IsEnabled = _foundLoops.Count > 0;
        if (_foundLoops.Count == 0)
        {
            LoopList.Children.Clear();
        }
    }

    private void OnLoopMaker(object sender, RoutedEventArgs e)
    {
        ShowMusic();
        LoopPopup.IsOpen = true;
    }

    private void OnBeatsToggle(object sender, RoutedEventArgs e)
    {
        _settings.SnapToBeats = BeatsToggle.IsChecked == true;
        _settings.Save();
        Waveform.ShowBeatGrid = _settings.SnapToBeats;
        UpdateSnap();
        ShowStatus(!_settings.SnapToBeats ? "Beats off: selections snap to zero crossings again, if Snap is on."
            : CurrentGrid is null ? "Beats on. There's no steady beat found in this sound, so there's nothing to snap to."
            : "Beats on: the bars are numbered, and the cursor and selection snap to the beat (half and quarter beats when zoomed in). Hold Ctrl to place them freely.");
    }

    /// <summary>The nearest beat, or half or quarter beat once beats are far enough apart on screen to aim between them.</summary>
    private int SnapToBeat(int frame)
    {
        if (CurrentGrid is not { } grid)
        {
            return SnapFrame(frame);
        }

        var beatPixels = grid.SamplesPerBeat / Math.Max(1e-9, Waveform.ViewLength / Math.Max(1, Waveform.ActualWidth));
        var division = beatPixels >= 160 ? 4 : beatPixels >= 80 ? 2 : 1;
        return Math.Clamp(grid.Nearest(frame, division), 0, _session?.Audio.Frames ?? 0);
    }

    private int SelectedBars => _settings.LoopBars is var bars && LoopLengths.Contains(bars) ? bars : 4;

    private void OnMakeLoop(object sender, RoutedEventArgs e)
    {
        if (CurrentGrid is not { } grid || _session is not { } session)
        {
            return;
        }

        var from = _player.IsPlaying ? _player.PositionFrame : Selection.IsEmpty ? Waveform.CursorFrame : Selection.Start;
        if (grid.BarsAt(from, SelectedBars, session.Audio.Frames) is not { } loop)
        {
            ShowStatus($"There aren't {SelectedBars} more bars after the cursor. Move it earlier, or pick a shorter loop.");
            return;
        }

        PlayLoop(loop);
    }

    private void OnLoopPrevious(object sender, RoutedEventArgs e) => MoveLoop(-1);

    private void OnLoopNext(object sender, RoutedEventArgs e) => MoveLoop(1);

    private void MoveLoop(int direction)
    {
        if (CurrentGrid is not { } grid || _session is not { } session || Selection.IsEmpty)
        {
            ShowStatus("Make a loop first, then move it along a loop at a time.");
            return;
        }

        if (grid.Move(Selection, direction, session.Audio.Frames) is { } moved)
        {
            PlayLoop(moved);
        }
        else
        {
            ShowStatus(direction < 0 ? "That's as early as a loop this long can go." : "That's as late as a loop this long can go.");
        }
    }

    private void OnLoopHalve(object sender, RoutedEventArgs e) => ResizeLoop(0.5);

    private void OnLoopDouble(object sender, RoutedEventArgs e) => ResizeLoop(2);

    private void ResizeLoop(double factor)
    {
        if (CurrentGrid is not { } grid || _session is not { } session || Selection.IsEmpty)
        {
            ShowStatus("Make a loop first, then halve or double it.");
            return;
        }

        if (grid.Resize(Selection, factor, session.Audio.Frames) is { } resized)
        {
            PlayLoop(resized);
        }
        else
        {
            ShowStatus(factor < 1 ? "A loop can't be shorter than a beat." : "There isn't room for a loop that long before the end.");
        }
    }

    /// <summary>Selects the loop and plays it round and round, so it can be heard as it will loop.</summary>
    private void PlayLoop(FrameRange loop)
    {
        Waveform.Selection = loop;
        if (loop.Start < Waveform.ViewStart || loop.End > Waveform.ViewStart + Waveform.ViewLength)
        {
            Waveform.ScrollTo(loop.Start, 0.1);
        }

        if (!_settings.Loop)
        {
            LoopToggle.IsChecked = true;
            SetLoop(true);
        }

        Play(loop.Start, loop.End);
        var length = CurrentGrid?.Measure(loop).Text;
        ShowStatus($"Looping {length} from {Format(loop.Start)}. Drag its edges to change it (they snap to the beat with Beats on), or Save loop to keep it.");
        ShowMusic();
    }

    private void OnFindLoops(object sender, RoutedEventArgs e) => FindLoops();

    private void FindLoops()
    {
        if (CurrentMusic is not { Grid: { } grid } music || _session is not { } session)
        {
            return;
        }

        _foundLoops = LoopFinder.Find(grid, music.Frames, session.Audio.Frames, SelectedBars, LoopsToFind);
        LoopList.Children.Clear();
        foreach (var loop in _foundLoops)
        {
            var row = new Button { Style = (Style)FindResource("PluginRow") };
            row.SetResourceReference(ForegroundProperty, "TextBrush");
            var text = new DockPanel();
            var score = new TextBlock { Text = $"{loop.ScoreText} seamless", Opacity = 0.7, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(score, Dock.Right);
            text.Children.Add(score);
            text.Children.Add(new TextBlock { Text = $"{Format(loop.Range.Start)}   {grid.Measure(loop.Range).Text}" });
            row.Content = text;
            row.ToolTip = "Select this loop and play it round and round";
            row.Click += (_, _) => PlayLoop(loop.Range);
            LoopList.Children.Add(row);
        }

        if (_foundLoops.Count == 0)
        {
            LoopList.Children.Add(new TextBlock
            {
                Style = (Style)FindResource("OptionHint"),
                Text = $"The sound isn't long enough for a {SelectedBars}-bar loop, or it's all quiet.",
            });
        }

        ShowMusic();
    }

    private async void OnSaveLoop(object sender, RoutedEventArgs e)
    {
        if (!Selection.IsEmpty)
        {
            await SaveLoopsAsync(new[] { Selection });
        }
    }

    private async void OnSaveFoundLoops(object sender, RoutedEventArgs e) =>
        await SaveLoopsAsync(_foundLoops.Select(loop => loop.Range).ToList());

    /// <summary>Saves loops as WAV files (which every music app takes) in a new folder beside the song.</summary>
    private async Task SaveLoopsAsync(IReadOnlyList<FrameRange> loops)
    {
        if (_session is not { } session || _info is not { } info || loops.Count == 0)
        {
            return;
        }

        LoopPopup.IsOpen = false;
        var audio = session.Audio;
        var music = CurrentMusic;
        var options = OptionsLike(info, ExportFormat.Wav);
        var folder = LoopFiles.NewFolderFor(_path);
        var source = _path;
        IReadOnlyList<string> files = Array.Empty<string>();
        var ok = await RunBusyAsync(loops.Count == 1 ? "Saving the loop…" : $"Saving {loops.Count} loops…", async (progress, token) =>
        {
            files = await Task.Run(() => LoopFiles.Save(audio, loops, source, folder, options, music?.Grid, music?.Key, progress.Report, token), token);
        });

        if (ok && files.Count > 0)
        {
            _lastSavedPath = files[0];
            ShowFileButton.Visibility = Visibility.Visible;
            ShowStatus(files.Count == 1
                ? $"Saved \"{Path.GetFileName(files[0])}\" in \"{Path.GetFileName(folder)}\"."
                : $"Saved {files.Count} loops in \"{Path.GetFileName(folder)}\".");
        }
    }
}
