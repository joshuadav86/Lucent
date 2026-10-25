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
  case "$name" in
    releases.*.json) path="update-check/${name#releases.}"; path="${path%.json}" ;;
    *) path="download/$name" ;;
  esac
  echo "$name: $count"
  # GoatCounter takes at most 500 pageviews a request and 4 requests a second.
  while [ "$count" -gt 0 ]; do
    batch=$(( count > 500 ? 500 : count ))
    hits=$(jq -nc --arg path "$path" --argjson n "$batch" \
      '[range($n) | {path: $path, title: $path, event: true, bot: 0,
        user_agent: "Mozilla/5.0 (X11; Linux x86_64) Lucent-download-totals"}]')
    curl -sS --fail-with-body -X POST "$site/api/v0/count" \
      -H "Authorization: Bearer $GOATCOUNTER_TOKEN" -H "Content-Type: application/json" \
      -d "{\"no_sessions\": true, \"filter\": [], \"hits\": $hits}" > /dev/null \
      || echo "::warning::Couldn't send the $name download count to GoatCounter."
    count=$(( count - batch ))
    sleep 0.3
  done
done <<< "$counts"
exit 0
