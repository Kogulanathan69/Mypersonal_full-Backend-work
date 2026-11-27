#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
TOOLS="$ROOT/tools/portable"
STATE="$HOME/.local/share/buildmind-portable"

mkdir -p "$STATE"
chmod 700 "$STATE"

exec 8>"$STATE/startup.lock"
flock -x 8

echo "[1/7] Preparing local credentials"
bash "$TOOLS/bootstrap.sh"

echo "[2/7] Preparing TLS certificates"
bash "$TOOLS/certificates.sh"
bash "$TOOLS/permissions.sh"

echo "[3/7] Preparing database and service configuration"
bash "$TOOLS/database-init.sh"
bash "$TOOLS/configure.sh"

echo "[4/7] Preparing Docker images"
bash "$TOOLS/compose.sh" build api python-services rag-service migrator
bash "$TOOLS/trust-bundle.sh"

echo "[5/7] Starting MySQL"
bash "$TOOLS/compose.sh" up -d --wait --wait-timeout 300 mysql

echo "[6/7] Applying database migrations"
bash "$TOOLS/migrate.sh"

echo "[7/7] Starting backend services"
bash "$TOOLS/compose.sh" up -d --no-build --wait --wait-timeout 300

echo ""
echo "BUILDMIND_PORTABLE_BACKEND_READY"
echo "Swagger: http://localhost:${BM_API_PORT:-5182}/swagger/index.html"
echo "Mailpit: http://localhost:${BM_MAIL_PORT:-8027}"
echo "Mode: Local development"
echo "Approved regulation corpus: Not configured"
