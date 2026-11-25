#!/usr/bin/env bash
# Final regression runner (resource-aware, resumable, fail-safe verdict).
#
# Usage (from Windows, after committing):  sha=$(git rev-parse HEAD); clean=$([ -z "$(git status --porcelain --untracked-files=no)" ] && echo clean || echo dirty)
#          wsl.exe -d Ubuntu-24.04 -- bash ~/bm-captain/src/tools/captain/final-regression.sh "$sha" "$clean" [RUN_DIR_TO_RESUME]
#
# - Records the exact Git commit SHA and the working-tree state passed by the caller (the worktree pointer is never used).
# - Every run gets a unique directory ~/bm-captain/results/final-regression/<UTC>-<sha12>; earlier runs are kept.
# - Stages run in resource groups (only the workloads each stage needs; the 3.9 GB host cannot run everything at once):
#     G1 build + .NET unit (no services)  G2 Python/RAG suites (no services)
#     G3 MySQL+python+RAG+API: persistence (with the stack running), mTLS x2, RAG acceptance, admin smoke,
#        full journey 2..12 with live contract capture, OpenAPI export
#     G4 python+API(LLM mode)+llama: LLM benchmark + smoke_llm        G5 Docker stack (no LLM): container journeys + RAG
# - Only Captain-owned workloads are stopped (stop-captain.sh); the Captain database is never reset or dropped.
# - VERDICT is PASS only if every expected stage ran to completion with exit 0. An interrupted run is INTERRUPTED.
# - Resume: pass an existing run directory; stages already marked PASS are skipped (their evidence is kept).
set -u
SHA="${1:?commit sha required}"; TREE="${2:?clean|dirty required}"; RESUME="${3:-}"
case "$SHA" in *[!0-9a-f]*|"") echo "invalid sha"; exit 2;; esac
SRC=~/bm-captain/src; CAP=$SRC/tools/captain
R="${RESUME:-$HOME/bm-captain/results/final-regression/$(date -u +%Y%m%dT%H%M%SZ)-${SHA:0:12}}"
mkdir -p "$R/stages"
[ -f "$R/RUN.json" ] || printf '{"commit":"%s","workingTree":"%s","startedUtc":"%s","host":"%s"}\n' "$SHA" "$TREE" "$(date -u -Is)" "$(hostname)" > "$R/RUN.json"
echo "INTERRUPTED" > "$R/VERDICT"
trap 'grep -q "^PASS$\|^FAIL$" "$R/VERDICT" || echo "INTERRUPTED" > "$R/VERDICT"' EXIT
STAGES=(build unit python rag rag-real stack persistence mtls-python mtls-rag rag-acceptance admin journey openapi llm-benchmark smoke-llm docker-journey docker-rag)

stage() { # name command...
  local name=$1; shift
  if [ -f "$R/stages/$name.PASS" ]; then echo "skip $name (PASS in this run dir)"; return 0; fi
  echo "== $name $(date -u +%H:%M:%S)"
  ( "$@" ) > "$R/$name.log" 2>&1; local rc=$?
  echo "$name exit=$rc" >> "$R/EXIT-CODES.txt"
  if [ $rc -eq 0 ]; then touch "$R/stages/$name.PASS"; rm -f "$R/stages/$name.FAIL"; else touch "$R/stages/$name.FAIL"; fi
  tail -2 "$R/$name.log"
  return $rc
}
stop_all() { bash $CAP/stop-captain.sh --containers >/dev/null 2>&1; }
cd $SRC
~/bm-captain/sync.sh >/dev/null

# G1 / G2: no services
stop_all
stage build dotnet build BuildMind.sln -c Release
stage unit env -u ConnectionStrings__BuildMind BUILDMIND_PDF_QA_PYTHON=$HOME/bm-captain/venv-python-services/bin/python bash $CAP/unit-suites.sh "$R/unit"
stage python bash -c "cd python-services && ~/bm-captain/venv-python-services/bin/python -m pytest -q -p no:cacheprovider"
stage rag bash -c "cd rag-service && ~/bm-captain/venv-rag-service/bin/python -B -m pytest -q -p no:cacheprovider"
C=~/bm-captain/rag-corpus
stage rag-real bash -c "cd rag-service && BUILDMIND_RAG_CORPUS_ROOT=$C BUILDMIND_RAG_MANIFEST=$C/approved_corpus_manifest.json BUILDMIND_RAG_INDEX=$C/multi_document_index.json ~/bm-captain/venv-rag-service/bin/python -B -m pytest -q -p no:cacheprovider tests_local"

# G3: MySQL + python + RAG + API (two instances)
stage stack bash -c "RAG=1 DETACH=1 bash $CAP/restart-stack.sh && for i in \$(seq 1 60); do curl -fsS http://127.0.0.1:5180/health/ready 2>/dev/null | grep -q '\"rag\",\"status\":\"Healthy\"' && exit 0; sleep 5; done; exit 1"
stage persistence bash -c "bash $CAP/persistence-tests.sh | tee /dev/stderr | grep -q 'exit=0'"
cd $CAP
stage mtls-python python3 mtls_tests.py
stage mtls-rag env MTLS_TARGET=rag python3 mtls_tests.py
mkdir -p "$R/capture"
stage rag-acceptance env BM_CAPTURE="$R/capture/rag.jsonl" ~/bm-captain/venv-python-services/bin/python rag_acceptance.py
sleep 61
stage admin env BM_CAPTURE="$R/capture/admin.jsonl" python3 smoke_admin.py
sleep 61
stage journey env BM_CAPTURE="$R/capture/journey.jsonl" timeout 4000 python3 smoke_journey.py
stage openapi bash -c "RAG=1 DETACH=1 bash $CAP/restart-stack.sh >/dev/null 2>&1; sleep 5; curl -fsS http://127.0.0.1:5180/swagger/v1/swagger.json -o '$R/openapi.raw.json' && python3 openapi_enrich.py '$R/openapi.raw.json' '$R/openapi.captain-dev.json' '$R/FINAL-API-ACCEPTANCE-MATRIX.md' '$R'/capture/*.jsonl"
stop_all

# G4: python + API (LLM mode) + local model
cd $SRC
stage llm-benchmark bash -c "nohup bash $CAP/run-python.sh 8091 > ~/bm-captain/logs/python.log 2>&1 &
  nohup bash $CAP/run-llm.sh 8093 > ~/bm-captain/logs/llm.log 2>&1 &
  for i in \$(seq 1 60); do curl -fsS http://127.0.0.1:8093/health >/dev/null 2>&1 && break; sleep 3; done
  export CaptainIntegration__GenerationPolicyFile=$SRC/docs/captain-development/samples/generation-policy.synthetic.json
  [ -s ~/bm-captain/secrets/captain-admin.email ] && export ADMIN_BOOTSTRAP_EMAIL=\$(cat ~/bm-captain/secrets/captain-admin.email)
  LLM_PORT=8093 PY_PORT=8091 nohup bash $CAP/run-api.sh 5180 > ~/bm-captain/logs/api.log 2>&1 &
  for i in \$(seq 1 60); do curl -fsS http://127.0.0.1:5180/health >/dev/null 2>&1 && break; sleep 3; done
  sleep 61; cd $CAP && timeout 5400 python3 llm_benchmark.py --out '$R/llm-benchmark.json'"
sleep 61
stage smoke-llm bash -c "cd $CAP && timeout 1800 python3 smoke_llm.py"
stop_all

# G5: Docker stack (no LLM)
cd $SRC
stage docker-journey bash -c "bash $CAP/compose-verify.sh build >/dev/null && bash $CAP/compose-verify.sh prepare >/dev/null && bash $CAP/compose-verify.sh up >/dev/null 2>&1;
  for i in \$(seq 1 90); do curl -fsS http://127.0.0.1:8080/health/ready 2>/dev/null | grep -q '\"rag\",\"status\":\"Healthy\"' && break; sleep 5; done
  sleep 61; cd $CAP && MAILPIT_API=http://127.0.0.1:8026/api/v1 timeout 4000 python3 smoke_journey.py http://127.0.0.1:8080 --phases 2,3,4,6,8,9,10,11,12"
stage docker-rag bash -c "cd $CAP && ~/bm-captain/venv-python-services/bin/python rag_acceptance.py http://127.0.0.1:8080"
bash $CAP/compose-verify.sh down >/dev/null 2>&1; stop_all

# Counts and verdict
python3 - "$R" <<'PY'
import json, os, re, sys
r = sys.argv[1]; counts = {}
def grab(name, pattern, group=1):
    try: text = open(os.path.join(r, name + ".log"), errors="replace").read()
    except OSError: return None
    m = re.findall(pattern, text); return [int(x if isinstance(x, str) else x[group - 1]) for x in m] if m else None
counts["dotnet_unit"] = {m[0]: int(m[1]) for m in re.findall(r"(BuildMind\.[\w.]+): Passed!\s+- Failed:\s+0, Passed:\s+(\d+)", open(os.path.join(r, "unit.log")).read())} if os.path.exists(os.path.join(r, "unit.log")) else None
for n in ("python", "rag", "rag-real"): counts[n] = grab(n, r"(\d+) passed")
counts["persistence"] = grab("persistence", r"Passed:\s+(\d+), Skipped")
for n in ("mtls-python", "mtls-rag", "rag-acceptance", "admin", "journey", "smoke-llm", "docker-journey", "docker-rag"):
    counts[n] = grab(n, r"SUMMARY: (\d+) passed, (\d+) failed", 1)
json.dump(counts, open(os.path.join(r, "COUNTS.json"), "w"), indent=2)
print(json.dumps(counts))
PY
ok=PASS
for s in "${STAGES[@]}"; do [ -f "$R/stages/$s.PASS" ] || { ok=FAIL; echo "stage not passed: $s"; }; done
echo "$ok" > "$R/VERDICT"; echo "VERDICT $ok  ($R)"
