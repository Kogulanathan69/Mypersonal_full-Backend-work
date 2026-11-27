#!/usr/bin/env bash
set -Eeuo pipefail
umask 077

STATE_DIR="${HOME}/.local/share/buildmind-portable"
SECRET_DIR="${STATE_DIR}/secrets"

for cmd in docker openssl flock; do
    command -v "$cmd" >/dev/null 2>&1 || {
        echo "MISSING_PREREQUISITE: $cmd" >&2
        exit 1
    }
done

docker info >/dev/null 2>&1 || {
    echo "DOCKER_ENGINE_UNAVAILABLE" >&2
    exit 1
}

mkdir -p "$STATE_DIR" "$SECRET_DIR"
chmod 700 "$STATE_DIR" "$SECRET_DIR"

exec 9>"${STATE_DIR}/bootstrap.lock"
flock -x 9

ensure_secret() {
    local name="$1"
    local format="$2"
    local target="${SECRET_DIR}/${name}"
    local temporary

    if [[ -e "$target" || -L "$target" ]]; then
        [[ -f "$target" && ! -L "$target" && -s "$target" ]] || {
            echo "INVALID_EXISTING_SECRET: $name" >&2
            exit 1
        }
        chmod 600 "$target"
        return
    fi

    temporary="$(mktemp "${SECRET_DIR}/.secret.XXXXXX")"
    openssl rand "-${format}" 32 > "$temporary"
    chmod 600 "$temporary"
    mv -- "$temporary" "$target"
}

ensure_secret mysql_root_password hex
ensure_secret mysql_runtime_password hex
ensure_secret mysql_migrator_password hex
ensure_secret jwt_signing_key base64
ensure_secret python_internal_token hex
ensure_secret rag_internal_token hex

echo "PORTABLE_CREDENTIALS_READY"
