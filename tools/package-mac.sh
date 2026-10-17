#!/bin/bash
# Wraps a published Lucent.Mac folder into Lucent.app, with its icon. Runs on a Mac (it uses sips
# and iconutil). Usage: tools/package-mac.sh <published folder> <output folder> <version>
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
</dict>
</plist>
PLIST

# Not signed with a Developer ID yet, so sign it ad hoc: Apple silicon Macs won't run unsigned code at all.
codesign --force --deep --sign - "$app"
echo "Made $app"
