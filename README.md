# Winamp-Inspired Visualizer

A modern, open-source music player for Windows with GPU-powered visualizations inspired by the classic Winamp era.

> Early days: right now the app opens a window. Music playback comes next, then the visualizer.

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
