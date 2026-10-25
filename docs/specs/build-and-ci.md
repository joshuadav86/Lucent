# Build and CI

## Projects

| Project | What it is |
| --- | --- |
| `src/Lucent.Core` | Everything platform-free: audio, analysis, EQ, editor, library, radio, streaming, settings |
| `src/Lucent.Windows` | The Windows app (WPF, Direct3D 11). Its `Assets` (icons) are used by the Mac app too |
| `content` | Shared by both apps: `Presets` (the visuals), `Shaders`, and `StarterMusic` |
| `src/Lucent.Mac` | The Mac app (Avalonia, Metal, Core Audio) |
| `src/PluginHost` | VST3 helper process (C++, CMake) |
| `tools/*` | CI checks and build scripts (see below) |

## CI jobs (`build.yml`)

| Job | Runs on | Does | Required |
| --- | --- | --- | --- |
| `changes` | Ubuntu | Decides which apps a change touches (`tools/ci/apps-changed.sh`) | |
| `build` | Windows | Builds, compiles every shader, self-tests (draws every visual), packages, signs when set up | Yes |
| `parity` | Ubuntu | `tools/ParityCheck`, `tools/LayoutCheck`, Avalonia style guard | Yes |
| `metal-shaders` | Ubuntu | Translates every HLSL file to Metal | Yes |
| `dxc-mac` | Intel Mac | Builds DXC for the Mac app's own-visuals support | Yes |
| `stem-model` | Ubuntu | Exports and checks the Demucs model | Yes |
| `mac (x64, macos-15-intel, x86_64)` | Intel Mac | Builds, self-tests, launch check, packages, signs, notarizes | Yes |
| `mac (arm64, macos-15-intel, x86_64)` | Intel Mac | Cross-builds Apple silicon | No |
| `mac-arm64-check` | Apple silicon | Self-tests the arm64 build | No |

Renaming a required job or changing its matrix changes the check name; the ruleset must be updated in the same go, or every PR waits forever.

## Releases

- Every app-changing merge to main replaces the "latest" pre-release (`1.0.<run>`). Docs, CI checks, and test media don't publish.
- `release.yml` promotes "latest" to a stable `v<version>` release.
- Windows and Mac versions on "latest" can differ, since each only builds when it changed.

## Pull requests

Auto-merge: when the work is done, mark the PR ready and enable auto-merge. Keep Windows and Mac in the same PR.

## Self-tests

`Lucent --self-test` runs in CI on both platforms: formats, loudness, editor feed, music analysis, stems, recording, video, song map, and every visual.

## Local builds

- .NET 10 SDK. The WPF app builds on Linux with `EnableWindowsTargeting` but only runs on Windows.
- The Mac app builds and runs headless on Linux (screenshots, layout check).
- Mac visuals need Metal shaders first: `dotnet run --project tools/MetalShaders -- content artifacts/metal-shaders`.
