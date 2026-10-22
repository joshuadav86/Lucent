#!/bin/bash
# Signs the installer .pkg with the Developer ID Installer identity from the build's signing keychain.
# Usage: sign-installer.sh <pkg> <identity> <keychain>
#
# productsign hung on CI for as long as it was let run, with no output. So it runs in the background, and
# if it's still going after a few minutes this prints where it's stuck (a stack sample and the security
# log) and tries once more without Apple's timestamp server, which is the one part that goes online.
set -u
pkg="$1"
identity="$2"
keychain="$3"
signed="${RUNNER_TEMP:-/tmp}/Setup-signed.pkg"

security find-identity -v -p basic "$keychain"

# Runs productsign with the given timestamp option; 0 when it signed, 1 when it failed or hung.
sign() {
  rm -f "$signed"
  productsign --sign "$identity" --keychain "$keychain" "$1" "$pkg" "$signed" &
  local pid=$! waited=0
  while kill -0 "$pid" 2> /dev/null; do
    if [ "$waited" -ge 240 ]; then
      echo "::warning::productsign $1 was still going after $waited s. Where it's stuck:"
      sample "$pid" 3 2> /dev/null | sed -n '1,120p'
      log show --last 5m --style compact \
        --predicate 'process == "productsign" OR process == "SecurityAgent" OR process == "securityd" OR process == "trustd"' \
        2> /dev/null | tail -n 60
      kill -9 "$pid" 2> /dev/null
      wait "$pid" 2> /dev/null
      return 1
    fi
    sleep 5
    waited=$((waited + 5))
  done
  wait "$pid"
}

if sign --timestamp; then
  echo "Signed the installer."
elif sign --timestamp=none; then
  echo "::warning::The installer is signed without a secure timestamp: productsign only finished without Apple's timestamp server."
else
  echo "::error::productsign couldn't sign the installer."
  exit 1
fi
mv "$signed" "$pkg"
pkgutil --check-signature "$pkg"
