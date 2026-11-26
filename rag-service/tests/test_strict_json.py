"""Strict raw-JSON validation before model deserialization, and authentication-header hardening."""
import pytest
from fastapi.testclient import TestClient

from app.auth import AUTH_HEADER
from support import TEST_TOKEN, authed_client
from synthetic_corpus import SyntheticEngine

ENDPOINTS = ["/internal/v1/regulations/search", "/internal/v1/regulations/constraints"]
JSON = {"content-type": "application/json"}


@pytest.fixture(scope="module")
def client():
    return authed_client(SyntheticEngine())


@pytest.mark.parametrize("endpoint", ENDPOINTS)
@pytest.mark.parametrize("raw", [
    b'{"query": "rear space", "query": "ignored twin"}',                     # duplicate key (root)
    b'{"jurisdiction": "x", "binding": {"project_revision": 1, "project_revision": 2}}',  # duplicate key (nested)
    b'{"query": "rear space", "k": NaN}',                                    # non-finite constant
    b'{"query": "rear space", "k": Infinity}',
    b'{"query": "rear space",}',                                             # trailing comma
    b'{"query": "rear space"',                                               # truncated
    b'["query", "rear space"]',                                              # non-object root
    b'"rear space"',
    b'',                                                                     # empty body
    b'{"query": "\xff\xfe"}',                                                # invalid UTF-8
    b'{"a":' * 40 + b'1' + b'}' * 40,                                        # nesting deeper than the limit
])
def test_malformed_json_is_rejected_before_deserialization(client, endpoint, raw):
    response = client.post(endpoint, content=raw, headers=JSON)

    assert response.status_code == 400
    assert response.json() == {"code": "MALFORMED_REQUEST"}


@pytest.mark.parametrize("endpoint", ENDPOINTS)
@pytest.mark.parametrize("content_type", ["text/plain", "application/x-www-form-urlencoded", "application/jsonp", ""])
def test_non_json_media_types_are_415(client, endpoint, content_type):
    response = client.post(endpoint, content=b'{"query": "rear space"}', headers={"content-type": content_type})

    assert response.status_code == 415 and response.json() == {"code": "UNSUPPORTED_MEDIA_TYPE"}


@pytest.mark.parametrize("endpoint", ENDPOINTS)
def test_chunked_oversized_body_without_content_length_is_413(client, endpoint):
    def chunks():
        yield b'{"query": "'
        for _ in range(40):
            yield b"x" * 1024
        yield b'"}'

    response = client.post(endpoint, content=chunks(), headers=JSON)

    assert response.status_code == 413 and response.json() == {"code": "PAYLOAD_TOO_LARGE"}


def test_charset_parameter_is_accepted(client):
    response = client.post(ENDPOINTS[0], content=b'{"query": "rear space"}', headers={"content-type": "application/json; charset=utf-8"})

    assert response.status_code == 200


def test_malformed_body_is_never_echoed(client):
    secret = "SYNTHETIC-SECRET-strict-json"
    response = client.post(ENDPOINTS[0], content=('{"query": "' + secret + '", "query": "x"}').encode(), headers=JSON)

    assert response.status_code == 400 and secret not in response.text


def test_repeated_token_headers_are_rejected():
    from app.auth import InternalAuth
    from app.main import create_app

    app = create_app(SyntheticEngine(), InternalAuth.from_tokens([TEST_TOKEN]))
    response = TestClient(app).post(
        ENDPOINTS[0], json={"query": "rear space"},
        headers=[(AUTH_HEADER, TEST_TOKEN), (AUTH_HEADER, TEST_TOKEN)])

    assert response.status_code == 401 and response.json() == {"code": "UNAUTHENTICATED"}
