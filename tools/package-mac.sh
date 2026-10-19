#!/bin/bash
# Wraps a published Lucent.Mac folder into Lucent.app, with its icon, and signs it. Runs on a Mac (it
# uses sips, iconutil and codesign). Usage: tools/package-mac.sh <published folder> <output folder> <version>
#
# Signing: ad hoc by default (Apple silicon Macs won't run unsigned code at all). To sign with a
# Developer ID instead, set MAC_SIGN_IDENTITY to the certificate's name, e.g.
# "Developer ID Application: Your Name (TEAMID)" (and MAC_KEYCHAIN if it isn't in the default keychain);
# it's then signed with the hardened runtime and tools/mac/Lucent.entitlements, ready to notarize.
# CI does the same, and Velopack (vpk pack) signs the installable copy again after adding its updater.
set -euo pipefail
published="$1"
output="$2"
version="$3"
root="$(cd "$(dirname "$0")/.." && pwd)"

app="$output/Lucent.app"
rm -rf "$app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp -R "$published"/. "$app/Contents/MacOS/"

# The icon, at every size macOS asks for.
iconset="$(mktemp -d)/Lucent.iconset"
mkdir -p "$iconset"
for size in 16 32 128 256 512; do
  sips -z $size $size "$root/assets/lucent-icon.png" --out "$iconset/icon_${size}x${size}.png" > /dev/null
  double=$((size * 2))
  if [ $double -le 512 ]; then
    sips -z $double $double "$root/assets/lucent-icon.png" --out "$iconset/icon_${size}x${size}@2x.png" > /dev/null
  fi
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/Lucent.icns"

cat > "$app/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleName</key><string>Lucent</string>
  <key>CFBundleDisplayName</key><string>Lucent</string>
  <key>CFBundleIdentifier</key><string>io.github.joshuadav86.lucent</string>
  <key>CFBundleExecutable</key><string>Lucent</string>
  <key>CFBundleIconFile</key><string>Lucent</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$version</string>
  <key>CFBundleVersion</key><string>$version</string>
  <key>LSMinimumSystemVersion</key><string>12.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>LSApplicationCategoryType</key><string>public.app-category.music</string>
  <key>NSAudioCaptureUsageDescription</key><string>Lucent listens to what other apps are playing so its visuals and analyzer can react to it. Nothing is recorded or sent anywhere.</string>
  <key>NSAppleEventsUsageDescription</key><string>Lucent shows what the Music app, or YouTube in your web browser, is playing and controls play, pause, skip and seek from its player.</string>
</dict>
</plist>
PLIST

identity="${MAC_SIGN_IDENTITY:--}"
if [ "$identity" = "-" ]; then
  # Not signed with a Developer ID, so sign it ad hoc.
  codesign --force --deep --sign - "$app"
else
  keychain=()
  if [ -n "${MAC_KEYCHAIN:-}" ]; then
    keychain=(--keychain "$MAC_KEYCHAIN")
  fi
  codesign --force --deep --timestamp --options runtime --entitlements "$root/tools/mac/Lucent.entitlements" \
    ${keychain[@]+"${keychain[@]}"} --sign "$identity" "$app"
fi
echo "Made $app"
