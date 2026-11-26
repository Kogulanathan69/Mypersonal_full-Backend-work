"""G11 MVP internal authentication: shared secret, constant-time check, rotation, fail closed, default-deny."""
import pytest
from fastapi.testclient import TestClient

from app.auth import AUTH_HEADER, InternalAuth
from app.main import create_app
from support import TEST_TOKEN
from synthetic_corpus import SyntheticEngine

SEARCH = "/internal/v1/regulations/search"
SOURCES = "/internal/v1/regulations/sources"
ROTATED = "rotated-internal-token-fedcba9876543210ab"


@pytest.fixture(scope="module")
def app():
    return create_app(SyntheticEngine(), InternalAuth.from_tokens([TEST_TOKEN, ROTATED]))


def call(app, path, token=None, json=None):
    client = TestClient(app)
    headers = {} if token is None else {AUTH_HEADER: token}
    return client.post(path, json=json, headers=headers) if json is not None else client.get(path, headers=headers)


@pytest.mark.parametrize("token", [None, "", "wrong-token-wrong-token-wrong-token-xx", TEST_TOKEN[:-1], TEST_TOKEN + "x", TEST_TOKEN.upper()])
def test_missing_or_wrong_token_is_401_without_detail(app, token):
    for response in (call(app, SEARCH, token, {"query": "rear space"}), call(app, SOURCES, token)):
        assert response.status_code == 401
        assert response.json() == {"code": "UNAUTHENTICATED"}
        assert TEST_TOKEN not in response.text and ROTATED not in response.text


@pytest.mark.parametrize("token", [TEST_TOKEN, ROTATED])
def test_both_rotation_tokens_are_accepted(app, token):
    assert call(app, SEARCH, token, {"query": "rear space"}).status_code == 200
    assert call(app, SOURCES, token).status_code == 200


def test_health_stays_open_for_identity_probes(app):
    response = call(app, "/health")

    assert response.status_code == 200 and response.json()["service"] == "buildmind-rag"


def test_authentication_runs_before_validation_and_size_checks(app):
    invalid = call(app, SEARCH, None, {"unexpected": True})
    oversized = TestClient(app).post(SEARCH, content=b"x" * 20000, headers={"content-type": "application/json"})

    assert invalid.status_code == 401 and oversized.status_code == 401


@pytest.mark.parametrize("path", ["/internal/v2/anything", "/admin", "/docs", "/openapi.json", "//internal/v1/regulations/search"])
def test_default_deny_hides_route_existence(app, path):
    assert call(app, path).status_code == 401


def test_required_without_configured_tokens_fails_closed():
    app = create_app(SyntheticEngine(), InternalAuth((), required=True))

    response = call(app, SEARCH, TEST_TOKEN, {"query": "rear space"})

    assert response.status_code == 503 and response.json() == {"code": "AUTH_NOT_CONFIGURED"}
    assert call(app, "/health").status_code == 200


def test_explicitly_disabled_auth_is_development_only_opt_out():
    app = create_app(SyntheticEngine(), InternalAuth((), required=False))

    assert call(app, SEARCH, None, {"query": "rear space"}).status_code == 200


@pytest.mark.parametrize("tokens", [
    ["too-short"],
    ["has space " + "x" * 40],
    ["line\nbreak" + "x" * 40],
    [TEST_TOKEN, ROTATED, "third-token-is-not-allowed-0123456789"],
    ["non-ascii-é" + "x" * 40],
])
def test_invalid_token_configuration_is_rejected_without_echo(tokens):
    with pytest.raises(ValueError) as error:
        InternalAuth.from_tokens(tokens)
    assert all(t not in str(error.value) for t in tokens)


@pytest.mark.parametrize("environment", [None, "", "production", "staging", "dev"])
def test_a_single_setting_can_never_disable_auth_outside_declared_development(monkeypatch, environment):
    """M6 H4-R1 C09: the opt-out needs BUILDMIND_RAG_ENVIRONMENT=development as a second, explicit declaration."""
    monkeypatch.setenv("BUILDMIND_RAG_AUTH", "disabled")
    if environment is None:
        monkeypatch.delenv("BUILDMIND_RAG_ENVIRONMENT", raising=False)
    else:
        monkeypatch.setenv("BUILDMIND_RAG_ENVIRONMENT", environment)

    with pytest.raises(RuntimeError, match="requires BUILDMIND_RAG_ENVIRONMENT=development"):
        InternalAuth.from_environment()


def test_environment_defaults_to_required(monkeypatch):
    monkeypatch.delenv("BUILDMIND_RAG_AUTH", raising=False)
    monkeypatch.setenv("BUILDMIND_RAG_INTERNAL_TOKENS", f" {TEST_TOKEN} , {ROTATED} ")

    auth = InternalAuth.from_environment()

    assert auth.required is True and len(auth.tokens) == 2
    assert auth.check(TEST_TOKEN) is None and auth.check(ROTATED) is None and auth.check("nope") == "UNAUTHENTICATED"


def test_environment_opt_out_must_be_explicit(monkeypatch):
    monkeypatch.setenv("BUILDMIND_RAG_AUTH", "disabled")
    monkeypatch.delenv("BUILDMIND_RAG_INTERNAL_TOKENS", raising=False)
    monkeypatch.setenv("BUILDMIND_RAG_ENVIRONMENT", "development")
    assert InternalAuth.from_environment().required is False

    monkeypatch.setenv("BUILDMIND_RAG_AUTH", "off")  # anything else stays required
    assert InternalAuth.from_environment().required is True
