#!/usr/bin/env bash
# Reads changed file paths, one per line, and prints which apps they change: "windows mac", "windows",
# "mac", or nothing when only the website changed. Anything it doesn't know is counted as both apps. The
# Mac app is built from the Windows app's visuals, shaders, assets and starter music too (see
# Lucent.Mac.csproj and tools/MetalShaders), so those count as both.
windows=0 mac=0 any=0
while IFS= read -r file; do
  [ -n "$file" ] || continue
  any=1
  case "$file" in
    site/* | .github/workflows/pages.yml) ;;
    src/Lucent.Mac/* | tools/mac/* | tools/build-lame-mac.sh | tools/build-pluginhost-mac.sh | tools/package-mac.sh) mac=1 ;;
    src/WinampInspiredVisualizer/Presets/* | src/WinampInspiredVisualizer/Shaders/* | src/WinampInspiredVisualizer/Assets/* | src/WinampInspiredVisualizer/StarterMusic/*) windows=1 mac=1 ;;
    src/WinampInspiredVisualizer/* | tools/drag-diagnose.ps1) windows=1 ;;
    *) windows=1 mac=1 ;;
  esac
done
[ "$any" -eq 1 ] || { windows=1 mac=1; }
apps=()
[ "$windows" -eq 0 ] || apps+=(windows)
[ "$mac" -eq 0 ] || apps+=(mac)
echo "${apps[*]}"
