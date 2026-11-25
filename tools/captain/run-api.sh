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
# Natural-language edits: local RULE-BASED parser (not an AI model); no external provider is configured.
if [ -n "${LLM_PORT:-}" ]; then
  # Real LLM (local llama.cpp, OpenAI-compatible; no project data leaves the host).
  export CaptainIntegration__AiEdit__Provider=openai-compatible CaptainIntegration__AiEdit__BaseUrl="http://127.0.0.1:$LLM_PORT"
  export CaptainIntegration__AiEdit__Model=qwen2.5-1.5b-instruct-q4_k_m CaptainIntegration__AiEdit__TimeoutSeconds=300
else
  export CaptainIntegration__AiEdit__Provider=rule-based-local
fi
# Local email capture (Mailpit, loopback only) for recovery and staff invitations; no real recipients.
export Authentication__PasswordRecovery__Enabled=true
export CaptainIntegration__Email__SmtpHost=127.0.0.1
export CaptainIntegration__Email__SmtpPort=1025
export CaptainIntegration__Email__FromAddress=no-reply@buildmind.invalid
# One-shot operator bootstrap (inert once an admin exists); set ADMIN_BOOTSTRAP_EMAIL to use it.
if [ -n "${ADMIN_BOOTSTRAP_EMAIL:-}" ]; then export CaptainIntegration__AdminBootstrap__Email="$ADMIN_BOOTSTRAP_EMAIL"; fi
if [ -n "${PY_PORT:-}" ] && [ -s "$S/python-internal.token" ]; then
  P=$S/pki
  if [ -s "$P/api-client.crt" ] && [ "${PY_MTLS:-1}" = 1 ]; then
    export Services__Python__Enabled=true Services__Python__BaseUrl="https://127.0.0.1:$PY_PORT"
    export CaptainIntegration__PythonMtls__ServerCaFile="$P/server-ca.crt" CaptainIntegration__PythonMtls__ClientCertFile="$P/api-client.crt" CaptainIntegration__PythonMtls__ClientKeyFile="$P/api-client.key"
  else
    export Services__Python__Enabled=true Services__Python__BaseUrl="http://127.0.0.1:$PY_PORT"
  fi
  export Services__Python__AuthToken="$(cat "$S/python-internal.token")" Services__Python__TimeoutSeconds=30
fi
if [ -n "${RAG_PORT:-}" ] && [ -s "$S/rag-internal.token" ]; then
  export Services__Rag__Enabled=true Services__Rag__BaseUrl="http://127.0.0.1:$RAG_PORT"
  export Services__Rag__AuthToken="$(cat "$S/rag-internal.token")"
  export Regulations__Retrieval__Enabled=true
fi
if [ "${PRINT_ENV:-0}" = 1 ]; then
  env | grep -E "^(ASPNETCORE|Database|Authentication|Jwt|CaptainIntegration|Services|ConnectionStrings|Regulations|Cors)__?" || true
  exit 0
fi
exec dotnet src/BuildMind.API/bin/Release/net10.0/BuildMind.API.dll
