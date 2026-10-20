#!/bin/bash
# Opens Lucent.app the way people do and checks it stays up, then checks its window layout (see
# src/Lucent.Mac/LayoutCheck.cs). The self-test never starts the app's window loop, so a crash at launch
# would otherwise ship. Runs on a Mac of the app's own chip. Usage: tools/mac/launch-check.sh <Lucent.app>
set -euo pipefail
exe="$1/Contents/MacOS/Lucent"
work="${RUNNER_TEMP:-$(mktemp -d)}"

# Make sure the player is still up after a while.
export LUCENT_DATA_FOLDER="$work/launch-check"
"$exe" > "$work/launch.log" 2>&1 &
app=$!
for _ in $(seq 1 20); do kill -0 "$app" 2> /dev/null || break; sleep 1; done
if ! kill -0 "$app" 2> /dev/null; then
  echo "::error::Lucent quit at launch."
  cat "$work/launch.log"
  exit 1
fi
kill "$app"
echo "Lucent launched and was still running after 20 seconds."

# Open it again and resize the visuals window a few times: the Metal view has to fill the
# space the layout gives it, or the visuals show squashed into part of the window. The
# check then minimizes the player (the other windows go with it) and quits. Then it opens
# once more on the same settings: the windows have to come back open, where they were left.
export LUCENT_DATA_FOLDER="$work/layout-check"
for run in first reopened; do
  export LUCENT_LAYOUT_CHECK="$work/layout-check-$run.txt"
  if [ "$run" = reopened ]; then export LUCENT_LAYOUT_CHECK_REOPENED=1; fi
  cp "$work/layout-check-first.txt.visuals" "$LUCENT_LAYOUT_CHECK.visuals" 2> /dev/null || true
  "$exe" > "$work/layout-$run.log" 2>&1 &
  app=$!
  for _ in $(seq 1 90); do kill -0 "$app" 2> /dev/null || break; sleep 1; done
  if kill -0 "$app" 2> /dev/null; then
    kill "$app"
    echo "::error::Lucent didn't quit when its player was closed ($run run)."
    cat "$LUCENT_LAYOUT_CHECK" 2> /dev/null
    exit 1
  fi
  cat "$LUCENT_LAYOUT_CHECK" 2> /dev/null || { echo "::error::The layout check wrote nothing ($run run)."; cat "$work/layout-$run.log"; exit 1; }
  if ! grep -qx PASSED "$LUCENT_LAYOUT_CHECK"; then
    echo "::error::The window layout check failed ($run run): see the MISMATCH lines above."
    exit 1
  fi
done
