#!/usr/bin/env bash
# Reads changed file paths, one per line, and prints which apps they change: "windows mac", "windows",
# "mac", or nothing when only the website or the specs in docs/specs changed. Anything it doesn't know is counted as both apps. The
# visuals, shaders and starter song in content/ go into both apps, and so does the Windows app's Assets
# folder (the Mac app takes its icon from there, see Lucent.Mac.csproj), so those count as both.
#
# The paths from before the folder tidy-up (PRs #328 and #332) are listed too: main's build sorts every
# past merge by the files it changed, for the apps' what's new. Before #328 the shared visuals, shaders and
# starter song lived in src/WinampInspiredVisualizer, so those old folders still count as both apps. Any
# later folder move has to list its old paths here the same way.
#
# With --shipped, it also leaves out files that don't change what people install: the docs, CI's own
# checks and test media, and the Mac app's website screenshot code. Pull requests still build and test
# for those; main just doesn't publish a new build (and an update prompt) for them. The next build
# that does publish carries them.
shipped=0
[ "${1:-}" = "--shipped" ] && shipped=1
windows=0 mac=0 any=0
while IFS= read -r file; do
  [ -n "$file" ] || continue
  any=1
  if [ "$shipped" -eq 1 ]; then
    case "$file" in
      README.md | CONTRIBUTING.md | TRADEMARK.md | LICENSE | docs/* | tools/ci/* | tools/test-media/* | tools/LayoutCheck/* | tools/AvsCheck/* | tools/MilkDropCheck/* \
        | tools/ParityCheck/* | tools/mac/launch-check.sh | tools/mac/make-test-music.sh \
        | .github/workflows/drag-diagnose.yml | tools/windows/drag-diagnose.ps1 | src/Lucent.Mac/Diagnostics/Screenshots*.cs \
        | tools/drag-diagnose.ps1 | src/Lucent.Mac/Screenshots*.cs) continue ;;
    esac
  fi
  case "$file" in
    site/* | .github/workflows/pages.yml | docs/specs/*) ;;
    src/Lucent.Mac/* | tools/mac/* | tools/build-lame-mac.sh | tools/build-pluginhost-mac.sh | tools/package-mac.sh) mac=1 ;;
    src/Lucent.Windows/Assets/* | src/WinampInspiredVisualizer/Assets/* | src/WinampInspiredVisualizer/Presets/* \
      | src/WinampInspiredVisualizer/Shaders/* | src/WinampInspiredVisualizer/StarterMusic/*) windows=1 mac=1 ;;
    src/Lucent.Windows/* | tools/windows/* | src/WinampInspiredVisualizer/* | tools/drag-diagnose.ps1) windows=1 ;;
    *) windows=1 mac=1 ;;
  esac
done
[ "$any" -eq 1 ] || { windows=1 mac=1; }
apps=()
[ "$windows" -eq 0 ] || apps+=(windows)
[ "$mac" -eq 0 ] || apps+=(mac)
echo "${apps[*]}"
