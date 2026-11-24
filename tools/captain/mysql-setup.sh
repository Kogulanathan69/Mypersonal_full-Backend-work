#!/usr/bin/env bash
# Captain-owned local development MySQL 8.4 (WSL Docker). Idempotent; never prints a credential.
#
#   container  bm-captain-mysql   (loopback only: 127.0.0.1:${BM_CAPTAIN_MYSQL_PORT:-3417})
#   volume     bm-captain-mysql-data (persistent development data; kept when the container is recreated)
#   databases  buildmind_dev (application), buildmind_test_* (disposable test databases)
#   accounts   bm_runtime  DML on buildmind_dev.*                  REQUIRE SSL
#              bm_migrator DDL+DML on buildmind_dev.*              REQUIRE SSL (migrations only)
#              bm_test     ALL on `buildmind\_test\_%`.*           REQUIRE SSL (integration tests only)
#   TLS        Captain local CA (~/bm-captain/secrets/tls, dir 0700) issues a server certificate with SANs
#              DNS:localhost and IP:127.0.0.1, so clients can use SslMode=VerifyFull (chain + host name).
#              Server files are bind-mounted individually and are 0644 because mysqld runs as uid 999 in the
#              container; the 0700 host directory keeps them private to the Captain account on the host.
#   secrets    ~/bm-captain/secrets (0700), one 0600 file per credential
#
# RECREATE=1 replaces the container (data volume kept), e.g. after TLS material changes.
set -euo pipefail

NAME=bm-captain-mysql
VOLUME=bm-captain-mysql-data
PORT="${BM_CAPTAIN_MYSQL_PORT:-3417}"
IMAGE='mysql:8.4@sha256:a81d043930e51745522bd5c430961cf8286f0a23f0a9a2a6e0108f7ea71b0902'
SECRETS="$HOME/bm-captain/secrets"
TLS="$SECRETS/tls"

umask 077
mkdir -p "$SECRETS" "$TLS"
chmod 700 "$SECRETS" "$TLS"
for account in root runtime migrator test; do
  [ -s "$SECRETS/mysql-$account.password" ] || openssl rand -hex 24 > "$SECRETS/mysql-$account.password"
done
pw() { cat "$SECRETS/mysql-$1.password"; }

if [ ! -s "$TLS/server-cert.pem" ]; then
  openssl req -x509 -newkey rsa:3072 -nodes -days 365 -sha256 -subj "/CN=BuildMind Captain Local CA" \
    -keyout "$TLS/ca-key.pem" -out "$TLS/ca.pem" 2>/dev/null
  openssl req -newkey rsa:3072 -nodes -sha256 -subj "/CN=localhost" \
    -keyout "$TLS/server-key.pem" -out "$TLS/server.csr" 2>/dev/null
  printf 'subjectAltName=DNS:localhost,IP:127.0.0.1\nextendedKeyUsage=serverAuth\nbasicConstraints=CA:FALSE\n' > "$TLS/server.ext"
  openssl x509 -req -in "$TLS/server.csr" -CA "$TLS/ca.pem" -CAkey "$TLS/ca-key.pem" -CAcreateserial -days 365 -sha256 \
    -extfile "$TLS/server.ext" -out "$TLS/server-cert.pem" 2>/dev/null
  rm -f "$TLS/server.csr"
  chmod 600 "$TLS/ca-key.pem"
  chmod 644 "$TLS/ca.pem" "$TLS/server-cert.pem" "$TLS/server-key.pem"
  echo "generated Captain local CA and server certificate (SAN localhost, 127.0.0.1)"
  RECREATE=1
fi

if [ "${RECREATE:-0}" = 1 ] && docker container inspect "$NAME" >/dev/null 2>&1; then
  docker rm -f "$NAME" >/dev/null
  echo "removed container $NAME (volume $VOLUME kept)"
fi

if ! docker container inspect "$NAME" >/dev/null 2>&1; then
  docker run -d --name "$NAME" \
    --label buildmind.owner=captain \
    -e MYSQL_ROOT_PASSWORD_FILE=/run/captain-secrets/root \
    -v "$SECRETS/mysql-root.password:/run/captain-secrets/root:ro" \
    -v "$TLS/ca.pem:/etc/mysql/captain-tls/ca.pem:ro" \
    -v "$TLS/server-cert.pem:/etc/mysql/captain-tls/server-cert.pem:ro" \
    -v "$TLS/server-key.pem:/etc/mysql/captain-tls/server-key.pem:ro" \
    -v "$VOLUME:/var/lib/mysql" \
    -p "127.0.0.1:$PORT:3306" \
    --health-cmd 'mysqladmin ping -h 127.0.0.1 --silent' \
    --health-interval 5s --health-timeout 5s --health-retries 40 \
    "$IMAGE" --require-secure-transport=ON \
    --ssl-ca=/etc/mysql/captain-tls/ca.pem --ssl-cert=/etc/mysql/captain-tls/server-cert.pem \
    --ssl-key=/etc/mysql/captain-tls/server-key.pem >/dev/null
  echo "created container $NAME"
else
  docker start "$NAME" >/dev/null
  echo "started existing container $NAME"
fi

for _ in $(seq 1 90); do
  [ "$(docker inspect --format '{{.State.Health.Status}}' "$NAME")" = healthy ] && break
  sleep 2
done
[ "$(docker inspect --format '{{.State.Health.Status}}' "$NAME")" = healthy ] || { echo "MySQL not healthy" >&2; exit 1; }

root_sql() { docker exec -i -e MYSQL_PWD="$(pw root)" "$NAME" mysql -uroot -N "$@"; }

[ "$(root_sql -e 'SELECT @@GLOBAL.partial_revokes;')" = 0 ] || { echo "partial_revokes must be OFF" >&2; exit 1; }

root_sql <<SQL
CREATE DATABASE IF NOT EXISTS buildmind_dev CHARACTER SET utf8mb4 COLLATE utf8mb4_0900_ai_ci;
CREATE USER IF NOT EXISTS 'bm_runtime'@'%' IDENTIFIED BY '$(pw runtime)' REQUIRE SSL;
ALTER USER 'bm_runtime'@'%' IDENTIFIED BY '$(pw runtime)' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE ON buildmind_dev.* TO 'bm_runtime'@'%';
CREATE USER IF NOT EXISTS 'bm_migrator'@'%' IDENTIFIED BY '$(pw migrator)' REQUIRE SSL;
ALTER USER 'bm_migrator'@'%' IDENTIFIED BY '$(pw migrator)' REQUIRE SSL;
GRANT SELECT, INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, INDEX, REFERENCES ON buildmind_dev.* TO 'bm_migrator'@'%';
CREATE USER IF NOT EXISTS 'bm_test'@'%' IDENTIFIED BY '$(pw test)' REQUIRE SSL;
ALTER USER 'bm_test'@'%' IDENTIFIED BY '$(pw test)' REQUIRE SSL;
GRANT ALL PRIVILEGES ON \`buildmind\\_test\\_%\`.* TO 'bm_test'@'%';
SQL

cp "$TLS/ca.pem" "$SECRETS/ca.pem"; chmod 600 "$SECRETS/ca.pem"

# MySql.Data (26.10.0) requires SslCert/SslKey whenever SslMode=VerifyFull is combined with PEM files. Issue a client
# certificate from the same local CA; the server keeps REQUIRE SSL (client certificates are presented, not required).
if [ ! -s "$TLS/client-cert.pem" ]; then
  openssl req -newkey rsa:3072 -nodes -sha256 -subj "/CN=buildmind-captain-client" \
    -keyout "$TLS/client-key.pem" -out "$TLS/client.csr" 2>/dev/null
  printf 'extendedKeyUsage=clientAuth\nbasicConstraints=CA:FALSE\n' > "$TLS/client.ext"
  openssl x509 -req -in "$TLS/client.csr" -CA "$TLS/ca.pem" -CAkey "$TLS/ca-key.pem" -CAcreateserial -days 365 -sha256 \
    -extfile "$TLS/client.ext" -out "$TLS/client-cert.pem" 2>/dev/null
  rm -f "$TLS/client.csr"
  chmod 600 "$TLS/client-key.pem" "$TLS/client-cert.pem"
  echo "issued Captain client certificate"
fi
# MySql.Data parses only traditional PKCS#1 RSA keys ("BEGIN RSA PRIVATE KEY"); OpenSSL 3 writes PKCS#8 by default.
if ! grep -q "BEGIN RSA PRIVATE KEY" "$TLS/client-key.pem"; then
  openssl rsa -in "$TLS/client-key.pem" -traditional -out "$TLS/client-key.pkcs1.pem" 2>/dev/null
  mv "$TLS/client-key.pkcs1.pem" "$TLS/client-key.pem"
  chmod 600 "$TLS/client-key.pem"
  echo "client key converted to PKCS#1 for MySql.Data"
fi

echo "server: $(root_sql -e 'SELECT VERSION();')"
echo "tls:    $(docker exec -e MYSQL_PWD="$(pw runtime)" "$NAME" mysql -ubm_runtime -h127.0.0.1 --ssl-mode=VERIFY_IDENTITY \
  --ssl-ca=/etc/mysql/captain-tls/ca.pem -N -e "SHOW SESSION STATUS LIKE 'Ssl_version';" | tr '\t' ' ') (VERIFY_IDENTITY)"
root_sql -e "SHOW GRANTS FOR 'bm_runtime'@'%'; SHOW GRANTS FOR 'bm_migrator'@'%'; SHOW GRANTS FOR 'bm_test'@'%';" | sed 's/IDENTIFIED.*//'
if docker exec -e MYSQL_PWD="$(pw runtime)" "$NAME" mysql -ubm_runtime -h127.0.0.1 --ssl-mode=DISABLED -N -e 'SELECT 1' >/dev/null 2>&1; then
  echo "plaintext login unexpectedly accepted" >&2; exit 1
fi
echo "plaintext login refused (expected)"
