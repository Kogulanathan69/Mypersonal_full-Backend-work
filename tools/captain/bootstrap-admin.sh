#!/usr/bin/env bash
# One-time operator step for the LOCAL Captain stack: registers a synthetic admin account through the public API and
# records its credentials under ~/bm-captain/secrets (0700, outside the repository). restart-stack.sh then passes the
# email to CaptainIntegration:AdminBootstrap:Email, which grants ADMIN only while no active admin exists (inert later).
# No credential is hardcoded or committed; the password is random. Requires the API on 127.0.0.1:5180.
set -euo pipefail
S=~/bm-captain/secrets
if [ -s "$S/captain-admin.email" ] && [ -s "$S/captain-admin.password" ]; then echo "admin credentials already recorded"; exit 0; fi
email="captain-admin-$(openssl rand -hex 4)@example.invalid"
password="Adm-$(openssl rand -base64 24 | tr -d '/+=')9a!"
body=$(python3 -c 'import json,sys; print(json.dumps({"email": sys.argv[1], "password": sys.argv[2]}))' "$email" "$password")
code=$(curl -s -o /dev/null -w '%{http_code}' -H 'Content-Type: application/json' -d "$body" http://127.0.0.1:5180/api/v1/auth/register)
if [ "$code" != 201 ] && [ "$code" != 200 ]; then echo "registration failed ($code)"; exit 1; fi
umask 077
printf '%s' "$email" > "$S/captain-admin.email"
printf '%s' "$password" > "$S/captain-admin.password"
echo "admin account registered; restart the stack to apply the bootstrap"
