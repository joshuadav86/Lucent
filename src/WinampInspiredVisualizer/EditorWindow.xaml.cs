using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Effects;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Windows;

namespace WinampInspiredVisualizer;

/// <summary>
/// A stereo (or mono) audio editor: shows a song's waveform or spectrogram, plays it with meters, selects part
/// of it (or of one channel), cuts, copies and pastes with crossfaded joins, processes it with built-in tools
/// and VST3 plugins, and saves it back or as a new file. Open it with <see cref="AudioTools.OpenEditor"/>.
/// </summary>
/// <remarks>
/// The rest of the window lives in partial files: EditorWindow.Clipboard.cs (cut, copy, paste),
/// EditorWindow.Tools.cs (the tool cards and menus), EditorWindow.Markers.cs (markers and analysis),
/// EditorWindow.History.cs (the history panel) and EditorWindow.Plugins.cs (VST3 effects and chains).
/// </remarks>
public partial class EditorWindow : Window
{
    /// <summary>With nothing selected, fades cover this much at the start or end.</summary>
    private static readonly TimeSpan DefaultFadeLength = TimeSpan.FromSeconds(3);

    /// <summary>The lengths of silence the right-click menu offers, in seconds.</summary>
    private static readonly double[] SilenceLengths = { 0.5, 1, 2, 3, 5, 10 };

    /// <summary>The main player: paused while the editor plays, and asked to let go of a file being saved over.</summary>
    private readonly AudioPlayer _mainPlayer;
    private readonly EditorPlayer _player = new();
    private readonly DispatcherTimer _playheadTimer;
    private string _path;
    private EditSession? _session;
    private SourceFileInfo? _info;
    private CancellationTokenSource? _busy;
    private bool _closeWhenIdle;
    private bool _closeConfirmed;
    private string? _lastSavedPath;
    private readonly FullScreen _fullScreen;
    private readonly EditorSettings _settings = EditorSettings.Load();

    public EditorWindow(string path, AudioPlayer mainPlayer)
    {
        InitializeComponent();
        _path = path;
        _mainPlayer = mainPlayer;
        FileTitle.Text = Path.GetFileName(path);
        Title = $"{Path.GetFileName(path)} - Audio editor - Lucent";
        _fullScreen = new FullScreen(this, FullScreenButton, FullScreenIcon);
        _fullScreen.Changed += (_, _) => OnWindowStateChanged(this, EventArgs.Empty);

        _playheadTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
        _playheadTimer.Tick += (_, _) => UpdatePlayhead();
        _player.Stopped += (_, error) =>
        {
            ShowStopped();
            if (error is not null)
            {
                ShowStatus("Playback stopped because of a problem with the audio device.");
            }
        };

        Waveform.SelectionChanged += (_, _) => UpdateSelectionInfo();
        Waveform.ViewChanged += (_, _) => Overview.SetVisibleRange(Waveform.ViewStart, Waveform.ViewLength);
        Waveform.CursorPlaced += (_, _) =>
        {
            // Clicking while playing carries on from the new spot (through the plugin, if previewing).
            if (_player.IsPlaying)
            {
                PlayFrom(Waveform.CursorFrame, _session?.Audio.Frames ?? 0, _previewThrough);
            }
        };
        Waveform.ClipMoveRequested += async (_, move) => await MoveClipAsync(move.Clip, move.ToIndex);
        Waveform.ClipSlideRequested += async (_, slide) => await SlideClipAsync(slide.Clip, slide.Offset);
        Waveform.ClipEdgeRequested += async (_, edge) => await MoveClipEdgeAsync(edge.Clip, edge.Start, edge.Frame);
        Waveform.ClipFadeRequested += async (_, fade) => await SetClipFadeAsync(fade.Clip, fade.FadeIn, fade.Frames);
        Waveform.SplitMoveRequested += (_, move) => MoveSplit(move.Split, move.Frame);
        Waveform.SilenceBeforeClip = clip => _session is { } session && !IsBusy ? session.SilenceBefore(clip) : 0;
        Waveform.MarkerClicked += (_, index) => GoToMarker(index);
        Waveform.MarkerMoved += (_, move) => MoveMarker(move.Marker, move.Frame);
        Waveform.MouseRightButtonUp += OnWaveformRightClick;
        Overview.NavigateRequested += (_, frame) => Waveform.ScrollTo(frame);
        Overview.ZoomRequested += (_, notches) => Waveform.Zoom(Math.Pow(0.8, notches));

        PitchSlider.Value = 0;
        UpdatePitchText();
        UpdateGainText();
        Waveform.View = _settings.View;
        Waveform.AmplitudeZoom = _settings.WaveZoom;
        Waveform.AmplitudeZoomChanged += (_, _) =>
        {
            _settings.WaveZoom = Waveform.AmplitudeZoom;
            _settings.Save();
        };
        SpectrogramToggle.IsChecked = _settings.View == EditorView.Spectrogram;
        SnapToggle.IsChecked = _settings.SnapToZeroCrossings;
        InitTransients();
        LoopToggle.IsChecked = _settings.Loop;
        HistoryToggle.IsChecked = _settings.ShowHistory;
        HistoryPanel.Visibility = _settings.ShowHistory ? Visibility.Visible : Visibility.Collapsed;
        // Until it's set here, the editor plays as loud as the main player.
        VolumeSlider.Value = _settings.Volume ?? _mainPlayer.Volume;
        _player.Volume = (float)VolumeSlider.Value;
        VolumeText.Text = $"{VolumeSlider.Value * 100:0}%";
        ShowInserts(_settings.ShowInserts);
        UpdateSnap();
        InitTools();
        SetEditingEnabled(false);
        Loaded += async (_, _) => await LoadAsync();
    }

    /// <summary>The file being edited (it changes after "Save as new version").</summary>
    public string FilePath => _path;

    /// <summary>True when there are edits that haven't been saved.</summary>
    public bool HasUnsavedChanges => _session?.IsModified == true;

    /// <summary>True while the editor is loading, processing or saving.</summary>
    public bool IsBusy => _busy is not null;

    private FrameRange Selection => Waveform.Selection;

    // ---- Opening ----

    private async Task LoadAsync()
    {
        _busy = new CancellationTokenSource();
        var progress = new Progress<double>(value => LoadingProgress.Value = value);
        string? problem = null;
        var cancelled = false;
        try
        {
            var token = _busy.Token;
            var (audio, info, mixedDown) = await Task.Run(() => AudioFileLoader.Load(_path, progress, token));
            LoadingText.Text = "Drawing the waveform…";
            var session = await Task.Run(() => new EditSession(audio));
            session.JoinFrames = JoinFrames(audio.SampleRate);
            _session = session;
            _info = info;
            Waveform.Peaks = session.Peaks;
            Overview.Peaks = session.Peaks;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ShowStatus(mixedDown
                ? $"This file has {info.Channels} channels; the editor mixes them down to stereo, so saving makes a stereo file."
                : "Drag across the waveform to select part of it, or right-click it for what you can do. The wheel zooms, Shift+wheel scrolls.");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            problem = ex is AudioFileException ? ex.Message : $"The file couldn't be opened. ({ex.Message})";
        }
        finally
        {
            _busy.Dispose();
            _busy = null;
        }

        if (problem is not null)
        {
            MessageBox.Show(this, problem, "Audio editor", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        if (problem is not null || cancelled || _closeWhenIdle)
        {
            Close();
            return;
        }

        SetEditingEnabled(true);
        UpdateSelectionInfo();
        UpdateHistory();
    }

    // ---- Busy work ----

    /// <summary>
    /// Runs slow work (processing, saving) off the UI thread with a progress bar and a Cancel button.
    /// Returns false if it was cancelled or failed (the user has been told why).
    /// </summary>
    private async Task<bool> RunBusyAsync(string message, Func<IProgress<double>, CancellationToken, Task> work)
    {
        if (IsBusy)
        {
            return false;
        }

        StopPlayback();
        _busy = new CancellationTokenSource();
        SetEditingEnabled(false);
        BusyPanel.Visibility = Visibility.Visible;
        BusyProgress.Value = 0;
        ShowFileButton.Visibility = Visibility.Collapsed;
        ShowStatus(message);
        var progress = new Progress<double>(value => BusyProgress.Value = value);
        try
        {
            await work(progress, _busy.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            ShowStatus("Cancelled. Nothing was changed.");
            return false;
        }
        catch (Exception ex)
        {
            ShowStatus("That didn't work.");
            var text = ex switch
            {
                AudioFileException => ex.Message,
                OutOfMemoryException => "There isn't enough memory for that. Try a shorter selection, or close other programs.",
                Editor.Plugins.PluginException => ex.Message,
                EditException => ex.Message,
                _ => FileConverter.FriendlyError(ex),
            };
            MessageBox.Show(this, text, "Audio editor", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
        finally
        {
            _busy.Dispose();
            _busy = null;
            BusyPanel.Visibility = Visibility.Collapsed;
            SetEditingEnabled(true);
            UpdateHistory();
            ClosePluginIfGone();
            if (_closeWhenIdle)
            {
                _ = Dispatcher.BeginInvoke(Close);
            }
        }
    }

    private void OnCancelBusy(object sender, RoutedEventArgs e) => _busy?.Cancel();

    private void SetEditingEnabled(bool enabled)
    {
        ProcessBar.IsEnabled = enabled && _session is not null;
        Toolbar.IsEnabled = enabled && _session is not null;
        SaveButtons.IsEnabled = enabled && _session is not null;
        PluginBar.IsEnabled = enabled && _session is not null;
        if (enabled)
        {
            UpdateSelectionInfo();
        }
    }

    private void ShowStatus(string text) => StatusText.Text = text;

    // ---- Processing ----

    /// <summary>
    /// Runs an effect over the selection (or the whole song) as one undoable step. Afterwards the processed
    /// part is selected, or <paramref name="selectAfter"/> if given.
    /// </summary>
    /// <remarks>
    /// When the selection is in one channel only (Alt+drag), only that channel changes, unless
    /// <paramref name="bothChannels"/> (swapping or widening needs both).
    /// </remarks>
    private async Task ApplyAsync(IAudioEffect effect, FrameRange? range = null, FrameRange? selectAfter = null, bool bothChannels = false)
    {
        if (_session is not { } session)
        {
            return;
        }

        var target = range ?? Selection;
        var hadSelection = !target.IsEmpty;
        var channel = bothChannels || !hadSelection ? null : Waveform.SelectedChannel;
        (FrameRange Range, string? Note) result = default;
        var done = await RunBusyAsync($"{effect.Description}…", async (progress, token) =>
        {
            result = await Task.Run(() => session.Apply(effect, target, progress, token, channel), token);
        });

        if (done)
        {
            ShowAudio();
            if (selectAfter is { } selection)
            {
                Waveform.Selection = selection;
            }
            else if (hadSelection)
            {
                Waveform.Selection = result.Range;
            }

            ShowStatus(result.Note ?? $"{effect.Description}: done. Ctrl+Z undoes it.");
        }
    }

    /// <summary>Runs a cut (delete or trim) as one undoable step.</summary>
    private async Task EditAsync(string description, Action<EditSession> edit, Func<FrameRange> selectionAfter)
    {
        if (_session is not { } session)
        {
            return;
        }

        if (await RunBusyAsync($"{description}…", async (_, token) => await Task.Run(() => edit(session), token)))
        {
            ShowAudio();
            Waveform.Selection = selectionAfter();
            ShowStatus($"{description}: done. Ctrl+Z undoes it.");
        }
    }

    /// <summary>Shows the session's current audio after a change.</summary>
    private void ShowAudio()
    {
        if (_session is null)
        {
            return;
        }

        Waveform.Peaks = _session.Peaks;
        Waveform.Splits = _session.Splits;
        Waveform.ClipEdgeDetails = _session.ClipEdgeDetails;
        Waveform.Markers = _session.Markers;
        // Problem spots found earlier may not be where they were any more.
        Waveform.Highlights = Array.Empty<FrameRange>();
        Overview.Peaks = _session.Peaks;
        Overview.SetVisibleRange(Waveform.ViewStart, Waveform.ViewLength);
        UpdateHistory();
        UpdateSelectionInfo();
    }

    private void OnNormalize(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = NormalizeButton, Placement = PlacementMode.Top, VerticalOffset = -6 };
        void Add(string header, NormalizeTarget target, double level, string? tip = null)
        {
            var item = new MenuItem { Header = header, ToolTip = tip };
            item.Click += async (_, _) => await ApplyAsync(new NormalizeEffect(target, level));
            menu.Items.Add(item);
        }

        menu.Items.Add(new MenuItem { Header = "Make the loudest peak reach", IsEnabled = false });
        Add("−0.1 dB  (as loud as it can be)", NormalizeTarget.Peak, -0.1);
        Add("−1 dB  (recommended)", NormalizeTarget.Peak, -1, "A little headroom, so it stays clean after converting to MP3");
        Add("−3 dB", NormalizeTarget.Peak, -3);
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "Make the overall loudness reach", IsEnabled = false });
        const string loudnessTip = "Measured the way streaming services do (ITU BS.1770). Peaks are kept below −1 dB.";
        Add("−14 LUFS  (Spotify, YouTube)", NormalizeTarget.Loudness, -14, loudnessTip);
        Add("−16 LUFS  (Apple Music, podcasts)", NormalizeTarget.Loudness, -16, loudnessTip);
        Add("−23 LUFS  (TV and radio)", NormalizeTarget.Loudness, -23, loudnessTip);
        menu.IsOpen = true;
    }

    private void OnGain(object sender, RoutedEventArgs e) => GainPopup.IsOpen = true;

    private void OnGainChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateGainText();

    private void UpdateGainText()
    {
        if (GainValue is not null)
        {
            GainValue.Text = $"{GainSlider.Value:+0.0;−0.0;0.0} dB";
        }
    }

    private async void OnApplyGain(object sender, RoutedEventArgs e)
    {
        GainPopup.IsOpen = false;
        if (Math.Abs(GainSlider.Value) > 0.01)
        {
            await ApplyAsync(new GainEffect(GainSlider.Value));
        }
    }

    private void OnPitch(object sender, RoutedEventArgs e) => PitchPopup.IsOpen = true;

    private void OnPitchChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdatePitchText();

    private void OnPitchDown(object sender, RoutedEventArgs e) => PitchSlider.Value = Math.Round(PitchSlider.Value - 0.1, 1);

    private void OnPitchUp(object sender, RoutedEventArgs e) => PitchSlider.Value = Math.Round(PitchSlider.Value + 0.1, 1);

    private void OnPitchModeChanged(object sender, RoutedEventArgs e) => UpdatePitchText();

    private void UpdatePitchText()
    {
        if (PitchValue is null || PitchHint is null)
        {
            return;
        }

        var semitones = Math.Round(PitchSlider.Value, 1);
        PitchValue.Text = Math.Abs(semitones) < 0.05 ? "0 semitones" : $"{semitones:+0.0;−0.0} semitones";
        var speed = Math.Pow(2, semitones / 12);
        PitchHint.Text = KeepSpeedChoice.IsChecked == true
            ? "The tempo and length stay the same. Small moves sound the most natural; a big jump can sound a little hollow."
            : $"Like a record played faster or slower: sounds the most natural, but the tempo changes too " +
              $"({speed * 100:0}% speed, so the selection gets {(speed >= 1 ? "shorter" : "longer")}). 12 semitones is an octave.";
    }

    private async void OnApplyPitch(object sender, RoutedEventArgs e)
    {
        PitchPopup.IsOpen = false;
        var semitones = Math.Round(PitchSlider.Value, 1);
        if (Math.Abs(semitones) >= 0.05)
        {
            await ApplyAsync(new PitchEffect(semitones, KeepSpeedChoice.IsChecked == true));
        }
    }

    private async void OnFadeIn(object sender, RoutedEventArgs e) =>
        await ApplyAsync(new FadeEffect(FadeDirection.In), FadeRange(FadeDirection.In));

    private async void OnFadeOut(object sender, RoutedEventArgs e) =>
        await ApplyAsync(new FadeEffect(FadeDirection.Out), FadeRange(FadeDirection.Out));

    /// <summary>The selection, or with nothing selected, the first or last few seconds of the song (or of <paramref name="clip"/>).</summary>
    private FrameRange FadeRange(FadeDirection direction, FrameRange? clip = null)
    {
        if (!Selection.IsEmpty || _session is null)
        {
            return Selection;
        }

        var scope = clip ?? new FrameRange(0, _session.Audio.Frames);
        var length = (int)Math.Min(scope.Length, DefaultFadeLength.TotalSeconds * _session.Audio.SampleRate);
        return direction == FadeDirection.In ? new FrameRange(scope.Start, length) : new FrameRange(scope.End - length, length);
    }

    private async void OnSilence(object sender, RoutedEventArgs e) => await ApplyAsync(new SilenceEffect());

    private async void OnReverse(object sender, RoutedEventArgs e) => await ApplyAsync(new ReverseEffect());

    private async void OnTrim(object sender, RoutedEventArgs e)
    {
        var selection = Selection;
        if (!selection.IsEmpty)
        {
            await EditAsync("Trim", session => session.TrimTo(selection), () => new FrameRange(0, 0));
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e) => await DeleteSelectionAsync();

    private async Task DeleteSelectionAsync()
    {
        var selection = Selection;
        if (selection.IsEmpty)
        {
            return;
        }

        if (Waveform.SelectedChannel is not null)
        {
            // One channel can't get shorter than the other, so what's selected in it is silenced instead.
            await ApplyAsync(new SilenceEffect());
            ShowStatus("Only one channel is selected, so that part of it was silenced (both channels must stay the same length).");
            return;
        }

        await EditAsync("Delete", session => session.Delete(selection), () => new FrameRange(selection.Start, 0));
    }

    // ---- Clips ----

    private void OnSplit(object sender, RoutedEventArgs e) => SplitAtCursor();

    /// <summary>
    /// Splits around the selection, or with nothing selected, at the cursor (at the playhead while playing,
    /// so S can be tapped along with the music). Splitting is instant, so playback carries on.
    /// </summary>
    private void SplitAtCursor()
    {
        var frames = !Selection.IsEmpty
            ? new[] { Selection.Start, Selection.End }
            : new[] { _player.IsPlaying ? SnapFrame(_player.PositionFrame) : Waveform.CursorFrame };
        Split(frames);
    }

    private void Split(int[] frames)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        if (!session.Split(frames))
        {
            ShowStatus(frames.All(f => f <= 0 || f >= session.Audio.Frames)
                ? "Put the cursor inside the sound to split it there."
                : "It's already split there.");
            return;
        }

        Waveform.Splits = session.Splits;

        Waveform.ClipEdgeDetails = session.ClipEdgeDetails;
        UpdateHistory();
        ShowStatus($"Split into {session.Clips.Count} clips. Drag a clip's tab to slide it along (Alt+drag to move it past the others), or right-click it for more. Ctrl+Z undoes it.");
    }

    private async Task MoveClipAsync(int clip, int toIndex)
    {
        var moved = default(FrameRange);
        await EditAsync("Move clip", session => moved = session.MoveClip(clip, toIndex), () => moved);
    }

    private async Task SlideClipAsync(int clip, int offset)
    {
        var moved = default(FrameRange);
        await EditAsync("Move clip", session => moved = session.SlideClip(clip, offset), () => moved);
    }

    /// <summary>Trims a clip by one of its edges, or brings back what was trimmed or cut, then selects the clip.</summary>
    private async Task MoveClipEdgeAsync(int clip, bool start, int frame)
    {
        if (_session is not { } session || clip >= session.Clips.Count)
        {
            return;
        }

        var range = session.Clips[clip];
        var trimming = start ? frame > range.Start : frame < range.End;
        var moved = default(FrameRange);
        await EditAsync(trimming ? "Trim clip" : "Extend clip", s => moved = s.MoveClipEdge(clip, start, frame), () => moved);
    }

    private async Task SetClipFadeAsync(int clip, bool fadeIn, int frames)
    {
        if (_session is not { } session || clip >= session.Clips.Count)
        {
            return;
        }

        var selection = Selection;
        await EditAsync(fadeIn ? "Fade in" : "Fade out", s => s.SetClipFade(clip, fadeIn, frames), () => selection);
    }

    /// <summary>Moves the split between two touching clips, so one grows as the other shrinks.</summary>
    private void MoveSplit(int split, int frame)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        session.MoveSplit(split, frame);
        Waveform.Splits = session.Splits;
        Waveform.ClipEdgeDetails = session.ClipEdgeDetails;
        UpdateHistory();
        ShowStatus("Moved the edge between the clips. Ctrl+Z undoes it.");
    }

    private async Task InsertSilenceAsync(int at, double seconds)
    {
        if (_session is not { } session)
        {
            return;
        }

        var silence = default(FrameRange);
        var frames = (int)Math.Round(seconds * session.Audio.SampleRate);
        await EditAsync("Add silence", s => silence = s.InsertSilence(at, frames), () => silence);
    }

    /// <summary>
    /// The right-click menu: what to do to the selection when the click is inside it, otherwise to the
    /// clip under the mouse (or the whole song before it's split), plus splitting, moving and silence.
    /// </summary>
    private void OnWaveformRightClick(object sender, MouseButtonEventArgs e)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        e.Handled = true;
        if (Waveform.MarkerAt(e.GetPosition(Waveform)) is { } marker)
        {
            ShowMarkerMenu(marker);
            return;
        }

        var frame = Waveform.FrameAt(e.GetPosition(Waveform).X);
        var onSelection = !Selection.IsEmpty && frame >= Selection.Start && frame <= Selection.End;
        if (!onSelection)
        {
            Waveform.ClearSelection();
            Waveform.CursorFrame = frame;
        }

        var clips = session.Clips;
        var clipIndex = session.ClipAt(frame);
        clipIndex = Math.Min(clipIndex, clips.Count - 1);
        var clip = clips[clipIndex];
        var split = clips.Count > 1;
        var noun = onSelection ? "selection" : split ? "clip" : "sound";
        // Effects work on the selection, or on the clip; an empty range means the whole song.
        var target = onSelection ? Selection : split ? clip : new FrameRange(0, 0);

        var menu = new ContextMenu { PlacementTarget = Waveform, Placement = PlacementMode.MousePoint };
        MenuItem Add(ItemsControl parent, string header, Func<Task> action, string? gesture = null, bool enabled = true)
        {
            var item = new MenuItem { Header = header, InputGestureText = gesture, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            parent.Items.Add(item);
            return item;
        }

        Task Run(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        // Selects the clip first, so the Volume and Pitch cards (which work on the selection) change just it.
        void SelectTarget()
        {
            if (!onSelection && split)
            {
                Waveform.Selection = clip;
            }
        }

        var playRange = onSelection ? Selection : split ? clip : new FrameRange(frame, session.Audio.Frames - frame);
        Add(menu, onSelection || split ? $"Play {noun}" : "Play from here", () => Run(() => Play(playRange.Start, playRange.End)), "Space");
        Add(menu, onSelection ? "Split around selection" : "Split here", () => Run(() =>
            Split(onSelection ? new[] { Selection.Start, Selection.End } : new[] { frame })), "S");
        if (split && !onSelection)
        {
            Add(menu, "Select clip", () => Run(() => Waveform.Selection = clip));
        }

        menu.Items.Add(new Separator());
        if (onSelection)
        {
            Add(menu, "Cut", CutAsync, "Ctrl+X");
            Add(menu, "Copy", () => Run(Copy), "Ctrl+C");
        }

        Add(menu, onSelection ? "Paste over selection" : "Paste here", PasteAsync, "Ctrl+V", enabled: HasClip);
        Add(menu, "Add marker here", () => Run(() => AddMarker(onSelection ? Selection.Start : frame)), "M");

        menu.Items.Add(new Separator());
        Add(menu, "Fade in", () => ApplyAsync(new FadeEffect(FadeDirection.In),
            FadeRange(FadeDirection.In, onSelection ? null : split ? clip : null)));
        Add(menu, "Fade out", () => ApplyAsync(new FadeEffect(FadeDirection.Out),
            FadeRange(FadeDirection.Out, onSelection ? null : split ? clip : null)));
        Add(menu, "Normalize to −1 dB", () => ApplyAsync(new NormalizeEffect(NormalizeTarget.Peak, -1), target));
        Add(menu, "Volume…", () => Run(() =>
        {
            SelectTarget();
            GainPopup.IsOpen = true;
        }));
        Add(menu, "Pitch…", () => Run(() =>
        {
            SelectTarget();
            PitchPopup.IsOpen = true;
        }));
        Add(menu, "Reverse", () => ApplyAsync(new ReverseEffect(), target));
        Add(menu, "Replace with silence", () => ApplyAsync(new SilenceEffect(), target));

        menu.Items.Add(new Separator());
        if (onSelection)
        {
            var selection = Selection;
            Add(menu, "Trim to selection", () => EditAsync("Trim", s => s.TrimTo(selection), () => new FrameRange(0, 0)));
            Add(menu, "Delete selection", DeleteSelectionAsync, "Delete");
        }
        else if (split)
        {
            Add(menu, "Delete clip", () => EditAsync("Delete clip", s => s.Delete(clip), () => new FrameRange(clip.Start, 0)));
        }

        if (split && !onSelection)
        {
            menu.Items.Add(new Separator());
            Add(menu, "Move clip earlier", () => MoveClipAsync(clipIndex, clipIndex - 1), enabled: clipIndex > 0);
            Add(menu, "Move clip later", () => MoveClipAsync(clipIndex, clipIndex + 1), enabled: clipIndex < clips.Count - 1);
            Add(menu, "Join with previous clip", () => Run(() => JoinAt(clip.Start)), enabled: clipIndex > 0);
            Add(menu, "Join with next clip", () => Run(() => JoinAt(clip.End)), enabled: clipIndex < clips.Count - 1);
        }

        menu.Items.Add(new Separator());
        void AddSilence(string header, int at)
        {
            var parent = new MenuItem { Header = header };
            foreach (var seconds in SilenceLengths)
            {
                Add(parent, seconds < 1 ? $"{seconds * 1000:0} ms" : $"{seconds:0} second{(seconds == 1 ? "" : "s")}",
                    () => InsertSilenceAsync(at, seconds));
            }

            menu.Items.Add(parent);
        }

        var block = onSelection ? Selection : clip;
        if (!onSelection)
        {
            AddSilence("Add silence here", frame);
        }

        AddSilence($"Add silence before {noun}", block.Start);
        AddSilence($"Add silence after {noun}", block.End);
        menu.IsOpen = true;
    }

    private void JoinAt(int split)
    {
        if (_session is not { } session)
        {
            return;
        }

        session.Join(split);
        Waveform.Splits = session.Splits;
        Waveform.ClipEdgeDetails = session.ClipEdgeDetails;
        UpdateHistory();
        ShowStatus("Joined. Ctrl+Z undoes it.");
    }

    // ---- Undo ----

    private async void OnUndo(object sender, RoutedEventArgs e) => await UndoRedoAsync(undo: true);

    private async void OnRedo(object sender, RoutedEventArgs e) => await UndoRedoAsync(undo: false);

    private async Task UndoRedoAsync(bool undo)
    {
        if (_session is not { } session || (undo ? !session.CanUndo : !session.CanRedo))
        {
            return;
        }

        var description = undo ? session.UndoDescription : session.RedoDescription;
        FrameRange? changed = null;
        if (await RunBusyAsync(undo ? "Undoing…" : "Redoing…", async (_, _) =>
            {
                // Not cancellable: it is only copying memory, and stopping halfway would leave the history inconsistent.
                changed = await Task.Run(() => undo ? session.Undo() : session.Redo());
            }))
        {
            ShowAudio();
            if (changed is { IsEmpty: false } range)
            {
                Waveform.Selection = range;
            }

            ShowStatus($"{(undo ? "Undid" : "Redid")}: {description}.");
        }
    }

    private void UpdateHistory()
    {
        var session = _session;
        UndoButton.IsEnabled = session?.CanUndo == true && !IsBusy;
        RedoButton.IsEnabled = session?.CanRedo == true && !IsBusy;
        UndoButton.ToolTip = session?.UndoDescription is { } undo ? $"Undo {undo} (Ctrl+Z)" : "Undo (Ctrl+Z)";
        RedoButton.ToolTip = session?.RedoDescription is { } redo ? $"Redo {redo} (Ctrl+Y)" : "Redo (Ctrl+Y)";
        var name = Path.GetFileName(_path);
        FileTitle.Text = HasUnsavedChanges ? $"{name}  •  edited" : name;
        UpdateHistoryList();
    }

    // ---- Playback ----

    private void OnPlay(object sender, RoutedEventArgs e) => TogglePlay();

    private void OnPlaySelection(object sender, RoutedEventArgs e)
    {
        if (!Selection.IsEmpty)
        {
            Play(Selection.Start, Selection.End);
        }
    }

    private void TogglePlay()
    {
        if (_player.IsPlaying)
        {
            StopPlayback();
        }
        else if (!Selection.IsEmpty)
        {
            Play(Selection.Start, Selection.End);
        }
        else if (_session is not null)
        {
            // From the end, start again at the top.
            var from = Waveform.CursorFrame >= _session.Audio.Frames - 1 ? 0 : Waveform.CursorFrame;
            Play(from, _session.Audio.Frames);
        }
    }

    /// <summary>Plays part of the song, through <paramref name="through"/> (the plugin chain or a tool preview) if given.</summary>
    private void PlayFrom(int from, int to, Func<NAudio.Wave.ISampleProvider, NAudio.Wave.ISampleProvider>? through = null)
    {
        if (_session is null || IsBusy)
        {
            return;
        }

        // Two songs playing over each other would be confusing.
        if (_mainPlayer.IsPlaying)
        {
            _mainPlayer.Pause();
            AudioTools.NotifyMainPlayerChanged();
        }

        try
        {
            _player.Play(_session.Audio, from, to, through, _settings.Loop);
            _playTo = to;
            _previewThrough = through;
            Meter.Reset();
        }
        catch (Exception ex)
        {
            ShowStatus($"Couldn't play: {ex.Message}");
            return;
        }

        _playheadTimer.Start();
        PlayIcon.Data = (Geometry)FindResource("StopIcon");
        PlayIcon.Margin = new Thickness(0);
        UpdatePlayhead();
    }

    /// <summary>The listening volume: what's heard while editing, never the audio itself.</summary>
    private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        _player.Volume = (float)e.NewValue;
        _settings.Volume = e.NewValue;
        if (VolumeText is not null)
        {
            VolumeText.Text = $"{e.NewValue * 100:0}%";
        }
    }

    private void StopPlayback()
    {
        _player.Stop();
        ShowStopped();
    }

    private void ShowStopped()
    {
        _playheadTimer.Stop();
        Waveform.PlayheadFrame = null;
        PlayIcon.Data = (Geometry)FindResource("PlayIcon");
        PlayIcon.Margin = new Thickness(2, 0, 0, 0);
        Meter.Show(null);
        ShowPreviewStopped();
        ShowToolPreviewStopped();
    }

    private void UpdatePlayhead()
    {
        if (!_player.IsPlaying)
        {
            ShowStopped();
            return;
        }

        var frame = _player.PositionFrame;
        Waveform.PlayheadFrame = frame;
        Waveform.KeepInView(frame);
        PositionText.Text = Format(frame);
        Meter.Show(_player.Meter?.Read());
        ShowToolReduction();
    }

    // ---- Selection and position ----

    private void UpdateSelectionInfo()
    {
        if (_session is null)
        {
            return;
        }

        var selection = Selection;
        var rate = _session.Audio.SampleRate;
        if (selection.IsEmpty)
        {
            PositionText.Text = Format(Waveform.CursorFrame);
            ScopeHint.Text = "Nothing is selected, so effects change the whole sound. Drag across the waveform to choose a part.";
        }
        else
        {
            PositionText.Text = $"{Format(selection.Start)} – {Format(selection.End)}";
            var which = Waveform.SelectedChannel switch
            {
                0 => " of the left channel",
                1 => " of the right channel",
                _ => string.Empty,
            };
            ScopeHint.Text = $"Effects change the selected {selection.Length / (double)rate:0.0#} seconds{which}. Esc selects nothing." +
                             (which.Length == 0 && _session.Audio.ChannelCount > 1 ? " Alt+drag selects in one channel." : string.Empty);
        }

        LengthText.Text = $"Length {Format(_session.Audio.Frames)}  ·  {rate / 1000.0:0.#} kHz  ·  " +
                          (_session.Audio.ChannelCount == 1 ? "mono" : "stereo");
        var hasSelection = !selection.IsEmpty && !IsBusy;
        TrimButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        PlaySelectionButton.IsEnabled = hasSelection;
    }

    private string Format(int frame) => WaveformView.FormatTime(frame / (double)(_session?.Audio.SampleRate ?? 44100));

    // ---- Saving ----

    private async void OnSave(object sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAs(object sender, RoutedEventArgs e) => await SaveAsNewVersionAsync(null);

    /// <summary>Replaces the original file, after asking.</summary>
    private async Task<bool> SaveAsync()
    {
        if (_session is not { } session || _info is not { } info)
        {
            return false;
        }

        if (ExportOptions.WritableFormat(_path) is not { } format)
        {
            var kind = Path.GetExtension(_path).TrimStart('.').ToUpperInvariant();
            return await SaveAsNewVersionAsync(
                $"Lucent can't write {kind} files, so the edit will be saved as a new file. FLAC is lossless: no quality is lost.");
        }

        var name = Path.GetFileName(_path);
        var warning = format == ExportFormat.Mp3
            ? "\n\nMP3s lose a little quality every time they are saved. To keep the original as it is, use Save as new version."
            : string.Empty;
        var playing = _mainPlayer.IsCurrentFile(_path) ? "\n\nIt's open in the player, which will pause for a moment while it's replaced." : string.Empty;
        var answer = MessageBox.Show(this,
            $"Replace \"{name}\" with your edited version?\n\nThe original can't be brought back afterwards.{warning}{playing}",
            "Save", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
        {
            return false;
        }

        return await WriteAsync(_path, OptionsLike(info, format), replaceExisting: true);
    }

    /// <summary>Asks for a name, format and quality, then saves a new file, keeping the original.</summary>
    private async Task<bool> SaveAsNewVersionAsync(string? note)
    {
        if (_session is null || _info is not { } info)
        {
            return false;
        }

        var format = ExportOptions.WritableFormat(_path) ?? ExportFormat.Flac;
        var choice = ExportWindow.ShowSaveDialog(this, _path, OptionsLike(info, format), note);
        if (choice is null)
        {
            return false;
        }

        var (target, options) = choice.Value;
        var exists = File.Exists(target);
        if (exists)
        {
            var same = string.Equals(Path.GetFullPath(target), Path.GetFullPath(_path), StringComparison.OrdinalIgnoreCase);
            var question = same
                ? $"\"{Path.GetFileName(target)}\" is the original file. Replace it with your edited version?"
                : $"There's already a file called \"{Path.GetFileName(target)}\". Replace it?";
            if (MessageBox.Show(this, question, "Save as new version", MessageBoxButton.YesNo, MessageBoxImage.Question,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return false;
            }
        }

        if (!await WriteAsync(target, options, exists))
        {
            return false;
        }

        // From now on the editor works on the new file, so Save updates it rather than the original.
        _path = target;
        _info = info with { Path = target, BitsPerSample = options.Format == ExportFormat.Flac ? options.FlacBitDepth : info.BitsPerSample };
        Title = $"{Path.GetFileName(target)} - Audio editor - Lucent";
        UpdateHistory();
        return true;
    }

    /// <summary>Encodes the audio into a temporary file, checks it, then moves it into place.</summary>
    private async Task<bool> WriteAsync(string target, ExportOptions options, bool replaceExisting)
    {
        if (_session is not { } session)
        {
            return false;
        }

        var audio = session.Audio;
        var tagSource = _path;
        PreparedFile? prepared = null;
        var ok = await RunBusyAsync($"Saving {Path.GetFileName(target)}…", async (progress, token) =>
        {
            prepared = await Task.Run(() => ExportPipeline.Prepare(new AudioBufferSampleProvider(audio), audio.Frames, target,
                tagSource, audioChanged: true, options, progress.Report, token), token);

            // The last step is instant: swap the file into place. Windows won't replace a file that's open,
            // so if the main player has it, it lets go for a moment and then carries on from the same spot.
            var ready = prepared;
            await _mainPlayer.RunWithFileReleasedAsync(target, () =>
            {
                ready.Commit(replaceExisting);
                return Task.CompletedTask;
            });
            AudioTools.NotifyMainPlayerChanged();
            if (replaceExisting)
            {
                AudioTools.NotifyFilesReplaced(new[] { target });
            }
        });

        if (!ok)
        {
            prepared?.Discard();
            return false;
        }

        session.MarkSaved();
        _lastSavedPath = target;
        UpdateHistory();
        ShowStatus(prepared?.Note ?? $"Saved \"{Path.GetFileName(target)}\" ({options.Summary}).");
        ShowFileButton.Visibility = Visibility.Visible;
        return true;
    }

    /// <summary>Save settings that match the original file as closely as Lucent can write.</summary>
    private static ExportOptions OptionsLike(SourceFileInfo info, ExportFormat format)
    {
        var options = new ExportOptions { Format = format, SampleRate = SampleRateChoice.Keep };
        options.FlacBitDepth = info.BitsPerSample > 16 ? 24 : 16;
        options.WavDepth = info.IsFloat ? PcmDepth.Float32 : info.BitsPerSample > 24 ? PcmDepth.Float32 : info.BitsPerSample > 16 ? PcmDepth.Int24 : PcmDepth.Int16;
        if (format == ExportFormat.Aiff && options.WavDepth == PcmDepth.Float32)
        {
            options.WavDepth = PcmDepth.Int24;
        }

        // The nearest standard bitrate at or above the original's, so re-saving doesn't lose more than it must.
        options.Mp3Quality = info.BitrateKbps switch
        {
            0 => Mp3Quality.VbrV0,
            > 256 => Mp3Quality.Cbr320,
            > 192 => Mp3Quality.Cbr256,
            > 128 => Mp3Quality.Cbr192,
            _ => Mp3Quality.Cbr128,
        };
        return options;
    }

    private void OnShowFile(object sender, RoutedEventArgs e)
    {
        if (_lastSavedPath is not null)
        {
            try
            {
                Process.Start("explorer.exe", $"/select,\"{_lastSavedPath}\"");
            }
            catch (Exception)
            {
                // Nothing useful to add; File Explorer usually opens.
            }
        }
    }

    // ---- Keys ----

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Typing in a box, or using the sliders in the tool cards, keeps its keys.
        if (e.OriginalSource is TextBox || ToolPopups.Any(popup => popup.IsOpen))
        {
            return;
        }

        if (FullScreen.IsToggleKey(e))
        {
            _fullScreen.Toggle();
            e.Handled = true;
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        var shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (IsBusy || _session is null)
        {
            if (e.Key == Key.Escape && IsBusy)
            {
                _busy?.Cancel();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape && _fullScreen.IsOn)
            {
                _fullScreen.Toggle();
                e.Handled = true;
            }

            return;
        }

        switch (e.Key)
        {
            case Key.Space:
                TogglePlay();
                break;
            case Key.A when ctrl:
                Waveform.SelectAll();
                break;
            case Key.Z when ctrl && shift:
            case Key.Y when ctrl:
                e.Handled = true;
                await UndoRedoAsync(undo: false);
                return;
            case Key.Z when ctrl:
                e.Handled = true;
                await UndoRedoAsync(undo: true);
                return;
            case Key.S when ctrl:
                e.Handled = true;
                await SaveAsync();
                return;
            case Key.S:
                SplitAtCursor();
                break;
            case Key.C when ctrl:
                Copy();
                break;
            case Key.X when ctrl:
                e.Handled = true;
                await CutAsync();
                return;
            case Key.V when ctrl:
                e.Handled = true;
                await PasteAsync();
                return;
            case Key.M:
                AddMarker(_player.IsPlaying ? _player.PositionFrame : Selection.IsEmpty ? Waveform.CursorFrame : Selection.Start);
                break;
            case Key.OemOpenBrackets:
                GoToNextMarker(forward: false);
                break;
            case Key.OemCloseBrackets:
                GoToNextMarker(forward: true);
                break;
            case Key.L:
                LoopToggle.IsChecked = LoopToggle.IsChecked != true;
                SetLoop(LoopToggle.IsChecked == true);
                break;
            case Key.G when !ctrl:
                SetHandTool(HandToggle.IsChecked != true);
                break;
            case Key.B:
                if (_chain.Count > 0)
                {
                    SetChainBypassed(!_chainBypassed);
                }

                break;
            case Key.H:
                ShowHistoryPanel(HistoryPanel.Visibility != Visibility.Visible);
                break;
            case Key.I:
                ShowInserts(PluginBar.Visibility != Visibility.Visible);
                break;
            case Key.V:
                SpectrogramToggle.IsChecked = SpectrogramToggle.IsChecked != true;
                SetView(SpectrogramToggle.IsChecked == true ? EditorView.Spectrogram : EditorView.Waveform);
                break;
            case Key.F3:
                GoToNextHighlight(forward: !shift);
                break;
            case Key.Tab when _settings.TabToTransient:
                e.Handled = true;
                await TabToTransientAsync(forward: !shift, extend: ctrl);
                return;
            case Key.Delete or Key.Back:
                e.Handled = true;
                await DeleteSelectionAsync();
                return;
            case Key.Escape:
                if (Waveform.IsDraggingClip)
                {
                    Waveform.CancelClipDrag();
                }
                else if (_fullScreen.IsOn)
                {
                    _fullScreen.Toggle();
                }
                else if (_player.IsPlaying)
                {
                    StopPlayback();
                }
                else
                {
                    Waveform.ClearSelection();
                }

                break;
            case Key.Home:
                Waveform.ClearSelection();
                Waveform.CursorFrame = 0;
                Waveform.ScrollTo(0, 0);
                break;
            case Key.End:
                Waveform.ClearSelection();
                Waveform.CursorFrame = _session.Audio.Frames;
                Waveform.ScrollTo(_session.Audio.Frames, 1);
                break;
            case Key.OemPlus or Key.Add:
                Waveform.Zoom(0.5);
                break;
            case Key.OemMinus or Key.Subtract:
                Waveform.Zoom(2);
                break;
            case Key.D0 or Key.NumPad0:
                Waveform.ZoomToFit();
                break;
            case Key.Up when shift:
                VolumeSlider.Value += 0.05;
                break;
            case Key.Down when shift:
                VolumeSlider.Value -= 0.05;
                break;
            case Key.Up when ctrl:
                Waveform.AmplitudeZoom *= 2;
                break;
            case Key.Down when ctrl:
                Waveform.AmplitudeZoom /= 2;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => Waveform.Zoom(0.5);

    private void OnZoomOut(object sender, RoutedEventArgs e) => Waveform.Zoom(2);

    private void OnZoomFit(object sender, RoutedEventArgs e) => Waveform.ZoomToFit();

    private void OnWaveTaller(object sender, RoutedEventArgs e) => Waveform.AmplitudeZoom *= 2;

    private void OnWaveShorter(object sender, RoutedEventArgs e) => Waveform.AmplitudeZoom /= 2;

    // ---- Window ----

    private void OnMinimize(object sender, RoutedEventArgs e) => SystemCommands.MinimizeWindow(this);

    private void OnMaximizeRestore(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void OnCloseWindow(object sender, RoutedEventArgs e) => SystemCommands.CloseWindow(this);

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized && !_fullScreen.IsOn;
        MaximizeIcon.Data = (Geometry)FindResource(maximized ? "RestoreIcon" : "MaximizeIcon");
        MaximizeButton.ToolTip = maximized ? "Restore" : "Maximize";
        MaximizeButton.Visibility = _fullScreen.IsOn ? Visibility.Collapsed : Visibility.Visible;
        // A maximized borderless window hangs slightly off-screen; pad it back in.
        WindowRoot.Margin = maximized ? new Thickness(7) : new Thickness(0);
    }

    /// <summary>
    /// Asks before throwing away unsaved edits. Returns false if the user chose to keep the editor open.
    /// Used when the editor closes, and when the main window closes with editors still open.
    /// </summary>
    public bool ConfirmClose()
    {
        if (_closeConfirmed || !HasUnsavedChanges)
        {
            return true;
        }

        Activate();
        var answer = MessageBox.Show(this,
            $"Your edits to \"{Path.GetFileName(_path)}\" haven't been saved. Close without saving?",
            "Audio editor", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        _closeConfirmed = answer == MessageBoxResult.Yes;
        return _closeConfirmed;
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (IsBusy)
        {
            // Stop the work first; the window closes once it has wound down.
            e.Cancel = true;
            _closeWhenIdle = true;
            _busy?.Cancel();
            return;
        }

        if (!ConfirmClose())
        {
            e.Cancel = true;
            _closeWhenIdle = false;
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // Saves the listening volume, which isn't saved on every move of the fader.
        _settings.Save();
        _playheadTimer.Stop();
        _player.Dispose();
        ClosePluginInBackground();
        // The last split's stems can take hundreds of megabytes.
        Editor.Stems.StemCache.Clear();
    }
}
