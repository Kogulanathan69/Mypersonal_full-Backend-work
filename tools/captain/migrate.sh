#!/usr/bin/env bash
# Applies pending BuildMindDbContext migrations to the Captain buildmind_dev database using the approved deployment
# path (Option B, 2026-11-18): a ranged (non-idempotent) script from the last applied migration, executed once by
# bm_migrator over verified TLS; __EFMigrationsHistory prevents reapplication. Never uses root, never drops data.
# Run from the WSL build copy root (~/bm-captain/src). Requires tools/captain/mysql-setup.sh to have run.
set -euo pipefail
cd "${1:-$HOME/bm-captain/src}"
. tools/captain/env.sh
OUT="$HOME/bm-captain/results/migrations"; mkdir -p "$OUT"
S="$HOME/bm-captain/secrets"
ef() { dotnet tool run dotnet-ef "$@" --context BuildMindDbContext --configuration Release \
  --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API; }

dotnet tool restore >/dev/null
ef migrations has-pending-model-changes
applied="$(docker exec -i -e MYSQL_PWD="$(cat "$S/mysql-migrator.password")" bm-captain-mysql \
  mysql -ubm_migrator -h127.0.0.1 --ssl-mode=VERIFY_IDENTITY --ssl-ca=/etc/mysql/captain-tls/ca.pem -N buildmind_dev \
  -e "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1" 2>/dev/null || true)"
from="${applied:-0}"
echo "last applied: ${applied:-<none>}"
script="$OUT/ranged-from-${from}-$(date +%Y%m%d%H%M%S).sql"
ef migrations script "$from" --output "$script" >/dev/null
if ! grep -qE "CREATE|ALTER|INSERT" "$script"; then echo "no pending migrations"; rm -f "$script"; exit 0; fi
docker exec -i -e MYSQL_PWD="$(cat "$S/mysql-migrator.password")" bm-captain-mysql \
  mysql -ubm_migrator -h127.0.0.1 --ssl-mode=VERIFY_IDENTITY --ssl-ca=/etc/mysql/captain-tls/ca.pem buildmind_dev < "$script"
echo "applied script: $script"
docker exec -i -e MYSQL_PWD="$(cat "$S/mysql-migrator.password")" bm-captain-mysql \
  mysql -ubm_migrator -h127.0.0.1 --ssl-mode=VERIFY_IDENTITY --ssl-ca=/etc/mysql/captain-tls/ca.pem -N buildmind_dev \
  -e "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId; SHOW TABLES;"
