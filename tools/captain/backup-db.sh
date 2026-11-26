#!/usr/bin/env bash
# Consistent logical backup of the LOCAL Captain database (InnoDB --single-transaction; routines/triggers included)
# plus a SHA-256 manifest. Output: ~/bm-captain/backups/<timestamp>/ (0700). Never touches the source database.
# Private artefacts (survey blobs, report PDFs) are archived alongside with their own checksums.
set -euo pipefail
S=~/bm-captain/secrets
stamp=$(date -u +%Y%m%dT%H%M%SZ)
out=~/bm-captain/backups/$stamp
mkdir -p "$out" && chmod 700 ~/bm-captain/backups "$out"
docker exec -e MYSQL_PWD="$(cat "$S/mysql-root.password")" bm-captain-mysql \
  mysqldump -uroot --single-transaction --routines --triggers --set-gtid-purged=OFF --databases buildmind_dev > "$out/buildmind_dev.sql"
tar -C ~/bm-captain/data -czf "$out/artifacts.tar.gz" surveys reports 2>/dev/null || tar -C ~/bm-captain/data -czf "$out/artifacts.tar.gz" .
(cd "$out" && sha256sum buildmind_dev.sql artifacts.tar.gz > SHA256SUMS)
chmod 600 "$out"/*
echo "$out"
