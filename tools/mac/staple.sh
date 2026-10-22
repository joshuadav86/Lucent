#!/bin/bash
# Staples Apple's notarization ticket to an app or installer that Apple has just accepted.
# Usage: staple.sh <Lucent.app or .pkg>
#
# stapler fetches the ticket from Apple's CloudKit server, and from GitHub's Mac runners that request
# has dropped mid-way ("The network connection was lost", Error 68) on build after build, for the app and
# the installer, Intel and Apple silicon alike. So it tries a few times with a growing wait, and if Apple
# still can't be reached it only warns: the build is notarized either way, and a Mac that's online fetches
# the ticket itself the first time Lucent opens. Failing here held the Apple silicon download back.
set -u
target="$1"

wait_s=30
for attempt in 1 2 3 4 5; do
  if xcrun stapler staple "$target"; then
    xcrun stapler validate "$target"
    exit 0
  fi
  [ "$attempt" -eq 5 ] && break
  echo "Stapling $(basename "$target") failed (attempt $attempt); trying again in $wait_s s."
  sleep "$wait_s"
  wait_s=$((wait_s * 2))
done
echo "::warning::Apple notarized $(basename "$target") but its ticket server couldn't be reached to staple it, so it goes out unstapled (Macs that are online still open it)."
exit 0
