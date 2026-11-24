#!/usr/bin/env bash
# Starts python-services on loopback with the internal token required (fail closed). Usage: run-python.sh [port]
set -euo pipefail
cd ~/bm-captain/src/python-services
export BUILDMIND_PY_INTERNAL_TOKENS="$(cat "$HOME/bm-captain/secrets/python-internal.token")"
export BUILDMIND_PY_AUTH=required
exec "$HOME/bm-captain/venv-python-services/bin/python" -m uvicorn app.main:app --host 127.0.0.1 --port "${1:-8091}" --no-access-log
