"""Request validation, failure translation and exposure rules (no corpus needed)."""
import pytest

from support import authed_client

from app.main import MAX_BODY_BYTES, SERVICE_NAME

SEARCH = "/internal/v1/regulations/search"
SECRET = "SYNTHETIC-SECRET-rag-7f3a"


class RaisingEngine:
    def __init__(self, error):
        self.error = error

    def search(self, *args):
        raise self.error

    def registry(self):
        raise self.error


class RecordingEngine:
    def __init__(self):
        self.calls = []

    def search(self, query, k, filters, requested):
        self.calls.append((query, k, filters, requested))
        return {"retrieval": {"status": "no_matching_passages", "results": [], "source_warnings": []},
                "evidence": {"status": "INSUFFICIENT_EVIDENCE", "groups": {}, "warnings": [], "reasons": []}}

    def registry(self):
        return {}


def test_health_reports_identity_and_corpus_state():
    body = authed_client(None).get("/health").json()

    assert body == {"service": SERVICE_NAME, "status": "ok", "contract_version": "1", "corpus_loaded": False}


@pytest.mark.parametrize("path,method", [(SEARCH, "post"), ("/internal/v1/regulations/sources", "get")])
def test_without_a_corpus_the_service_is_truthfully_unavailable(path, method):
    client = authed_client(None)
    response = client.post(path, json={"query": "rear space"}) if method == "post" else client.get(path)

    assert response.status_code == 503
    assert response.json() == {"code": "CORPUS_UNAVAILABLE"}


@pytest.mark.parametrize("body", [
    {},
    {"query": ""},
    {"query": "   "},
    {"query": "x" * 501},
    {"query": "rear space", "k": 0},
    {"query": "rear space", "k": 11},
    {"query": "rear space", "k": "5"},
    {"query": "rear space", "unexpected": True},
    {"query": "rear space", "jurisdiction": ""},
    {"query": "rear space", "document_ids": []},
    {"query": "rear space", "document_ids": ["not-a-hash"]},
    {"query": "rear space", "document_ids": ["a" * 64, "b" * 64, "c" * 64, "d" * 64]},
])
def test_invalid_requests_are_422_without_echoing_input(body):
    response = authed_client(RecordingEngine()).post(SEARCH, json=body)

    assert response.status_code == 422
    data = response.json()
    assert data["code"] == "VALIDATION_FAILED"
    assert all(set(error) == {"loc", "msg"} for error in data.get("errors", []))  # no "input" echo


def test_oversized_body_is_rejected_before_parsing():
    response = authed_client(RecordingEngine()).post(
        SEARCH, content=b"{" + b" " * MAX_BODY_BYTES + b"}", headers={"content-type": "application/json"})

    assert response.status_code == 413
    assert response.json() == {"code": "PAYLOAD_TOO_LARGE"}


def test_engine_validation_error_is_422_without_detail():
    client = authed_client(RaisingEngine(ValueError(SECRET)), raise_server_exceptions=False)

    response = client.post(SEARCH, json={"query": "rear space"})

    assert response.status_code == 422 and SECRET not in response.text


def test_unexpected_engine_failure_is_a_generic_500(caplog):
    client = authed_client(RaisingEngine(RuntimeError(SECRET)), raise_server_exceptions=False)

    response = client.post(SEARCH, json={"query": "rear space"})

    assert response.status_code == 500
    assert response.json() == {"code": "INTERNAL_ERROR"}
    assert SECRET not in caplog.text


def test_filters_are_passed_exactly_as_requested():
    engine = RecordingEngine()
    client = authed_client(engine)

    client.post(SEARCH, json={"query": "rear space", "jurisdiction": "North", "k": 3})
    client.post(SEARCH, json={"query": "rear space", "document_ids": ["a" * 64]})
    client.post(SEARCH, json={"query": "rear space", "document_ids": ["a" * 64, "b" * 64]})

    assert engine.calls[0] == ("rear space", 3, {"jurisdiction": "North"}, None)
    assert engine.calls[1] == ("rear space", 5, {"document_id": "a" * 64}, ["a" * 64])
    assert engine.calls[2] == ("rear space", 5, {}, ["a" * 64, "b" * 64])  # coverage request, no single filter


@pytest.mark.parametrize("path", ["/docs", "/redoc", "/openapi.json"])
def test_interactive_docs_and_schema_are_not_exposed(path):
    assert authed_client(None).get(path).status_code == 404


@pytest.mark.parametrize("path", [SEARCH, "/internal/v1/regulations/constraints"])
def test_validation_errors_never_reflect_submitted_names_or_values(path):
    """M6 H4-R1 C09: a fixed 422 body; attacker-chosen property names and values never come back."""
    body = {"query": "rear space", "zz_injected_property_name": "zz-injected-value", "kinds": "zz-injected-kind"}

    response = authed_client(RecordingEngine()).post(path, json=body)

    assert response.status_code in (422, 503)
    assert "zz_injected" not in response.text and "zz-injected" not in response.text
    if response.status_code == 422:
        assert response.json() == {"code": "VALIDATION_FAILED"}
