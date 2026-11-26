#!/usr/bin/env bash
# BuildMind full-stack backend smoke test (MVP, development/CI only; lead-approved 2026-11-19).
#
# Starts the BUILT C# API on loopback against already-running dependencies, then checks:
#   GET /health        -> 200 {"service":"buildmind-api","status":"ok"}
#   GET /health/ready  -> per-check status/reason match the expectations below (no URLs, no exception text)
#
# Dependencies (python-services, rag-service, MySQL) are started by the caller (CI job or runbook doc 30).
# All configuration comes from the environment; this script never prints secret values.
#
# Required:  ConnectionStrings__BuildMind (when SMOKE_EXPECT_MYSQL != skip), Services__* variables for the API.
# Optional:  API_DLL, API_PORT, SMOKE_OUT, SMOKE_EXPECT_MYSQL / _PYTHON / _RAG ("<Status>:<reason>" or "skip").
set -euo pipefail

API_DLL="${API_DLL:-src/BuildMind.API/bin/Release/net10.0/BuildMind.API.dll}"
API_PORT="${API_PORT:-8080}"
SMOKE_OUT="${SMOKE_OUT:-TestResults/smoke}"
EXPECT_MYSQL="${SMOKE_EXPECT_MYSQL:-Healthy:reachable}"
EXPECT_PYTHON="${SMOKE_EXPECT_PYTHON:-Healthy:reachable}"
EXPECT_RAG="${SMOKE_EXPECT_RAG:-Healthy:reachable}"
BASE="http://127.0.0.1:${API_PORT}"

[ -f "$API_DLL" ] || { echo "API assembly not found: $API_DLL (build Release first)" >&2; exit 1; }
mkdir -p "$SMOKE_OUT"
log="$(mktemp)"

# The API must own its port: if anything already answers there, the readiness checks below would silently test
# another service (the 8097 clash with the acceptance service in PR #41 CI).
if [ "$(curl -s -o /dev/null -w '%{http_code}' --max-time 2 "$BASE/" || true)" != "000" ]; then
  echo "Port ${API_PORT} is already in use; the smoke API must not share it" >&2
  rm -f "$log"
  exit 1
fi

ASPNETCORE_URLS="$BASE" dotnet "$API_DLL" > "$log" 2>&1 &
api_pid=$!
cleanup() {
  kill "$api_pid" 2>/dev/null || true
  sleep 1
  kill -9 "$api_pid" 2>/dev/null || true
  rm -f "$log"
}
trap cleanup EXIT

for attempt in $(seq 1 60); do
  if curl -fsS --max-time 2 "$BASE/health" > "$SMOKE_OUT/health.json" 2>/dev/null; then
    break
  fi
  if ! kill -0 "$api_pid" 2>/dev/null; then
    echo "API exited during start-up (last log lines below; configuration values are not printed by the API)" >&2
    tail -n 30 "$log" >&2
    exit 1
  fi
  [ "$attempt" -eq 60 ] && { echo "API did not answer /health within 60 s" >&2; tail -n 30 "$log" >&2; exit 1; }
  sleep 1
done

ready_status="$(curl -sS --max-time 15 -o "$SMOKE_OUT/ready.json" -w '%{http_code}' "$BASE/health/ready")"
echo "$ready_status" > "$SMOKE_OUT/ready.http-status"

python3 - "$SMOKE_OUT" "$EXPECT_MYSQL" "$EXPECT_PYTHON" "$EXPECT_RAG" <<'PY'
import json, sys
out, expectations = sys.argv[1], dict(zip(("mysql", "python", "rag"), sys.argv[2:5]))
health = json.load(open(f"{out}/health.json"))
assert health == {"service": "buildmind-api", "status": "ok"}, health
ready = json.load(open(f"{out}/ready.json"))
http = open(f"{out}/ready.http-status").read().strip()
checks = {c["name"]: (c["status"], c["reason"]) for c in ready["checks"]}
failures = []
for name, expected in expectations.items():
    if expected == "skip":
        continue
    status, reason = expected.split(":")
    if checks.get(name) != (status, reason):
        failures.append(f"{name}: expected {status}/{reason}, got {checks.get(name)}")
# C11b: only MySQL can make readiness 503; Python/RAG are never Unhealthy.
if any(checks.get(n, ("", ""))[0] == "Unhealthy" for n in ("python", "rag")):
    failures.append("python/rag must never be Unhealthy")
expected_http = "503" if checks.get("mysql", ("",))[0] == "Unhealthy" else "200"
if http != expected_http:
    failures.append(f"/health/ready HTTP {http}, expected {expected_http}")
text = json.dumps(ready)
for leak in ("http://", "https://", "Exception", "Password", "Server="):
    if leak in text:
        failures.append(f"readiness body contains {leak!r}")
summary = {"health": health, "ready_http": http, "checks": {k: list(v) for k, v in checks.items()}, "failures": failures}
json.dump(summary, open(f"{out}/smoke-summary.json", "w"), indent=2)
print(json.dumps(summary, indent=2))
sys.exit(1 if failures else 0)
PY
