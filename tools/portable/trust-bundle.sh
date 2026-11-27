#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

STATE="${HOME}/.local/share/buildmind-portable"
TARGET="${STATE}/ca-bundle.pem"

[[ -s "$STATE/pki/server-ca.crt" ]] || {
    echo "PORTABLE_SERVER_CA_MISSING" >&2
    exit 1
}

TEMP="$(mktemp "$STATE/.ca-bundle.XXXXXX")"
trap 'rm -f "$TEMP"' EXIT

docker run --rm \
    --network none \
    --entrypoint cat \
    buildmind-api:portable \
    /etc/ssl/certs/ca-certificates.crt > "$TEMP"

printf '\n' >> "$TEMP"
cat "$STATE/pki/server-ca.crt" >> "$TEMP"

openssl verify \
    -CAfile "$TEMP" \
    -purpose sslserver \
    -verify_hostname mailpit \
    "$STATE/pki/mailpit.crt"

chmod 644 "$TEMP"
mv -f "$TEMP" "$TARGET"

echo "PORTABLE_CA_TRUST_BUNDLE_READY"
