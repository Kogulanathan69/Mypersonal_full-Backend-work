#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
STATE="${HOME}/.local/share/buildmind-portable"

exec 9>"$STATE/migration.lock"
flock -x 9

compose() {
    bash "$ROOT/tools/portable/compose.sh" "$@"
}

mysql_query() {
    MYSQL_PWD="$(cat "$STATE/secrets/mysql_migrator_password")" \
        docker exec -i -e MYSQL_PWD bm-portable-mysql-1 \
        mysql \
        --host=mysql \
        --user=bm_migrator \
        --ssl-mode=VERIFY_IDENTITY \
        --ssl-ca=/etc/mysql/tls/ca.pem \
        --batch \
        --skip-column-names \
        buildmind_dev "$@"
}

MARKER="$STATE/migration-in-progress"

if [[ -e "$MARKER" ]]; then
    echo "PREVIOUS_MIGRATION_REQUIRES_REVIEW" >&2
    exit 1
fi

# Docker's health probe can report ready before the migrator's own TLS path
# accepts connections. Probe that exact path with a finite deadline.
ready=0
for attempt in $(seq 1 30); do
    if mysql_query -e 'SELECT 1;' >/dev/null 2>&1; then
        ready=1
        break
    fi
    sleep 2
done
if [[ "$ready" != "1" ]]; then
    echo "MIGRATOR_TLS_CONNECTION_UNAVAILABLE_AFTER_60_SECONDS" >&2
    mysql_query -e 'SELECT 1;' >/dev/null
    exit 1
fi
echo "MIGRATOR_TLS_CONNECTION_PASS"

history_exists="$(mysql_query -e "
SELECT COUNT(*)
FROM information_schema.tables
WHERE table_schema = 'buildmind_dev'
AND table_name = '__EFMigrationsHistory';
")"

if [[ "$history_exists" == "1" ]]; then
    current="$(mysql_query -e '
SELECT MigrationId
FROM __EFMigrationsHistory
ORDER BY MigrationId DESC LIMIT 1;
')"
    [[ -n "$current" ]] || {
        echo "EMPTY_MIGRATION_HISTORY_REQUIRES_REVIEW" >&2
        exit 1
    }
elif [[ "$history_exists" == "0" ]]; then
    tables="$(mysql_query -e "
SELECT COUNT(*) FROM information_schema.tables
WHERE table_schema = 'buildmind_dev';
")"
    [[ "$tables" == "0" ]] || {
        echo "UNTRACKED_DATABASE_SCHEMA_REQUIRES_REVIEW" >&2
        exit 1
    }
    current=0
else
    echo "INVALID_MIGRATION_HISTORY_STATE" >&2
    exit 1
fi

echo "CURRENT_MIGRATION: $current"

# Generate SQL in the migrator container's own writable filesystem.
# Stream it directly to the host-side migration process; never bind-mount a
# writable single file from WSL (Docker Desktop can reject those writes).
SQL_FILE="$(mktemp "$STATE/migration.XXXXXX.sql")"
trap 'rm -f "$SQL_FILE"' EXIT

compose run --rm --no-deps -T \
    --entrypoint sh migrator -ec '
        set -eu
        sql_file="$(mktemp /tmp/buildmind-migrations.XXXXXX.sql)"
        trap '\''rm -f "$sql_file"'\'' EXIT
        dotnet tool run dotnet-ef migrations script "$@" \
            --context BuildMindDbContext \
            --configuration Release \
            --no-build \
            --project src/BuildMind.Infrastructure \
            --startup-project src/BuildMind.API \
            --output "$sql_file" >&2
        # Only SQL goes to stdout; EF informational output stays on stderr.
        cat "$sql_file"
    ' sh "$current" > "$SQL_FILE"

if grep -Eiq '^[[:space:]]*(CREATE|ALTER|DROP|INSERT|UPDATE|DELETE|RENAME|TRUNCATE)[[:space:]]' "$SQL_FILE"; then
    touch "$MARKER"
    mysql_query < "$SQL_FILE"
    echo "PENDING_MIGRATIONS_APPLIED"
else
    echo "NO_SCHEMA_CHANGES_DETECTED"
fi

mysql_query -e '
SELECT MigrationId
FROM __EFMigrationsHistory
ORDER BY MigrationId;
'

compose run --rm --no-deps -T migrator \
    migrations has-pending-model-changes \
    --context BuildMindDbContext \
    --configuration Release \
    --no-build \
    --project src/BuildMind.Infrastructure \
    --startup-project src/BuildMind.API

rm -f "$MARKER"

echo "PORTABLE_DATABASE_MIGRATION_COMPLETE"
