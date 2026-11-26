"""BM-INT-01: bounded HTTP request streaming regression."""

import json

from app.main import EXACT_PLAN_MAX_BODY_BYTES
from app.internal.auth import AUTH_HEADER
from test_internal_api import client, TOKEN
from test_exact_plan_validation import ROUTE, request_body


def streamed_post(chunks):
    return client().post(
        ROUTE,
        content=iter(chunks),
        headers={
            AUTH_HEADER: TOKEN,
            "Content-Type": "application/json",
        },
    )


def test_streamed_valid_geometry_is_accepted():
    body = request_body()
    raw = json.dumps(body).encode("utf-8")

    response = streamed_post(
        raw[i:i + 128] for i in range(0, len(raw), 128)
    )

    assert "content-length" not in response.request.headers
    assert response.status_code == 200
    assert response.json()["geometryValid"] is True
    assert response.json()["geometryHash"] == body["geometryHash"]


def test_streamed_oversized_body_is_rejected():
    response = streamed_post([
        b" " * EXACT_PLAN_MAX_BODY_BYTES,
        b" ",
    ])

    assert "content-length" not in response.request.headers
    assert response.status_code == 413
    assert response.json() == {"code": "PAYLOAD_TOO_LARGE"}


def test_streamed_exact_limit_reaches_json_validation():
    response = streamed_post([
        b" " * (EXACT_PLAN_MAX_BODY_BYTES // 2),
        b" " * (EXACT_PLAN_MAX_BODY_BYTES // 2),
    ])

    assert response.status_code == 422
    assert response.json() == {"code": "VALIDATION_FAILED"}


def test_declared_oversized_body_is_rejected():
    response = client().post(
        ROUTE,
        content=b"{}",
        headers={
            AUTH_HEADER: TOKEN,
            "Content-Type": "application/json",
            "Content-Length": str(EXACT_PLAN_MAX_BODY_BYTES + 1),
        },
    )

    assert response.status_code == 413
    assert response.json() == {"code": "PAYLOAD_TOO_LARGE"}


def test_unauthenticated_stream_is_rejected():
    response = client().post(
        ROUTE,
        content=iter([b"{}"]),
        headers={"Content-Type": "application/json"},
    )

    assert response.status_code == 401
    assert response.json() == {"code": "UNAUTHENTICATED"}


def test_asgi_stops_reading_after_limit():
    import asyncio

    from app.main import create_app
    from app.internal.auth import InternalAuth

    async def run():
        app = create_app(InternalAuth.from_tokens([TOKEN]))
        messages = []
        reads = 0

        async def receive():
            nonlocal reads
            reads += 1

            if reads == 1:
                return {
                    "type": "http.request",
                    "body": b" " * EXACT_PLAN_MAX_BODY_BYTES,
                    "more_body": True,
                }

            if reads == 2:
                return {
                    "type": "http.request",
                    "body": b" ",
                    "more_body": True,
                }

            raise AssertionError(
                "Request reader consumed data after exceeding limit"
            )

        async def send(message):
            messages.append(message)

        scope = {
            "type": "http",
            "asgi": {"version": "3.0", "spec_version": "2.4"},
            "http_version": "1.1",
            "method": "POST",
            "scheme": "http",
            "path": ROUTE,
            "raw_path": ROUTE.encode("ascii"),
            "root_path": "",
            "query_string": b"",
            "headers": [
                (AUTH_HEADER.lower().encode("ascii"),
                 TOKEN.encode("ascii")),
                (b"content-type", b"application/json"),
            ],
            "client": ("127.0.0.1", 12345),
            "server": ("127.0.0.1", 8000),
        }

        await asyncio.wait_for(
            app(scope, receive, send), timeout=5
        )

        assert reads == 2

        start = next(
            m for m in messages
            if m["type"] == "http.response.start"
        )
        assert start["status"] == 413

        raw = b"".join(
            m.get("body", b"")
            for m in messages
            if m["type"] == "http.response.body"
        )
        assert json.loads(raw) == {
            "code": "PAYLOAD_TOO_LARGE"
        }

    asyncio.run(run())


def test_route_specific_limits_are_isolated():
    from app.main import (
        _limit,
        MAX_BODY_BYTES,
        SURVEY_MAX_BODY_BYTES,
        SURVEY_EXTRACT_PATH,
        EXACT_PLAN_VALIDATE_PATH,
    )

    assert MAX_BODY_BYTES == 2 * 1024 * 1024
    assert EXACT_PLAN_MAX_BODY_BYTES == 8 * 1024 * 1024
    assert SURVEY_MAX_BODY_BYTES == 9 * 1024 * 1024

    assert _limit(EXACT_PLAN_VALIDATE_PATH) == 8 * 1024 * 1024
    assert _limit(SURVEY_EXTRACT_PATH) == 9 * 1024 * 1024
    assert _limit("/internal/v1/plans/validate") == 2 * 1024 * 1024
    assert _limit("/internal/v1/generation/run") == 2 * 1024 * 1024
    assert _limit("/internal/v1/plans/edit-preview") == 2 * 1024 * 1024
    assert _limit(EXACT_PLAN_VALIDATE_PATH + "/") == 2 * 1024 * 1024


def test_legacy_route_retains_original_payload_limit():
    from app.main import MAX_BODY_BYTES

    response = client().post(
        "/internal/v1/plans/validate",
        content=iter([b" " * MAX_BODY_BYTES, b" "]),
        headers={
            AUTH_HEADER: TOKEN,
            "Content-Type": "application/json",
        },
    )

    assert response.status_code == 413
    assert response.json() == {"code": "PAYLOAD_TOO_LARGE"}


def test_v2_accepts_expanded_exact_geometry_payload():
    from app.main import MAX_BODY_BYTES
    from test_exact_plan_validation import digest

    body = request_body()
    raw = body["geometryJson"]

    raw += "\t" * (1048576 - len(raw.encode("utf-8")))

    assert len(raw.encode("utf-8")) == 1048576

    body["geometryJson"] = raw
    body["geometryHash"] = digest(raw)

    outer = json.dumps(body).encode("utf-8")

    # Simulate six-character JSON escaping of tab characters.
    outer = outer.replace(b"\\t", b"\\u0009")

    assert MAX_BODY_BYTES < len(outer) <= EXACT_PLAN_MAX_BODY_BYTES

    response = streamed_post([outer])

    assert response.status_code == 200
    assert response.json()["geometryValid"] is True
    assert response.json()["geometryHash"] == body["geometryHash"]
