"""Test helpers: every client authenticates like the C# API does (G11 MVP)."""
from fastapi.testclient import TestClient

from app.auth import AUTH_HEADER, InternalAuth
from app.main import create_app

TEST_TOKEN = "synthetic-internal-token-0123456789abcdef"  # synthetic, not a real secret


def authed_client(engine, **kwargs) -> TestClient:
    return TestClient(create_app(engine, InternalAuth.from_tokens([TEST_TOKEN])), headers={AUTH_HEADER: TEST_TOKEN}, **kwargs)
