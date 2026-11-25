#!/usr/bin/env bash
# Restarts ONLY the API (5180), keeping python-services/LLM/MySQL running (memory-friendly). Pass LLM_PORT=8093 for the
# real local LLM. Builds first. Ends with `wait` so it can run as a background task.
set -euo pipefail
cd ~/bm-captain/src
pkill -f 'BuildMind.API.dll' || true
dotnet build src/BuildMind.API/BuildMind.API.csproj -c Release 2>&1 | grep -E " error |Error\(s\)" | sort -u
export CaptainIntegration__GenerationPolicyFile="$PWD/docs/captain-development/samples/generation-policy.synthetic.json"
if [ -s ~/bm-captain/secrets/captain-admin.email ]; then export ADMIN_BOOTSTRAP_EMAIL="$(cat ~/bm-captain/secrets/captain-admin.email)"; fi
PY_PORT=8091 nohup bash tools/captain/run-api.sh 5180 > ~/bm-captain/logs/api.log 2>&1 &
wait
