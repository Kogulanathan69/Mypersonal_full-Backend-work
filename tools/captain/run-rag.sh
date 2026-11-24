#!/usr/bin/env bash
# Starts rag-service (unchanged M7 code) on loopback with the internal token required (fail closed). Usage: run-rag.sh [port]
#
# Corpus: NOT configured in WSL. The approved STEP 4 manifest at C:\BuildMind-PDF-Processor pins source identities that
# contain Windows path separators; the loader correctly refuses a normalised copy ("Approved identity/scope mismatch"),
# and that integrity control is not bypassed. Without a corpus the service answers search/sources/constraints with
# 503 (CORPUS_UNAVAILABLE / CONSTRAINTS_UNAVAILABLE), which is the fail-closed path the API must handle.
set -euo pipefail
cd ~/bm-captain/src/rag-service
export BUILDMIND_RAG_INTERNAL_TOKENS="$(cat "$HOME/bm-captain/secrets/rag-internal.token")"
export BUILDMIND_RAG_AUTH=required
exec "$HOME/bm-captain/venv-rag-service/bin/python" -B -m uvicorn app.main:app --host 127.0.0.1 --port "${1:-8092}" --no-access-log
