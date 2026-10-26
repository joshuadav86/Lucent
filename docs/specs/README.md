# Lucent specs

One short spec per feature area: what it does, how Windows and Mac each do it, and where they differ. Read the spec before changing a feature, and update it in the same pull request when the behavior changes.

| Spec | Covers |
| --- | --- |
| [Playback](playback.md) | Formats, gapless, crossfade, outputs, bit-perfect mode, media keys |
| [Equalizer and analyzer](equalizer-and-analyzer.md) | EQ, presets, spectrum, analyzer panels, LUFS |
| [Library and queue](library-and-queue.md) | Library, queue, favorites, playlists, imports, Bandcamp, tags, drag and drop, default player |
| [Streaming control](streaming-control.md) | Spotify, Apple Music, TIDAL, YouTube, SoundCloud, Pandora |
| [Online radio](radio.md) | Station directory, featured stations, HLS, video radio, live shows |
| [Visuals](visuals.md) | Lucent originals, MilkDrop, AVS, picker, Auto, build-ups and drops, recording a visual |
| [Video](video.md) | Video files, the Video window, video in the editor |
| [Editor](editor.md) | Audio and video editor, effects, VST3, stems, loops, converting |
| [Live recording](recording.md) | Record window, track markers, song ID |
| [Windows and layout](windows-and-layout.md) | Docking, snapping, locks, swap, full screen, tidy, shortcuts |
| [Skins and themes](skins-and-themes.md) | Skins, themes, classic .wsz skins |
| [Updates and crash reports](updates-and-crash-reports.md) | Self-update, channels, crash reports, bug reports, data folder |
| [Website](website.md) | lucentaudio.net, screenshots, analytics |
| [Build and CI](build-and-ci.md) | Projects, CI jobs, required checks, releases, self-tests |

## Rules that apply everywhere

- **Parity.** Every feature ships on Windows and Mac in the same pull request. A gap is allowed only when one platform can't do it, and it's listed in [PARITY.md](../PARITY.md), which CI checks.
- **Shared first.** Logic goes in `src/Lucent.Core`. The apps hold only UI and platform calls.
- **Visual safety.** No visual may flash more than 3 times a second (`FlashCheck`). Strobing presets are dropped.
- **Privacy.** Crash reports carry no paths, URLs, emails, or user or machine names.

## Spec format

Each spec has: **What it does**, **Windows**, **Mac**, **Parity gaps**, **Code**, and **Checks**. Keep it short. Files under "Code" are in each app's feature folders (see [LAYOUT.md](../LAYOUT.md)) unless they start with `Lucent.Core/`.
