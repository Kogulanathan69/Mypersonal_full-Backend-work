#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATE="$HOME/.local/share/buildmind-portable"

mkdir -p "$STATE"
chmod 700 "$STATE"

exec 8>"$STATE/startup.lock"
flock -x 8

bash "$ROOT/tools/portable/compose.sh" stop --timeout 30

echo "BUILDMIND_PORTABLE_BACKEND_STOPPED"
echo "Database data preserved"
echo "Artifacts preserved"
echo "Credentials and certificates preserved"
