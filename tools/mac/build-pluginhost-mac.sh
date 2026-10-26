#!/bin/bash
# Builds LucentPluginHost, the helper that runs VST3 plugins for the audio editor, as one universal
# (Apple silicon and Intel) program, and puts it in artifacts/mac-native, where src/Lucent.Mac picks it up.
# Runs on a Mac with the Xcode command line tools and CMake 3.20 or later; the VST3 SDK is downloaded
# by CMake (or pass -DLUCENT_VST3SDK_DIR=... as a second argument to use a copy you have).
# Usage: tools/mac/build-pluginhost-mac.sh [output folder] [extra CMake arguments...]
set -euo pipefail
root="$(cd "$(dirname "$0")/../.." && pwd)"
output="${1:-$root/artifacts/mac-native}"
shift || true
build="$root/build/PluginHost-mac"

cmake -S "$root/src/PluginHost" -B "$build" -DCMAKE_BUILD_TYPE=Release -DCMAKE_POLICY_VERSION_MINIMUM=3.5 \
  -DCMAKE_OSX_ARCHITECTURES="arm64;x86_64" -DCMAKE_OSX_DEPLOYMENT_TARGET=12.0 "$@"
cmake --build "$build" --config Release --parallel

mkdir -p "$output"
cp "$build/LucentPluginHost" "$output/LucentPluginHost"
chmod +x "$output/LucentPluginHost"
lipo -info "$output/LucentPluginHost"
# The VST3 SDK's licence ships with the app.
sdk_licence="$(find "$build" -maxdepth 4 -path '*vst3sdk*' -name LICENSE.txt | head -n 1)"
if [ -n "$sdk_licence" ]; then cp "$sdk_licence" "$output/LICENSE-VST3SDK.txt"; fi
echo "Made $output/LucentPluginHost"
