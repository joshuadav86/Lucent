# Equalizer and analyzer

## What it does

**Equalizer (E):**
- 4, 8, 16, 24, or 32 faders drawn over the live spectrum, with 15 presets and a balance control.
- The response curve above the faders can be dragged up or down: it moves the fader under the pointer (half-dB steps, double-click resets, the wheel nudges), and hovering it names that band.
- The spectrum shows energy per slice of an octave (bins averaged, 3 dB per octave lift), so pink noise draws flat and music falls away at the top as much as it really does.
- Shapes only the player's sound. Its spectrum pauses while the editor plays, because the EQ doesn't shape the editor.
- Shapes the streaming app Lucent controls (Spotify, Apple Music, YouTube Music, and the rest) only when "EQ streaming apps too" is on in Options › Sound (off by default). While an app is in control the status line says whether the EQ reaches it, with a Turn on or Set up button when it doesn't; the spectrum moves either way because it hears the app.
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

WPF drawing (`Windows/Spectrogram*.cs`, `Windows/EqualizerSpectrum.cs`). Other apps' sound comes from WASAPI loopback; music Lucent plays in exclusive mode skips Windows' mixer, so loopback can't hear it. Windows can't quieten another app for Lucent to play, so a streaming app is shaped through Equalizer APO (`Audio/EqualizerApo.cs`, installed separately): Lucent writes the curve to lucent.txt in its settings folder (included from config.txt, the boost paid for in the preamp), flattens it when the app stops being controlled, and on start in case it was left on. Equalizer APO shapes every app on that output meanwhile.

## Mac

Avalonia drawing. Other apps' sound comes from a Core Audio process tap (macOS 14.2 or later, needs the user's OK). To shape a streaming app, the tap of just that app plays through a copy of the player's equalizer (`Equalizer.CopyFrom`, following `Equalizer.Changed`) on the tap's own aggregate device, so it shares the output's clock. macOS quietens the app (muted when tapped) only once the tap has heard it, so a tap without permission never leaves the app silent, and the app is heard again when Lucent stops.

## Parity gaps

None.

## Code

- `Lucent.Core/Audio/Equalizer.cs`, `EqualizerBands.cs`, `EqualizerDesign.cs`, `EqualizerPresets.cs`
- `Lucent.Core/Audio/LoudnessMeter.cs`, `AudioCapture.cs`, `EditorFeed.cs`, `InputFeed.cs`
- `Lucent.Core/Audio/StreamingEqualizerStatus.cs`, Windows' `Audio/EqualizerApo.cs`
- `EqualizerWindow.*`, `AnalyzerWindow.*`, `Audio/SystemAudio.cs`, `MainWindow.ListenInput.cs` in each app
- Settings: `AnalyzerModules`, `AnalyzerSensitivity`, `ListenInput`, `ListenInputChannel`, `EqualizerSettings`, `EqualizeStreamingApps`

## Checks

`LoudnessSelfTest` (EBU Tech 3341/3342 tones), `EqualizerSelfTest` (each band's slider moves a tone at its centre by that much, mid-song changes take effect, and off is untouched), and `EditorFeedSelfTest` run in both apps' `--self-test`. Windows also checks the curve it hands Equalizer APO (`TestEqualizerApo`).

## Adding an audio source

Add a Begin/Write/End trio to `AudioCapture` and slot it into the precedence above. Don't write through the player's `Write`.
