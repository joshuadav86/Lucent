# Windows and Mac parity

Lucent for Windows (`src/WinampInspiredVisualizer`) and Lucent for the Mac (`src/Lucent.Mac`) are meant to be the same app: every window, option, and feature on both. A new feature lands on both platforms in the same pull request. When it can't (the other platform has no way to do it, or it's being ported separately), it's listed here, with why and what the other platform has instead.

Every pull request runs `dotnet run --project tools/ParityCheck`, which checks three things and fails if one of them is on one platform without being listed in a table below:

1. Every source file in one app has its counterpart at the same place in the other. A WPF window's `.xaml` and `.xaml.cs` pair with an Avalonia window's `.axaml` and `.axaml.cs`.
2. Every setting in `Lucent.Core/Settings` is used by both apps.
3. Every public type in `Lucent.Core` that one app uses, the other uses too.

It also fails when an entry here is out of date, such as a file that's now on both platforms, so take an entry off the list when its gap closes. In the tables, the first column names files as each app has them (under its own folder), settings, or `Lucent.Core` types, in `code`; the second says which app has them. Rows without `code` are notes the check doesn't read.

## Features on one platform

| What | Only on | Why, and what the other platform has |
| --- | --- | --- |
| MusicBee import button | Windows | MusicBee is Windows-only. On a Mac, the Apple Music import takes a MusicBee library file too. |

## Same feature, different platform pieces

Each platform does these its own way. The feature is on both; only the files differ.

| What | Only on | Its counterpart on the other platform |
| --- | --- | --- |
| Drawing the visuals: `Visualizer/Renderer.cs`, `Visualizer/Renderer.Offscreen.cs`, `Visualizer/ShaderCache.cs`, `Visualizer/PresetLibrary.cs`, `Visualizer/MilkDrop/MilkDropRenderer.cs`, `Visualizer/MilkDrop/MilkDropVisual.cs` | Windows | Direct3D 11 with HLSL compiled as it loads. The Mac draws the same visuals with Metal: `Visuals/` (shaders translated from the HLSL when the app is built, and by DXC as it runs for your own visuals). |
| Drawing the visuals with Metal: `Visuals/MetalRenderer.cs`, `Visuals/MetalView.cs`, `Visuals/VisualEngine.cs`, `Visuals/MetalSupport.cs`, `Visuals/MetalTranslation.cs`, `Visuals/MetalShaderFiles.cs`, `Visuals/ShaderCache.cs`, `Visuals/DxcCompiler.cs`, `Visuals/MetalPipelines.cs`, `Visuals/MilkDropRenderer.cs`, `Visuals/MilkDropVisual.cs`, `Visuals/VisualEngine.Offscreen.cs` | Mac | Windows' `Visualizer/Renderer.cs` and friends. |
| The library's store headings and folder subheadings: `Windows/FolderGroupStyleSelector.cs` | Windows | WPF picks each group's look with a style selector. The Mac lays out its heading and subheading rows itself in `Library/LibraryRows.cs`. |
| Exclusive (bit-perfect) output through WASAPI: `Audio/PcmOutput.cs` | Windows | WASAPI exclusive mode is Windows-only. The Mac does the same with Core Audio: `Audio/ExclusiveDevice.cs` takes the device in hog mode (other apps go quiet) and switches it to each track's own sample rate. |
| Exclusive (bit-perfect) output through Core Audio: `Audio/ExclusiveDevice.cs` | Mac | Windows' `Audio/PcmOutput.cs`, through WASAPI exclusive mode. |
| Playing sound through Core Audio: `Audio/AudioQueueOutput.cs`, `Audio/MacAudioFileReader.cs`, `Interop/CoreAudio.cs` | Mac | Windows plays through WASAPI (NAudio, and `Audio/PcmOutput.cs` for exclusive mode) and decodes with Media Foundation and NAudio. The Mac decodes the same formats with macOS's own decoders (Extended Audio File Services). |
| Handing recorded frames to the encoder: `Export/VideoFrameConverter.cs` | Windows | Windows converts each frame to NV12 on the graphics card for Media Foundation. The Mac reads each frame back from Metal into AVFoundation's pixel buffers: `Export/VideoFrameReader.cs`. |
| Handing recorded frames to the encoder: `Export/VideoFrameReader.cs` | Mac | Windows' `Export/VideoFrameConverter.cs`. |
| macOS interop: `Interop/ObjC.cs`, `Interop/AppKit.cs`, `Interop/CoreGraphics.cs`, `Interop/StringHelper.cs`, `Interop/WebKit.cs`, `Interop/Keychain.cs`, `Interop/AVFoundation.cs`, `Interop/CoreMedia.cs`, `Interop/AudioTap.cs` | Mac | WPF and Windows' own APIs. The Bandcamp and Spotify sign-ins use the keychain on a Mac, DPAPI on Windows. Listening to other apps uses a Core Audio process tap on a Mac (`Interop/AudioTap.cs`), NAudio's WASAPI loopback on Windows. |
| Listening to other apps' sound (Audio/SystemAudio.cs, on both) | Both | Windows listens through WASAPI loopback on the default output; music Lucent plays in exclusive mode skips Windows' mixer, so loopback can't hear it. The Mac uses a Core Audio process tap, which needs macOS 14.2 or later and the user's OK under "System Audio Recording Only" (the menu item is greyed out on older macOS). |
| Controlling Apple Music (Streaming/AppleMusicRemote.cs, on both) | Both | Apple's MusicKit only works on Apple's platforms and plays inside the app using it, so neither app uses it. Windows reads and presses the Apple Music app's (or iTunes') buttons through Windows' media controls. The Mac drives the Music app with AppleScript, after the user allows it under Automation. Neither needs an Apple sign-in. |
| Controlling YouTube in a browser (Streaming/YouTubeRemote.cs, on both) | Both | Windows reads and presses the browser's buttons through Windows' media controls, which Chrome, Edge, Firefox and the rest already report YouTube to, so nothing needs turning on. Windows doesn't say which site a browser is playing, so it's whatever the browser's media controls show. macOS has no media controls other apps may press, so the Mac finds the YouTube tab in Safari, Chrome, Edge or Brave with AppleScript and runs JavaScript in it. That needs the browser's "Allow JavaScript from Apple Events" turned on, and the user's OK under Automation. Firefox and Arc can't run JavaScript for AppleScript, so the Mac can't control YouTube in them. |
| Other apps' players through Windows' media controls: `Streaming/MediaControlsRemote.cs` | Windows | What Windows' Apple Music and YouTube control share. The Mac reaches the Music app and browsers with AppleScript, in each one's own file. |
| The menu bar: `MainWindow.MenuBar.cs` | Mac | Macs have one menu bar at the top of the screen. Windows has the same commands in the player's right-click menu and keyboard shortcuts. |
| WPF stand-ins for Avalonia: `MessageWindow.axaml`, `MessageWindow.axaml.cs`, `Windows/Placement.cs`, `Windows/ResourceReferences.cs`, `Windows/Chrome.cs`, `Library/LibraryRows.cs`, `Editor/EditorFonts.cs`, `Editor/AudioClipboard.cs` | Mac | What WPF has built in: MessageBox, window bounds, SetResourceReference, WindowChrome, grouped lists, Consolas, and Clipboard.SetAudio. |
| Menus kept clear of the taskbar: `Windows/DropDownMenu.cs` | Windows | Avalonia's menus keep themselves on screen and clear of the Dock. |
| The self-test's sound checks and visual timing: `SelfTest.Sound.cs`, `SelfTest.Timing.cs` | Mac | Kept in their own files on the Mac. Windows' self-test doesn't check gapless or the outputs yet, and doesn't time the visuals: the slow ones turned up on Retina Macs. |
| Screenshot tool: `Screenshots.cs`, `Screenshots.TrackInfo.cs`, `MainWindow.Preview.cs`, `AnalyzerSampleMusic.cs`, `LayoutCheck.cs` | Mac | `Lucent --screenshots` draws the Mac windows on any OS (Linux included) for checking them by eye, and the layout check runs in CI. Windows' windows are checked on Windows. |
| `MilkTranslatedShader` | Mac | Both apps translate MilkDrop shaders with `MilkShaderTranslator`, but only the Mac's code names the result's type. |
