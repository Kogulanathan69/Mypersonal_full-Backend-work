#!/usr/bin/env bash
# Local LLM for natural-language plan edits: llama.cpp b12237 (pinned, SHA-256 verified) serving
# Qwen2.5-1.5B-Instruct Q4_K_M (SHA-256 verified) through its OpenAI-compatible API on loopback only.
# No project data leaves the host. One slot, 2048-token context, 2 threads (memory-constrained host).
set -euo pipefail
BIN=$(find ~/bm-captain/tools/llama-b12237 -name llama-server -type f | head -1)
MODEL=~/bm-captain/models/qwen2.5-1.5b-instruct-q4_k_m.gguf
[ -x "$BIN" ] && [ -s "$MODEL" ] || { echo "llama-server or model missing"; exit 1; }
export LD_LIBRARY_PATH="$(dirname "$BIN"):${LD_LIBRARY_PATH:-}"
exec "$BIN" -m "$MODEL" --host 127.0.0.1 --port "${1:-8093}" -c 2048 -t 2 -np 1 --alias qwen2.5-1.5b-instruct-q4_k_m
