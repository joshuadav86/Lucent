# Winamp-Inspired Visualizer

A modern, open-source music player for Windows with GPU-powered visualizations inspired by the classic Winamp era.

> Early days: the music player and GPU visualizer work. Themes and more visuals are next.

## Features

- Plays MP3, FLAC, WAV, AAC/M4A, ALAC, WMA, OGG Vorbis, Opus and AIFF
- Add files or whole folders, or drag them onto the window
- Playlist with play, pause, stop, previous, next, seek and volume
- Shows track title, artist and length from the file's tags
- GPU visualizer (Direct3D 11) that reacts to bass, mids, treble and beats, with MilkDrop-style feedback trails
- 19 visuals, including fractals like Crystal Kaleidoscope, Julia Kaleidoscope, Apollonian Lace and Sacred Geometry, and fly-through worlds like Menger Voyage, Kaleido Cathedral, Gyroid Caverns, Hyperspace Rings, Neon Lattice and Sierpinski Dive
- A visual picker: click the visual's name in the player bar (or press V) to choose any visual from a list
- Smooth transitions between visuals (crossfade, expanding ring, cloudy dissolve or spiral sweep), and an Auto mode that moves to a new visual on the beat every 25 seconds or so
- Fullscreen visuals: double-click the visualizer or press F11

**Shortcuts:** Space plays or pauses, Enter plays the selected track, Delete removes selected tracks from the playlist, N and B switch to the next or previous visual, V opens the visual picker, A turns Auto mode on or off, F11 toggles fullscreen and Esc leaves it.

## Download and run

1. Go to the [Latest build](../../releases/tag/latest) release.
2. Download `WinampInspiredVisualizer-win-x64.zip` and unzip it.
3. Double-click `WinampInspiredVisualizer.exe`.

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

`Bass`, `Mid`, `Treble`, `Beat`, `Level`, `Time`, `spectrum(x)`, `waveform(x)` and helpers like `previous`, `rotate`, `kaleidoscope`, `palette`, `neon`, `polygon`, `glow`, `fbm` and `cameraRay` (for fly-through visuals) are listed in [`Shaders/Common.hlsli`](src/WinampInspiredVisualizer/Shaders/Common.hlsli). Drop a new `.hlsl` file into the `Presets` folder and restart the app to see it. Every pull request checks that all presets compile.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet run --project src/WinampInspiredVisualizer
```

## License

[MIT](LICENSE). Not affiliated with or endorsed by Winamp.
