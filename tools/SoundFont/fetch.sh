#!/bin/bash
# Fetches GeneralUser GS, the SoundFont Lucent plays MIDI files with (Lucent.Core's Audio/Midi/SoundFonts.cs), from its
# author's GitHub at one fixed commit, and checks it's the file Lucent was built for. CI publishes it once under the
# midi-soundfont-<version> release, which is where the app downloads it from, and the self-tests play with it.
# GeneralUser GS is by S. Christian Collins, free to use and pass on (LICENSE-GeneralUser-GS.txt).
# Usage: tools/SoundFont/fetch.sh <folder>
set -euo pipefail
out=${1:?folder to put it in}
commit=684543d5e5efaef08d02be50dcda8d552478fa60   # GeneralUser GS 2.0.3
sha256=9575028c7a1f589f5770fccc8cff2734566af40cd26ed836944e9a5152688cfe
mkdir -p "$out"
curl -sSfL --retry 4 -o "$out/GeneralUser-GS.sf2" \
  "https://raw.githubusercontent.com/mrbumpy409/GeneralUser-GS/$commit/GeneralUser-GS.sf2"
echo "$sha256  $out/GeneralUser-GS.sf2" | sha256sum -c -
cp "$(dirname "$0")/LICENSE-GeneralUser-GS.txt" "$out/"
ls -l "$out"
