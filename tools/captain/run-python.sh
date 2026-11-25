#!/usr/bin/env bash
# python-services on loopback. With PY_MTLS=1 (default when the Captain PKI exists) it serves HTTPS only and REQUIRES a
# client certificate issued by the dedicated client CA (ssl-cert-reqs 2 = CERT_REQUIRED); there is no plaintext
# listener. The internal token is still required on every internal route.
set -euo pipefail
cd ~/bm-captain/src/python-services
export BUILDMIND_PY_INTERNAL_TOKENS="$(cat "$HOME/bm-captain/secrets/python-internal.token")"
export BUILDMIND_PY_AUTH=required
P=$HOME/bm-captain/secrets/pki
tls=()
if [ "${PY_MTLS:-$([ -s "$P/python-server.crt" ] && echo 1 || echo 0)}" = 1 ]; then
  tls=(--ssl-keyfile "$P/python-server.key" --ssl-certfile "$P/python-server.crt" --ssl-ca-certs "$P/client-ca.crt" --ssl-cert-reqs 2)
fi
exec "$HOME/bm-captain/venv-python-services/bin/python" -m uvicorn app.main:app --host 127.0.0.1 --port "${1:-8091}" --no-access-log "${tls[@]}"
