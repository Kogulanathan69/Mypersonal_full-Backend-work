#!/usr/bin/env bash
# Waits (bounded) until the Captain API and python-services answer /health [and rag-service when RAG=1].
for i in $(seq 1 150); do
  if curl -fsS http://127.0.0.1:5180/health >/dev/null 2>&1 && curl -fsS --cacert ~/bm-captain/secrets/pki/server-ca.crt --cert ~/bm-captain/secrets/pki/api-client.crt --key ~/bm-captain/secrets/pki/api-client.key https://127.0.0.1:8091/health >/dev/null 2>&1 \
     && { [ "${RAG:-0}" != 1 ] || curl -fsS http://127.0.0.1:8092/health >/dev/null 2>&1; }; then
    echo "stack ready after $((i * 2))s"
    exit 0
  fi
  sleep 2
done
echo "stack not ready"
exit 1
