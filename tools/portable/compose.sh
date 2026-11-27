#!/usr/bin/env bash
set -Eeuo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

export BM_STATE="${HOME}/.local/share/buildmind-portable"
export BM_UID="$(id -u)"
export BM_GID="$(id -g)"
export BM_API_PORT="${BM_API_PORT:-5182}"
export BM_MAIL_PORT="${BM_MAIL_PORT:-8027}"

command -v docker >/dev/null 2>&1 || {
    echo "DOCKER_CLI_UNAVAILABLE" >&2
    exit 1
}

docker compose version

sample_file="$(find "$ROOT" -type f \
    -name requirement-catalogue.synthetic.json \
    -print -quit)"

if [[ -z "$sample_file" ]]; then
    echo "REQUIREMENT_SAMPLE_NOT_FOUND" >&2
    exit 1
fi

export BM_SAMPLES="$(dirname "$sample_file")"

for file in \
    requirement-catalogue.synthetic.json \
    generation-policy.synthetic.json \
    architect-directory.synthetic.json
do
    [[ -f "$BM_SAMPLES/$file" ]] || {
        echo "REQUIRED_SAMPLE_MISSING: $file" >&2
        exit 1
    }
    echo "SAMPLE_FOUND: $file"
done

docker compose \
    --project-name bm-portable \
    --file "$ROOT/deploy/portable/compose.yml" \
    "$@"
