#!/usr/bin/env bash
# Starts the BuildMind API (Release build output) in Development with the Captain local-integration profile,
# bound to loopback. Secrets come from ~/bm-captain/secrets and are passed as environment variables only.
# Usage: run-api.sh [port]   (default 5180). Python/RAG are enabled when their token files and PY/RAG ports are set.
set -euo pipefail
cd ~/bm-captain/src
. tools/captain/env.sh
S="$HOME/bm-captain/secrets"
umask 077
[ -s "$S/jwt-signing.key" ] || openssl rand -base64 32 > "$S/jwt-signing.key"
mkdir -p "$HOME/bm-captain/data/surveys" "$HOME/bm-captain/data/reports"
chmod 700 "$HOME/bm-captain/data" "$HOME/bm-captain/data/surveys" "$HOME/bm-captain/data/reports"

export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="http://127.0.0.1:${1:-5180}"
export Database__Enabled=true
export Authentication__Enabled=true
export Authentication__RefreshTokens__Enabled=true
export Jwt__Issuer=buildmind-captain-local
export Jwt__Audience=buildmind-captain-local-api
export Jwt__SigningKey="$(cat "$S/jwt-signing.key")"
export CaptainIntegration__Enabled=true
export CaptainIntegration__RequirementCatalogueFile="$PWD/docs/captain-development/samples/requirement-catalogue.synthetic.json"
export CaptainIntegration__SurveyStorageDirectory="$HOME/bm-captain/data/surveys"
export CaptainIntegration__ReportStorageDirectory="$HOME/bm-captain/data/reports"
export CaptainIntegration__ArchitectRecipientsFile="$PWD/docs/captain-development/samples/architect-directory.synthetic.json"
export CaptainIntegration__SharingPolicyVersion="captain-sharing-policy-dev-1"
export CaptainIntegration__SurveyWorker__PollSeconds=2
export CaptainIntegration__SurveyWorker__LeaseSeconds=120
if [ -n "${PY_PORT:-}" ] && [ -s "$S/python-internal.token" ]; then
  export Services__Python__Enabled=true Services__Python__BaseUrl="http://127.0.0.1:$PY_PORT"
  export Services__Python__AuthToken="$(cat "$S/python-internal.token")" Services__Python__TimeoutSeconds=30
fi
if [ -n "${RAG_PORT:-}" ] && [ -s "$S/rag-internal.token" ]; then
  export Services__Rag__Enabled=true Services__Rag__BaseUrl="http://127.0.0.1:$RAG_PORT"
  export Services__Rag__AuthToken="$(cat "$S/rag-internal.token")"
  export Regulations__Retrieval__Enabled=true
fi
exec dotnet src/BuildMind.API/bin/Release/net10.0/BuildMind.API.dll
