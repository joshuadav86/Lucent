# Where things live

```
src/
  Lucent.Core/      Shared logic, no UI: audio, analysis, EQ, editor, library, radio, streaming, settings
  Lucent.Windows/   The Windows app (WPF, Direct3D 11)
  Lucent.Mac/       The Mac app (Avalonia, Metal, Core Audio)
  PluginHost/       VST3 helper process (C++, CMake)
content/            What both apps ship: Presets (the visuals), Shaders, StarterMusic
tools/              CI checks (ParityCheck, LayoutCheck, MilkDropCheck, AvsCheck), MetalShaders,
                    StemModel, ClassicSkin, test-media, ci/, mac/, windows/
docs/               PARITY.md (allowed Windows/Mac differences), specs/ (one spec per feature)
site/               lucentaudio.net
assets/             App icon source and third-party licences
```

## Inside each app

Windows and Mac use the same folders and file names, so a file's counterpart is at the same path in the other app (`tools/ParityCheck` checks this).

| Folder | Holds |
| --- | --- |
| *(root)* | `App`, `Program`, `AppInfo` |
| `Player/` | The player (`MainWindow.*`, split by feature: `MainWindow.Sound.cs`, `MainWindow.Radio.cs` …), equalizer, analyzer, artwork |
| `Visualizer/` | The visuals window and its renderer (Mac's Metal renderer is in `Visuals/`) |
| `Editor/` | The editor window, its views, export, and stems windows |
| `Library/` | Library, queue, import, track info, default-player and open-file handling |
| `Stores/` | Bandcamp and Beatport |
| `Streaming/` | Control of Spotify, Apple Music, TIDAL, YouTube, SoundCloud, Pandora |
| `Radio/` | The radio window |
| `Recording/` | The Record window |
| `Video/` | The video window and video export windows |
| `Audio/` | Output, inputs, media keys, system audio capture |
| `Export/` | Encoders and video recording |
| `Windows/` | Shared window pieces: snapping, locks, full screen, menus, dialogs |
| `Themes/` | Skins and themes |
| `Updates/` | Self-update |
| `Diagnostics/` | Crash reports, error log, bug report, self-test (Mac also: screenshots, layout check) |
| `Interop/` | Mac only: macOS APIs |
| `Assets/` | Windows only: the app icons (the Mac app uses `Lucent-64.png` from here too) |

`Lucent.Core` is split the same way by feature (`Audio/`, `Editor/`, `Radio/`, `Streaming/`, `Visualizer/` …).

## Where to start

- What a feature does: [docs/specs](specs/README.md).
- Adding a feature: put the logic in `Lucent.Core`, the UI in the same folder in both apps, and update its spec.
- Adding a visual: one `.hlsl` file in `content/Presets` ([README](../README.md#make-your-own-visuals)).
- Moving or renaming a folder: keep its old path in `tools/ci/apps-changed.sh` (main sorts every past merge by its files for each app's what's new), and update the paths in `.github/workflows`, `tools/`, `docs/specs`, and this file.
