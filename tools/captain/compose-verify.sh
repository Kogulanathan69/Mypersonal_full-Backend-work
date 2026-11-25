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
COMPOSE=(docker compose -p bm-captain-verify -f deploy/captain/compose.verify.yml)

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
  chmod 644 "$V/mysql-certs/"*.pem   # read by the mysql (uid 999) and app containers
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
  chmod 644 "$V/initdb/10-accounts.sh" "$V/initdb/schema/20-schema.sql"; chmod 755 "$V/initdb" "$V/initdb/schema"
  # python-services mTLS material (existing PKI: server cert SAN includes python-services) and token.
  cp "$S/pki/python-server.crt" "$S/pki/python-server.key" "$S/pki/client-ca.crt" "$S/pki/server-ca.crt" \
     "$S/pki/api-client.crt" "$S/pki/api-client.key" "$V/pki/"
  chmod 644 "$V/pki/"*
  cp "$S/python-internal.token" "$V/secrets/python-internal.token"; chmod 644 "$V/secrets/python-internal.token" "$V/secrets/root.password"
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
  chmod 600 "$V/secrets/api.env"
  echo "prepared $V"
}

case "${1:-status}" in
  prepare) prepare ;;
  build)
    docker build -f deploy/captain/python.Dockerfile -t buildmind-python:verify . 2>&1 | tail -2
    docker build -f deploy/captain/api.Dockerfile -t buildmind-api:verify . 2>&1 | tail -2
    docker image ls --format '{{.Repository}}:{{.Tag}} {{.ID}} {{.Size}}' | grep -E "buildmind-(api|python):verify" ;;
  up) BM_VERIFY_DIR=$V BM_SAMPLES="$PWD/docs/captain-development/samples" "${COMPOSE[@]}" up -d && BM_VERIFY_DIR=$V BM_SAMPLES="$PWD/docs/captain-development/samples" "${COMPOSE[@]}" ps ;;
  down) BM_VERIFY_DIR=$V BM_SAMPLES="$PWD/docs/captain-development/samples" "${COMPOSE[@]}" down ;;
  reset) BM_VERIFY_DIR=$V BM_SAMPLES="$PWD/docs/captain-development/samples" "${COMPOSE[@]}" down -v ;;  # disposable verification volumes only
  status) BM_VERIFY_DIR=$V BM_SAMPLES="$PWD/docs/captain-development/samples" "${COMPOSE[@]}" ps ;;
esac
