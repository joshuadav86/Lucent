# Playback

## What it does

- Plays local music and video sound through one `AudioPlayer`, so the EQ, visuals, analyzer, and recording all hear it.
- **Formats:** MP3, FLAC, WAV (any depth, float, RF64, Wave64), AIFF/AIFF-C, CAF, AAC/M4A, ALAC, Ogg Vorbis, Opus, Ogg FLAC, WavPack, Monkey's Audio (APE), WMA, AC-3/E-AC-3, and anything libVLC reads. A format that plays in one part of Lucent must play in every part (player, editor, converter, stems, beats).
- **Gapless:** the next track is lined up ahead of time (`TrackChain`) and joins with no gap.
- **Crossfade:** Options > Sound "Crossfade songs", off by default, 1 to 12 s (default 5), equal-power. Automatic track changes only; a manual skip still cuts. Videos don't crossfade.
- **Output choice:** pick the output device in Options > Sound.
- **Bit-perfect (exclusive) mode:** Lucent takes the device alone and switches it to each track's sample rate. Volume is 100% and the EQ is off.
- **Media keys and system now-playing:** play, pause, next, and previous work in the background, and the song shows in the system's media overlay.
- **Long recordings with marked songs** (cue points in a WAV, or a `.cue` beside it): Next and Previous skip between songs in files of 8 minutes or more.
- **First start** puts Kepler North's "Self Actualize" in the library.

## Windows

- WASAPI shared output through NAudio; exclusive mode through WASAPI exclusive (`Audio/PcmOutput.cs`).
- Decoding falls back to Media Foundation, then libVLC.
- Media keys and the volume flyout through Windows' media controls (SMTC, `Audio/MediaControls.cs`).

## Mac

- Core Audio output through an Audio Queue (`Audio/AudioQueueOutput.cs`); exclusive mode is hog mode plus the device's nominal sample rate (`Audio/ExclusiveDevice.cs`).
- Decoding falls back to Extended Audio File Services (`Audio/MacAudioFileReader.cs`), then libVLC.
- Now Playing and media keys through `MPNowPlayingInfoCenter` and `MPRemoteCommandCenter`.

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
