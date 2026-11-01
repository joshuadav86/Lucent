# Playback

## What it does

- Plays local music and video sound through one `AudioPlayer`, so the EQ, visuals, analyzer, and recording all hear it.
- **Formats:** MP3, FLAC, WAV (any depth, float, RF64, Wave64), AIFF/AIFF-C, CAF, AAC/M4A, ALAC, Ogg Vorbis, Opus, Ogg FLAC, WavPack, Monkey's Audio (APE), WMA, AC-3/E-AC-3, and anything libVLC reads. A format that plays in one part of Lucent must play in every part (player, editor, converter, stems, beats).
- **Gapless:** the next track is lined up ahead of time (`TrackChain`) and joins with no gap.
- **Crossfade:** Options > Sound "Crossfade songs", off by default, 1 to 12 s (default 5), equal-power. Automatic track changes only; a manual skip still cuts. Videos don't crossfade.
- **Output choice:** pick the output device in Options > Sound.
- **Volume bar:** Lucent's own volume, applied after the EQ, visuals, and analyzer hear the music, so they read the song as it is. With "Visuals for every app's sound" on (and not in bit-perfect mode) it's the computer's volume instead (Windows' default output, the Mac's default output; `Audio/ComputerVolume.cs`, `Player/MainWindow.ComputerVolume.cs`): it follows the volume keys, says Muted when muted, and Lucent's own music plays at full level into the mix, so the analyzer still reads true loudness. Lucent's own volume comes back when the switch is off. While Lucent controls a streaming app that plays on this computer, the bar is the computer's volume too: the app's own volume is applied before Lucent hears it, so the app's volume is handed over once (turned up to full, with the computer's turned down by as much, at most 30 dB) and the analyzer reads the song's true loudness. While the app plays on another device, or the output has no volume Lucent can set, the bar is the app's own volume.
- **Bit-perfect (exclusive) mode:** Lucent takes the device alone and switches it to each track's sample rate. Volume is 100% and the EQ is off.
- **Media keys and system now-playing:** play, pause, next, and previous work in the background, and the song shows in the system's media overlay.
- **Controls on the app icon:** Play/Pause, Next, and Previous on Lucent's icon, so a song can be skipped without bringing the player up.
- **Long recordings with marked songs** (cue points in a WAV, or a `.cue` beside it): Next and Previous skip between songs in files of 8 minutes or more.
- **First start** puts Kepler North's "Self Actualize" in the library.

## Windows

- WASAPI shared output through NAudio; exclusive mode through WASAPI exclusive (`Audio/PcmOutput.cs`).
- Decoding falls back to Media Foundation, then libVLC.
- Media keys and the volume flyout through Windows' media controls (SMTC, `Audio/MediaControls.cs`).
- Previous, Play/Pause, and Next buttons under the player's taskbar preview (`Player/MainWindow.IconControls.cs`, WPF `TaskbarItemInfo`). The preview shows the song as the window's title.

## Mac

- Core Audio output through an Audio Queue (`Audio/AudioQueueOutput.cs`); exclusive mode is hog mode plus the device's nominal sample rate (`Audio/ExclusiveDevice.cs`).
- Decoding falls back to Extended Audio File Services (`Audio/MacAudioFileReader.cs`), then libVLC.
- Now Playing and media keys through `MPNowPlayingInfoCenter` and `MPRemoteCommandCenter`.
- The Dock menu (right-click or hold Lucent's icon) shows Now Playing with the song, then Play/Pause, Next, and Previous (`Player/MainWindow.IconControls.cs`, Avalonia `NativeDock`).

## Parity gaps

None in behavior. Only the platform pieces differ (see [PARITY.md](../PARITY.md)).

## Code

- `Lucent.Core/Audio/AudioReaderFactory.cs`: reader order is Lucent's own PCM reader, managed Ogg/Opus/WavPack/APE, system decoders, then libVLC. `OpenQuickly` never uses libVLC (gapless uses it).
- `Lucent.Core/Audio/TrackChain.cs`: gapless and crossfade.
- `Audio/AudioPlayer.cs`, `Audio/OutputDevices.cs`, `MainWindow.Sound.cs`, `MainWindow.MediaControls.cs`, `MainWindow.TrackMarks.cs` in each app.
- Settings: `Crossfade`, `CrossfadeSeconds`, output and exclusive settings in `AppSettings`.

## Checks

- `--formats tools/test-media/formats` plays 47 test files on both CI platforms (`FormatSelfTest`).
- The Mac self-test checks gapless, crossfade, and outputs (`SelfTest.Sound.cs`). Windows doesn't yet.

## Not done

DSD (.dsf/.dff), tracker and game music, casting (AirPlay, Chromecast, UPnP), and "Lucent as a speaker" are parked for a future update.
