#!/usr/bin/env bash
# Sends the download counts of files on the "latest" release to Joshua's private GoatCounter dashboard,
# just before a build deletes or replaces those files (which sets GitHub's counts back to zero). Each
# download becomes one event there, named after the file: the installers and zips count downloads, and
# the releases.*.json feeds count update checks from installed copies following every build. Only the
# totals are sent, nothing about who downloaded. Does nothing when the GOATCOUNTER_TOKEN secret isn't set.
#
# Usage: send-download-counts.sh [grep -E pattern of file names to send; default: all]
set -uo pipefail
pattern="${1:-.}"
site="https://joshuadav86.goatcounter.com"
[ -n "${GOATCOUNTER_TOKEN:-}" ] || { echo "No GOATCOUNTER_TOKEN, so no download counts sent."; exit 0; }

counts=$(gh release view latest -R "$GITHUB_REPOSITORY" --json assets \
  --jq '.assets[] | select(.downloadCount > 0) | "\(.name) \(.downloadCount)"' 2> /dev/null) || exit 0
counts=$(awk -v p="$pattern" '$1 ~ p && ($1 ~ /\.(exe|zip|pkg)$/ || $1 ~ /^releases\..*\.json$/)' <<< "$counts")
[ -n "$counts" ] || { echo "No new downloads to send."; exit 0; }

while read -r name count; do
  # Names Joshua reads on the dashboard. Update checks are automatic: each running copy of Lucent asks
  # for a newer build 20 seconds after it starts and every 4 hours, so they're not clicks.
  case "$name" in
    Lucent-Setup.exe) what="Windows installer" ;;
    Lucent-win-x64.zip) what="Windows portable zip" ;;
    Lucent-Setup-mac-arm64.pkg) what="Mac installer (Apple silicon)" ;;
    Lucent-Setup-mac-x64.pkg) what="Mac installer (Intel)" ;;
    Lucent-mac-arm64.zip) what="Mac zip (Apple silicon)" ;;
    Lucent-mac-x64.zip) what="Mac zip (Intel)" ;;
    releases.win.json) what="Windows" ;;
    releases.osx-arm64.json) what="Mac (Apple silicon)" ;;
    releases.osx-x64.json) what="Mac (Intel)" ;;
    *) what="$name" ;;
  esac
  case "$name" in
    releases.*.json) path="update-check-${name#releases.}"; path="${path%.json}"; title="Update check (automatic): $what" ;;
    *) path="github-download-${name%.*}"; title="GitHub download: $what" ;;
  esac
  echo "$name: $count"
  # GoatCounter shows visits, so each download gets its own made-up session to count once apiece
  # (without one, a whole batch showed as a single visit). It takes at most 500 a request.
  while [ "$count" -gt 0 ]; do
    batch=$(( count > 500 ? 500 : count ))
    hits=$(jq -nc --arg path "$path" --arg title "$title" --arg run "$GITHUB_RUN_ID-$GITHUB_JOB-$name" --argjson n "$batch" --argjson left "$count" \
      '[range($n) | {path: $path, title: $title, event: true, bot: 0, session: "\($run)-\($left - .)",
        user_agent: "Mozilla/5.0 (X11; Linux x86_64) Lucent-download-totals"}]')
    curl -sS --fail-with-body -X POST "$site/api/v0/count" \
      -H "Authorization: Bearer $GOATCOUNTER_TOKEN" -H "Content-Type: application/json" \
      -d "{\"filter\": [], \"hits\": $hits}" > /dev/null \
      || echo "::warning::Couldn't send the $name download count to GoatCounter."
    count=$(( count - batch ))
    sleep 0.3
  done
done <<< "$counts"
exit 0
