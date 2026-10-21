using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Lucent.Mac.Audio;
using Lucent.Mac.Editor;
using Lucent.Mac.Windows;
using WinampInspiredVisualizer.Audio;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Effects;
using WinampInspiredVisualizer.Export;
using WinampInspiredVisualizer.Settings;
using WinampInspiredVisualizer.Video;

namespace Lucent.Mac;

/// <summary>
/// A stereo (or mono) audio editor: shows a song's waveform or spectrogram, plays it with meters, selects part
/// of it (or of one channel), cuts, copies and pastes with crossfaded joins, processes it with built-in tools
/// and VST3 plugins, and saves it back or as a new file. Open it with <see cref="AudioTools.OpenEditor"/>, or open an
/// empty one with <see cref="AudioTools.ShowEditor"/> and drop sounds into it.
/// </summary>
/// <remarks>
/// The rest of the window lives in partial files: EditorWindow.Drop.cs (dropped files), EditorWindow.Clipboard.cs (cut, copy, paste),
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
    private readonly EditorSettings _settings = EditorSettings.Load();
    private string _path = string.Empty;
    private EditSession? _session;
    private SourceFileInfo? _info;
    private CancellationTokenSource? _busy;
    private bool _closeWhenIdle;
    private bool _closeConfirmed;
    private bool _askingToClose;
    // Opened with no sound, so a sound that can't be opened leaves it empty rather than closing it.
    private bool _startedEmpty;
    private string? _lastSavedPath;
    private readonly FullScreen _fullScreen;

    /// <summary>For the XAML previewer only.</summary>
    public EditorWindow()
        : this(string.Empty, new AudioPlayer())
    {
    }

    /// <summary>Opens <paramref name="path"/>, or with an empty path an empty editor waiting for a sound to be dropped in.</summary>
    public EditorWindow(string path, AudioPlayer mainPlayer)
    {
        InitializeComponent();
        Chrome.UseLucentFrame(this);
        LoadTitleIcon();
        _path = path;
        _mainPlayer = mainPlayer;
        ShowFileName();
        _fullScreen = new FullScreen(this, FullScreenButton, FullScreenIcon);
        _fullScreen.Changed += (_, _) => UpdateMaximizeButton();

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
        Waveform.PlanSlide = (clip, offset) => _session is { } session && !IsBusy && clip < session.Clips.Count ? session.PlanSlide(clip, offset) : null;
        Waveform.MarkerClicked += (_, index) => GoToMarker(index);
        Waveform.MarkerMoved += (_, move) => MoveMarker(move.Marker, move.Frame);
        Waveform.RightClicked += (_, position) => OnWaveformRightClick(position);
        Overview.NavigateRequested += (_, frame) => Waveform.ScrollTo(frame);
        Overview.ZoomRequested += (_, notches) => Waveform.Zoom(Math.Pow(0.8, notches));
        Waveform.HeardChannelsChanged += (_, _) => OnHeardChannelsChanged();
        Waveform.LengthInTime = _settings.LengthInTime;
        Waveform.LengthUnitsChanged += (_, _) =>
        {
            _settings.LengthInTime = Waveform.LengthInTime;
            _settings.Save();
            ShowStatus(Waveform.LengthInTime
                ? "The selection's length shows in minutes and seconds. Click it to go back to bars and beats."
                : "The selection's length shows in bars and beats. Click it for minutes and seconds.");
        };

        // Like WPF's PreviewKeyDown: the editor's keys work wherever the focus is, unless typing.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        PluginCard.AddHandler(KeyDownEvent, OnPluginBrowserKeyDown, RoutingStrategies.Tunnel);
        PluginSearchBox.AddHandler(KeyDownEvent, OnPluginSearchKeyDown, RoutingStrategies.Tunnel);
        EffectsCard.AddHandler(KeyDownEvent, OnEffectsKeyDown, RoutingStrategies.Tunnel);
        EffectSearchBox.AddHandler(KeyDownEvent, OnEffectSearchKeyDown, RoutingStrategies.Tunnel);

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
        HandToggle.IsChecked = _settings.DragMovesClips;
        Waveform.HandTool = _settings.DragMovesClips;
        InitTransients();
        InitLoops();
        LoopToggle.IsChecked = _settings.Loop;
        HistoryToggle.IsChecked = _settings.ShowHistory;
        HistoryPanel.IsVisible = _settings.ShowHistory;
        // Until it's set here, the editor plays as loud as the main player.
        VolumeSlider.Value = _settings.Volume ?? _mainPlayer.Volume;
        _player.Volume = (float)VolumeSlider.Value;
        VolumeText.Text = $"{VolumeSlider.Value * 100:0}%";
        ShowInserts(_settings.ShowInserts);
        UpdateSnap();
        InitTools();
        SetEditingEnabled(false);
        InitDrop();
        if (path.Length > 0)
        {
            Opened += async (_, _) => await LoadAsync(path);
        }
        else
        {
            ShowEmpty();
        }
    }

    /// <summary>The file being edited (it changes after "Save as new version"); empty while no sound is open.</summary>
    public string FilePath => _path;

    /// <summary>True while no sound is open (or opening): the editor is waiting for one to be dropped in.</summary>
    public bool IsEmpty => _path.Length == 0;

    /// <summary>
    /// True when another sound can open here without losing anything: nothing is open, or what's open has no unsaved
    /// edits and nothing is working on it.
    /// </summary>
    public bool CanOpenAnother => !IsBusy && (IsEmpty || _session is { IsModified: false });

    /// <summary>True while the editor is playing (or previewing).</summary>
    public bool IsPlaying => _player.IsPlaying;

    /// <summary>True when there are edits that haven't been saved.</summary>
    public bool HasUnsavedChanges => _session?.IsModified == true;

    /// <summary>True when closing would lose edits and the user hasn't said that's fine yet.</summary>
    public bool NeedsCloseConfirmation => !_closeConfirmed && HasUnsavedChanges;

    /// <summary>True while the editor is loading, processing or saving.</summary>
    public bool IsBusy => _busy is not null;

    /// <summary>Finishes when the file has been opened (or failed to open); for screenshots.</summary>
    internal Task Loading { get; private set; } = Task.CompletedTask;

    private FrameRange Selection => Waveform.Selection;

    /// <summary>
    /// For screenshots: once the file has loaded, adds a few markers and a split and selects part of it,
    /// in the given view, so the picture shows what editing looks like.
    /// </summary>
    internal void ShowPreviewEdits(EditorView view)
    {
        if (_session is not { } session)
        {
            return;
        }

        var frames = session.Audio.Frames;
        session.AddMarker((int)(frames * 0.08), "Intro");
        session.AddMarker((int)(frames * 0.31), "Verse");
        session.AddMarker((int)(frames * 0.63), "Chorus");
        session.Split(new[] { (int)(frames * 0.47) });
        // The second clip trimmed back a little and slid over the first, so they crossfade, with fades dragged out
        // at the song's start and end (the last one a fast start).
        session.MoveClipEdge(1, start: true, (int)(frames * 0.5));
        session.SlideClip(session.Clips.Count - 1, -(int)(frames * 0.06));
        session.SetClipFade(0, fadeIn: true, (int)(frames * 0.05));
        session.SetClipFade(1, fadeIn: false, (int)(frames * 0.07));
        session.SetFadeShape(1, fadeIn: false, FadeShape.FastStart);
        Waveform.View = view;
        SpectrogramToggle.IsChecked = view == EditorView.Spectrogram;
        ShowAudio();
        Waveform.Selection = new FrameRange((int)(frames * 0.66), (int)(frames * 0.14));
        Waveform.CursorFrame = (int)(frames * 0.66);
        if (view == EditorView.Spectrogram)
        {
            Waveform.RenderSpectrogramNow();
        }

        ShowStatus("The clips overlap, with a crossfade. Right-click it to change its shape, or slide the clip away to take it out.");
    }

    private void LoadTitleIcon()
    {
        try
        {
            var file = Path.Combine(AppContext.BaseDirectory, "Assets", "Lucent-64.png");
            if (File.Exists(file))
            {
                TitleIcon.Source = new Bitmap(file);
            }
        }
        catch (Exception)
        {
            // Just the little icon; the title bar works without it.
        }
    }

    // ---- Opening ----

    private Task LoadAsync(string path) => Loading = LoadCoreAsync(path);

    /// <summary>
    /// Opens <paramref name="path"/> in this window. A sound that's already open stays until the new one has loaded,
    /// and stays if it can't be opened.
    /// </summary>
    private async Task LoadCoreAsync(string path)
    {
        var replacing = _session is not null;
        var oldFormat = _session is { } old ? (old.Audio.SampleRate, old.Audio.ChannelCount) : default;
        var swapped = false;
        _busy = new CancellationTokenSource();
        SetEditingEnabled(false);
        EmptyPanel.IsVisible = false;
        LoadingText.Text = "Opening the sound…";
        LoadingProgress.Value = 0;
        LoadingPanel.IsVisible = true;
        var progress = new Progress<double>(value => LoadingProgress.Value = value);
        string? problem = null;
        var cancelled = false;
        try
        {
            var token = _busy.Token;
            // A video's sound is what's edited; its pictures are cut along with it (EditorWindow.Video.cs).
            VideoEdit? video = null;
            var (audio, info, mixedDown) = await Task.Run(() =>
            {
                if (VideoEdit.IsEditableVideo(path))
                {
                    var loaded = VideoEdit.Load(path, progress, token);
                    video = loaded.Video;
                    return (loaded.Audio, loaded.Info, loaded.MixedDown);
                }

                if (VideoFiles.IsVideo(path))
                {
                    throw new AudioFileException("The editor cuts MP4, M4V and MOV videos. Other kinds of video can't be edited yet.");
                }

                return AudioFileLoader.Load(path, progress, token);
            });
            LoadingText.Text = "Drawing the waveform…";
            var session = await Task.Run(() => new EditSession(audio));
            session.JoinFrames = JoinFrames(audio.SampleRate);
            if (replacing)
            {
                ForgetSound();
            }

            _path = path;
            ShowFileName();
            _session = session;
            _info = info;
            if (video is not null)
            {
                OpenVideo(video);
            }

            if (replacing)
            {
                swapped = true;
                ShowAudio();
            }

            Waveform.Peaks = session.Peaks;
            Overview.Peaks = session.Peaks;
            LoadingPanel.IsVisible = false;
            ShowStatus(video is not null
                ? "Select part of the video, then Delete cuts it out or Trim keeps only it. Saving copies the pictures and sound as they are, without re-encoding."
                : mixedDown
                ? $"This file has {info.Channels} channels; the editor mixes them down to stereo, so saving makes a stereo file."
                : "Drag across the waveform to select part of it, or right-click it for what you can do. Scrolling zooms, Shift+scroll moves along.");
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }
        catch (Exception ex)
        {
            // Core's message names Windows' decoders; on the Mac it's macOS doing the decoding.
            problem = ex is AudioFileException
                ? ex.Message.Replace("Windows can't decode", "macOS can't decode")
                : $"The file couldn't be opened. ({ex.Message})";
        }
        finally
        {
            _busy.Dispose();
            _busy = null;
        }

        if (problem is not null)
        {
            await MessageWindow.ShowAsync(this, problem, "Audio editor");
        }

        if (_closeWhenIdle || ((problem is not null || cancelled) && _session is null && !_startedEmpty))
        {
            Close();
            return;
        }

        LoadingPanel.IsVisible = false;
        if (_session is null)
        {
            // Nothing could be opened into an empty editor: it waits for another sound.
            ShowEmpty();
            return;
        }

        SetEditingEnabled(true);
        UpdateSelectionInfo();
        UpdateHistory();
        if (swapped)
        {
            await FitChainToSoundAsync(oldFormat.SampleRate, oldFormat.ChannelCount);
        }
    }

    /// <summary>
    /// Opens another sound in this window in place of the one that's open, asking first if it has unsaved edits.
    /// Returns false if the user chose to keep their edits, or the editor is busy.
    /// </summary>
    public async Task<bool> OpenAsync(string path)
    {
        if (IsBusy)
        {
            ShowStatus("Wait for the editor to finish, then drop the sound in again.");
            return false;
        }

        if (HasUnsavedChanges)
        {
            Activate();
            var answer = await MessageWindow.ShowAsync(this,
                $"Your edits to \"{Path.GetFileName(_path)}\" haven't been saved. Open \"{Path.GetFileName(path)}\" without saving them?",
                "Audio editor", MessageButtons.YesNo, MessageResult.No);
            if (answer != MessageResult.Yes)
            {
                return false;
            }
        }

        StopPlayback();
        await LoadAsync(path);
        return SamePath(_path, path);
    }

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && b.Length > 0 && string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    /// <summary>Lets go of everything that belonged to the sound that was open, before another one takes its place.</summary>
    private void ForgetSound()
    {
        CloseVideo();
        _stemsWindow?.Close();
        _closeConfirmed = false;
        _lastSavedPath = null;
        ShowFileButton.IsVisible = false;
        Waveform.Selection = default;
        Waveform.SelectedChannel = null;
        Waveform.CursorFrame = 0;
        Waveform.ResetListening();
        // Cleared first, so the new sound starts zoomed to fit.
        Waveform.Peaks = null;
        Overview.Peaks = null;
    }

    /// <summary>A lane's solo or mute changed: what's heard follows straight away, even while playing.</summary>
    private void OnHeardChannelsChanged()
    {
        var heard = Waveform.HeardChannels;
        _player.HeardChannels = heard;
        ShowStatus(heard.All(on => on) ? "Both channels are heard."
            : heard.Any(on => on) ? $"Only the {(heard[0] ? "left" : "right")} channel is heard. Solo and mute change what you hear, not the sound itself."
            : "Both channels are muted. Solo and mute change what you hear, not the sound itself.");
    }

    private void ShowFileName()
    {
        var name = Path.GetFileName(_path);
        FileTitle.Text = IsEmpty ? "Drop a sound here" : name;
        Title = IsEmpty ? "Audio editor - Lucent" : $"{name} - Audio editor - Lucent";
    }

    /// <summary>Shows the editor with no sound in it, waiting for one to be dropped in or chosen.</summary>
    private void ShowEmpty()
    {
        _startedEmpty = true;
        _path = string.Empty;
        ShowFileName();
        LoadingPanel.IsVisible = false;
        EmptyPanel.IsVisible = true;
        SetEditingEnabled(false);
        ShowStatus("Drop a sound here to edit it. Once one is open, drop more onto the waveform to add them where you let go.");
    }

    private void OnChooseSound(object? sender, RoutedEventArgs e) => AudioTools.ChooseFileToEdit(this, _mainPlayer);

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
        BusyPanel.IsVisible = true;
        BusyProgress.Value = 0;
        ShowFileButton.IsVisible = false;
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
                OutOfMemoryException => "There isn't enough memory for that. Try a shorter selection, or quit other apps.",
                WinampInspiredVisualizer.Editor.Plugins.PluginException => ex.Message,
                EditException => ex.Message,
                _ => FileConverter.FriendlyError(ex),
            };
            await MessageWindow.ShowAsync(this, text, "Audio editor");
            return false;
        }
        finally
        {
            _busy.Dispose();
            _busy = null;
            BusyPanel.IsVisible = false;
            SetEditingEnabled(true);
            UpdateHistory();
            ClosePluginIfGone();
            if (_closeWhenIdle)
            {
                Dispatcher.UIThread.Post(Close);
            }
        }
    }

    private void OnCancelBusy(object? sender, RoutedEventArgs e) => _busy?.Cancel();

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
    /// When the selection is in one channel only (Option-drag), only that channel changes, unless
    /// <paramref name="bothChannels"/> (swapping or widening needs both).
    /// </remarks>
    private async Task ApplyAsync(IAudioEffect effect, FrameRange? range = null, FrameRange? selectAfter = null, bool bothChannels = false)
    {
        if (_session is not { } session || VideoRefuses(effect.Description))
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

            ShowStatus(result.Note ?? $"{effect.Description}: done. ⌘Z undoes it.");
        }
    }

    /// <summary>Runs a cut (delete or trim) as one undoable step. On a video, only its own cuts run (<paramref name="video"/>).</summary>
    private async Task EditAsync(string description, Action<EditSession> edit, Func<FrameRange> selectionAfter, bool video = false)
    {
        if (_session is not { } session || (!video && VideoRefuses(description)))
        {
            return;
        }

        if (await RunBusyAsync($"{description}…", async (_, token) => await Task.Run(() => edit(session), token)))
        {
            ShowAudio();
            Waveform.Selection = selectionAfter();
            ShowStatus($"{description}: done. ⌘Z undoes it.");
        }
    }

    /// <summary>Shows the session's current audio after a change.</summary>
    private void ShowAudio()
    {
        if (_session is null)
        {
            return;
        }

        // A video's cut follows the sound's history (an undo brings its pictures back too).
        _videoEdit?.Follow(_session.StateId);
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

    private void OnGain(object? sender, RoutedEventArgs e) => GainPopup.IsOpen = true;

    private void OnGainChanged(object? sender, RangeBaseValueChangedEventArgs e) => UpdateGainText();

    private void UpdateGainText()
    {
        if (GainValue is not null)
        {
            GainValue.Text = $"{GainSlider.Value:+0.0;−0.0;0.0} dB";
        }
    }

    private async void OnApplyGain(object? sender, RoutedEventArgs e)
    {
        GainPopup.IsOpen = false;
        if (Math.Abs(GainSlider.Value) > 0.01)
        {
            await ApplyAsync(new GainEffect(GainSlider.Value));
        }
    }

    private void OnPitch(object? sender, RoutedEventArgs e) => PitchPopup.IsOpen = true;

    private void OnPitchChanged(object? sender, RangeBaseValueChangedEventArgs e) => UpdatePitchText();

    private void OnPitchDown(object? sender, RoutedEventArgs e) => PitchSlider.Value = Math.Round(PitchSlider.Value - 0.1, 1);

    private void OnPitchUp(object? sender, RoutedEventArgs e) => PitchSlider.Value = Math.Round(PitchSlider.Value + 0.1, 1);

    private void OnPitchModeChanged(object? sender, RoutedEventArgs e) => UpdatePitchText();

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

    private async void OnApplyPitch(object? sender, RoutedEventArgs e)
    {
        PitchPopup.IsOpen = false;
        var semitones = Math.Round(PitchSlider.Value, 1);
        if (Math.Abs(semitones) >= 0.05)
        {
            await ApplyAsync(new PitchEffect(semitones, KeepSpeedChoice.IsChecked == true));
        }
    }

    private async void OnFadeIn(object? sender, RoutedEventArgs e) =>
        await ApplyAsync(new FadeEffect(FadeDirection.In), FadeRange(FadeDirection.In));

    private async void OnFadeOut(object? sender, RoutedEventArgs e) =>
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

    private async void OnSilence(object? sender, RoutedEventArgs e) => await ApplyAsync(new SilenceEffect());

    private async void OnReverse(object? sender, RoutedEventArgs e) => await ApplyAsync(new ReverseEffect());

    private async void OnTrim(object? sender, RoutedEventArgs e)
    {
        var selection = Selection;
        if (!selection.IsEmpty && IsVideo)
        {
            await CutVideoAsync(selection, trim: true);
        }
        else if (!selection.IsEmpty)
        {
            await EditAsync("Trim", session => session.TrimTo(selection), () => new FrameRange(0, 0));
        }
    }

    private async void OnDelete(object? sender, RoutedEventArgs e) => await DeleteSelectionAsync();

    private async Task DeleteSelectionAsync()
    {
        var selection = Selection;
        if (selection.IsEmpty)
        {
            return;
        }

        if (IsVideo)
        {
            await CutVideoAsync(selection, trim: false);
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

    private void OnSplit(object? sender, RoutedEventArgs e) => SplitAtCursor();

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
        if (_session is not { } session || IsBusy || VideoRefuses("Splitting"))
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
        ShowStatus(Waveform.HandTool
            ? $"Split into {session.Clips.Count} clips. Drag a clip to move it, or onto the next one to crossfade them; ⇧drag or drag in the ruler to select. ⌘Z undoes it."
            : $"Split into {session.Clips.Count} clips. Drag a clip's tab to slide it along (Option+drag to move it past the others), or right-click it for more. ⌘Z undoes it.");
    }

    private async Task MoveClipAsync(int clip, int toIndex)
    {
        var moved = default(FrameRange);
        await EditAsync("Move clip", session => moved = session.MoveClip(clip, toIndex), () => moved);
    }

    private async Task SlideClipAsync(int clip, int offset)
    {
        var plan = _session is { } current && clip < current.Clips.Count ? current.PlanSlide(clip, offset) : null;
        var moved = default(FrameRange);
        await EditAsync("Move clip", session => moved = session.SlideClip(clip, offset), () => moved);
        if (plan is { OverlapBefore: > 0 } or { OverlapAfter: > 0 })
        {
            ShowStatus("The clips overlap, with a crossfade. Right-click it to change its shape, or slide the clip away to take it out.");
        }
    }

    /// <summary>The fade shapes, with a little drawing of each, for a fade or crossfade that was right-clicked.</summary>
    private void ShowFadeShapeMenu(FadeSpot spot)
    {
        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        menu.Items.Add(MenuEntry(spot.Crossfade ? "Crossfade shape" : spot.FadeIn ? "Fade in shape" : "Fade out shape", enabled: false));
        foreach (var shape in FadeShapes.All)
        {
            var item = MenuEntry(FadeShapes.Title(shape));
            item.ToggleType = MenuItemToggleType.CheckBox;
            item.IsChecked = shape == spot.Shape;
            item.Icon = FadeShapeIcon(shape, spot.FadeIn, spot.Crossfade);
            item.Click += async (_, _) => await SetFadeShapeAsync(spot, shape);
            menu.Items.Add(item);
        }

        if (!spot.Crossfade)
        {
            menu.Items.Add(new Separator());
            var remove = MenuEntry(spot.FadeIn ? "Remove fade in" : "Remove fade out");
            remove.Click += async (_, _) => await SetClipFadeAsync(spot.Clip, spot.FadeIn, 0);
            menu.Items.Add(remove);
        }

        menu.Open(Waveform);
    }

    /// <summary>A small drawing of a fade's curve (both curves, for a crossfade).</summary>
    private Avalonia.Controls.Shapes.Path FadeShapeIcon(FadeShape shape, bool rising, bool crossfade)
    {
        var geometry = new StreamGeometry();
        using (var g = geometry.Open())
        {
            foreach (var up in crossfade ? new[] { false, true } : new[] { rising })
            {
                const int steps = 12;
                for (var i = 0; i <= steps; i++)
                {
                    var point = new Point(1 + 14.0 * i / steps, 13 - 12 * FadeShapes.Gain(i / (double)steps, up, shape));
                    if (i == 0)
                    {
                        g.BeginFigure(point, false);
                    }
                    else
                    {
                        g.LineTo(point);
                    }
                }

                g.EndFigure(false);
            }
        }

        return new Avalonia.Controls.Shapes.Path
        {
            Data = geometry,
            Width = 16,
            Height = 14,
            Stroke = ThemeBrush("TextBrush"),
            StrokeThickness = 1.3,
            StrokeJoin = PenLineJoin.Round,
        };
    }

    private async Task SetFadeShapeAsync(FadeSpot spot, FadeShape shape)
    {
        if (_session is not { } session || spot.Clip >= session.Clips.Count)
        {
            return;
        }

        var selection = Selection;
        var what = spot.Crossfade ? "Crossfade shape" : "Fade shape";
        await EditAsync(what, s => s.SetFadeShape(spot.Clip, spot.FadeIn, shape), () => selection);
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
        if (_session is not { } session || IsBusy || VideoRefuses("Moving a split"))
        {
            return;
        }

        session.MoveSplit(split, frame);
        Waveform.Splits = session.Splits;
        Waveform.ClipEdgeDetails = session.ClipEdgeDetails;
        UpdateHistory();
        ShowStatus("Moved the edge between the clips. ⌘Z undoes it.");
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
    /// The right-click (or Control-click) menu: what to do to the selection when the click is inside it, otherwise
    /// to the clip under the mouse (or the whole song before it's split), plus splitting, moving and silence.
    /// </summary>
    private void OnWaveformRightClick(Point position)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        if (Waveform.MarkerAt(position) is { } marker)
        {
            ShowMarkerMenu(marker);
            return;
        }

        // On a fade or crossfade: its shapes, as in REAPER.
        if (Waveform.FadeUnder(position) is { } fade)
        {
            ShowFadeShapeMenu(fade);
            return;
        }

        var frame = Waveform.FrameAt(position.X);
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

        var menu = new ContextMenu { Placement = PlacementMode.Pointer };
        MenuItem Add(ItemsControl parent, string header, Func<Task> action, string? gesture = null, bool enabled = true)
        {
            var item = MenuEntry(header, gesture, enabled);
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
            Add(menu, "Cut", CutAsync, "⌘X");
            Add(menu, "Copy", () => Run(Copy), "⌘C");
        }

        Add(menu, onSelection ? "Paste over selection" : "Paste here", PasteAsync, "⌘V", enabled: HasClip);
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
            Add(menu, "Delete selection", DeleteSelectionAsync, "⌫");
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
            var parent = MenuEntry(header);
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
        menu.Open(Waveform);
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
        ShowStatus("Joined. ⌘Z undoes it.");
    }

    // ---- Menus ----

    /// <summary>A menu that opens above its button, like the Windows editor's.</summary>
    private static ContextMenu MenuAbove() =>
        new() { Placement = PlacementMode.TopEdgeAlignedLeft, VerticalOffset = -6 };

    /// <summary>
    /// A menu item, with its shortcut shown on the right like WPF's InputGestureText (Avalonia only shows
    /// shortcuts it can parse as key gestures, and these include plain letters and brackets).
    /// </summary>
    private static MenuItem MenuEntry(string header, string? gesture = null, bool enabled = true, string? tip = null)
    {
        var item = new MenuItem { IsEnabled = enabled };
        if (gesture is null)
        {
            item.Header = header;
        }
        else
        {
            var shortcut = new TextBlock
            {
                Text = gesture,
                Margin = new Thickness(28, 0, 0, 0),
                Opacity = 0.6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            DockPanel.SetDock(shortcut, Dock.Right);
            var row = new DockPanel();
            row.Children.Add(shortcut);
            row.Children.Add(new TextBlock { Text = header, VerticalAlignment = VerticalAlignment.Center });
            item.Header = row;
        }

        if (tip is not null)
        {
            ToolTip.SetTip(item, tip);
        }

        return item;
    }

    // ---- Undo ----

    private async void OnUndo(object? sender, RoutedEventArgs e) => await UndoRedoAsync(undo: true);

    private async void OnRedo(object? sender, RoutedEventArgs e) => await UndoRedoAsync(undo: false);

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
        ToolTip.SetTip(UndoButton, session?.UndoDescription is { } undo ? $"Undo {undo} (⌘Z)" : "Undo (⌘Z)");
        ToolTip.SetTip(RedoButton, session?.RedoDescription is { } redo ? $"Redo {redo} (⇧⌘Z)" : "Redo (⇧⌘Z)");
        var name = Path.GetFileName(_path);
        FileTitle.Text = HasUnsavedChanges ? $"{name}  •  edited" : name;
        UpdateHistoryList();
    }

    // ---- Playback ----

    private void OnPlay(object? sender, RoutedEventArgs e) => TogglePlay();

    private void OnPlaySelection(object? sender, RoutedEventArgs e)
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
            AudioTools.NotifyEditorPlayingChanged();
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
        PlayIcon.Data = ThemeIcon("StopIcon");
        PlayIcon.Margin = new Thickness(0);
        UpdatePlayhead();
    }

    private Geometry? ThemeIcon(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var icon) ? icon as Geometry : null;

    /// <summary>The listening volume: what's heard while editing, never the audio itself.</summary>
    private void OnVolumeChanged(object? sender, RangeBaseValueChangedEventArgs e)
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
        AudioTools.NotifyEditorPlayingChanged();
        _playheadTimer.Stop();
        Waveform.PlayheadFrame = null;
        PlayIcon.Data = ThemeIcon("PlayIcon");
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
                             (which.Length == 0 && _session.Audio.ChannelCount > 1 ? " ⌥-drag selects in one channel." : string.Empty);
        }

        LengthText.Text = $"Length {Format(_session.Audio.Frames)}  ·  {rate / 1000.0:0.#} kHz  ·  " +
                          (_session.Audio.ChannelCount == 1 ? "mono" : "stereo");
        var hasSelection = !selection.IsEmpty && !IsBusy;
        SaveLoopButton.IsEnabled = hasSelection;
        RefreshMusic();
        TrimButton.IsEnabled = hasSelection;
        DeleteButton.IsEnabled = hasSelection;
        PlaySelectionButton.IsEnabled = hasSelection;
    }

    private string Format(int frame) => WaveformView.FormatTime(frame / (double)(_session?.Audio.SampleRate ?? 44100));

    // ---- Saving ----

    private async void OnSave(object? sender, RoutedEventArgs e) => await SaveAsync();

    private async void OnSaveAs(object? sender, RoutedEventArgs e) => await SaveAsNewVersionAsync(null);

    /// <summary>Replaces the original file, after asking.</summary>
    private async Task<bool> SaveAsync()
    {
        if (_session is null || _info is not { } info)
        {
            return false;
        }

        if (IsVideo)
        {
            var playingVideo = _mainPlayer.IsCurrentFile(_path) ? "\n\nIt's open in the player, which will pause for a moment while it's replaced." : string.Empty;
            if (await MessageWindow.ShowAsync(this,
                    $"Replace \"{Path.GetFileName(_path)}\" with your cut version?\n\nNothing is re-encoded, so no quality is lost, but what was cut out can't be brought back afterwards.{playingVideo}",
                    "Save", MessageButtons.YesNo, MessageResult.No) != MessageResult.Yes)
            {
                return false;
            }

            return await SaveVideoAsync(_path, replaceExisting: true);
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
        var answer = await MessageWindow.ShowAsync(this,
            $"Replace \"{name}\" with your edited version?\n\nThe original can't be brought back afterwards.{warning}{playing}",
            "Save", MessageButtons.YesNo, MessageResult.No);
        if (answer != MessageResult.Yes)
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

        if (IsVideo)
        {
            return await SaveVideoAsNewAsync();
        }

        var format = ExportOptions.WritableFormat(_path) ?? ExportFormat.Flac;
        var choice = await ExportWindow.ShowSaveDialogAsync(this, _path, OptionsLike(info, format), note);
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
            if (await MessageWindow.ShowAsync(this, question, "Save as new version", MessageButtons.YesNo, MessageResult.No)
                != MessageResult.Yes)
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

            // The last step is instant: swap the file into place. If the main player has the file open, it
            // lets go for a moment (so it doesn't go on reading the old copy) and then carries on from the same spot.
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
        ShowFileButton.IsVisible = true;
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

    private void OnShowFile(object? sender, RoutedEventArgs e)
    {
        if (_lastSavedPath is not null)
        {
            try
            {
                // Opens a Finder window with the file selected.
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-R", _lastSavedPath }, UseShellExecute = false });
            }
            catch (Exception)
            {
                // Nothing useful to add; Finder usually opens.
            }
        }
    }

    // ---- Keys ----

    private async void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Typing in a box, using the sliders in the tool cards, or choosing a plugin keeps its keys.
        if (e.Source is TextBox || ToolPopups.Any(popup => popup.IsOpen) || PluginPopup.IsOpen)
        {
            return;
        }

        if (FullScreen.IsToggleKey(e))
        {
            _fullScreen.Toggle();
            e.Handled = true;
            return;
        }

        // ⌘ on the Mac where Windows uses Ctrl.
        var command = e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
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
            case Key.A when command:
                Waveform.SelectAll();
                break;
            case Key.Z when command && shift:
            case Key.Y when command:
                e.Handled = true;
                await UndoRedoAsync(undo: false);
                return;
            case Key.Z when command:
                e.Handled = true;
                await UndoRedoAsync(undo: true);
                return;
            case Key.S when command:
                e.Handled = true;
                await SaveAsync();
                return;
            case Key.S:
                SplitAtCursor();
                break;
            case Key.C when command:
                Copy();
                break;
            case Key.X when command:
                e.Handled = true;
                await CutAsync();
                return;
            case Key.V when command:
                e.Handled = true;
                await PasteAsync();
                return;
            case Key.W when command:
                // ⌘W closes the window, as everywhere on the Mac.
                Close();
                break;
            case Key.M when !command:
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
            case Key.E when !command:
                ShowEffects();
                break;
            case Key.G when !command:
                SetHandTool(HandToggle.IsChecked != true);
                break;
            case Key.B when !command:
                if (_chain.Count > 0)
                {
                    SetChainBypassed(!_chainBypassed);
                }

                break;
            case Key.H when !command:
                ShowHistoryPanel(!HistoryPanel.IsVisible);
                break;
            case Key.I when !command:
                ShowInserts(!PluginBar.IsVisible);
                break;
            case Key.V:
                SpectrogramToggle.IsChecked = SpectrogramToggle.IsChecked != true;
                SetView(SpectrogramToggle.IsChecked == true ? EditorView.Spectrogram : EditorView.Waveform);
                break;
            case Key.F3:
                GoToNextHighlight(forward: !shift);
                break;
            case Key.Tab when _settings.TabToTransient:
                // ⌥ stretches the selection: ⌘Tab belongs to macOS.
                e.Handled = true;
                await TabToTransientAsync(forward: !shift, extend: e.KeyModifiers.HasFlag(KeyModifiers.Alt));
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
            case Key.Up when command:
                Waveform.AmplitudeZoom *= 2;
                break;
            case Key.Down when command:
                Waveform.AmplitudeZoom /= 2;
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void OnZoomIn(object? sender, RoutedEventArgs e) => Waveform.Zoom(0.5);

    private void OnZoomOut(object? sender, RoutedEventArgs e) => Waveform.Zoom(2);

    private void OnZoomFit(object? sender, RoutedEventArgs e) => Waveform.ZoomToFit();

    private void OnWaveTaller(object? sender, RoutedEventArgs e) => Waveform.AmplitudeZoom *= 2;

    private void OnWaveShorter(object? sender, RoutedEventArgs e) => Waveform.AmplitudeZoom /= 2;

    // ---- Window ----

    private void OnMinimize(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeRestore(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object? sender, RoutedEventArgs e) => Close();

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && MaximizeIcon is not null)
        {
            UpdateMaximizeButton();
        }
    }

    private void UpdateMaximizeButton()
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeIcon.Data = ThemeIcon(maximized ? "RestoreIcon" : "MaximizeIcon");
        ToolTip.SetTip(MaximizeButton, maximized ? "Restore" : "Maximize");
        // Maximize means nothing while the window already fills the screen.
        MaximizeButton.IsVisible = _fullScreen is not { IsOn: true };
    }

    /// <summary>
    /// Asks before throwing away unsaved edits. Returns false if the user chose to keep the editor open.
    /// Used when the editor closes, and when the main window closes with editors still open.
    /// </summary>
    public async Task<bool> ConfirmCloseAsync()
    {
        if (!NeedsCloseConfirmation)
        {
            return true;
        }

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        var answer = await MessageWindow.ShowAsync(this,
            $"Your edits to \"{Path.GetFileName(_path)}\" haven't been saved. Close without saving?",
            "Audio editor", MessageButtons.YesNo, MessageResult.No);
        _closeConfirmed = answer == MessageResult.Yes;
        return _closeConfirmed;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel)
        {
            return;
        }

        if (IsBusy)
        {
            // Stop the work first; the window closes once it has wound down.
            e.Cancel = true;
            _closeWhenIdle = true;
            _busy?.Cancel();
            return;
        }

        if (NeedsCloseConfirmation)
        {
            // Avalonia can't wait for an answer here, so the close is held back while the question is asked.
            e.Cancel = true;
            if (_askingToClose)
            {
                return;
            }

            _askingToClose = true;
            Dispatcher.UIThread.Post(async () =>
            {
                var close = await ConfirmCloseAsync();
                _askingToClose = false;
                if (close)
                {
                    Close();
                }
                else
                {
                    _closeWhenIdle = false;
                    AudioTools.CancelQuit();
                }
            });
        }
    }

    /// <summary>
    /// Remembers where the window is, from the player's corner, so the next editor opens docked in the same place,
    /// and whether it was open when Lucent quit, so it opens again with the player.
    /// </summary>
    private void RememberPlacement()
    {
        if (AudioTools.MainWindow is not { } player)
        {
            return;
        }

        _settings.OpenAtStart = player.IsShuttingDown;
        if (WindowState == WindowState.Normal && !_fullScreen.IsOn && player.WindowState == WindowState.Normal && Bounds.Width > 0)
        {
            _settings.WindowOffsetX = this.Left() - player.Left();
            _settings.WindowOffsetY = this.Top() - player.Top();
            _settings.WindowWidth = this.ActualWidth();
            _settings.WindowHeight = this.ActualHeight();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        RememberPlacement();
        // Saves the listening volume, which isn't saved on every move of the fader.
        _settings.Save();
        _playheadTimer.Stop();
        _player.Dispose();
        CloseVideo();
        ClosePluginInBackground();
        // The last split's stems can take hundreds of megabytes.
        _stemsWindow?.Close();
        WinampInspiredVisualizer.Editor.Stems.StemCache.Clear();
    }
}
