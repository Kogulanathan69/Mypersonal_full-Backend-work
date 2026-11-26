"""Contract v1 end-to-end over the real copied retrieval/evidence code with a synthetic corpus."""
import pytest

from support import authed_client

from synthetic_corpus import (
    NORTH, NORTH_JURISDICTION, SOUTH, SOUTH_JURISDICTION, UNKNOWN_DOCUMENT, SyntheticEngine, page_text,
)

SEARCH = "/internal/v1/regulations/search"


@pytest.fixture(scope="module")
def client():
    return authed_client(SyntheticEngine(), raise_server_exceptions=False)


def search(client, **body):
    response = client.post(SEARCH, json=body)
    assert response.status_code == 200, response.text
    data = response.json()
    assert data["legal_status_verified"] is False and data["answer_generated"] is False
    return data


def test_evidence_found_with_exact_citations(client):
    data = search(client, query="rear space residential building", jurisdiction=NORTH_JURISDICTION, k=5)

    assert data["status"] == "EVIDENCE_FOUND"
    assert data["passages"], "expected at least one cited passage"
    first = data["passages"][0]
    assert first["document_id"] == NORTH and first["jurisdiction"] == NORTH_JURISDICTION
    assert first["source_title"] == "north_plan.pdf"  # file name only; folder layout never exposed
    assert first["page"] == 1
    assert page_text(NORTH, first["page"])[first["start_char"]:first["end_char"]] == first["excerpt"]
    assert "rear space" in first["excerpt"].lower()
    assert first["evidence_status"] == "EVIDENCE_CANDIDATE"
    assert any("not verified" in w.lower() for w in data["warnings"])


def test_jurisdiction_filter_isolates_sources(client):
    data = search(client, query="rear space residential building", jurisdiction=SOUTH_JURISDICTION)

    assert all(p["document_id"] == SOUTH for p in data["passages"])
    assert all("rear space" not in p["excerpt"].lower() for p in data["passages"])


def test_location_scoped_ranking_returns_the_local_rule(client):
    data = search(client, query="plot coverage residential building", jurisdiction=SOUTH_JURISDICTION)

    assert data["status"] == "EVIDENCE_FOUND"
    assert data["passages"][0]["document_id"] == SOUTH
    assert "plot coverage" in data["passages"][0]["excerpt"].lower()


def test_unknown_jurisdiction_is_reported_not_guessed(client):
    data = search(client, query="rear space", jurisdiction="Atlantis Urban Council planning area")

    assert data["status"] == "INSUFFICIENT_JURISDICTION_EVIDENCE"
    assert data["passages"] == []


def test_no_matching_evidence(client):
    data = search(client, query="helipad zeppelin mooring mast")

    assert data["status"] == "NO_MATCHING_EVIDENCE"
    assert data["passages"] == []


def test_unknown_source_is_insufficient_source_evidence(client):
    data = search(client, query="rear space", document_ids=[UNKNOWN_DOCUMENT])

    assert data["status"] == "INSUFFICIENT_SOURCE_EVIDENCE"
    assert data["passages"] == []


def test_missing_requested_source_coverage_is_explicit(client):
    # Both sources requested; only the north one can lexically support this query: evidence is incomplete, not "found".
    data = search(client, query="minimum rear space metres behind", document_ids=[NORTH, SOUTH])

    assert data["status"] == "NO_MATCHING_EVIDENCE"
    assert data["passages"] == []
    assert "INCOMPLETE_REQUESTED_SOURCE_COVERAGE" in data["warnings"]


def test_approval_seeking_query_is_flagged_ambiguous(client):
    data = search(client, query="approve rear space residential building", jurisdiction=NORTH_JURISDICTION)

    assert data["status"] == "AMBIGUOUS_EVIDENCE"
    assert data["passages"] and all(p["evidence_status"] == "AMBIGUOUS_EVIDENCE" for p in data["passages"])
    assert any("LEGAL_APPLICABILITY_OR_APPROVAL_NOT_VERIFIED" in w for p in data["passages"] for w in p["warnings"])


def test_embedded_instructions_in_sources_are_data_not_commands(client):
    data = search(client, query="building height residential", jurisdiction=NORTH_JURISDICTION)

    assert data["answer_generated"] is False and data["legal_status_verified"] is False
    assert data["status"] in ("EVIDENCE_FOUND", "AMBIGUOUS_EVIDENCE")
    assert all(p["page"] >= 1 for p in data["passages"])


def test_k_limits_the_number_of_passages(client):
    data = search(client, query="residential building", k=1)

    assert len(data["passages"]) <= 1


def test_sources_lists_jurisdiction_labels_without_paths(client):
    response = client.get("/internal/v1/regulations/sources")

    assert response.status_code == 200
    sources = response.json()["sources"]
    assert {s["jurisdiction"] for s in sources} == {NORTH_JURISDICTION, SOUTH_JURISDICTION}
    assert all("\\" not in s["source_title"] and "/" not in s["source_title"] for s in sources)
    assert all(s["legal_status"] == "not_verified" for s in sources)
