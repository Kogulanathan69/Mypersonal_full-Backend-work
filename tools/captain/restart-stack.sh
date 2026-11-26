#!/usr/bin/env bash
# Rebuilds and restarts the Captain local stack: sync -> build -> migrate -> python-services (8091) -> API (5180)
# [-> rag-service (8092) when RAG=1]. Logs: ~/bm-captain/logs. Loopback only; secrets from ~/bm-captain/secrets.
set -euo pipefail
bash ~/bm-captain/src/tools/captain/stop-captain.sh
~/bm-captain/sync.sh
cd ~/bm-captain/src
dotnet build BuildMind.sln -c Release 2>&1 | grep -E " error |Warning\(s\)|Error\(s\)" | sort -u
bash tools/captain/migrate.sh 2>&1 | tail -1
mkdir -p ~/bm-captain/logs
nohup bash tools/captain/run-python.sh 8091 > ~/bm-captain/logs/python.log 2>&1 &
if [ "${RAG:-0}" = 1 ]; then
  nohup bash tools/captain/run-rag.sh 8092 > ~/bm-captain/logs/rag.log 2>&1 &
  export RAG_PORT=8092
fi
if [ -s ~/bm-captain/secrets/captain-admin.email ]; then export ADMIN_BOOTSTRAP_EMAIL="$(cat ~/bm-captain/secrets/captain-admin.email)"; fi
bash tools/captain/mailpit.sh start || true
export CaptainIntegration__GenerationPolicyFile="$PWD/docs/captain-development/samples/generation-policy.synthetic.json"
PY_PORT=8091 nohup bash tools/captain/run-api.sh 5180 > ~/bm-captain/logs/api.log 2>&1 &
if [ "${RAG:-0}" = 1 ]; then
  # Second API instance whose generation policy uses the M7 constraint service (evidence mode rag-constraints).
  CaptainIntegration__GenerationPolicyFile="$PWD/docs/captain-development/samples/generation-policy.rag.json" \
    CaptainIntegration__SurveyWorker__Enabled=false CaptainIntegration__Architect__ConsentLifetimeMinutes=1 PY_PORT=8091 nohup bash tools/captain/run-api.sh 5181 > ~/bm-captain/logs/api-rag.log 2>&1 &
fi
ports=(5180)
if [ "${RAG:-0}" = 1 ]; then ports+=(5181); fi
for port in "${ports[@]}"; do
  curl --retry 60 --retry-delay 2 --retry-connrefused --max-time 10 -fsS "http://127.0.0.1:$port/health" >/dev/null
done
curl --retry 30 --retry-delay 2 --retry-connrefused -fsS --cacert ~/bm-captain/secrets/pki/server-ca.crt --cert ~/bm-captain/secrets/pki/api-client.crt --key ~/bm-captain/secrets/pki/api-client.key https://127.0.0.1:8091/health >/dev/null
# Keep the services attached to this session (a detached wsl.exe session would terminate them on exit).
[ "${DETACH:-0}" = 1 ] || wait
