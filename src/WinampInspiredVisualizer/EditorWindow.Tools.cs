using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Dsp;
using WinampInspiredVisualizer.Editor.Effects;
using WinampInspiredVisualizer.Settings;

namespace WinampInspiredVisualizer;

/// <summary>
/// The editor's own tools: snapping, the view and loop switches, tempo, the equalizer, the compressor and
/// limiter (both heard live while they're set up), the repair tools, and the channel tools.
/// </summary>
public partial class EditorWindow
{
    /// <summary>The equalizer's frequency slider runs from 20 Hz to 20 kHz on a log scale over 0 to 1000.</summary>
    private const double FrequencySteps = 1000;
    private const double MinQ = 0.1;
    private const double MaxQ = 30;

    /// <summary>The tool being heard while it's set up (the equalizer, compressor or limiter), or null.</summary>
    private IStreamProcessor? _toolPreview;
    private Button? _toolPreviewButton;
    private ParametricEq? _eqLive;
    private Compressor? _compressorLive;
    private Limiter? _limiterLive;
    private bool _showingBand;
    private NoiseProfile? _noiseProfile;

    /// <summary>Every card that pops up over the editor; while one is open, its sliders keep the keyboard.</summary>
    private Popup[] ToolPopups => new[]
    {
        GainPopup, PitchPopup, TempoPopup, EqPopup, DynamicsPopup, WidthPopup, NoisePopup, ClickPopup, AnalysisPopup, StemsPopup,
        TransientPopup, EffectsPopup,
    };

    private void InitTools()
    {
        UpdateTempoText();

        EqCurve.Bands = _settings.EqBands;
        EqCurve.BandsChanged += (_, _) =>
        {
            ShowEqBand();
            EqChanged();
        };
        EqCurve.SelectedBandChanged += (_, _) => ShowEqBand();
        EqCurve.SelectedBand = Math.Min(2, _settings.EqBands.Count - 1);

        BuildDynamicsControls();
        CompressorChoice.IsChecked = !_settings.DynamicsIsLimiter;
        LimiterChoice.IsChecked = _settings.DynamicsIsLimiter;
        ShowDynamicsMode();

        WidthSlider.Value = Math.Round(_settings.StereoWidth * 100);
        UpdateWidthText();
        NoiseAmountSlider.Value = _settings.NoiseReductionDb;
        NoiseSensitivitySlider.Value = _settings.NoiseSensitivityDb;
        UpdateNoiseTexts();
        ClickSlider.Value = _settings.ClickSensitivity;
        UpdateClickText();
    }

    // ---- Snap, view and loop ----

    /// <summary>Moves a frame to the nearest zero crossing when snapping is on, so cuts don't click.</summary>
    private int SnapFrame(int frame) =>
        _settings.SnapToZeroCrossings && _session is { } session ? ZeroCrossing.Nearest(session.Audio, frame) : frame;

    private void UpdateSnap() => Waveform.Snap = _settings.SnapToZeroCrossings ? SnapFrame : null;

    private void OnSnapToggle(object sender, RoutedEventArgs e)
    {
        _settings.SnapToZeroCrossings = SnapToggle.IsChecked == true;
        _settings.Save();
        UpdateSnap();
        ShowStatus(_settings.SnapToZeroCrossings
            ? "Selections and splits now snap to the nearest zero crossing, so edits don't click. Hold Ctrl to place them exactly."
            : "Snapping is off: selections and splits go exactly where you put them.");
    }

    private void OnHandToggle(object sender, RoutedEventArgs e) => SetHandTool(HandToggle.IsChecked == true);

    /// <summary>
    /// Turns the hand tool on (once the sound is split, dragging a clip's waveform moves the clip) or off (dragging
    /// selects). It stays as it was left, next time.
    /// </summary>
    private void SetHandTool(bool on)
    {
        HandToggle.IsChecked = on;
        Waveform.HandTool = on;
        _settings.DragMovesClips = on;
        _settings.Save();
        ShowStatus(on
            ? "Move clips on: once the sound is split, drag a clip to move it, or onto the next one to crossfade them. Shift+drag, or drag in the ruler, to select. Press G to turn it off."
            : "Move clips off: dragging selects again. Clips can still be moved by their tabs.");
    }

    private void OnViewToggle(object sender, RoutedEventArgs e) =>
        SetView(SpectrogramToggle.IsChecked == true ? EditorView.Spectrogram : EditorView.Waveform);

    private void SetView(EditorView view)
    {
        _settings.View = view;
        _settings.Save();
        Waveform.View = view;
        ShowStatus(view == EditorView.Spectrogram
            ? "Spectrogram: low notes at the bottom, high at the top, brighter is louder. Hum shows as a line along the bottom, clicks as thin upright lines. V switches back."
            : "Waveform view. V switches to the spectrogram.");
    }

    private void OnLoopToggle(object sender, RoutedEventArgs e) => SetLoop(LoopToggle.IsChecked == true);

    private void SetLoop(bool loop)
    {
        _settings.Loop = loop;
        _settings.Save();
        ShowStatus(loop
            ? "Loop is on: playing the selection goes round and round" + (_player.IsPlaying ? ", from the next time you play it." : ".")
            : "Loop is off.");
    }

    // ---- Live previews ----

    /// <summary>Plays the selection (or from the cursor) through a tool, or stops if it's already playing through it.</summary>
    private void PreviewThrough(IStreamProcessor processor, Button button)
    {
        if (_session is not { } session || IsBusy)
        {
            return;
        }

        var wasThis = _toolPreviewButton == button && _player.IsPlaying;
        StopPlayback();
        if (wasThis)
        {
            return;
        }

        var selection = Selection;
        var (from, to) = selection.IsEmpty
            ? (Waveform.CursorFrame >= session.Audio.Frames - 1 ? 0 : Waveform.CursorFrame, session.Audio.Frames)
            : (selection.Start, selection.End);
        _toolPreview = processor;
        _toolPreviewButton = button;
        PlayFrom(from, to, source => new StreamPreviewProvider(source, processor));
        if (_player.IsPlaying)
        {
            button.Content = "Stop preview";
        }
        else
        {
            ShowToolPreviewStopped();
        }
    }

    private void ShowToolPreviewStopped()
    {
        if (_toolPreviewButton is { } button)
        {
            button.Content = "Preview";
        }

        _toolPreview = null;
        _toolPreviewButton = null;
        _eqLive = null;
        _compressorLive = null;
        _limiterLive = null;
        if (ReductionText is not null)
        {
            ReductionText.Text = string.Empty;
        }
    }

    /// <summary>While the compressor or limiter is heard, shows how much it's turning the music down.</summary>
    private void ShowToolReduction()
    {
        var reduction = _toolPreview switch
        {
            Compressor compressor => compressor.ReductionDb,
            Limiter limiter => limiter.ReductionDb,
            _ => (double?)null,
        };
        if (reduction is { } db)
        {
            ReductionText.Text = db < 0.05 ? "Not reducing" : $"Reducing by {db:0.0} dB";
        }
    }

    /// <summary>Closing the equalizer or dynamics card stops its preview and remembers its settings.</summary>
    private void OnToolPopupClosed(object? sender, EventArgs e)
    {
        if (_toolPreview is not null)
        {
            StopPlayback();
        }

        _settings.Save();
    }

    private (int Channels, int SampleRate) AudioFormat() =>
        _session is { } session ? (session.Audio.ChannelCount, session.Audio.SampleRate) : (2, 44100);

    // ---- Tempo ----

    private void OnTempo(object sender, RoutedEventArgs e) => TempoPopup.IsOpen = true;

    private void OnTempoChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateTempoText();

    private void OnTempoModeChanged(object sender, RoutedEventArgs e) => UpdateTempoText();

    private void UpdateTempoText()
    {
        if (TempoValue is null || TempoHint is null)
        {
            return;
        }

        var speed = TempoSlider.Value / 100;
        TempoValue.Text = $"{TempoSlider.Value:0}% speed";
        var length = Math.Abs(speed - 1) < 0.005 ? "the same length" : $"{100 / speed:0}% as long";
        TempoHint.Text = TempoKeepPitch.IsChecked == true
            ? $"The pitch stays the same; it becomes {length}. Small changes sound the most natural."
            : $"Like a record played faster or slower: it becomes {length} and the pitch moves too " +
              $"({12 * Math.Log2(Math.Max(speed, 0.01)):+0.0;−0.0;0} semitones).";
    }

    private async void OnApplyTempo(object sender, RoutedEventArgs e)
    {
        TempoPopup.IsOpen = false;
        var speed = TempoSlider.Value / 100;
        if (Math.Abs(speed - 1) >= 0.005)
        {
            await ApplyAsync(new TempoEffect(speed, TempoKeepPitch.IsChecked == true), bothChannels: true);
        }
    }

    // ---- Equalizer ----

    private async void OnEq(object sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var (_, rate) = AudioFormat();
        EqCurve.SampleRate = rate;
        EqCurve.Bands = _settings.EqBands;
        ShowEqBand();
        EqPopup.IsOpen = true;

        // The music's spectrum, drawn behind the curve, so it's clear what each band is touching.
        var audio = session.Audio;
        var range = Selection;
        EqCurve.Spectrum = await Task.Run(() => AudioAnalysis.AverageSpectrum(audio, range));
    }

    private EqBand? SelectedEqBand =>
        EqCurve.SelectedBand >= 0 && EqCurve.SelectedBand < _settings.EqBands.Count ? _settings.EqBands[EqCurve.SelectedBand] : null;

    /// <summary>Shows the chosen band's settings in the controls under the curve.</summary>
    private void ShowEqBand()
    {
        if (SelectedEqBand is not { } band || EqBandTitle is null)
        {
            return;
        }

        _showingBand = true;
        try
        {
            EqBandTitle.Text = $"Band {EqCurve.SelectedBand + 1}";
            EqBandOn.IsChecked = band.Enabled;
            foreach (var shape in EqShapes.Children.OfType<RadioButton>())
            {
                shape.IsChecked = shape.Tag as string == band.Shape.ToString();
            }

            EqFrequencySlider.Value = FrequencySteps * Math.Log(band.Frequency / EqCurveView.MinHz) / Math.Log(EqCurveView.MaxHz / EqCurveView.MinHz);
            EqGainSlider.Value = band.GainDb;
            EqGainSlider.IsEnabled = band.HasGain;
            EqQSlider.Value = FrequencySteps * Math.Log(band.Q / MinQ) / Math.Log(MaxQ / MinQ);
            ShowEqValues(band);
        }
        finally
        {
            _showingBand = false;
        }
    }

    private void ShowEqValues(EqBand band)
    {
        EqFrequencyValue.Text = band.Frequency >= 1000 ? $"{band.Frequency / 1000:0.0#} kHz" : $"{band.Frequency:0} Hz";
        EqGainValue.Text = band.HasGain ? $"{band.GainDb:+0.0;−0.0;0.0} dB" : "–";
        EqQValue.Text = $"{band.Q:0.0#}";
    }

    private void OnEqBandSlider(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_showingBand || SelectedEqBand is not { } band || EqFrequencyValue is null)
        {
            return;
        }

        band.Frequency = Math.Round(EqCurveView.MinHz * Math.Pow(EqCurveView.MaxHz / EqCurveView.MinHz, EqFrequencySlider.Value / FrequencySteps));
        if (band.HasGain)
        {
            band.GainDb = EqGainSlider.Value;
        }

        band.Q = Math.Round(MinQ * Math.Pow(MaxQ / MinQ, EqQSlider.Value / FrequencySteps), 2);
        if (!band.Enabled)
        {
            band.Enabled = true;
            EqBandOn.IsChecked = true;
        }

        ShowEqValues(band);
        EqCurve.Refresh();
        EqChanged();
    }

    private void OnEqShape(object sender, RoutedEventArgs e)
    {
        if (SelectedEqBand is not { } band || sender is not RadioButton { Tag: string tag } || !Enum.TryParse<FilterShape>(tag, out var shape))
        {
            return;
        }

        band.Shape = shape;
        band.Enabled = true;
        ShowEqBand();
        EqCurve.Refresh();
        EqChanged();
    }

    private void OnEqBandControl(object sender, RoutedEventArgs e)
    {
        if (SelectedEqBand is { } band)
        {
            band.Enabled = EqBandOn.IsChecked == true;
            EqCurve.Refresh();
            EqChanged();
        }
    }

    private void OnEqReset(object sender, RoutedEventArgs e)
    {
        _settings.EqBands = EqBand.Defaults();
        EqCurve.Bands = _settings.EqBands;
        ShowEqBand();
        EqChanged();
    }

    /// <summary>Passes a change to the equalizer being heard, so it's heard at once.</summary>
    private void EqChanged() => _eqLive?.Update(_settings.EqBands);

    private void OnEqPreview(object sender, RoutedEventArgs e)
    {
        var (channels, rate) = AudioFormat();
        var eq = new ParametricEq(_settings.EqBands, channels, rate);
        PreviewThrough(eq, EqPreviewButton);
        _eqLive = _toolPreview == eq ? eq : null;
    }

    private async void OnApplyEq(object sender, RoutedEventArgs e)
    {
        EqPopup.IsOpen = false;
        if (!_settings.EqBands.Any(band => band.IsActive))
        {
            ShowStatus("Every band is flat or off, so the equalizer wouldn't change anything.");
            return;
        }

        await ApplyAsync(new EqualizerEffect(_settings.EqBands));
    }

    // ---- Compressor and limiter ----

    private void OnDynamics(object sender, RoutedEventArgs e) => DynamicsPopup.IsOpen = true;

    private void OnDynamicsMode(object sender, RoutedEventArgs e)
    {
        var limiter = LimiterChoice.IsChecked == true;
        if (limiter != _settings.DynamicsIsLimiter && _toolPreview is not null)
        {
            StopPlayback();
        }

        _settings.DynamicsIsLimiter = limiter;
        ShowDynamicsMode();
    }

    private void ShowDynamicsMode()
    {
        CompressorPanel.Visibility = _settings.DynamicsIsLimiter ? Visibility.Collapsed : Visibility.Visible;
        LimiterPanel.Visibility = _settings.DynamicsIsLimiter ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Fills the compressor and limiter cards with a slider for each setting.</summary>
    private void BuildDynamicsControls()
    {
        var c = _settings.Compressor;
        AddDynamicsSlider(CompressorPanel, "Threshold", -60, 0, 0.5, c.ThresholdDb, v => c.ThresholdDb = v, v => $"{v:0.0} dB",
            "Louder than this gets turned down");
        AddDynamicsSlider(CompressorPanel, "Ratio", 1, 20, 0.1, c.Ratio, v => c.Ratio = v, v => $"{v:0.0}:1",
            "How hard: at 4:1, 4 dB over the threshold comes out 1 dB over");
        AddDynamicsSlider(CompressorPanel, "Attack", 0.1, 100, 0.1, c.AttackMs, v => c.AttackMs = v, v => $"{v:0.0} ms",
            "How quickly it reacts. Slower lets the punch of drums through");
        AddDynamicsSlider(CompressorPanel, "Release", 10, 1000, 5, c.ReleaseMs, v => c.ReleaseMs = v, v => $"{v:0} ms",
            "How quickly it lets go afterwards");
        AddDynamicsSlider(CompressorPanel, "Knee", 0, 24, 0.5, c.KneeDb, v => c.KneeDb = v, v => $"{v:0.0} dB",
            "Wider eases in more gently around the threshold");
        AddDynamicsSlider(CompressorPanel, "Make-up gain", 0, 24, 0.5, c.MakeupDb, v => c.MakeupDb = v, v => $"+{v:0.0} dB",
            "Turns the whole thing back up afterwards");

        var l = _settings.Limiter;
        AddDynamicsSlider(LimiterPanel, "Louder by", 0, 24, 0.5, l.InputGainDb, v => l.InputGainDb = v, v => $"+{v:0.0} dB",
            "Turns it up into the limiter, which keeps the peaks down");
        AddDynamicsSlider(LimiterPanel, "Ceiling", -12, 0, 0.1, l.CeilingDb, v => l.CeilingDb = v, v => $"{v:0.0} dB",
            "Nothing goes above this. −1 dB leaves room for MP3 and streaming");
        AddDynamicsSlider(LimiterPanel, "Release", 1, 1000, 1, l.ReleaseMs, v => l.ReleaseMs = v, v => $"{v:0} ms",
            "Faster is louder, slower is cleaner");
        var truePeak = new CheckBox
        {
            Style = (Style)FindResource("SwitchToggle"),
            Content = "Catch peaks between samples (true peak)",
            IsChecked = l.TruePeak,
            Margin = new Thickness(0, 10, 0, 0),
            ToolTip = "Stops the peaks that only appear once the audio is converted back to sound, or to MP3",
        };
        truePeak.Click += (_, _) =>
        {
            l.TruePeak = truePeak.IsChecked == true;
            DynamicsChanged();
        };
        LimiterPanel.Children.Add(truePeak);
    }

    private void AddDynamicsSlider(Panel panel, string label, double min, double max, double step, double value,
        Action<double> set, Func<double, string> format, string hint)
    {
        var shown = new TextBlock { Style = (Style)FindResource("CardValue"), Text = format(value) };
        DockPanel.SetDock(shown, Dock.Right);
        var title = new TextBlock { Style = (Style)FindResource("OptionLabel"), Margin = new Thickness(0), Text = label, ToolTip = hint };
        var header = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        header.Children.Add(shown);
        header.Children.Add(title);
        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            TickFrequency = step,
            IsSnapToTickEnabled = true,
            Value = value,
            ToolTip = hint,
        };
        slider.ValueChanged += (_, _) =>
        {
            set(slider.Value);
            shown.Text = format(slider.Value);
            DynamicsChanged();
        };
        panel.Children.Add(header);
        panel.Children.Add(slider);
    }

    private void DynamicsChanged()
    {
        _compressorLive?.Update(_settings.Compressor);
        _limiterLive?.Update(_settings.Limiter);
    }

    private void OnDynamicsPreview(object sender, RoutedEventArgs e)
    {
        var (channels, rate) = AudioFormat();
        if (_settings.DynamicsIsLimiter)
        {
            var limiter = new Limiter(_settings.Limiter, channels, rate);
            PreviewThrough(limiter, DynamicsPreviewButton);
            _limiterLive = _toolPreview == limiter ? limiter : null;
        }
        else
        {
            var compressor = new Compressor(_settings.Compressor, rate);
            PreviewThrough(compressor, DynamicsPreviewButton);
            _compressorLive = _toolPreview == compressor ? compressor : null;
        }
    }

    private async void OnApplyDynamics(object sender, RoutedEventArgs e)
    {
        DynamicsPopup.IsOpen = false;
        await ApplyAsync(_settings.DynamicsIsLimiter
            ? new LimiterEffect(_settings.Limiter)
            : new CompressorEffect(_settings.Compressor));
    }

    // ---- Repair ----

    private void OnClickSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateClickText();

    private void UpdateClickText()
    {
        if (ClickValue is not null)
        {
            ClickValue.Text = $"{ClickSlider.Value:0.0} of 10";
        }
    }

    private async void OnApplyClicks(object sender, RoutedEventArgs e)
    {
        ClickPopup.IsOpen = false;
        _settings.ClickSensitivity = ClickSlider.Value;
        _settings.Save();
        await ApplyAsync(new ClickRemovalEffect(ClickSlider.Value));
    }

    private void OnNoiseSlider(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateNoiseTexts();

    private void UpdateNoiseTexts()
    {
        if (NoiseAmountValue is null || NoiseSensitivityValue is null || NoiseProfileText is null)
        {
            return;
        }

        NoiseAmountValue.Text = $"{NoiseAmountSlider.Value:0} dB";
        NoiseSensitivityValue.Text = $"+{NoiseSensitivitySlider.Value:0.0} dB";
        var rate = _session?.Audio.SampleRate;
        NoiseProfileText.Text = _noiseProfile is null
            ? "Select a stretch of just the noise (a pause, or the lead-in), then press Learn."
            : _noiseProfile.SampleRate != rate
                ? "The noise was learned from another sound. Learn it again from this one."
                : "Learned. Press Learn again to learn from a different part.";
        NoiseApplyButton.IsEnabled = _noiseProfile is not null && _noiseProfile.SampleRate == rate;
    }

    private async void OnLearnNoise(object sender, RoutedEventArgs e)
    {
        if (_session is not { } session)
        {
            return;
        }

        var selection = Selection;
        var audio = session.Audio;
        var profile = selection.IsEmpty ? null : await Task.Run(() => NoiseProfile.Learn(audio, selection));
        if (profile is null)
        {
            NoiseProfileText.Text = "Select at least a quarter of a second with only the noise in it, then press Learn.";
            return;
        }

        _noiseProfile = profile;
        UpdateNoiseTexts();
        // The learned part is just noise, so the next thing to do is usually to clean the whole song.
        Waveform.ClearSelection();
        NoiseProfileText.Text = $"Learned from {selection.Length / (double)audio.SampleRate:0.0#} seconds. " +
                                "Now select what to clean, or leave nothing selected for the whole sound.";
    }

    private async void OnApplyNoise(object sender, RoutedEventArgs e)
    {
        if (_noiseProfile is not { } profile)
        {
            return;
        }

        NoisePopup.IsOpen = false;
        _settings.NoiseReductionDb = NoiseAmountSlider.Value;
        _settings.NoiseSensitivityDb = NoiseSensitivitySlider.Value;
        _settings.Save();
        await ApplyAsync(new NoiseReductionEffect(profile, NoiseAmountSlider.Value, NoiseSensitivitySlider.Value));
    }

    // ---- Channels ----

    private async Task ConvertChannelsAsync(int channels)
    {
        if (_session is not { } session)
        {
            return;
        }

        Waveform.SelectedChannel = null;
        var description = channels == 1 ? "Convert to mono" : "Convert to stereo";
        await EditAsync(description, s => s.ConvertChannels(channels), () => new FrameRange(0, 0));
        if (session.Audio.ChannelCount == channels)
        {
            UpdateNoiseTexts();
            ShowStatus(channels == 1
                ? "Converted to mono. Saving now writes a one-channel file. Ctrl+Z undoes it."
                : "Converted to stereo. Saving now writes a two-channel file. Ctrl+Z undoes it.");
        }
    }

    private void OnWidthChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => UpdateWidthText();

    private void UpdateWidthText()
    {
        if (WidthValue is not null)
        {
            WidthValue.Text = WidthSlider.Value == 0 ? "0% (mono)" : $"{WidthSlider.Value:0}%";
        }
    }

    private async void OnApplyWidth(object sender, RoutedEventArgs e)
    {
        WidthPopup.IsOpen = false;
        _settings.StereoWidth = WidthSlider.Value / 100;
        _settings.Save();
        if (Math.Abs(WidthSlider.Value - 100) >= 1)
        {
            await ApplyAsync(new StereoWidthEffect(WidthSlider.Value / 100), bothChannels: true);
        }
    }

    // ---- Batch ----

    private void OnBatch(object sender, RoutedEventArgs e)
    {
        if (Application.Current.MainWindow is MainWindow main)
        {
            main.ChooseFilesToConvert(this);
        }
    }
}
