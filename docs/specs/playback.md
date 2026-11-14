# Playback

## What it does

- Plays local music and video sound through one `AudioPlayer`, so the EQ, visuals, analyzer, and recording all hear it.
- **Formats:** MP3, FLAC, WAV (any depth, float, RF64, Wave64), AIFF/AIFF-C, CAF, AAC/M4A, ALAC, Ogg Vorbis, Opus, Ogg FLAC, WavPack, Monkey's Audio (APE), WMA, AC-3/E-AC-3, MIDI (.mid, .midi, .kar, .rmi), tracker modules (MOD, XM, S3M, IT, and others) and game music (NSF, SPC, VGM, GBS, and others) through libVLC, and anything else libVLC reads. A format that plays in one part of Lucent must play in every part (player, editor, converter, stems, beats).
- **Gapless:** the next track is lined up ahead of time (`TrackChain`) and joins with no gap.
- **Crossfade:** Options > Sound "Crossfade songs", off by default, 1 to 12 s (default 5), equal-power. Automatic track changes only; a manual skip still cuts. Videos don't crossfade.
- **Output choice:** pick the output device in Options > Sound.
- **Volume bar:** Lucent's own volume, applied after the EQ, visuals, and analyzer hear the music, so they read the song as it is. With "Visuals for every app's sound" on (and not in bit-perfect mode) it's the computer's volume instead (Windows' default output, the Mac's default output; `Audio/ComputerVolume.cs`, `Player/MainWindow.ComputerVolume.cs`): it follows the volume keys, says Muted when muted, and Lucent's own music plays at full level into the mix, so the analyzer still reads true loudness. Lucent's own volume comes back when the switch is off. While Lucent controls a streaming app that plays on this computer, the bar is the computer's volume too: the app's own volume is applied before Lucent hears it, so the app's volume is handed over once (turned up to full, with the computer's turned down by as much, at most 30 dB) and the analyzer reads the song's true loudness. While the app plays on another device, or the output has no volume Lucent can set, the bar is the app's own volume.
- **Bit-perfect (exclusive) mode:** Lucent takes the device alone and switches it to each track's sample rate. Volume is 100% and the EQ is off.
- **Media keys and system now-playing:** play, pause, next, and previous work in the background, and the song shows in the system's media overlay.
- **Controls on the app icon:** Play/Pause, Next, and Previous on Lucent's icon, so a song can be skipped without bringing the player up.
- **Long recordings with marked songs** (cue points in a WAV, or a `.cue` beside it): Next and Previous skip between songs in files of 8 minutes or more.
- **MIDI:** played by Lucent's own synthesizer (MeltySynth) with a SoundFont, so it goes through the EQ, visuals, analyzer, and recording like any song. Lucent's own SoundFont, GeneralUser GS (about 32 MB), is downloaded the first time a MIDI file plays, with a progress toast, and the file plays once it's in. Options > Sound "MIDI instruments" picks your own .sf2 instead (checked before it's taken), and a MIDI file that's playing switches over at the same place.
- **Volume leveling:** Options > Sound, Off (the default), Each song, or Each album. Songs are brought to −18 LUFS: from their ReplayGain tags if they have them, otherwise measured once in the background and kept in `Loudness.json` (by path, size, and date). A boost never pushes the song's peak over full scale, and the gain is held between −24 and +12 dB. Each album uses one gain for the songs in the same folder with the same album tag. The visuals and analyzer still see the song's true level. Off in exclusive mode (bit-perfect) and for videos.
- **Pick up long files where you left off:** Options > Sound, on by default. A file of 20 minutes or more (a podcast, a DJ mix, an audiobook) starts where it was left, with a toast saying so. Places within 30 s of either end are forgotten, and the 100 most recent are kept.
- **Shortcuts in every app:** Options > Sound, keys for Play/Pause, Stop, Next, Previous, Volume up, Volume down, Favorite, and Show Lucent that work while another app is in front. None are set until you set one: click it, press the keys (Ctrl, Alt, or the Windows key, or ⌃, ⌥, or ⌘, with a letter, number, or F key, or an F key alone), Backspace or Delete clears it, Esc leaves it. Keys another app already has are named in the options.
- **First start** puts Kepler North's "Self Actualize" in the library.

## Windows

- WASAPI shared output through NAudio; exclusive mode through WASAPI exclusive (`Audio/PcmOutput.cs`).
- Decoding falls back to Media Foundation, then libVLC.
- Media keys and the volume flyout through Windows' media controls (SMTC, `Audio/MediaControls.cs`).
- Previous, Play/Pause, and Next buttons under the player's taskbar preview (`Player/MainWindow.IconControls.cs`, WPF `TaskbarItemInfo`). The preview shows the song as the window's title.
- Shortcuts in every app through `RegisterHotKey` (`Audio/GlobalHotkeys.cs`), on the player's window.

## Mac

- Core Audio output through an Audio Queue (`Audio/AudioQueueOutput.cs`); exclusive mode is hog mode plus the device's nominal sample rate (`Audio/ExclusiveDevice.cs`).
- Decoding falls back to Extended Audio File Services (`Audio/MacAudioFileReader.cs`), then libVLC.
- Now Playing and media keys through `MPNowPlayingInfoCenter` and `MPRemoteCommandCenter`.
- The Dock menu (right-click or hold Lucent's icon) shows Now Playing with the song, then Play/Pause, Next, and Previous (`Player/MainWindow.IconControls.cs`, Avalonia `NativeDock`).
- Shortcuts in every app through Carbon's `RegisterEventHotKey` (`Audio/GlobalHotkeys.cs`), which needs no Accessibility permission.

## Parity gaps

None in behavior. Only the platform pieces differ (see [PARITY.md](../PARITY.md)). The Finder lists Lucent under Open With for MIDI files but not for tracker or game music, which macOS has no file types for (they open from File > Open or by dragging in); Windows lists them all.

## Code

- `Lucent.Core/Audio/AudioReaderFactory.cs`: reader order is Lucent's own PCM reader, managed Ogg/Opus/WavPack/APE, system decoders, then libVLC. `OpenQuickly` never uses libVLC (gapless uses it).
- `Lucent.Core/Audio/TrackChain.cs`: gapless and crossfade, and each track's leveling gain (`LevelledSamples`, which glides to a new gain once a song has started). `CapturingSampleProvider` divides the gain back out for the visuals and analyzer.
- `Lucent.Core/Audio/Midi/`: `MidiSong` (reads Standard MIDI Files 0, 1, and 2, and RMID), `MidiReader` (renders with MeltySynth, seeks by replaying the controllers), `SoundFonts` (download, checks, and the one in use). `LibVlcOnlyExtensions` in `AudioReaderFactory` are the tracker and game formats.
- `Lucent.Core/Audio/VolumeLeveling.cs`, `Lucent.Core/Playlist/ResumePoints.cs`, `Lucent.Core/Settings/GlobalHotkeys.cs` (the actions, keys, and both platforms' key codes).
- `MainWindow.PlaybackExtras.cs` and `MainWindow.Hotkeys.cs` in each app.
- `Audio/AudioPlayer.cs`, `Audio/OutputDevices.cs`, `MainWindow.Sound.cs`, `MainWindow.MediaControls.cs`, `MainWindow.TrackMarks.cs` in each app.
- Settings: `Crossfade`, `CrossfadeSeconds`, `Leveling`, `MidiSoundFont`, `ResumeLongFiles`, `ResumePoints`, `GlobalHotkeys`, output and exclusive settings in `AppSettings`.
- The SoundFont: `tools/SoundFont/fetch.sh` takes GeneralUser GS from its author's GitHub at a fixed commit and checks its SHA-256. CI publishes it once as the `midi-soundfont-v1` release, which is where the app downloads it from (its author asks that his own download files aren't linked to). A new SoundFont is a new `Version` in `SoundFonts.cs`.

## Checks

- `--formats tools/test-media/formats` plays 50 test files on both CI platforms (`FormatSelfTest`), MIDI with the SoundFont (`LUCENT_SOUNDFONT`) and a tracker module and VGM through libVLC (`tools/test-media/make-retro.py` makes them).
- `PlaybackExtrasSelfTest` checks MIDI timing, running status, and RMID, leveling gains and the glide, resume places, and reading and labeling shortcuts.
- The Mac self-test checks gapless, crossfade, and outputs (`SelfTest.Sound.cs`). Windows doesn't yet.

## Not done

DSD (.dsf/.dff), SID chiptunes (the libVLC Lucent ships has no SID player), casting (AirPlay, Chromecast, UPnP), and "Lucent as a speaker" are parked for a future update.
