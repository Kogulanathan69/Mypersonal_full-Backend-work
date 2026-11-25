#!/usr/bin/env bash
# Local internal PKI for API <-> python-services mutual TLS (LOCAL ONLY; private keys never leave ~/bm-captain/secrets).
#   server-ca: signs the python-services server certificate (SAN: localhost, 127.0.0.1, python-services).
#   client-ca: signs the API client certificate (EKU clientAuth). python-services trusts ONLY this CA for clients,
#              so a server certificate or any certificate from another CA cannot authenticate as a client.
# Separate private key per identity (EC P-256). Usage: pki.sh [init|rotate|negatives]
#   init      create both CAs and leaf certificates if absent
#   rotate    re-issue both LEAF certificates (new keys) under the existing CAs
#   negatives create test-only material: rogue CA + client cert, expired client cert, wrong-SAN server cert
set -euo pipefail
D=~/bm-captain/secrets/pki
mkdir -p "$D" && chmod 700 "$D"
cd "$D"
umask 077

ca() { # name subject
  [ -s "$1.key" ] && return 0
  openssl ecparam -name prime256v1 -genkey -noout -out "$1.key"
  openssl req -x509 -new -key "$1.key" -sha256 -days 825 -subj "/CN=$2" \
    -addext "basicConstraints=critical,CA:TRUE,pathlen:0" -addext "keyUsage=critical,keyCertSign,cRLSign" -out "$1.crt"
}

leaf() { # name subject ca eku san days
  openssl ecparam -name prime256v1 -genkey -noout -out "$1.key.new"
  openssl req -new -key "$1.key.new" -subj "/CN=$2" -out "$1.csr"
  printf 'basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature\nextendedKeyUsage=%s\n%s\n' "$4" "$5" > "$1.ext"
  openssl x509 -req -in "$1.csr" -CA "$3.crt" -CAkey "$3.key" -CAcreateserial -sha256 -days "${6:-90}" -extfile "$1.ext" -out "$1.crt.new"
  mv "$1.key.new" "$1.key"; mv "$1.crt.new" "$1.crt"; rm -f "$1.csr" "$1.ext"
}

issue_leaves() {
  leaf python-server "python-services" server-ca serverAuth "subjectAltName=DNS:localhost,DNS:python-services,IP:127.0.0.1"
  leaf api-client "buildmind-api" client-ca clientAuth "subjectAltName=DNS:buildmind-api"
}

case "${1:-init}" in
  init)
    ca server-ca "BuildMind Captain internal server CA (local)"
    ca client-ca "BuildMind Captain internal client CA (local)"
    [ -s python-server.crt ] && [ -s api-client.crt ] || issue_leaves
    ;;
  rotate)
    issue_leaves
    ;;
  negatives)
    ca rogue-ca "Rogue CA (test only)"
    leaf rogue-client "buildmind-api" rogue-ca clientAuth "subjectAltName=DNS:buildmind-api"
    leaf wrong-san-server "python-services" server-ca serverAuth "subjectAltName=DNS:not-python.example"
    # Expired client certificate (validity entirely in the past) via a throwaway openssl-ca database.
    work=$(mktemp -d); trap 'rm -rf "$work"' EXIT
    touch "$work/index.txt"; echo 1000 > "$work/serial"
    cat > "$work/ca.cnf" <<EOF
[ca]
default_ca = local
[local]
database = $work/index.txt
serial = $work/serial
new_certs_dir = $work
certificate = $D/client-ca.crt
private_key = $D/client-ca.key
default_md = sha256
policy = any
[any]
commonName = supplied
[ext]
basicConstraints = critical,CA:FALSE
keyUsage = critical,digitalSignature
extendedKeyUsage = clientAuth
EOF
    openssl ecparam -name prime256v1 -genkey -noout -out expired-client.key
    openssl req -new -key expired-client.key -subj "/CN=buildmind-api" -out "$work/e.csr"
    openssl ca -batch -config "$work/ca.cnf" -extensions ext -startdate 20200101000000Z -enddate 20200201000000Z \
      -in "$work/e.csr" -out expired-client.crt 2>/dev/null
    ;;
esac
chmod 600 "$D"/*.key 2>/dev/null || true
ls -1 "$D" | grep -E '\.crt$' | tr '\n' ' '; echo
