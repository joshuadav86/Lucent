<p align="center"><img src="assets/lucid-icon.png" width="128" alt="Lucid icon"></p>

# Lucid

A modern, open-source music player for Windows with GPU-powered visualizations inspired by the classic Winamp era.

> Early days: the music player, GPU visualizer, options and themes work.

## Features

- Plays MP3, FLAC, WAV, AAC/M4A, ALAC, WMA, OGG Vorbis, Opus and AIFF
- A small player window like Winamp's, with a big time display, a spectrum meter (click it for a waveform, or to hide it), a scrolling title, and the bitrate and sample rate
- The visuals, the queue and the library each have their own window. Their edges snap to each other and to the screen, and dragging the player carries every window docked to it. The visuals open with the player; the Visuals, Queue and Library switches in the player show or hide each one, and Lucid remembers where you left them
- Add files or whole folders with the eject button, or drag them onto any Lucid window
- Queue with play, pause, stop, previous, next, seek, volume (the mouse wheel over the player works too), shuffle, and repeat all or one
- A music library: link a folder and every song inside it is listed, newest first, with other sort orders and folder headings you can open or close
- Favourite songs: click the heart in the player (or press H), or right-click songs in the queue or library. The FAVOURITES pill in the library shows only them
- Playlists: save the queue as a playlist, add songs to one from the queue or library (right-click), and play them or add them to the queue from **Playlists** in the queue window. They're saved as standard .m3u8 files, so Winamp and other players can open them too
- Shows track title, artist and length from the file's tags
- Edit a song's info: right-click it in the queue or library (or the song name in the player) and choose **Edit info…**, or press Ctrl+I. Change the title, artist, album, album artist, year, track and disc numbers, genre, composer, BPM, comment and lyrics, and see the file's format, quality and size. Choose several songs to change them all at once (fields that differ say "several values" and stay as they are unless you type in them)
  - Artwork: add a cover by choosing a picture, dropping one onto the cover, or pasting one you've copied (Ctrl+V). Remove it, or save it as an image. Very large pictures are shrunk to 1600 pixels so files don't balloon
  - Saving is safe: the new info is written to a copy that's checked before it replaces the original, so a problem can't damage your music. Files that can't be saved are listed with the reason. The song playing only pauses for a moment
  - Songs keep their date when edited, so they stay put in the library's newest-first order (switch "Keep the files' dates" off if you'd rather they move to the top)
- An artwork window: click **Art** in the player, or press C, to see the playing song's cover as big as you like. It uses the cover stored in the file, or a folder.jpg, cover.jpg or front.jpg next to it. Pin it on top of other windows, and right-click it to copy or save the picture or edit the song's info
- GPU visualizer (Direct3D 11) that reacts to bass, mids, treble and beats, with MilkDrop-style feedback trails
- 71 visuals in six families:
  - **Fly-through worlds:** Menger Voyage, Gyroid Caverns, Hyperspace Rings, Mandelbox Tunnel, Fractal Temple, Crystal Cavern, Wormhole Transit, Folded Metropolis, Kleinian Reef, Fibre Optic Braid and more
  - **Kaleidoscopes and sacred geometry:** Crystal Kaleidoscope, Kaleido Cathedral, Sacred Geometry, Rose Window, Metatron's Cube, Flower of Life, Penrose Mosaic, Prism Mandala, Lotus Bloom, Droste Zoom and more
  - **Fractals:** Julia Kaleidoscope, Apollonian Lace, Mandelbrot Dive, Mandelbulb Bloom, Burning Ship, Kleinian Limit, Newton Basins, Kali Flame, Koch Snowflake, Fractal Forest and more
  - **Music-shaped:** Spectrum Tunnel, Neon Oscilloscope, Cymatic Plate, Pulsar Ridges, Spectral Sunflower, Spectrum Galaxy, Lissajous Laser, Plasma Orb, Caustic Rings, Beat Shards and more
  - **Cosmic and organic:** Bass Nebula, Aurora Veil, Black Hole, Cosmic Jellyfish, Supernova Remnant, Electric Storm, Ink Flow, Chrome Blobs, Shot Silk, Living Cells and more
  - **Particles:** hundreds of thousands of glowing particles moving on the graphics card every frame: Strange Attractors, Galaxy Collision, Firework Mandala, Silk Currents, Sonic Dandelion and Knot Stream
- A visual picker: click the visual's name under the visuals (the button with the screen-and-spark icon), or press V, to choose any visual from a list
- Favourite visuals and lists of visuals: star a visual in the picker, with the star under the visuals, or with F. The menu at the top of the picker switches between all visuals, your favourites and lists you make (right-click a visual to add it to one). The picker, N and B, and Auto mode then stick to that list
- Smooth transitions between visuals, including morphs where one visual melts, glows, flies or folds into the next, and an Auto mode that moves to a new visual on the beat every 25 seconds or so. The AUTO pill under the visuals shows how often; click the arrow beside it (or right-click the pill) to choose anything from 10 seconds to 3 minutes
- Options (the gear in the player's title bar, or O):
  - **Resolution:** draw visuals at the window's size, or at 1080p, 1440p or 4K. Above the window's size they're drawn bigger and shrunk to fit (supersampling), for smoother, finer detail
  - **Detail:** Low to Ultra, for more ray-marching steps and deeper fractals in the 3D and fractal visuals, and up to four times as many particles in the particle visuals
  - **Glow:** how strongly bright parts bloom, from off to strong
  - **HDR:** on screens with HDR turned on in Windows, highlights shine brighter than normal white
  - How often Auto mode changes visual, how long transitions take, morphs only, and an optional frame rate readout (shown under the visuals)
- Eight themes for everything around the visuals: Midnight, Deep Ocean, Glacier (light), Ultraviolet, Emerald, Ember, Graphite and Rosé
- Remembers the last visual, your options and your theme
- Visuals compile in the background and are kept on disk, so the app starts straight away
- Fullscreen visuals: double-click the visualizer or press F11

**Shortcuts** (from any Lucid window): Space plays or pauses, Z and X go to the previous or next track, S turns shuffle on or off, R switches between repeat off, all and one, Enter plays the selected track, Delete removes selected tracks from the queue, N and B switch to the next or previous visual, V opens the visual picker, W, Q and L show or hide the visuals, queue and library, O opens options, A turns Auto mode on or off, F stars or unstars the visual that's showing, H does the same for the song, C opens the artwork window, Ctrl+I edits the selected (or playing) song's info, F11 toggles fullscreen and Esc leaves it. The media keys on your keyboard work while Lucid has focus.

## Download and run

1. Go to the [Latest build](../../releases/tag/latest) release.
2. Download `Lucid-win-x64.zip` and unzip it.
3. Double-click `Lucid.exe`.

Nothing needs installing. If Windows SmartScreen warns about an unknown app, click **More info**, then **Run anyway**.

## Roadmap

1. Project setup and automatic builds
2. Music player: play, pause, seek, volume, playlist, and wide format support via FFmpeg
3. Visualizer engine: live audio analysis driving GPU shaders, with starter visuals
4. Themes, community visual presets, and a contributor guide

## Making your own visuals

Each visual is one HLSL pixel shader file in [`src/WinampInspiredVisualizer/Presets`](src/WinampInspiredVisualizer/Presets) (and in the `Presets` folder next to the app once downloaded). A preset defines one function:

```hlsl
// Name: My Visual
float3 Render(float2 uv, float2 p)
{
    float3 trail = previous(toUv(p * 1.02)) * 0.92;   // last frame, zoomed in a little
    float ring = glow(length(p) - 0.4 - Bass * 0.2, 120.0);
    return trail + hsv2rgb(Time * 0.1, 0.8, 1.0) * ring;
}
```

`Bass`, `Mid`, `Treble`, `Beat`, `Level`, `Time`, `spectrum(x)`, `waveform(x)` and helpers like `previous`, `rotate`, `kaleidoscope`, `palette`, `neon`, `polygon`, `glow`, `fbm` and `cameraRay` (for fly-through visuals) are listed in [`Shaders/Common.hlsli`](src/WinampInspiredVisualizer/Shaders/Common.hlsli). `qualitySteps(n)` and `fractalDepth(n)` scale loops with the Detail option, and `pixelSize()` keeps thin lines one screen pixel wide at every Resolution setting. Drop a new `.hlsl` file into the `Presets` folder and restart the app to see it. Every pull request checks that all presets compile.

A particle visual also defines `Spawn`, `Simulate` and `Show`, which start, move and draw each of its particles; the engine runs them for every particle each frame on the graphics card and adds the glowing dots into a layer that `Render` reads with `particles(uv)`. A `// Particles: 300000` line sets how many run on Normal detail. Random numbers, 3D projection with lens blur, curl-noise flow and motion streaks are in [`Shaders/Particles.hlsli`](src/WinampInspiredVisualizer/Shaders/Particles.hlsli).

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet run --project src/WinampInspiredVisualizer
```

## License

[MIT](LICENSE). Not affiliated with or endorsed by Winamp.
