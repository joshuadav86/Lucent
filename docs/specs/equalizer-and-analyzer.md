# Equalizer and analyzer

## What it does

**Equalizer (E):**
- 4, 8, 16, 24, or 32 faders drawn over the live spectrum, with 15 presets and a balance control.
- The spectrum shows energy per slice of an octave (bins averaged, 3 dB per octave lift), so pink noise draws flat and music falls away at the top as much as it really does.
- Shapes only the player's sound. Its spectrum pauses while the editor plays, because the EQ doesn't shape the editor.
- Switches to the classic skin format's 10 octave bands while a .wsz skin is on.
- Off in bit-perfect mode.

**Analyzer (Y):** a studio window with panels you switch on and off:
- Heatmap (spectrogram), spectrum bars, scrolling waveform, stereo scope with correlation.
- Sensitivity slider in the title bar (-12 to +24 dB, saved) brightens or calms the heatmap and spectrum bars.
- The dotted frequency lines across the heatmap, and the waveform's centre line, scroll with the picture.
- Loudness strip: momentary, short-term, and integrated LUFS, loudness range (LRA), and true peak (dBTP, hot above -1).
- Loudness restarts on a new song, a source change, a gapless track change, a click on the strip, or "Measure loudness from here".

**What they listen to:** the visuals, meter, EQ spectrum, and analyzer all read one `AudioCapture`. Precedence: a live input being recorded, then the editor while it plays, then other apps' sound or a chosen input, then the player.

## Windows

WPF drawing (`Windows/Spectrogram*.cs`, `Windows/EqualizerSpectrum.cs`). Other apps' sound comes from WASAPI loopback; music Lucent plays in exclusive mode skips Windows' mixer, so loopback can't hear it.

## Mac

Avalonia drawing. Other apps' sound comes from a Core Audio process tap (macOS 14.2 or later, needs the user's OK).

## Parity gaps

None.

## Code

- `Lucent.Core/Audio/Equalizer.cs`, `EqualizerBands.cs`, `EqualizerDesign.cs`, `EqualizerPresets.cs`
- `Lucent.Core/Audio/LoudnessMeter.cs`, `AudioCapture.cs`, `EditorFeed.cs`, `InputFeed.cs`
- `EqualizerWindow.*`, `AnalyzerWindow.*`, `Audio/SystemAudio.cs`, `MainWindow.ListenInput.cs` in each app
- Settings: `AnalyzerModules`, `AnalyzerSensitivity`, `ListenInput`, `ListenInputChannel`, `EqualizerSettings`

## Checks

`LoudnessSelfTest` (EBU Tech 3341/3342 tones) and `EditorFeedSelfTest` run in both apps' `--self-test`.

## Adding an audio source

Add a Begin/Write/End trio to `AudioCapture` and slot it into the precedence above. Don't write through the player's `Write`.
