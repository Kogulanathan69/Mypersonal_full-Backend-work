#!/usr/bin/env bash
# Starts rag-service (unchanged M7 code) on loopback with the internal token required (fail closed). Usage: run-rag.sh [port]
#
# Corpus: the approved STEP 4 corpus staged by rag-service/tools/stage_approved_corpus.py into ~/bm-captain/rag-corpus
# (identity pins, PDF/record SHA-256 re-verified by the unchanged loaders at start-up). RAG_CORPUS=0 runs without it
# (fail closed: 503 CORPUS_UNAVAILABLE). Transport: mutual TLS with the Captain PKI when present (RAG_MTLS=0 disables).
set -euo pipefail
cd ~/bm-captain/src/rag-service
export BUILDMIND_RAG_INTERNAL_TOKENS="$(cat "$HOME/bm-captain/secrets/rag-internal.token")"
export BUILDMIND_RAG_AUTH=required
C="$HOME/bm-captain/rag-corpus"
if [ "${RAG_CORPUS:-1}" = 1 ] && [ -s "$C/STAGING-RECEIPT.json" ]; then
  export BUILDMIND_RAG_CORPUS_ROOT="$C" BUILDMIND_RAG_MANIFEST="$C/approved_corpus_manifest.json" BUILDMIND_RAG_INDEX="$C/multi_document_index.json"
fi
P="$HOME/bm-captain/secrets/pki"; TLS=()
if [ "${RAG_MTLS:-1}" = 1 ] && [ -s "$P/rag-server.crt" ]; then
  TLS=(--ssl-keyfile "$P/rag-server.key" --ssl-certfile "$P/rag-server.crt" --ssl-ca-certs "$P/client-ca.crt" --ssl-cert-reqs 2)
fi
exec "$HOME/bm-captain/venv-rag-service/bin/python" -B -m uvicorn app.main:app --host 127.0.0.1 --port "${1:-8092}" --no-access-log "${TLS[@]}"
