# Visuals

## What it does

- **450 visuals**, drawn live on the GPU: 202 Lucent originals (HLSL), 214 MilkDrop presets, and 34 AVS presets (Nullsoft's stock set, credited in `content/Presets/AVS/CREDITS.md`). Switches for each group (Lucent originals, MilkDrop, AVS) hide them from the picker and Auto.
- **Lucent originals:** one `.hlsl` file each in the shared `Presets` folder, with `// Name:`, `// Family:`, and `// Tags:` headers. Families: Cosmic, Cyberpunk, Fly-through, Fractal, Geometric, Kaleidoscope, Minimal, Music-shaped, Organic, Particles, Ray traced. Users can drop their own `.hlsl`, `.milk`, and `.avs` files in the `Presets` folder next to the app.
- **MilkDrop:** `.milk` presets run by Lucent's own engine, at most 60 steps a second. "Modern" look (default on) brings them into Lucent's lighting.
- **AVS:** a C# port of AVS 2.81d plus common APEs, drawn on the CPU and shown through a shader. Modern or classic look.
- **Picker (V):** families, tags, search, favorites (F), and each preset's author.
- **Auto (A):** changes visual on downbeats and drops; next (N) and back (B).
- **Build-ups and drops:** songs are mapped in the background (bars, sections, build-ups, drops). Through a build-up the picture zooms in with quickening hits; the drop brings a shock wave and a brighter picture. Radio and other apps are followed by ear at half strength. Switch: "Build-ups and drops".
- **Fullscreen visuals (F11)** or double-click. A single click does nothing (no click-to-pause). Right-click is the menu.
- **Mouse push (hidden):** moving the mouse over any particle visual (everything the picker tags Particles, e.g. Cathedral Light's dust, not just the Particles family) pushes the dots near it aside with a little swirl, and they ease back over a couple of seconds while still moving with the music. Only a moving mouse pushes; a resting one does nothing. Pushed dots draw a short motion streak (capped, so a flick stays a dot, not a smear); a dot the visual respawns, or moves far in one frame (wrapping round an edge), drops its push. Holding the left button calls dots in from around the press: about 40% of the dots within reach, nearest first, spiral in over a second or so and gather into a swirling cloud round the mouse that rides with it; letting go throws the cloud on with the mouse's speed while the dots stream back home. Only a share comes, so the picture thins where you clicked rather than losing a chunk (lifting the whole patch into a ball cut a hole and packed a white blob). The cloud is worked out each frame from where the visual draws its dots, so it stays crisp (grabbing dots one by one scattered them into a haze). Visuals whose dots don't keep a place from frame to frame (dust that jumps about its shape every frame like Clifford Dust and the fractals, fast flows, sparks that live under a second: 11 visuals) declare `#define MOUSE_MOVES_PLACES`, and for those the moving mouse parts the dust where it's drawn instead of pushing dots one by one, which only smeared the picture. A click alone still does nothing else, double-click is still full screen. Shader-only visuals, MilkDrop, and AVS presets are never affected (classic presets stay as their authors made them). It can be turned off with **Mouse moves particles** (Options and the visuals' right-click menu, on by default; setting `MouseMovesParticles`). It all lives in the engine (`Push`, `Part` and `Gather` in `ParticleMain.hlsli`, `PointerStir` in Core), so visuals don't have to do anything beyond that one line when their dots jump.
- **Record a visual to video** from the right-click menu (MP4 with the song).
- **Detail levels** up to Ultra and 4K; resolution adapts to keep frame rate (`AutoResolution`).
- **Power** (Options › Graphics and the right-click menu; setting `Power`, `VisualsPower` in Core): **Auto** (default) picks one of the other three from what the computer reports, read every 5 seconds (`PowerSource` in each app): Power saver on battery, in Low Power Mode / Energy Saver, or when the Mac's thermal state is serious or worse; Balanced on a laptop on power; Smoothest on a desktop. The right-click menu shows what Auto is using now. **Smoothest** draws one frame per screen refresh. **Balanced** draws up to about 60 fps: on a screen faster than 90 Hz each frame stays up for whole refreshes (two at 120 or 144 Hz), about half the CPU, GPU and compositor work. **Power saver** draws up to about 30 fps at half the pixels (recordings are still drawn in full) and lets the system slow Lucent down behind other apps. Mac reads IOKit's power sources and `NSProcessInfo` (Low Power Mode, thermal state); Windows reads `GetSystemPowerStatus` and has no simple heat reading, so heat isn't part of Auto there.
- **Nothing is drawn** while the visuals are hidden, minimized, or can't be seen: on the Mac when the window is fully covered or on another Space (checked 5 times a second), on Windows when `Present` reports the window occluded.
- **Full frame rate in the background** (Smoothest and Balanced; off in Power saver): the visuals keep their frame rate while another app has focus, such as with Lucent on a second screen. Both systems otherwise slow down an app that isn't in front. Windows: Lucent opts out of power throttling (EcoQoS and the lower quality of service unfocused windows get) and the render thread runs above normal priority. Mac: the render thread asks for user-interactive quality of service, and while the visuals are on screen (drawing and not fully covered) Lucent holds a latency-critical user activity so App Nap and timer coalescing stay off. Minimized or hidden visuals still draw nothing, and on the Mac a fully covered window lets the activity go (`BackgroundFrameRate` in each app). A graphics driver's own background cap, such as NVIDIA's "Background Application Max Frame Rate", still applies.

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
