# Editor

The "Video and audio editor" opens from the title bar's waveform button or a song's right-click menu.

## What it does

- **Editing:** split into clips, slide clips (overlaps crossfade), trim, delete, fades with shapes, undo history (jump to any step). Hand tool drags clips.
- **Effects menu** (searchable): normalize (peak or LUFS), volume, fades, pitch, tempo, reverse, EQ, dynamics, hum, click and noise repair, DC offset, channel tools, silence, stems, analyze.
- **VST3 plugins:** an Inserts strip (I) beside the waveform with numbered slots, bypass (B), live through Play. Plugin browser with search, types, makers, and favorites. Chains save as `.lucentchain`; REAPER `.RfxChain` imports.
- **Stems:** splits vocals, drums, bass, and other with Demucs (HTDemucs), on the GPU when it passes a quality check, else the processor. The Listen… window mixes them; saved stems go to a Stems folder.
- **Beats, key, and loops:** BPM and key detection, a beat grid, and a loop maker that snaps to beats and shows bars/beats. Loops save as WAV.
- **Video:** trim and delete on MP4/MOV without re-encoding, frame and GIF export. See [Video](video.md).
- **Saving:** save over, or "Save as new version" in MP3, FLAC, WAV, or AIFF. The same window converts files in batches.
- The editor feeds the visuals and analyzer while it plays, after plugins and before its own volume.

## Windows

ONNX Runtime with DirectML for stems. MP3 via LAME (`NAudio.Lame`).

## Mac

ONNX Runtime with Core ML for stems. MP3 via a bundled `libmp3lame` (`tools/mac/build-lame-mac.sh`).

## Parity gaps

None in behavior. Platform helpers listed in [PARITY.md](../PARITY.md) (fonts, clipboard).

## Code

- `Lucent.Core/Editor/` (`EditSession*`, `EffectCatalog`, `Effects/`, `Dsp/`, `Plugins/`, `Stems/`, `LoopFiles`)
- `Lucent.Core/Export/` (encoders, `FileConverter`, `ExportPipeline`)
- `src/PluginHost/` (C++ VST3 helper process, built with CMake)
- `EditorWindow.*`, `Editor/`, `ExportWindow.*`, `StemsWindow.*`, `TrackInfoWindow.Detect.cs` in each app
- Settings: `EditorSettings` (`ShowInserts`, `Volume`, `DragMovesClips`, `FavoritePlugins`, `PluginTypes`)

## Checks

`MusicSelfTest` (beats, key, loops) and `StemSelfTest` run in both self-tests. The `stem-model` CI job exports the model and checks it against Demucs; the app downloads it from the `stem-model-v1` release on first use.
