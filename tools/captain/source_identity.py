"""Record the Windows Git inputs and verify their bytes in the WSL mirror."""
import hashlib
import hmac
import json
import os
from pathlib import Path
import subprocess
import sys

root = Path("/mnt/c/Users/kogul/Documents/BuildMind-Worktrees/captain-full-backend-completion")
mirror = Path.home() / "bm-captain/src"


def git(*args):
    return subprocess.check_output(["git.exe", *args], cwd=root)


def digest(path):
    with path.open("rb") as source:
        return hashlib.file_digest(source, "sha256").hexdigest()


commit = git("rev-parse", "HEAD").decode().strip()
status = git("status", "--porcelain=v1", "-z")
tree = "dirty" if status else "clean"
if [commit, tree] != sys.argv[1:3]:
    sys.exit("caller identity differs from authoritative Git status")
inputs = {}
for raw in git("ls-files", "-z", "--cached", "--others", "--exclude-standard").split(b"\0"):
    if not raw:
        continue
    name = raw.decode()
    if "TestResults" in Path(name).parts:
        continue
    source, copy = root / name, mirror / name
    expected = digest(source) if source.exists() else None
    if (digest(copy) if copy.exists() else None) != expected:
        sys.exit(f"source mirror differs at {name}")
    inputs[name] = expected
generated = {str(p.relative_to(mirror)): digest(p) for p in mirror.glob("tests/*/GeneratedMigrations/*.cs")}
captain = mirror.parent
key_path = captain / "secrets/provenance.key"
try:
    fd = os.open(key_path, os.O_CREAT | os.O_EXCL | os.O_WRONLY, 0o600)
except FileExistsError:
    pass
else:
    with os.fdopen(fd, "wb") as stream:
        stream.write(os.urandom(32))
key = key_path.read_bytes()
if len(key) != 32 or key_path.stat().st_mode & 0o077:
    sys.exit("invalid local provenance key")
config = {}
for directory in ("secrets", "rag-corpus", "compose-verify"):
    for path in sorted((captain / directory).rglob("*")):
        if path.is_file() and path != key_path:
            name = str(path.relative_to(captain))
            config[name] = hmac.digest(key, digest(path).encode(), "sha256").hex()
print(json.dumps({"commit": commit, "workingTree": tree, "configurationHmac": config, "generatedMirrorInputs": generated,
                  "status": status.decode().split("\0"), "inputs": inputs}, sort_keys=True))
