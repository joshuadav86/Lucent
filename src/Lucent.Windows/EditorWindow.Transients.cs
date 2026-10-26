using System.Windows;
using System.Windows.Input;
using WinampInspiredVisualizer.Editor;
using WinampInspiredVisualizer.Editor.Dsp;

namespace WinampInspiredVisualizer;

/// <summary>
/// Tab to Transient, as in Pro Tools: with it on, Tab jumps the cursor to the next drum hit or note start,
/// Shift+Tab to the one before, and holding Ctrl as well stretches the selection to it instead.
/// Right-clicking the toolbar's Tab button sets how sensitive the detection is.
/// </summary>
public partial class EditorWindow
{
    // The transients found in the audio as it is now, measured when Tab is first pressed after a change.
    private Transients? _transients;
    private AudioBuffer? _transientsOf;
    private Task<Transients>? _analyzingTransients;

    private void InitTransients()
    {
        TransientToggle.IsChecked = _settings.TabToTransient;
        TransientSlider.Value = _settings.TransientSensitivity;
        UpdateTransientText();
    }

    private void OnTransientToggle(object sender, RoutedEventArgs e)
    {
        _settings.TabToTransient = TransientToggle.IsChecked == true;
        _settings.Save();
        ShowStatus(_settings.TabToTransient
            ? "Tab to Transient is on: Tab jumps to the next hit, Shift+Tab to the one before. Add Ctrl to select up to it. Right-click Tab to set how sensitive it is."
            : "Tab to Transient is off.");
    }

    private void OnTransientMenu(object sender, MouseButtonEventArgs e)
    {
        TransientPopup.IsOpen = true;
        e.Handled = true;
    }

    private void OnTransientSensitivityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TransientValue is null)
        {
            return;
        }

        _settings.TransientSensitivity = TransientSlider.Value;
        _settings.Save();
        UpdateTransientText();
    }

    private void UpdateTransientText()
    {
        TransientValue.Text = $"{TransientSlider.Value:0}";
        TransientCount.Text = _transients is { } transients && ReferenceEquals(_transientsOf, _session?.Audio)
            ? $"{transients.At(TransientSlider.Value).Count} transients in this sound at this setting."
            : "Higher finds softer hits too: every note of a hi-hat, not just the kick and snare.";
    }

    /// <summary>The transients in the audio as it is now, measuring them first if it has changed.</summary>
    private async Task<Transients?> CurrentTransientsAsync()
    {
        if (_session is not { } session)
        {
            return null;
        }

        var audio = session.Audio;
        if (_transients is not null && ReferenceEquals(_transientsOf, audio))
        {
            return _transients;
        }

        if (_analyzingTransients is null || !ReferenceEquals(_transientsOf, audio))
        {
            _transientsOf = audio;
            _transients = null;
            _analyzingTransients = Task.Run(() => Transients.Analyze(audio));
        }

        var analyzing = _analyzingTransients;
        var found = await analyzing;
        // Another edit while measuring makes this out of date; the next Tab measures again.
        if (!ReferenceEquals(_transientsOf, audio) || !ReferenceEquals(_session?.Audio, audio))
        {
            return null;
        }

        _transients = found;
        _analyzingTransients = null;
        UpdateTransientText();
        return found;
    }

    /// <summary>Moves the cursor to the next transient (or the one before), or stretches the selection to it.</summary>
    private async Task TabToTransientAsync(bool forward, bool extend)
    {
        if (await CurrentTransientsAsync() is not { } transients || _session is not { } session)
        {
            return;
        }

        var sensitivity = _settings.TransientSensitivity;
        var playing = _player.IsPlaying && !extend;
        var selection = Selection;
        var from = playing ? _player.PositionFrame
            : extend && !selection.IsEmpty ? forward ? selection.End : selection.Start
            : Waveform.CursorFrame;
        var to = forward ? transients.Next(from, sensitivity) : transients.Previous(from, sensitivity);
        if (to is not { } frame)
        {
            ShowStatus(forward ? "There are no more transients after this." : "There are no transients before this.");
            return;
        }

        if (extend)
        {
            var anchor = selection.IsEmpty ? Waveform.CursorFrame : forward ? selection.Start : selection.End;
            Waveform.Selection = new FrameRange(Math.Min(anchor, frame), Math.Abs(frame - anchor));
        }
        else
        {
            Waveform.ClearSelection();
            Waveform.CursorFrame = frame;
            if (playing)
            {
                PlayFrom(frame, session.Audio.Frames, _previewThrough);
            }
        }

        Waveform.KeepInView(frame);
    }
}
