#!/usr/bin/env bash
# Local mail capture for Development email tests (Mailpit). Loopback only: SMTP 127.0.0.1:1025, API/UI 127.0.0.1:8025.
# Nothing is relayed to real recipients. Usage: mailpit.sh [start|stop|status]
set -euo pipefail
NAME=bm-captain-mailpit
IMAGE=axllent/mailpit:v1.21.8
case "${1:-start}" in
  start)
    if docker ps --format '{{.Names}}' | grep -qx "$NAME"; then echo "mailpit running"; exit 0; fi
    docker rm "$NAME" >/dev/null 2>&1 || true
    docker run -d --name "$NAME" --label buildmind.owner=captain --restart unless-stopped \
      -p 127.0.0.1:1025:1025 -p 127.0.0.1:8025:8025 "$IMAGE" >/dev/null
    for _ in $(seq 1 30); do curl -fsS http://127.0.0.1:8025/api/v1/info >/dev/null 2>&1 && { echo "mailpit started"; exit 0; }; sleep 1; done
    echo "mailpit not ready"; exit 1 ;;
  stop) docker stop "$NAME" >/dev/null && echo "mailpit stopped" ;;
  status) docker ps --filter "name=$NAME" --format '{{.Names}} {{.Status}}' ;;
esac
