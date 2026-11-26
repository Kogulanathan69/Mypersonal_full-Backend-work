"""TEST-ONLY synthetic cross-service acceptance service (M4/M6/M7). Never part of the deployed application.

Serves the REAL rag-service FastAPI app (real internal auth, strict raw-JSON parsing, real HTTP serialization) once
per shared setback scenario, mounted at /scenario/<name>/, each with that scenario's sealed SYNTHETIC table, the
synthetic reviewer registry and the synthetic corpus. The C# acceptance tests call it through the production
RagServiceClient, so the evidence-pack digest is recomputed in C# from bytes produced by Python over a real socket.

    BUILDMIND_ACCEPTANCE_SYNTHETIC=1 BUILDMIND_RAG_INTERNAL_TOKENS=<random> \\
        python -B -m uvicorn acceptance_app:app --app-dir tests --host 127.0.0.1 --port 8097

Refuses to start unless BUILDMIND_ACCEPTANCE_SYNTHETIC=1 and an internal token are set (auth is never disabled).
"""
import os
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path[:0] = [str(ROOT), str(ROOT / "tests"), str(ROOT / "tools")]

from starlette.applications import Starlette  # noqa: E402
from starlette.routing import Mount  # noqa: E402

from app.auth import InternalAuth  # noqa: E402
from app.constraints import ConstraintProvider  # noqa: E402
from app.main import create_app  # noqa: E402
from build_contract_fixtures import reviewers, scenario_cases  # noqa: E402
from synthetic_corpus import SyntheticEngine, page_text  # noqa: E402

if os.environ.get("BUILDMIND_ACCEPTANCE_SYNTHETIC") != "1":
    raise RuntimeError("acceptance_app is test-only: set BUILDMIND_ACCEPTANCE_SYNTHETIC=1 in an isolated test environment")

_auth = InternalAuth.from_environment()
if not _auth.required or not _auth.tokens:
    raise RuntimeError("acceptance_app requires internal authentication with a token")

_engine = SyntheticEngine()
app = Starlette(routes=[
    Mount(f"/scenario/{name}", app=create_app(_engine, _auth, ConstraintProvider(table, page_text, reviewers(), allow_synthetic=True)))
    for name, _description, table, _overrides, _status in scenario_cases()
])
