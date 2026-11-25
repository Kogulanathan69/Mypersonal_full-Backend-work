#!/usr/bin/env bash
# Stops ONLY Captain-owned workloads: processes of the current user whose command line runs from ~/bm-captain
# (API build output, the Captain venvs, the Captain llama.cpp build) and containers of the bm-captain-verify compose
# project (including profiled services). Never touches other users, other members' containers (e.g. bm-m4-*),
# the Captain MySQL/Mailpit containers or any data volume. Usage: stop-captain.sh [--containers]
set -u
ME=$(id -u)
ROOT="$HOME/bm-captain"
patterns=("$ROOT/src/src/BuildMind.API/bin/" "$ROOT/venv-python-services/bin/python"
          "$ROOT/venv-rag-service/bin/python" "$ROOT/tools/llama-b12237/")
for pid in $(pgrep -u "$ME" -f "BuildMind.API.dll|venv-python-services|venv-rag-service|llama-b12237" || true); do
  [ "$pid" = "$$" ] && continue
  cwd=$(readlink "/proc/$pid/cwd" 2>/dev/null || true); cmd=$(tr '\0' ' ' < "/proc/$pid/cmdline" 2>/dev/null || true)
  owned=0
  for p in "${patterns[@]}"; do case "$cmd" in *"$p"*) owned=1 ;; esac; done
  case "$cwd" in "$ROOT"/*) [ $owned = 0 ] && case "$cmd" in *BuildMind.API.dll*) owned=1 ;; esac ;; esac
  if [ $owned = 1 ]; then kill "$pid" 2>/dev/null && echo "stopped $pid ${cmd:0:70}"; fi
done
if [ "${1:-}" = "--containers" ]; then
  for c in $(docker ps -q --filter "label=com.docker.compose.project=bm-captain-verify"); do
    docker stop "$c" >/dev/null && echo "stopped container $(docker inspect -f '{{.Name}}' "$c")"
  done
fi
sleep 2
