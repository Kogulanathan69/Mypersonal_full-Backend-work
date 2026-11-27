#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATE="${HOME}/.local/share/buildmind-portable"
SECRETS="${STATE}/secrets"
INIT="${STATE}/mysql-init"

mkdir -p "$INIT"
chmod 755 "$INIT"

runtime_password="$(cat "$SECRETS/mysql_runtime_password")"
migrator_password="$(cat "$SECRETS/mysql_migrator_password")"

for password in "$runtime_password" "$migrator_password"; do
    [[ "$password" =~ ^[0-9a-f]{64}$ ]] || {
        echo "INVALID_DATABASE_CREDENTIAL_FORMAT" >&2
        exit 1
    }
done

cat > "$INIT/01-database.sql" <<SQL
CREATE DATABASE IF NOT EXISTS buildmind_dev
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_0900_ai_ci;

CREATE USER IF NOT EXISTS 'bm_runtime'@'%'
    IDENTIFIED BY '${runtime_password}' REQUIRE SSL;

CREATE USER IF NOT EXISTS 'bm_migrator'@'%'
    IDENTIFIED BY '${migrator_password}' REQUIRE SSL;

GRANT SELECT, INSERT, UPDATE, DELETE
    ON buildmind_dev.* TO 'bm_runtime'@'%';

GRANT SELECT, INSERT, UPDATE, DELETE,
    CREATE, ALTER, DROP, INDEX, REFERENCES
    ON buildmind_dev.* TO 'bm_migrator'@'%';

SET PERSIST max_execution_time = 25000;
SET PERSIST innodb_lock_wait_timeout = 20;
SQL

python3 "$ROOT/tools/captain/restrict-credential.py" \
    "$INIT/01-database.sql" 999

echo "PORTABLE_DATABASE_INIT_READY"
