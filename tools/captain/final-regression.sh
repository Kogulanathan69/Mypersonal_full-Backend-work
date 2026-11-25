R=~/bm-captain/results/final-regression; rm -rf $R; mkdir -p $R
x() { echo "$1 exit=$2" >> $R/EXIT-CODES.txt; }
cd ~/bm-captain/src
bash tools/captain/compose-verify.sh down > /dev/null 2>&1
pkill -f 'BuildMind.API.dll' || true; pkill -f 'uvicorn app.main:app' || true; pkill -f llama-server || true; sleep 2
~/bm-captain/sync.sh >/dev/null
echo "commit-under-test: $(cat /mnt/c/Users/kogul/Documents/BuildMind-Worktrees/captain-full-backend-completion/.git 2>/dev/null | head -1)" > $R/HEADER.txt
echo "== build"; dotnet build BuildMind.sln -c Release > $R/build.log 2>&1; x build $?; grep -E "Warning\(s\)|Error\(s\)" $R/build.log | sort -u
echo "== unit (clean env, PDF QA)"; ( env -u ConnectionStrings__BuildMind BUILDMIND_PDF_QA_PYTHON=$HOME/bm-captain/venv-python-services/bin/python bash tools/captain/unit-suites.sh $R/unit ) > $R/unit.log 2>&1; x unit-suites $?; cat $R/unit.log
echo "== python"; (cd python-services && ~/bm-captain/venv-python-services/bin/python -m pytest -q -p no:cacheprovider > $R/python.log 2>&1); x python-services $?; tail -1 $R/python.log
(cd rag-service && ~/bm-captain/venv-rag-service/bin/python -B -m pytest -q -p no:cacheprovider > $R/rag.log 2>&1); x rag-service $?; tail -1 $R/rag.log
C=~/bm-captain/rag-corpus
(cd rag-service && BUILDMIND_RAG_CORPUS_ROOT=$C BUILDMIND_RAG_MANIFEST=$C/approved_corpus_manifest.json BUILDMIND_RAG_INDEX=$C/multi_document_index.json ~/bm-captain/venv-rag-service/bin/python -B -m pytest -q -p no:cacheprovider tests_local > $R/rag-real-corpus.log 2>&1); x rag-real-corpus $?; tail -1 $R/rag-real-corpus.log
echo "== stack"; RAG=1 DETACH=1 bash tools/captain/restart-stack.sh > $R/stack.log 2>&1; x stack-start $?
for i in $(seq 1 60); do curl -fsS http://127.0.0.1:5180/health/ready 2>/dev/null | grep -q '"rag","status":"Healthy"' && break; sleep 5; done
curl -s http://127.0.0.1:5180/health/ready > $R/ready.json; cat $R/ready.json; echo
echo "== persistence (stack running)"; bash tools/captain/persistence-tests.sh > $R/persistence.log 2>&1; x persistence $?; grep -E "Passed!|Failed!|^\s+Failed |^[0-9]+$" $R/persistence.log
cd tools/captain
python3 mtls_tests.py > $R/mtls-python.log 2>&1; x mtls-python $?; grep SUMMARY $R/mtls-python.log
MTLS_TARGET=rag python3 mtls_tests.py > $R/mtls-rag.log 2>&1; x mtls-rag $?; grep SUMMARY $R/mtls-rag.log
~/bm-captain/venv-python-services/bin/python rag_acceptance.py > $R/rag-acceptance.log 2>&1; x rag-acceptance $?; grep -E "SUMMARY|independently" $R/rag-acceptance.log
sleep 61; python3 smoke_admin.py > $R/admin.log 2>&1; x smoke-admin $?; grep SUMMARY $R/admin.log
sleep 61; timeout 3600 python3 smoke_journey.py > $R/journey.log 2>&1; x journey $?; grep -E "^FAIL|SUMMARY" $R/journey.log
cat $R/EXIT-CODES.txt
echo "== done-regression $(date -Is)"
