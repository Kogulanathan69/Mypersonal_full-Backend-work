#!/usr/bin/env bash
# Runs every in-solution .NET test project (Release, no build) plus the console contract runner, without live
# connection-string variables. Prints one summary line per suite. Usage: unit-suites.sh [results-dir]
set -o pipefail
failed=0
cd ~/bm-captain/src || exit 1
out="${1:-$HOME/bm-captain/results/unit}"; mkdir -p "$out"
for p in BuildMind.Application.UnitTests BuildMind.Infrastructure.Tests BuildMind.Reports.Tests BuildMind.Requirements.Tests BuildMind.Workflow.Tests; do
  line="$(timeout 1500 dotnet test "tests/$p" -c Release --no-build --logger "trx;LogFileName=$p.trx" --results-directory "$out" 2>&1 \
    | grep -E "Passed!|Failed!" | sed 's/, Duration.*//')" || failed=1
  echo "$p: ${line:-NO SUMMARY (see TRX)}"
done
if timeout 600 dotnet run --project tests/BuildMind.Application.ContractTests -c Release --no-build > "$out/contract-tests.log" 2>&1; then
  echo "BuildMind.Application.ContractTests: exit 0 ($(grep -ciE 'pass' "$out/contract-tests.log") PASS lines)"
else
  echo "BuildMind.Application.ContractTests: exit $? (see $out/contract-tests.log)"
  failed=1
fi
exit "$failed"
