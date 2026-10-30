# Visuals

## What it does

- **448 visuals**, drawn live on the GPU: 200 Lucent originals (HLSL), 214 MilkDrop presets, and 34 AVS presets (Nullsoft's stock set, credited in `content/Presets/AVS/CREDITS.md`). Switches for each group (Lucent originals, MilkDrop, AVS) hide them from the picker and Auto.
- **Lucent originals:** one `.hlsl` file each in the shared `Presets` folder, with `// Name:`, `// Family:`, and `// Tags:` headers. Families: Cosmic, Cyberpunk, Fly-through, Fractal, Geometric, Kaleidoscope, Minimal, Music-shaped, Organic, Particles, Ray traced. Users can drop their own `.hlsl`, `.milk`, and `.avs` files in the `Presets` folder next to the app.
- **MilkDrop:** `.milk` presets run by Lucent's own engine, at most 60 steps a second. "Modern" look (default on) brings them into Lucent's lighting.
- **AVS:** a C# port of AVS 2.81d plus common APEs, drawn on the CPU and shown through a shader. Modern or classic look.
- **Picker (V):** families, tags, search, favorites (F), and each preset's author.
- **Auto (A):** changes visual on downbeats and drops; next (N) and back (B).
- **Build-ups and drops:** songs are mapped in the background (bars, sections, build-ups, drops). Through a build-up the picture zooms in with quickening hits; the drop brings a shock wave and a brighter picture. Radio and other apps are followed by ear at half strength. Switch: "Build-ups and drops".
- **Fullscreen visuals (F11)** or double-click. A single click does nothing (no click-to-pause). Right-click is the menu.
- **Record a visual to video** from the right-click menu (MP4 with the song).
- **Detail levels** up to Ultra and 4K; resolution adapts to keep frame rate (`AutoResolution`).

## Rules

- No visual may flash more than 3 times a second. Both self-tests warn ("Flashed more than 3 times a second"); drop presets it flags.
- A dropped visual goes in `ReplacedVisuals` or `RemovedVisuals` in `AppSettings`, so favorites and lists are fixed up. Update the counts in the README and `site/index.html`.
- When a visual is dropped, a new Lucent original replaces it.
- Bundled MilkDrop presets come only from projectM's Cream of the Crop; AVS from Nullsoft's BSD release. Credits are in each folder's `CREDITS.md`.

## Windows

Direct3D 11. HLSL compiles as it loads (`Visualizer/Renderer.cs`, `ShaderCache.cs`). Video recording converts frames to NV12 on the GPU for Media Foundation.

## Mac

Metal. The HLSL is translated to Metal when the app is built (`tools/MetalShaders`, needs DXC), and by DXC at run time for your own visuals (`Visuals/`). Video recording reads frames back into AVFoundation.

## Parity gaps

None in behavior. The renderers are separate by design (see [PARITY.md](../PARITY.md)).

## Code

- Shared shaders: `content/Shaders/` (`Common.hlsli` has the inputs and helpers; `Engine.hlsl` does bloom, tone map, and the build-up/drop look). Shared presets: `content/Presets/`, `content/Presets/MilkDrop/`, `content/Presets/AVS/`. Both live in `content/`, which both apps link.
- `Lucent.Core/Visualizer/` (`AudioAnalyzer`, `SongFollower`, `LiveBeatTracker`, `PresetSource`, `VisualChoice`, `FlashCheck`, `MilkDrop/`, `Avs/`)
- `Lucent.Core/Audio/BeatMap*.cs` (song map)
- `VisualizerWindow.*`, `MainWindow.VisualsMenu.cs`, `MainWindow.VideoExport.cs` in each app

## Checks

- Windows CI compiles every shader and draws every visual (warns "Still black after 12 seconds").
- Mac CI translates every shader to Metal (`metal-shaders`, `dxc-mac`) and draws every visual.
- `tools/MilkDropCheck` and `tools/AvsCheck` load every preset.
