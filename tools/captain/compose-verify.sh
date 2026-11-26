#!/usr/bin/env bash
# Containerized LOCAL verification stack (deploy/captain/compose.verify.yml). All generated material lives in
# ~/bm-captain/compose-verify (0700), never in the repository. Usage: compose-verify.sh prepare|build|up|down|status
#   prepare  MySQL server certificate for hostname "mysql" (Captain CA), fresh passwords, idempotent schema SQL,
#            init script with least-privilege accounts, API env translated from run-api.sh (PRINT_ENV=1)
#   build    sequential image builds (memory-friendly)
#   up/down  start/stop the isolated stack (project "bm-captain-verify"; volumes kept on down)
set -euo pipefail
cd ~/bm-captain/src
V=~/bm-captain/compose-verify
S=~/bm-captain/secrets
export BM_UID="$(id -u)" BM_GID="$(id -g)" BM_VERIFY_DIR="$V" BM_SAMPLES="$PWD/docs/captain-development/samples" BM_RAG_CORPUS="$HOME/bm-captain/rag-corpus"        BM_LLM_MODELS="$HOME/bm-captain/models"
COMPOSE=(docker compose -p bm-captain-verify -f deploy/captain/compose.verify.yml)
[ "${LLM:-0}" = 1 ] && COMPOSE+=(--profile llm)

prepare() {
  mkdir -p "$V"/{mysql-certs,initdb,pki,secrets} && chmod 700 "$V"
  umask 077
  # MySQL server certificate valid for the in-network hostname "mysql" (signed by the existing Captain MySQL CA).
  openssl genrsa -out "$V/mysql-certs/server-key.pem" 2048 2>/dev/null
  openssl req -new -key "$V/mysql-certs/server-key.pem" -subj "/CN=mysql" -out "$V/mysql-certs/server.csr"
  printf 'subjectAltName=DNS:mysql\nextendedKeyUsage=serverAuth\n' > "$V/mysql-certs/server.ext"
  openssl x509 -req -in "$V/mysql-certs/server.csr" -CA "$S/tls/ca.pem" -CAkey "$S/tls/ca-key.pem" -CAcreateserial -days 30 -sha256 \
    -extfile "$V/mysql-certs/server.ext" -out "$V/mysql-certs/server-cert.pem" 2>/dev/null
  cp "$S/tls/ca.pem" "$S/tls/client-cert.pem" "$S/tls/client-key.pem" "$V/mysql-certs/"
  rm -f "$V/mysql-certs/server.csr" "$V/mysql-certs/server.ext"
  chmod 644 "$V/mysql-certs/ca.pem" "$V/mysql-certs/server-cert.pem" "$V/mysql-certs/client-cert.pem"   # read by the mysql (uid 999) and app containers
  for name in root runtime migrator; do [ -s "$V/secrets/$name.password" ] || openssl rand -hex 24 > "$V/secrets/$name.password"; done
  # Full idempotent schema script from the current migrations.
  . tools/captain/env.sh
  # Full (non-idempotent) script: the init runs only on an empty volume; the MySQL provider's --idempotent output
  # uses IF blocks outside stored procedures, which MySQL rejects (ERROR 1064, found in container verification).
  dotnet tool run dotnet-ef migrations script --context BuildMindDbContext --configuration Release \
    --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API --output "$V/initdb/20-schema.sql" >/dev/null
  cat > "$V/initdb/10-accounts.sh" <<EOF
#!/bin/bash
set -e
mysql -uroot -p"\$(cat /run/secrets/mysql_root_password)" <<SQL
CREATE DATABASE IF NOT EXISTS buildmind_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS 'bm_runtime'@'%' IDENTIFIED BY '$(cat "$V/secrets/runtime.password")' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE ON buildmind_dev.* TO 'bm_runtime'@'%';
CREATE USER IF NOT EXISTS 'bm_migrator'@'%' IDENTIFIED BY '$(cat "$V/secrets/migrator.password")' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON buildmind_dev.* TO 'bm_migrator'@'%';
SET PERSIST max_execution_time = 25000;
SET PERSIST innodb_lock_wait_timeout = 20;
SQL
mysql -uroot -p"\$(cat /run/secrets/mysql_root_password)" buildmind_dev < /docker-entrypoint-initdb.d/schema/20-schema.sql
EOF
  mkdir -p "$V/initdb/schema" && mv "$V/initdb/20-schema.sql" "$V/initdb/schema/20-schema.sql"
  chmod 600 "$V/initdb/10-accounts.sh"; chmod 644 "$V/initdb/schema/20-schema.sql"; chmod 755 "$V/initdb" "$V/initdb/schema"
  # python-services mTLS material (existing PKI: server cert SAN includes python-services) and token.
  cp "$S/pki/python-server.crt" "$S/pki/python-server.key" "$S/pki/client-ca.crt" "$S/pki/server-ca.crt" \
     "$S/pki/api-client.crt" "$S/pki/api-client.key" "$V/pki/"
  chmod 644 "$V/pki/"*.crt
  cp "$S/python-internal.token" "$V/secrets/python-internal.token"; chmod 600 "$V/secrets/python-internal.token" "$V/secrets/root.password"
  # API environment: identical to the local stack, translated to container hostnames and paths.
  CaptainIntegration__GenerationPolicyFile="$PWD/docs/captain-development/samples/generation-policy.synthetic.json" \
  ADMIN_BOOTSTRAP_EMAIL="$(cat "$S/captain-admin.email" 2>/dev/null || true)" \
  PY_PORT=8091 PRINT_ENV=1 bash tools/captain/run-api.sh 8080 \
    | sed -e "s#^ASPNETCORE_URLS=.*#ASPNETCORE_URLS=http://+:8080#" \
          -e "s#Server=127.0.0.1;Port=3417#Server=mysql;Port=3306#; s#Server=localhost;Port=3417#Server=mysql;Port=3306#" \
          -e "s#Password=[^;]*#Password=$(cat "$V/secrets/runtime.password")#" \
          -e "s#$S/tls/#/run/bm/mysql/#g; s#$S/ca.pem#/run/bm/mysql/ca.pem#g" \
          -e "s#https://127.0.0.1:8091#https://python-services:8091#" \
          -e "s#$S/pki/#/run/bm/pki/#g" \
          -e "s#$HOME/bm-captain/data/#/var/lib/buildmind/#g" \
          -e "s#$HOME/bm-captain/src/docs/captain-development/samples/#/app/samples/#g" \
          -e "/CaptainIntegration__Email__/d; /Services__Rag__/d; /Regulations__/d" \
    > "$V/secrets/api.env"
  # Email: TLS-required capture (mailpit) reached over the internal network with the unchanged external-SMTP policy.
  mkdir -p "$V/mail"
  openssl ecparam -name prime256v1 -genkey -noout -out "$V/mail/mailpit.key"
  openssl req -new -key "$V/mail/mailpit.key" -subj "/CN=mailpit" -out "$V/mail/mailpit.csr"
  printf 'subjectAltName=DNS:mailpit\nextendedKeyUsage=serverAuth\nkeyUsage=digitalSignature\n' > "$V/mail/mailpit.ext"
  openssl x509 -req -in "$V/mail/mailpit.csr" -CA "$S/pki/server-ca.crt" -CAkey "$S/pki/server-ca.key" -CAcreateserial \
    -days 30 -sha256 -extfile "$V/mail/mailpit.ext" -out "$V/mail/mailpit.crt" 2>/dev/null
  rm -f "$V/mail/mailpit.csr" "$V/mail/mailpit.ext"
  { docker run --rm --entrypoint cat buildmind-api:verify /etc/ssl/certs/ca-certificates.crt; cat "$S/pki/server-ca.crt"; } > "$V/mail/ca-bundle.pem"
  chmod 755 "$V/mail"; chmod 644 "$V/mail/"*.crt "$V/mail/"*.pem; chmod 600 "$V/mail/mailpit.key"
  {
    echo "SSL_CERT_FILE=/run/bm/ca-bundle.pem"
    echo "CaptainIntegration__Email__SmtpHost=mailpit"
    echo "CaptainIntegration__Email__SmtpPort=1025"
    echo "CaptainIntegration__Email__FromAddress=no-reply@buildmind.invalid"
    echo "CaptainIntegration__Email__UseStartTls=true"
    echo "CaptainIntegration__Email__AllowExternalDelivery=true"
  } >> "$V/secrets/api.env"
  # RAG over internal mTLS (same handler and trust as python-services) with the staged approved corpus.
  {
    echo "Services__Rag__Enabled=true"
    echo "Services__Rag__BaseUrl=https://rag-service:8092"
    echo "Services__Rag__AuthToken=$(cat "$S/rag-internal.token")"
    echo "Regulations__Retrieval__Enabled=true"
  } >> "$V/secrets/api.env"
  if [ "${LLM:-0}" = 1 ]; then
    {
      echo "CaptainIntegration__AiEdit__Provider=openai-compatible"
      echo "CaptainIntegration__AiEdit__BaseUrl=http://llm:8093"
      echo "CaptainIntegration__AiEdit__Model=qwen2.5-1.5b-instruct-q4_k_m"
      echo "CaptainIntegration__AiEdit__TimeoutSeconds=300"
    } >> "$V/secrets/api.env"
  fi
  printf 'BUILDMIND_RAG_AUTH=required
BUILDMIND_RAG_INTERNAL_TOKENS=%s
' "$(cat "$S/rag-internal.token")" > "$V/secrets/rag.env"
  chmod 600 "$V/secrets/api.env" "$V/secrets/rag.env"
  cp "$S/pki/rag-server.crt" "$S/pki/rag-server.key" "$V/pki/"; chmod 644 "$V/pki/"*.crt
  echo "prepared $V"
  restrict_credentials
}

restrict_credentials() {
  local acl=tools/captain/restrict-credential.py
  python3 "$acl" "$V/mysql-certs/server-key.pem" 999
  python3 "$acl" "$V/mysql-certs/client-key.pem" 1654
  python3 "$acl" "$V/secrets/root.password" 999
  python3 "$acl" "$V/initdb/10-accounts.sh" 999
  python3 "$acl" "$V/secrets/python-internal.token" 10001
  python3 "$acl" "$V/pki/python-server.key" 10001
  python3 "$acl" "$V/pki/rag-server.key" "$BM_UID"
  python3 "$acl" "$V/pki/api-client.key" 1654 10001 "$BM_UID"
  python3 "$acl" "$V/mail/mailpit.key"
}

# Upgrade path for an EXISTING verification volume: the init script only runs on an empty volume, so pending migrations
# are applied as a ranged script (from the last applied migration) by the least-privilege migrator over verified TLS.
migrate() {
  "${COMPOSE[@]}" up -d mysql >/dev/null || return 1
  for i in $(seq 1 60); do docker inspect -f '{{.State.Health.Status}}' bm-captain-verify-mysql-1 2>/dev/null | grep -q healthy && break; sleep 3; done
  docker inspect -f '{{.State.Health.Status}}' bm-captain-verify-mysql-1 | grep -qx healthy || { echo "migrate: MySQL readiness failed" >&2; return 1; }
  local mysqlx=(docker exec -i -e MYSQL_PWD="$(cat "$V/secrets/migrator.password")" bm-captain-verify-mysql-1
                mysql -ubm_migrator -hmysql --ssl-mode=VERIFY_IDENTITY --ssl-ca=/etc/mysql/tls/ca.pem buildmind_dev)
  local applied; applied=$("${mysqlx[@]}" -N -e "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId DESC LIMIT 1")
  [ -n "$applied" ] || { echo "migrate: no history (volume not initialised)"; return 1; }
  . tools/captain/env.sh
  local script="$V/initdb/pending-$(date +%s).sql"
  dotnet tool run dotnet-ef migrations script "$applied" --context BuildMindDbContext --configuration Release     --project src/BuildMind.Infrastructure --startup-project src/BuildMind.API --output "$script" >/dev/null || return 1
  if grep -qE "CREATE|ALTER|INSERT" "$script"; then "${mysqlx[@]}" < "$script" || return 1; echo "migrate: applied pending migrations after $applied"
  else echo "migrate: up to date ($applied)"; fi
  rm -f "$script"
}

case "${1:-status}" in
  restrict-credentials) restrict_credentials ;;
  expiry-up) "${COMPOSE[@]}" run -d --rm --no-deps --name bm-captain-verify-expiry -p 127.0.0.1:5181:8080 -e CaptainIntegration__SurveyWorker__Enabled=false -e CaptainIntegration__Architect__ConsentLifetimeMinutes=1 -e CaptainIntegration__GenerationPolicyFile=/app/samples/generation-policy.rag.json api ;;
  migrate) migrate ;;
  prepare) prepare ;;
  build)
    docker build -f deploy/captain/python.Dockerfile -t buildmind-python:verify . 2>&1 | tail -2
    docker build -f deploy/captain/api.Dockerfile -t buildmind-api:verify . 2>&1 | tail -2
    docker build -f deploy/captain/rag.Dockerfile -t buildmind-rag:verify . 2>&1 | tail -2
    [ "${LLM:-0}" = 1 ] && docker build -f "$PWD/deploy/captain/llm.Dockerfile" -t buildmind-llm:verify "$HOME/bm-captain/tools/llama-b12237/llama-b12237" 2>&1 | tail -2
    docker image ls --format '{{.Repository}}:{{.Tag}} {{.ID}} {{.Size}}' | grep -E "buildmind-(api|python|rag|llm):verify" ;;
  up) migrate && "${COMPOSE[@]}" up -d && "${COMPOSE[@]}" ps ;;
  down) "${COMPOSE[@]}" down ;;
  reset) "${COMPOSE[@]}" down -v ;;  # disposable verification volumes only
  status) "${COMPOSE[@]}" ps ;;
  llm-up) "${COMPOSE[@]}" up -d --no-deps llm api ;;  # LLM=1: start the model and recreate the API with AiEdit settings
esac
