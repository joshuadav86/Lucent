<p align="center"><img src="assets/lucid-icon.png" width="112" alt="Lucid icon"></p>

<h1 align="center">Lucid</h1>

<p align="center"><b>A music player for Windows with 88 GPU visuals that dance to your music.</b><br>
Inspired by Winamp and MilkDrop, rebuilt for modern graphics cards.</p>

<p align="center"><a href="../../releases/tag/latest"><b>⬇ Download the latest build</b></a> · Windows 10 and 11 · free and open source</p>

![Lucid's Black Hole visual, fullscreen](assets/screenshots/hero-black-hole.jpg)

## A player that looks the part

The player, equalizer, queue, library and visuals each get their own window, and they snap together like Winamp's did. Drag the player and everything docked to it comes along. Press D to tidy them back into one neat rectangle.

![The Lucid player with its library, equalizer, queue and visuals docked together](assets/screenshots/player-layout.jpg)

## 88 visuals, drawn live on your graphics card

Fly through fractal worlds, kaleidoscopes, galaxies made of hundreds of thousands of particles, and rain-soaked neon cities. Every visual reacts to the bass, mids, treble and beat of what's playing. Auto mode listens to each song ahead of time and switches visuals right on a downbeat.

<table>
<tr>
<td><img src="assets/screenshots/sunset-highway.jpg" alt="Sunset Highway"><br><sub>Sunset Highway</sub></td>
<td><img src="assets/screenshots/mandelbulb-bloom.jpg" alt="Mandelbulb Bloom"><br><sub>Mandelbulb Bloom</sub></td>
<td><img src="assets/screenshots/rose-window.jpg" alt="Rose Window"><br><sub>Rose Window</sub></td>
</tr>
<tr>
<td><img src="assets/screenshots/galaxy-collision.jpg" alt="Galaxy Collision"><br><sub>Galaxy Collision</sub></td>
<td><img src="assets/screenshots/kaleido-cathedral.jpg" alt="Kaleido Cathedral"><br><sub>Kaleido Cathedral</sub></td>
<td><img src="assets/screenshots/flower-of-life.jpg" alt="Flower of Life"><br><sub>Flower of Life</sub></td>
</tr>
<tr>
<td><img src="assets/screenshots/neon-downpour.jpg" alt="Neon Downpour"><br><sub>Neon Downpour</sub></td>
<td><img src="assets/screenshots/menger-voyage.jpg" alt="Menger Voyage"><br><sub>Menger Voyage</sub></td>
<td><img src="assets/screenshots/cosmic-jellyfish.jpg" alt="Cosmic Jellyfish"><br><sub>Cosmic Jellyfish</sub></td>
</tr>
</table>

## Find the right visual in seconds

Press V and start typing. Visuals are sorted into families and tagged (Calm, Intense, Neon, Space, 3D and more), and you can star favorites or make your own lists for Auto mode to play from.

![The visual picker, searching for Kaleido Cathedral](assets/screenshots/visual-picker.jpg)

## Fullscreen, without losing control

Double-click the visuals (or press F11) to go fullscreen. Click once to pause or play, and right-click for a menu with a seek bar, track controls, volume and every graphics option.

![The right-click menu over the Neon Downpour visual in fullscreen](assets/screenshots/fullscreen-menu.jpg)

## Highlights

- **Plays almost anything:** MP3, FLAC, WAV, AAC/M4A, ALAC, WMA, OGG Vorbis, Opus and AIFF
- **A real library:** link a folder and every song shows up, searchable as you type, with favorites and playlists that Winamp can open too
- **Equalizer:** 4 to 32 bands, 15 presets, and a live response curve over the music's spectrum
- **Analyzer:** a scrolling heatmap of every frequency over time, the waveform, a live spectrum and a stereo spread scope, like the Vision plugin
- **Built-in audio editor:** trim, fade, normalize, change pitch and more, with undo
- **Converter and tag editor:** make MP3, FLAC or WAV, and edit titles, artists and cover art safely
- **Graphics options:** up to 4K supersampling, Ultra detail, adjustable glow and HDR
- **Five skins and eight themes:** skins change the shape, type and finish of every window (Classic's bevels and LCD, Glass, Neon, brushed-metal Studio), themes change the colours, and any skin goes with any theme
- **Make your own visuals:** each one is a single shader file (see below)

## Download and run

1. Go to the [Latest build](../../releases/tag/latest) release.
2. Download `Lucid-win-x64.zip`, right-click it and choose **Extract All**.
3. Double-click `Lucid.exe` in the extracted folder.

Nothing needs installing. If Windows SmartScreen warns about an unknown app, click **More info**, then **Run anyway**. Lucid.exe works on its own too, but keep the Presets folder beside it if you want to change or add visuals.

## Everything Lucid does

<details>
<summary>The full feature list and keyboard shortcuts</summary>

- Plays MP3, FLAC, WAV, AAC/M4A, ALAC, WMA, OGG Vorbis, Opus and AIFF
- A small player window like Winamp's, with a big time display, a spectrum meter (click it for a waveform, or to hide it), a scrolling title, and the bitrate and sample rate
- The visuals, the queue and the library each have their own window. Their edges snap to each other and to the screen (keep dragging to pull a window free), and a window dropped beside others with one corner lined up stretches a little to line up its other corner too, and dragging the player carries every window docked to it, including ones that are switched off. The equalizer opens with the player, docked right underneath it like Winamp's, with the queue below it. The visuals and library docked beside the player always line up with the bottom of the column under it, so the windows form one neat rectangle whichever of the equalizer and queue are open. The queue and library always open docked to the player (the queue underneath, the library to the left), and the artwork window opens docked to the outside of the group wherever there's room, and you can drag any of them away from there. Resizing a window resizes the windows docked to it: drag the edge between two windows to move both, or an outside edge they share (like the bottom of the queue and the visuals) to stretch them together; hold Ctrl to resize just one. Press D, or right-click the window switches in the player, to dock every window back around the player, G to fill the screen with them, or K to shrink them all to their smallest. The visuals open with the player; the Visuals, Queue and Library switches in the player show or hide each one, and Lucid remembers where you left them
- Add files or whole folders with the eject button, or drag them onto any Lucid window
- Queue with play, pause, stop, previous, next, seek, volume (the mouse wheel over the player works too), shuffle, and repeat all or one
- A music library: link a folder and every song inside it is listed, newest first, with other sort orders and folder headings you can open or close. Type in the search box at the top (or press Ctrl+F) to find songs by title, artist, album or file name as you type; Esc clears it
- Favorite songs: click the heart in the player (or press H), or right-click songs in the queue or library. The FAVORITES pill in the library shows only them
- Playlists: save the queue as a playlist, add songs to one from the queue or library (right-click), and play them or add them to the queue from **Playlists** in the queue window. They're saved as standard .m3u8 files, so Winamp and other players can open them too
- Shows track title, artist and length from the file's tags
- Edit a song's info: right-click it in the queue or library (or the song name in the player) and choose **Edit info…**, or press Ctrl+I. Change the title, artist, album, album artist, year, track and disc numbers, genre, composer, BPM, comment and lyrics, and see the file's format, quality and size. Choose several songs to change them all at once (fields that differ say "several values" and stay as they are unless you type in them)
  - Artwork: add a cover by choosing a picture, dropping one onto the cover, or pasting one you've copied (Ctrl+V). Remove it, or save it as an image. Very large pictures are shrunk to 1600 pixels so files don't balloon
  - Saving is safe: the new info is written to a copy that's checked before it replaces the original, so a problem can't damage your music. Files that can't be saved are listed with the reason. The song playing only pauses for a moment
  - Songs keep their date when edited, so they stay put in the library's newest-first order (switch "Keep the files' dates" off if you'd rather they move to the top)
- An artwork window: click **Art** in the player, or press C, to see the playing song's cover as big as you like. It uses the cover stored in the file, or a folder.jpg, cover.jpg or front.jpg next to it. Pin it on top of other windows, and right-click it to copy or save the picture or edit the song's info
- An audio editor: click the waveform button in the player's title bar, or right-click a song in the queue or library and choose **Open in editor…**. It shows the song's left and right channels; drag to select part of it (Shift extends, Ctrl+A selects everything), the wheel zooms and Shift+wheel scrolls, and Space plays from the cursor or plays the selection
  - Effects change the selection, or the whole song when nothing is selected: normalize (to a peak of −0.1, −1 or −3 dB, or to a loudness of −14, −16 or −23 LUFS), change the pitch by up to 12 semitones either way in 0.1 steps (keeping the speed, or like a faster or slower record), gain, fade in and out, trim, delete, silence and reverse. Ctrl+Z undoes and Ctrl+Y redoes
  - **Save** replaces the original after asking (formats Lucid can't write, like M4A or OGG, are saved as a new FLAC file instead). **Save as new version** keeps the original and suggests "Song (edited)". The tags and artwork come along, and the new file is checked before it replaces anything
- A converter: right-click songs and choose **Convert to…**, or use the waveform button. Make MP3 (128 to 320 kbps, or VBR V0 and V2), FLAC (16 or 24-bit) or WAV (16, 24 or 32-bit float), keep the sample rate or change it to 44.1 or 48 kHz, and put the new files next to the originals or in a folder you choose. Several songs convert at once, tags and artwork are copied, and nothing is replaced without asking
- GPU visualizer (Direct3D 11) that reacts to bass, mids, treble and beats, with MilkDrop-style feedback trails
- 94 visuals in eight families:
  - **Fly-through worlds:** Menger Voyage, Gyroid Caverns, Hyperspace Rings, Mandelbox Tunnel, Fractal Temple, Crystal Cavern, Wormhole Transit, Folded Metropolis, Kleinian Reef, Fibre Optic Braid and more
  - **Kaleidoscopes and sacred geometry:** Crystal Kaleidoscope, Kaleido Cathedral, Sacred Geometry, Rose Window, Metatron's Cube, Flower of Life, Penrose Mosaic, Prism Mandala, Lotus Bloom, Droste Zoom and more
  - **Fractals:** Julia Kaleidoscope, Apollonian Lace, Mandelbrot Dive, Mandelbulb Bloom, Burning Ship, Kleinian Limit, Newton Basins, Kali Flame, Koch Snowflake, Fractal Forest and more
  - **Music-shaped:** Spectrum Tunnel, Neon Oscilloscope, Cymatic Plate, Pulsar Ridges, Spectral Sunflower, Spectrum Galaxy, Lissajous Laser, Plasma Orb, Caustic Rings, Beat Shards and more
  - **Cosmic and organic:** Bass Nebula, Aurora Veil, Black Hole, Cosmic Jellyfish, Supernova Remnant, Electric Storm, Ink Flow, Chrome Blobs, Shot Silk, Living Cells and more
  - **Particles:** hundreds of thousands of glowing particles moving on the graphics card every frame: Strange Attractors, Galaxy Collision, Firework Mandala, Silk Currents, Sonic Dandelion, Knot Stream, Lava Lamp, Fireworks Night, Abyssal Glow, Stellar Nursery, Grand Spiral and Orrery, and five built on sacred geometry: Sand Yantra, Seed of Life, Platonic Nest, Torus Field and Golden Angle
  - **Cyberpunk:** rain-soaked neon cities and retro-futurist worlds: Neon Downpour, Sunset Highway, Hologram Billboards, Neon Alley and Circuit City
  - **Ray traced:** clean, physically lit 3D scenes with real reflections, glass that bends light, and soft shadows: Glass and Chrome, Newton's Cradle, Glass Prism, Sunlit Pool, Hall of Mirrors and Gyroscope
- A visual picker: click the visual's name under the visuals (the button with the screen-and-spark icon), or press V, to choose any visual from a list. Just start typing to search it by name, family or tag: Enter shows the first match, Esc clears the search, and N and B, and Auto mode, stick to the matches
- Favorite visuals and lists of visuals: star a visual in the picker, with the star under the visuals, or with F. The menu at the top of the picker switches between all visuals, your favorites and lists you make (right-click a visual to add it to one). The picker, N and B, and Auto mode then stick to that list
- Folders and tags in the visual picker: visuals are listed in folders by family (Fractal, Cosmic, Particles and so on) that you can open or close, or switch off with the FOLDERS pill. Tick tags on the left (Calm, Intense, Neon, Space, 3D and more) to show only visuals with any of those tags; CLEAR shows everything again. N and B, and Auto mode, stick to what the picker shows
- Smooth transitions between visuals, including morphs where one visual melts, glows, flies or folds into the next, and an Auto mode that moves to a new visual on the beat every 25 seconds or so. Lucid listens to each song ahead of time to find its bars, so Auto's changes land right on a downbeat, at the start of a phrase where it can. The AUTO pill under the visuals shows how often; click the arrow beside it (or right-click the pill) to choose anything from 10 seconds to 3 minutes
- Options (the gear in the player's title bar, or O):
  - **Resolution:** draw visuals at the window's size, or at 1080p, 1440p or 4K. Above the window's size they're drawn bigger and shrunk to fit (supersampling), for smoother, finer detail
  - **Detail:** Low to Ultra, for more ray-marching steps and deeper fractals in the 3D and fractal visuals, and up to four times as many particles in the particle visuals
  - **Glow:** how strongly bright parts bloom, from off to strong
  - **HDR:** on screens with HDR turned on in Windows, highlights shine brighter than normal white
  - How often Auto mode changes visual, how long transitions take, morphs only, and an optional frame rate readout (shown under the visuals)
- A Winamp-style equalizer under the player, the same width, open from the start (the EQ switch in the player, or E, shows or hides it, and the queue moves to make room): choose 4, 8, 16, 24 or 32 bands, drag the sliders (double-click one to put it back to 0) with the response curve drawn live above them over the music's live spectrum, so problem frequencies show up as peaks, set the preamp, and pick from 15 presets such as Bass Boost, Rock, Vocal, Late Night and Small Speakers, or save your own. Changing the band count keeps your curve (the new sliders rise into place one after another, and presets and Reset glide the sliders and curve to their new settings), the visuals react to the equalized sound, and Lucid turns the volume down a touch when you boost so the music doesn't distort
- An analyzer window (the heatmap button in the player, or Y) that paints the music as a heatmap scrolling right to left, low notes at the bottom and high ones at the top, coloured by how loud each frequency is, with the waveform scrolling underneath in step, like Winamp's Vision plugin. Beside them are the spectrum right now, as bars on the same frequency scale with peak marks, and a stereo spread scope that shows how wide the mix is, with a bar for how alike the two sides are. Choose which of the four to show with Panels (or a right-click), pick Classic (blue through red), Fire or Theme colours, right-click to scroll slower or faster, and point at it to read the frequency. It docks and snaps with the other windows and follows the skin and theme, and it holds still while the music is paused so you can look closer
- Eight themes for everything around the visuals: Midnight, Deep Ocean, Glacier (light), Ultraviolet, Emerald, Ember, Graphite and Rosé
- Five skins that change the whole look: Lucid, Classic, Glass, Neon and Studio. Each comes with a theme it was made for, and you can pick another theme to go with it
- Remembers the last visual, your options, your skin and your theme
- Visuals compile in the background and are kept on disk, so the app starts straight away
- Fullscreen visuals: double-click the visualizer or press F11. To leave, press Esc, double-click again, click the × that appears in the top-right corner when you move the mouse, or right-click and choose Leave fullscreen
- Click the visuals once to pause or play the music, in a window or fullscreen
- Right-click the visuals for a menu: fullscreen, a bar to move through the song, play or pause, stop, next and previous track and a volume slider (in fullscreen, where the player is hidden), the next or previous visual, the picker, favorite, Auto and how often it changes, and the graphics options (resolution, detail, glow, HDR and the frame rate readout), in fullscreen too

**Shortcuts** (from any Lucid window): Space plays or pauses, Z and X go to the previous or next track, S turns shuffle on or off, R switches between repeat off, all and one, Enter plays the selected track, Delete removes selected tracks from the queue, N and B switch to the next or previous visual, V opens the visual picker, W, Q and L show or hide the visuals, queue and library, D docks them all back around the player, G stretches them to fill the screen, K shrinks them all to their smallest, O opens options, E opens the equalizer, Y opens the analyzer, A turns Auto mode on or off, F stars or unstars the visual that's showing, H does the same for the song, C opens the artwork window, Ctrl+I edits the selected (or playing) song's info, F11 toggles fullscreen and Esc leaves it. The media keys on your keyboard work while Lucid has focus.

</details>

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

`Bass`, `Mid`, `Treble`, `Beat`, `Level`, `Time`, `spectrum(x)`, `waveform(x)` and helpers like `previous`, `rotate`, `kaleidoscope`, `palette`, `neon`, `polygon`, `glow`, `fbm` and `cameraRay` (for fly-through visuals) are listed in [`Shaders/Common.hlsli`](src/WinampInspiredVisualizer/Shaders/Common.hlsli). `qualitySteps(n)` and `fractalDepth(n)` scale loops with the Detail option, and `pixelSize()` keeps thin lines one screen pixel wide at every Resolution setting. Optional `// Family: Fractal` and `// Tags: Calm, Neon, 3D` lines put a visual in a picker folder and give it tags (the families and tags are listed in [`Visualizer/VisualStyles.cs`](src/WinampInspiredVisualizer/Visualizer/VisualStyles.cs); without them it goes in Other). Drop a new `.hlsl` file into the `Presets` folder and restart the app to see it. Every pull request checks that all presets compile.

A particle visual also defines `Spawn`, `Simulate` and `Show`, which start, move and draw each of its particles; the engine runs them for every particle each frame on the graphics card and adds the glowing dots into a layer that `Render` reads with `particles(uv)`. A `// Particles: 300000` line sets how many run on Normal detail. Random numbers, 3D projection with lens blur, curl-noise flow and motion streaks are in [`Shaders/Particles.hlsli`](src/WinampInspiredVisualizer/Shaders/Particles.hlsli).

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet run --project src/WinampInspiredVisualizer
```

## License

[MIT](LICENSE). Not affiliated with or endorsed by Winamp.

MP3s are made with [LAME](https://lame.sourceforge.io/) (LGPL), which ships as the separate `libmp3lame.64.dll` and `libmp3lame.32.dll` next to `Lucid.exe` and can be swapped for your own build. It's used through [NAudio.Lame](https://github.com/Corey-M/NAudio.Lame) (MIT).
