#!/usr/bin/env bash
# Restore drill: verifies a backup's checksums, restores it into a DISPOSABLE database (buildmind_test_restore_*),
# compares per-table row counts with the source, then drops the disposable database. The source is never modified.
# Usage: restore-verify.sh <backup-dir>
set -euo pipefail
dir=${1:?backup directory}
S=~/bm-captain/secrets
(cd "$dir" && sha256sum -c SHA256SUMS --quiet) && echo "checksums: OK"
target="buildmind_test_restore_$(date +%s)"
mysql() { docker exec -i -e MYSQL_PWD="$(cat "$S/mysql-root.password")" bm-captain-mysql mysql -uroot -N "$@"; }
cleanup() { mysql -e "DROP DATABASE IF EXISTS \`$target\`" || true; }
trap cleanup EXIT
mysql -e "CREATE DATABASE \`$target\`"
# Rewrite only the database name in the CREATE DATABASE/USE statements of the dump.
sed -e "s/^CREATE DATABASE \/\*!32312 IF NOT EXISTS\*\/ \`buildmind_dev\`/CREATE DATABASE IF NOT EXISTS \`$target\`/" \
    -e "s/^USE \`buildmind_dev\`;/USE \`$target\`;/" "$dir/buildmind_dev.sql" | mysql
tables=$(mysql -e "SELECT table_name FROM information_schema.tables WHERE table_schema='buildmind_dev' AND table_type='BASE TABLE' ORDER BY 1")
mismatch=0; count=0
for t in $tables; do
  a=$(mysql -e "SELECT COUNT(*) FROM \`buildmind_dev\`.\`$t\`")
  b=$(mysql -e "SELECT COUNT(*) FROM \`$target\`.\`$t\`")
  count=$((count + 1))
  if [ "$a" != "$b" ]; then echo "MISMATCH $t source=$a restored=$b"; mismatch=$((mismatch + 1)); fi
done
mkdir -p /tmp/bm-restore-check && tar -xzf "$dir/artifacts.tar.gz" -C /tmp/bm-restore-check && rm -rf /tmp/bm-restore-check
echo "tables compared: $count, mismatches: $mismatch (writes after the backup can cause differences)"
[ "$mismatch" -eq 0 ]
