#!/usr/bin/env bash
set -euo pipefail
umask 077
evidence=${1:?new evidence directory required}
mkdir -p "$evidence"
cd ~/bm-captain/src/tools/captain
diagnose() {
  local id
  for id in $(docker ps -aq --filter label=com.docker.compose.project=bm-captain-verify); do
    docker inspect -f '{{.Name}} {{.Image}} {{.State.Status}} exit={{.State.ExitCode}} oom={{.State.OOMKilled}} {{if .State.Health}}{{.State.Health.Status}}{{end}}' "$id"
    # Keep log event categories only. Raw messages may include account or configuration data.
    docker logs --tail 500 "$id" 2>&1 | grep -Eio 'OutOfMemoryException|TimeoutException|AuthenticationException|MySqlException|Permission denied|Connection refused' | sort | uniq -c || true
  done
  docker ps -q --filter label=com.docker.compose.project=bm-captain-verify | xargs -r docker stats --no-stream --format '{{.Name}} {{.MemUsage}}' || true
}
trap 'diagnose > "$evidence/container-status.txt" 2>&1' EXIT
bash compose-verify.sh up
bash compose-verify.sh expiry-up
for port in 8080 5181; do
  for attempt in {1..60}; do
    if curl -fsS --max-time 10 "http://127.0.0.1:$port/health/ready" > "$evidence/ready-$port.json" &&
       python3 -c 'import json,sys; d={c["name"]:c["status"] for c in json.load(open(sys.argv[1]))["checks"]}; sys.exit(any(d.get(k)!="Healthy" for k in ("mysql","rag","python")))' "$evidence/ready-$port.json"; then break; fi
    if ((attempt == 60)); then exit 1; fi
    sleep 3
  done
done
# Require the local mail capture API before running email-dependent journeys.
for attempt in {1..30}; do
  if curl -fsS --max-time 5 "http://127.0.0.1:8026/api/v1/info" > "$evidence/mailpit-readiness.json" &&
     python3 -c 'import json,sys; d=json.load(open(sys.argv[1])); sys.exit(not isinstance(d,dict) or not d.get("Version"))' "$evidence/mailpit-readiness.json"; then
    echo "Mailpit HTTP readiness: PASS"
    break
  fi
  if ((attempt == 30)); then
    echo "Mailpit HTTP readiness: FAIL" >&2
    exit 1
  fi
  sleep 2
done
BM_CAPTURE="$evidence/http.jsonl" CAPTAIN_SECONDARY_API_URL=http://127.0.0.1:5181 MAILPIT_API=http://127.0.0.1:8026/api/v1 \
  timeout 4000 python3 -u smoke_journey.py http://127.0.0.1:8080 --phases 2,3,4,5,6,8,9,10,11,12,13,14
