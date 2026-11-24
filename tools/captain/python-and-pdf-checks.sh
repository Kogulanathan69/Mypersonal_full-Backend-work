#!/usr/bin/env bash
# Independent PDF content check, the PDF-QA Reports test (needs BUILDMIND_PDF_QA_PYTHON) and Python suites in WSL.
cd ~/bm-captain/src
PY=~/bm-captain/venv-python-services/bin/python
echo "== journey report PDF (pypdf)"
"$PY" tools/captain/check_report_pdf.py ~/bm-captain/results/journey-report.pdf
echo "== Reports.Tests PDF QA"
BUILDMIND_PDF_QA_PYTHON="$PY" timeout 900 dotnet test tests/BuildMind.Reports.Tests -c Release --no-build 2>&1 | grep -E "Passed!|Failed!|Skipped " | sed 's/, Duration.*//'
echo "== python-services pytest"
(cd python-services && "$PY" -m pytest -q -p no:cacheprovider 2>&1 | tail -1)
echo "== rag-service pytest"
(cd rag-service && ~/bm-captain/venv-rag-service/bin/python -B -m pytest -q -p no:cacheprovider 2>&1 | tail -1)
