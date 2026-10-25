# Live recording

## What it does

- **Record window:** pick an audio interface, inputs, sample rate, bit depth, a naming template, and a save folder. Live VST3 plugins on the input. The visuals react to the input while recording.
- Records to WAV (RF64 past 4 GB).
- **Track markers:** song changes are detected and written as cue points and labels in the WAV, plus a `.cue` sheet. Listeners skip songs with Next and Previous.
- **Song ID (optional):** each DJ pastes their own ACRCloud key (SET UP in the Record window). Each detected track is looked up at 25 s and 85 s and its marker named. No key ships with Lucent.
- **Listen to an input without recording:** under "Visuals for every app's sound", pick an input instead (e.g. a DAW through an interface's loopback).

## Windows

WASAPI inputs (at the rate set in Windows, converted) or the interface's ASIO driver (at the chosen rate, lowest delay). ACRCloud key stored with DPAPI. "Listen to an input" lists WASAPI inputs only.

## Mac

Core Audio Audio Queue input; Lucent sets the interface's rate itself. Asks once for microphone permission. ACRCloud key in the login keychain. The process tap should already hear a DAW; the input route is the fallback.

## Parity gaps

ASIO is Windows-only; Core Audio needs no equivalent.

## Code

- `Lucent.Core/Recording/` (`LiveRecorder`, `WavRecording`, `SongChangeDetector`, `TrackMarkers`, `SongIdentifier`, `RecordingNames`)
- `Lucent.Core/Settings/RecordingSettings.cs` (`recording.json`)
- `RecordWindow.*`, `Audio/LiveInputs.cs`, `Audio/SongIdAccount.cs`, `MainWindow.TrackMarks.cs`, `MainWindow.ListenInput.cs` in each app

## Checks

`RecordingSelfTest` in both self-tests. Song ID hasn't been tried against real ACRCloud.
