#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

STATE_DIR="${HOME}/.local/share/buildmind-portable"
PKI_DIR="${STATE_DIR}/pki"

mkdir -p "$PKI_DIR"
chmod 700 "$PKI_DIR"

exec 9>"${STATE_DIR}/certificates.lock"
flock -x 9

create_ca() {
    local name="$1"

    if [[ -e "$PKI_DIR/$name.key" || -e "$PKI_DIR/$name.crt" ]]; then
        [[ -s "$PKI_DIR/$name.key" && -s "$PKI_DIR/$name.crt" ]] || {
            echo "INCOMPLETE_CA: $name" >&2
            exit 1
        }
        openssl x509 -checkend 86400 -noout \
            -in "$PKI_DIR/$name.crt"
        return
    fi

    openssl genrsa -out "$PKI_DIR/$name.key" 3072

    openssl req -new -x509 -sha256 -days 825 \
        -key "$PKI_DIR/$name.key" \
        -out "$PKI_DIR/$name.crt" \
        -subj "/CN=BuildMind Portable $name" \
        -addext "basicConstraints=critical,CA:TRUE,pathlen:0" \
        -addext "keyUsage=critical,keyCertSign,cRLSign"
}

create_leaf() {
    local name="$1"
    local ca="$2"
    local usage="$3"
    local san="$4"

    if [[ -e "$PKI_DIR/$name.key" || -e "$PKI_DIR/$name.crt" ]]; then
        [[ -s "$PKI_DIR/$name.key" && -s "$PKI_DIR/$name.crt" ]] || {
            echo "INCOMPLETE_CERTIFICATE: $name" >&2
            exit 1
        }
        openssl x509 -checkend 86400 -noout \
            -in "$PKI_DIR/$name.crt"
        openssl verify -CAfile "$PKI_DIR/$ca.crt" \
            "$PKI_DIR/$name.crt"
        return
    fi

    openssl genrsa -traditional \
        -out "$PKI_DIR/$name.key" 2048

    openssl req -new \
        -key "$PKI_DIR/$name.key" \
        -out "$PKI_DIR/$name.csr" \
        -subj "/CN=$name"

    printf '%s\n' \
        'basicConstraints=critical,CA:FALSE' \
        'keyUsage=critical,digitalSignature,keyEncipherment' \
        "extendedKeyUsage=$usage" \
        "subjectAltName=$san" \
        > "$PKI_DIR/$name.ext"

    openssl x509 -req -sha256 -days 90 \
        -in "$PKI_DIR/$name.csr" \
        -CA "$PKI_DIR/$ca.crt" \
        -CAkey "$PKI_DIR/$ca.key" \
        -CAcreateserial \
        -extfile "$PKI_DIR/$name.ext" \
        -out "$PKI_DIR/$name.crt"

    openssl verify -CAfile "$PKI_DIR/$ca.crt" \
        "$PKI_DIR/$name.crt"

    rm -f "$PKI_DIR/$name.csr" "$PKI_DIR/$name.ext"
}

create_ca server-ca
create_ca client-ca
create_ca mysql-ca

create_leaf python-server server-ca serverAuth DNS:python-services
create_leaf rag-server server-ca serverAuth DNS:rag-service
create_leaf mailpit server-ca serverAuth DNS:mailpit
create_leaf api-client client-ca clientAuth DNS:buildmind-api
create_leaf health-client client-ca clientAuth DNS:buildmind-health
create_leaf mysql-server mysql-ca serverAuth DNS:mysql
create_leaf mysql-client mysql-ca clientAuth DNS:buildmind-api
create_leaf mysql-migrator mysql-ca clientAuth DNS:buildmind-migrator

chmod 600 "$PKI_DIR"/*.key
chmod 644 "$PKI_DIR"/*.crt

echo "PORTABLE_TLS_CERTIFICATES_READY"
