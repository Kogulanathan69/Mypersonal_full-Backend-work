#!/usr/bin/env bash
# Live MySQL integration tests against the Captain container (disposable buildmind_test_* databases via bm_test).
# Usage: persistence-tests.sh [extra dotnet-test filter]. Python/RAG live tests are excluded unless SERVICES=1.
# RH09 (command-timeout classification) is not part of this baseline; it is DEFERRED and never run here.
set -o pipefail
cd ~/bm-captain/src
. tools/captain/env.sh
SPIKE=tests/BuildMind.Persistence.IntegrationTests/BuildMind.Persistence.IntegrationTests.csproj
dotnet build "$SPIKE" -c Release 2>&1 | grep -E "error|Warning\(s\)|Error\(s\)" | sort -u
if [ ! -d tests/BuildMind.Persistence.IntegrationTests/GeneratedMigrations ]; then
  dotnet tool run dotnet-ef migrations add CiGeneratedSpike --context GeneratedSpikeDbContext \
    --output-dir GeneratedMigrations --configuration Release --project "$SPIKE" --startup-project "$SPIKE" >/dev/null
  dotnet build "$SPIKE" -c Release 2>&1 | grep -E "Error\(s\)"
fi
filter="FullyQualifiedName!~PythonLiveSmokeTests&FullyQualifiedName!~RagLiveSmokeTests&FullyQualifiedName!~RagConstraintAcceptanceTests"
[ "${SERVICES:-0}" = 1 ] && filter=""
[ -n "${1:-}" ] && filter="${filter:+$filter&}$1"
mkdir -p ~/bm-captain/results/mysql
timeout 1500 dotnet test "$SPIKE" -c Release --no-build ${filter:+--filter "$filter"} \
  --logger "trx;LogFileName=mysql-$(date +%H%M%S).trx" --results-directory ~/bm-captain/results/mysql 2>&1 \
  | grep -E "Passed!|Failed!|^\s+Failed |Error Message" -A2 | grep -v Password | head -60
echo "exit=${PIPESTATUS[0]}"
# Leftover check: every test database must have been dropped.
docker exec -e MYSQL_PWD="$(cat ~/bm-captain/secrets/mysql-root.password)" bm-captain-mysql mysql -uroot -N \
  -e "SELECT COUNT(*) AS leftover_test_databases FROM information_schema.schemata WHERE schema_name LIKE 'buildmind\\_test\\_%';"
