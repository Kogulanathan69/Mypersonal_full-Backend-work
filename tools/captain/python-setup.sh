#!/usr/bin/env bash
# Creates Captain-owned Python 3.13 virtual environments in WSL from the repository's pinned requirements, using a
# pinned, checksum-verified uv binary (~/bm-captain/tools/uv-0.9.5/uv; no sudo, no system packages), and generates
# per-service internal tokens (0600) under ~/bm-captain/secrets. Never prints a token.
set -euo pipefail
cd ~/bm-captain/src
UV="$HOME/bm-captain/tools/uv-0.9.5/uv"
export UV_PYTHON_INSTALL_DIR="$HOME/bm-captain/tools/python" UV_CACHE_DIR="$HOME/bm-captain/tools/uv-cache"
S="$HOME/bm-captain/secrets"; umask 077; mkdir -p "$S"
for svc in python-services rag-service; do
  venv="$HOME/bm-captain/venv-$svc"
  [ -x "$venv/bin/python" ] || "$UV" venv -q --python 3.13 "$venv"
  "$UV" pip install -q --python "$venv/bin/python" -r "$svc/requirements.txt"
  [ -f "$svc/requirements-dev.txt" ] && "$UV" pip install -q --python "$venv/bin/python" -r "$svc/requirements-dev.txt"
  echo "$svc venv ready: $("$venv/bin/python" --version)"
done
[ -s "$S/python-internal.token" ] || openssl rand -hex 32 > "$S/python-internal.token"
[ -s "$S/rag-internal.token" ] || openssl rand -hex 32 > "$S/rag-internal.token"
echo "tokens present"
