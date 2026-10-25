#!/bin/bash
# Builds libmp3lame.dylib, the MP3 encoder the Mac app's Mp3Writer loads, as one universal (Apple silicon
# and Intel) library, and puts it in artifacts/mac-native, where src/Lucent.Mac picks it up.
# Runs on a Mac with the Xcode command line tools. Usage: tools/mac/build-lame-mac.sh [output folder]
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="${1:-$root/artifacts/mac-native}"
version=3.100
sha256=ddfe36cab873794038ae2c1210557ad34857a4b6bdc515785d1da9e175b1da1e
# The same as LSMinimumSystemVersion in tools/mac/package-mac.sh.
export MACOSX_DEPLOYMENT_TARGET=12.0

work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
curl -fsSL -o "$work/lame.tar.gz" "https://downloads.sourceforge.net/project/lame/lame/$version/lame-$version.tar.gz"
echo "$sha256  $work/lame.tar.gz" | shasum -a 256 -c -
tar -xzf "$work/lame.tar.gz" -C "$work"
source="$work/lame-$version"
# 3.100's list of exports names a function it no longer has, which Apple's linker refuses.
sed -i '' '/lame_init_old/d' "$source/include/libmp3lame.sym"

for arch in arm64 x86_64; do
  build="$work/build-$arch"
  mkdir -p "$build"
  (
    cd "$build"
    host=$([ "$arch" = arm64 ] && echo aarch64-apple-darwin || echo x86_64-apple-darwin)
    CC="clang -arch $arch" CFLAGS="-O2" "$source/configure" --host="$host" --prefix="$build/install" \
      --disable-frontend --disable-static --enable-shared --disable-dependency-tracking > /dev/null
    make -j"$(sysctl -n hw.ncpu)" > /dev/null
    make install > /dev/null
  )
done

mkdir -p "$output"
lipo -create "$work/build-arm64/install/lib/libmp3lame.0.dylib" "$work/build-x86_64/install/lib/libmp3lame.0.dylib" \
  -output "$output/libmp3lame.dylib"
# Found next to Lucent rather than at the prefix it was built for.
install_name_tool -id @rpath/libmp3lame.dylib "$output/libmp3lame.dylib"
lipo -info "$output/libmp3lame.dylib"
# LAME is LGPL: its licence ships with the app.
cp "$source/COPYING" "$output/LICENSE-LAME.txt"
echo "Made $output/libmp3lame.dylib (LAME $version, LGPL)"
