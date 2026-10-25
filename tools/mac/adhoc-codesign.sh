#!/bin/bash
# Stands in for codesign while vpk packs a Lucent that has no Developer ID certificate (CI copies it onto
# the PATH as "codesign"). vpk always signs as if for notarization, with a secure timestamp, the hardened
# runtime and its entitlements; ad-hoc signatures have none of those (the same as tools/mac/package-mac.sh
# signs). This passes everything else on to the real codesign, and turns vpk's
# "--signAppIdentity adhoc" into codesign's ad-hoc identity, "-".
args=()
previous=""
skip_value=0
for arg in "$@"; do
  if [ "$skip_value" = 1 ]; then
    skip_value=0
    continue
  fi
  case "$arg" in
    --timestamp | --timestamp=*) ;;
    --options | --entitlements) skip_value=1 ;;
    --options=* | --entitlements=*) ;;
    *)
      if { [ "$previous" = "-s" ] || [ "$previous" = "--sign" ]; } && [ "$arg" = "adhoc" ]; then
        args+=("-")
      else
        args+=("$arg")
      fi
      ;;
  esac
  previous="$arg"
done
exec /usr/bin/codesign "${args[@]}"
