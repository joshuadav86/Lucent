# Winamp-Inspired Visualizer

A modern, open-source music player for Windows with GPU-powered visualizations inspired by the classic Winamp era.

> Early days: the music player works. The GPU visualizer is coming next.

## Features

- Plays MP3, FLAC, WAV, AAC/M4A, ALAC, WMA, OGG Vorbis, Opus and AIFF
- Add files or whole folders, or drag them onto the window
- Playlist with play, pause, stop, previous, next, seek and volume
- Shows track title, artist and length from the file's tags

**Shortcuts:** Space plays or pauses, Enter plays the selected track, Delete removes selected tracks from the playlist.

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

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows.

```
dotnet run --project src/WinampInspiredVisualizer
```

## License

[MIT](LICENSE). Not affiliated with or endorsed by Winamp.
