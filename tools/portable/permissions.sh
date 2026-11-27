#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATE="${HOME}/.local/share/buildmind-portable"
PKI="${STATE}/pki"
SECRETS="${STATE}/secrets"
HOST_UID="$(id -u)"

restrict() {
    python3 "$ROOT/tools/captain/restrict-credential.py" "$@"
}

restrict "$PKI/mysql-server.key" 999
restrict "$SECRETS/mysql_root_password" 999

restrict "$PKI/mysql-client.key" 1654
restrict "$PKI/api-client.key" 1654

restrict "$PKI/python-server.key" 10001
restrict "$SECRETS/python_internal_token" 10001

restrict "$PKI/rag-server.key" "$HOST_UID"
restrict "$PKI/mailpit.key" "$HOST_UID"

restrict "$PKI/health-client.key" 10001 "$HOST_UID"

chmod 600 "$PKI/server-ca.key"
chmod 600 "$PKI/client-ca.key"
chmod 600 "$PKI/mysql-ca.key"
chmod 600 "$PKI/mysql-migrator.key"

echo "PORTABLE_CONTAINER_PERMISSIONS_READY"
