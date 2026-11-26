"""LOCAL-ONLY real-corpus verification (not collected by CI: the corpus never enters the repository).

Run explicitly, read-only, against the approved STEP 4 corpus:
    BUILDMIND_RAG_CORPUS_ROOT=<root>  BUILDMIND_RAG_MANIFEST=<approved_corpus_manifest.json>
    BUILDMIND_RAG_INDEX=<multi_document_index.json>  python -B -m pytest -p no:cacheprovider tests_local
The tests FAIL (never skip) if the variables are missing.
"""
import pytest

from app.engine import EngineSettings, Step6Engine
from support import authed_client

COLOMBO = "Colombo Municipal Council planning area"
MAHARAGAMA = "Maharagama Urban Council planning area"
SEARCH = "/internal/v1/regulations/search"


@pytest.fixture(scope="module")
def engine():
    settings = EngineSettings.from_environment()
    assert settings is not None, "BUILDMIND_RAG_CORPUS_ROOT / _MANIFEST / _INDEX must point at the approved corpus"
    return Step6Engine(settings)  # real constructor: original-PDF and record hashes are verified


@pytest.fixture(scope="module")
def client(engine):
    return authed_client(engine)


def _records(engine):
    return engine._service.assessor.records


def test_constraints_citing_passages_absent_from_the_real_corpus_are_refused(engine):
    """A table whose citations do not reconstruct from the approved, hash-verified corpus can never load."""
    import json
    from pathlib import Path

    from app.constraints import ConstraintProvider

    fixtures = Path(__file__).resolve().parents[1] / "tests" / "fixtures"
    synthetic = json.loads((fixtures / "synthetic_constraints.v0.json").read_text(encoding="utf-8"))
    reviewers = json.loads((fixtures / "synthetic_reviewers.v0.json").read_text(encoding="utf-8"))
    with pytest.raises(ValueError, match="does not reconstruct"):
        ConstraintProvider(synthetic, engine.page_text, reviewers, allow_synthetic=True)


def test_exactly_the_three_approved_sources_are_served(client):
    sources = client.get("/internal/v1/regulations/sources").json()["sources"]

    assert len(sources) == 3
    assert {s["jurisdiction"] for s in sources} == {"Sri Lanka", COLOMBO, MAHARAGAMA}


@pytest.mark.parametrize("jurisdiction", [COLOMBO, MAHARAGAMA])
def test_location_filter_isolates_each_council_and_citations_reconstruct(client, engine, jurisdiction):
    data = client.post(SEARCH, json={"query": "plot coverage", "jurisdiction": jurisdiction, "k": 5}).json()

    assert data["status"] in ("EVIDENCE_FOUND", "AMBIGUOUS_EVIDENCE"), data["status"]
    assert data["passages"]
    for p in data["passages"]:
        assert p["jurisdiction"] == jurisdiction
        page = _records(engine)[p["document_id"]]["pages"][p["page"] - 1]["text"]
        assert page[p["start_char"]:p["end_char"]] == p["excerpt"]
    assert data["legal_status_verified"] is False and data["answer_generated"] is False


def test_unknown_location_is_insufficient_jurisdiction_evidence(client):
    data = client.post(SEARCH, json={"query": "plot coverage", "jurisdiction": "Kandy Municipal Council planning area"}).json()

    assert data["status"] == "INSUFFICIENT_JURISDICTION_EVIDENCE" and data["passages"] == []


def test_nonsense_query_has_no_evidence(client):
    data = client.post(SEARCH, json={"query": "zeppelin mooring mast helipad"}).json()

    assert data["status"] in ("NO_MATCHING_EVIDENCE",), data["status"]
    assert data["passages"] == []


def test_rear_space_research_returns_cited_pages(client):
    data = client.post(SEARCH, json={"query": "rear space requirements", "k": 5}).json()

    assert data["status"] in ("EVIDENCE_FOUND", "AMBIGUOUS_EVIDENCE")
    assert all(p["page"] >= 1 and p["source_title"].endswith(".pdf") for p in data["passages"])
